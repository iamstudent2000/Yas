using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public sealed class AuditEventWriterTests
{
    [Fact]
    public async Task Explicit_event_is_stored_with_details_and_the_given_actor()
    {
        var (options, writer, _) = Create();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        await writer.WriteAsync(AuditActions.LoginSucceeded, AuditEntityTypes.Session, employeeId.ToString(),
            new { Username = "ali", Ip = "10.0.0.5" }, employeeId: employeeId, activePositionId: positionId, anonymous: true);

        await using var db = new ApplicationDbContext(options);
        var entry = Assert.Single(db.AuditEntries);
        Assert.Equal(AuditActions.LoginSucceeded, entry.Action);
        Assert.Equal(AuditEntityTypes.Session, entry.EntityType);
        Assert.Equal(employeeId.ToString(), entry.EntityId);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(positionId, entry.ActivePositionId);
        Assert.Null(entry.BeforeJson);
        Assert.Contains("\"Username\":\"ali\"", entry.AfterJson);
    }

    [Fact]
    public async Task Anonymous_event_ignores_the_ambient_actor()
    {
        var (options, writer, actors) = Create();
        actors.Set(Guid.NewGuid(), Guid.NewGuid(), isAdmin: false);

        await writer.WriteAsync(AuditActions.LoginFailed, AuditEntityTypes.Session, null, new { Username = "nobody" }, anonymous: true);

        await using var db = new ApplicationDbContext(options);
        var entry = Assert.Single(db.AuditEntries);
        Assert.Null(entry.EmployeeId);
        Assert.Null(entry.ActivePositionId);
    }

    [Fact]
    public async Task Event_without_an_actor_uses_the_signed_in_one()
    {
        var (options, writer, actors) = Create();
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        actors.Set(employeeId, positionId, isAdmin: false);

        await writer.WriteAsync(AuditActions.ReportExported, AuditEntityTypes.Report, "audit-log", new { RowCount = 3 });

        await using var db = new ApplicationDbContext(options);
        var entry = Assert.Single(db.AuditEntries);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(positionId, entry.ActivePositionId);
    }

    [Fact]
    public async Task Event_without_an_actor_uses_the_signed_in_claims_first()
    {
        var employeeId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, employeeId.ToString()), new Claim(AuthClaimNames.ActivePositionId, positionId.ToString()) };
        var writer = new AuditEventWriter(new Factory(options), new AuditActorContext(), NullLogger<AuditEventWriter>.Instance,
            new FixedAuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));

        await writer.WriteAsync(AuditActions.RequestViewed, "WorkflowRequest", Guid.NewGuid().ToString());

        await using var db = new ApplicationDbContext(options);
        var entry = Assert.Single(db.AuditEntries);
        Assert.Equal(employeeId, entry.EmployeeId);
        Assert.Equal(positionId, entry.ActivePositionId);
    }

    [Fact]
    public void Flatten_keeps_only_the_filters_that_are_set()
    {
        var flat = AuditEventWriter.Flatten(new SampleFilter { Text = "abc", Kind = SampleKind.Second, Blank = " ", Flag = false });

        // Blank text, a false flag, a null id and the computed Count are left out.
        Assert.Equal(["Kind", "Text"], flat.Keys.OrderBy(k => k).ToArray());
        Assert.Equal("abc", ((JsonNode)flat["Text"]!).GetValue<string>());
        Assert.Equal("Second", ((JsonNode)flat["Kind"]!).GetValue<string>());
    }

    [Fact]
    public void Describe_puts_the_fixed_values_before_the_filters()
    {
        var details = AuditEventWriter.Describe(("Permission", "Reports.View"), new SampleFilter { Text = "abc" }, ("RowCount", 7));

        Assert.Equal(["Permission", "RowCount"], details.Keys.Take(2).ToArray());
        Assert.Contains("Text", details.Keys);
        Assert.Equal(7, details["RowCount"]);
    }

    [Fact]
    public async Task A_seen_event_is_recorded_once_per_person_per_window()
    {
        var (_, writer, _) = Create();
        var person = Guid.NewGuid();
        var key = "request:" + Guid.NewGuid();

        Assert.True(await writer.ShouldLogAsync(key, TimeSpan.FromMinutes(5), person));
        Assert.False(await writer.ShouldLogAsync(key, TimeSpan.FromMinutes(5), person));
        Assert.True(await writer.ShouldLogAsync(key, TimeSpan.FromMinutes(5), Guid.NewGuid()));   // someone else saw it too
        Assert.True(await writer.ShouldLogAsync(key, TimeSpan.Zero, person));                      // the window has passed
    }

    [Fact]
    public async Task A_seen_event_needs_a_person()
    {
        var (_, writer, _) = Create();

        Assert.False(await writer.ShouldLogAsync("request:" + Guid.NewGuid(), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task A_failing_database_never_breaks_the_caller()
    {
        var writer = new AuditEventWriter(new ThrowingFactory(), new AuditActorContext(), NullLogger<AuditEventWriter>.Instance);

        await writer.WriteAsync(AuditActions.LoggedOut, AuditEntityTypes.Session, null, null, anonymous: true);
    }

    private static (DbContextOptions<ApplicationDbContext> Options, AuditEventWriter Writer, AuditActorContext Actors) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        var actors = new AuditActorContext();
        return (options, new AuditEventWriter(new Factory(options), actors, NullLogger<AuditEventWriter>.Instance), actors);
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }

    private enum SampleKind { First, Second }

    // A page filter written as a plain class with public fields, like the users page's.
    private sealed class SampleFilter
    {
        public string? Text;
        public string Blank = "";
        public bool Flag;
        public Guid? Id;
        public SampleKind Kind;
        public int Count => 5;
    }

    private sealed class FixedAuthenticationState(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private sealed class ThrowingFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => throw new InvalidOperationException("database is down");
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is down");
    }
}

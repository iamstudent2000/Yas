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

    private sealed class ThrowingFactory : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => throw new InvalidOperationException("database is down");
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("database is down");
    }
}

using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Tests;

/// <summary>The request-list filters and ID search of spec §25.2/§25.3.</summary>
public class WorkflowRequestFilterTests
{
    private sealed record Seed(WorkflowRequest Leave, WorkflowRequest Purchase, WorkflowRequest Rejected, Employee Sara, Employee Ali);

    private static ApplicationDbContext Create(string name) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options);

    private static WorkflowRequest Request(WorkflowTypeCode type, Guid requester, params string[] stepNames) =>
        new(type, requester, Guid.NewGuid(), "{}", stepNames.Select((n, i) => (Guid.NewGuid(), i + 1, n, Guid.NewGuid())).ToList());

    private static async Task<(ApplicationDbContext Db, Seed Data)> SeedAsync()
    {
        var db = Create(Guid.NewGuid().ToString());
        var org = Guid.NewGuid();
        var sara = new Employee("E1", "sara", "سارا احمدی", org, false);
        var ali = new Employee("E2", "ali", "علی رضایی", org, false);
        db.Employees.AddRange(sara, ali);

        var leave = Request(WorkflowTypeCode.Leave, sara.Id, "مدیر مستقیم", "منابع انسانی");
        var purchase = Request(WorkflowTypeCode.Purchase, ali.Id, "مدیر مستقیم", "مالی");
        purchase.Approve(purchase.CurrentStep.Id, Guid.NewGuid(), null); // now at "مالی"
        var rejected = Request(WorkflowTypeCode.Leave, ali.Id, "مدیر مستقیم");
        rejected.Reject(rejected.CurrentStep.Id, Guid.NewGuid(), null);

        db.WorkflowRequests.AddRange(leave, purchase, rejected);
        await db.SaveChangesAsync();
        return (db, new Seed(leave, purchase, rejected, sara, ali));
    }

    private static async Task<List<Guid>> RunAsync(ApplicationDbContext db, WorkflowRequestFilter filter) =>
        await filter.Apply(db.WorkflowRequests.AsNoTracking(), db).Select(x => x.Id).ToListAsync();

    [Fact]
    public async Task An_empty_filter_returns_everything()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;
        var filter = new WorkflowRequestFilter();

        Assert.True(filter.IsEmpty);
        Assert.Equal(3, (await RunAsync(db, filter)).Count);
    }

    [Fact]
    public async Task Filters_by_type_and_status()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;

        var leaves = await RunAsync(db, new WorkflowRequestFilter { Type = WorkflowTypeCode.Leave });
        Assert.Equal(2, leaves.Count);

        var rejected = await RunAsync(db, new WorkflowRequestFilter { Status = WorkflowRequestStatus.Rejected });
        Assert.Equal(new[] { data.Rejected.Id }, rejected);

        var both = await RunAsync(db, new WorkflowRequestFilter { Type = WorkflowTypeCode.Leave, Status = WorkflowRequestStatus.PendingApproval });
        Assert.Equal(new[] { data.Leave.Id }, both);
    }

    [Fact]
    public async Task Filters_by_part_of_the_requesters_name()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;

        var sara = await RunAsync(db, new WorkflowRequestFilter { RequesterName = " سارا " });
        Assert.Equal(new[] { data.Leave.Id }, sara);

        var ali = await RunAsync(db, new WorkflowRequestFilter { RequesterName = "رضایی" });
        Assert.Equal(2, ali.Count);
        Assert.DoesNotContain(data.Leave.Id, ali);
    }

    [Fact]
    public async Task Filters_by_the_step_the_request_is_currently_at()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;

        var atFinance = await RunAsync(db, new WorkflowRequestFilter { StepName = "مالی" });

        Assert.Equal(new[] { data.Purchase.Id }, atFinance);
    }

    [Fact]
    public async Task Finds_a_request_by_its_short_id_with_or_without_the_hash()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;
        var shortId = data.Purchase.Id.ToString("N")[..8];

        Assert.Equal(new[] { data.Purchase.Id }, await RunAsync(db, new WorkflowRequestFilter { IdText = shortId }));
        Assert.Equal(new[] { data.Purchase.Id }, await RunAsync(db, new WorkflowRequestFilter { IdText = "#" + shortId.ToUpperInvariant() }));
        Assert.Equal(new[] { data.Purchase.Id }, await RunAsync(db, new WorkflowRequestFilter { IdText = data.Purchase.Id.ToString() }));
        Assert.Empty(await RunAsync(db, new WorkflowRequestFilter { IdText = "zzzzzzzz" }));
    }

    [Fact]
    public async Task Filters_by_a_date_range_that_includes_both_end_days()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;
        var today = DateTime.UtcNow.Date;

        Assert.Equal(3, (await RunAsync(db, new WorkflowRequestFilter { From = today, To = today })).Count);
        Assert.Empty(await RunAsync(db, new WorkflowRequestFilter { From = today.AddDays(1) }));
        Assert.Empty(await RunAsync(db, new WorkflowRequestFilter { To = today.AddDays(-1) }));
    }

    [Fact]
    public async Task Filters_combine_and_clear_resets_them()
    {
        var (db, data) = await SeedAsync();
        await using var _ = db;
        var filter = new WorkflowRequestFilter { Type = WorkflowTypeCode.Leave, RequesterName = "علی" };

        Assert.Equal(new[] { data.Rejected.Id }, await RunAsync(db, filter));
        Assert.False(filter.IsEmpty);

        filter.Clear();

        Assert.True(filter.IsEmpty);
        Assert.Equal(3, (await RunAsync(db, filter)).Count);
    }
}

using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Tests;

/// <summary>Position deactivation and change protection (spec §3.3, §3.4, §19, §22.2).</summary>
public class PositionLifecycleTests
{
    private static ApplicationDbContext Create() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static WorkflowRequest RequestAt(Guid requesterPosition, params Guid[] approverPositions) =>
        new(WorkflowTypeCode.Purchase, Guid.NewGuid(), requesterPosition, "{}",
            approverPositions.Select((p, i) => (Guid.NewGuid(), i + 1, "مرحله " + (i + 1), p)).ToList());

    [Fact]
    public void A_new_position_is_active_and_can_be_deactivated_and_reactivated()
    {
        var position = new Position("POS-1", "مدیر");
        Assert.True(position.IsActive);
        Assert.Null(position.OrganizationId);

        position.Deactivate();
        Assert.False(position.IsActive);
        position.Activate();
        Assert.True(position.IsActive);
    }

    [Fact]
    public void A_position_can_be_given_a_department()
    {
        var position = new Position("POS-1", "مدیر");
        var organization = Guid.NewGuid();
        position.ChangeOrganization(organization);
        Assert.Equal(organization, position.OrganizationId);
        position.ChangeOrganization(null);
        Assert.Null(position.OrganizationId);
        Assert.Throws<ArgumentException>(() => position.ChangeOrganization(Guid.Empty));
    }

    [Fact]
    public async Task Requests_waiting_on_a_position_block_it()
    {
        await using var db = Create();
        var approver = Guid.NewGuid();
        var request = RequestAt(Guid.NewGuid(), approver);
        db.WorkflowRequests.Add(request);
        await db.SaveChangesAsync();

        var blockers = await PositionChangeGuard.ActiveRequestsTouchingAsync(db, [approver]);

        var blocker = Assert.Single(blockers);
        Assert.Equal(request.Id, blocker.RequestId);
    }

    [Fact]
    public async Task Requests_submitted_from_a_position_block_it()
    {
        await using var db = Create();
        var requesterPosition = Guid.NewGuid();
        db.WorkflowRequests.Add(RequestAt(requesterPosition, Guid.NewGuid()));
        await db.SaveChangesAsync();

        Assert.Single(await PositionChangeGuard.ActiveRequestsTouchingAsync(db, [requesterPosition]));
    }

    [Fact]
    public async Task Closed_requests_and_decided_steps_do_not_block()
    {
        await using var db = Create();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var rejected = RequestAt(Guid.NewGuid(), first);
        rejected.Reject(rejected.CurrentStep.Id, Guid.NewGuid(), "no");
        var progressing = RequestAt(Guid.NewGuid(), first, second);
        progressing.Approve(progressing.CurrentStep.Id, Guid.NewGuid(), null);
        db.WorkflowRequests.AddRange(rejected, progressing);
        await db.SaveChangesAsync();

        // The first position already decided on the open request and the other request is closed.
        Assert.Empty(await PositionChangeGuard.ActiveRequestsTouchingAsync(db, [first]));
        Assert.Single(await PositionChangeGuard.ActiveRequestsTouchingAsync(db, [second]));
    }

    [Fact]
    public async Task No_positions_means_no_blockers()
    {
        await using var db = Create();
        Assert.Empty(await PositionChangeGuard.ActiveRequestsTouchingAsync(db, []));
    }

    [Fact]
    public async Task A_subtree_contains_the_position_and_every_descendant()
    {
        await using var db = Create();
        var root = new Position("R", "ریشه");
        var child = new Position("C", "فرزند", root.Id);
        var grandchild = new Position("G", "نوه", child.Id);
        var other = new Position("O", "دیگر");
        db.Positions.AddRange(root, child, grandchild, other);
        await db.SaveChangesAsync();

        var subtree = await PositionChangeGuard.SubtreeAsync(db, child.Id);

        Assert.Equal(new[] { child.Id, grandchild.Id }.OrderBy(x => x), subtree.OrderBy(x => x));
    }

    [Fact]
    public async Task A_used_position_reports_its_history_and_an_unused_one_does_not()
    {
        await using var db = Create();
        var unused = new Position("U", "بدون سابقه");
        var used = new Position("S", "با سابقه");
        db.Positions.AddRange(unused, used);
        db.WorkflowRequests.Add(RequestAt(Guid.NewGuid(), used.Id));
        await db.SaveChangesAsync();

        Assert.Null(await PositionChangeGuard.HistoricalUsageAsync(db, unused.Id));
        Assert.NotNull(await PositionChangeGuard.HistoricalUsageAsync(db, used.Id));
    }

    [Fact]
    public async Task Workflow_steps_naming_a_position_are_listed()
    {
        await using var db = Create();
        var position = Guid.NewGuid();
        db.WorkflowStepDefinitions.Add(new WorkflowStepDefinition(WorkflowTypeCode.Purchase, 1, "مالی", position));
        await db.SaveChangesAsync();

        var steps = await PositionChangeGuard.WorkflowStepsUsingAsync(db, position);

        Assert.Contains("مالی", Assert.Single(steps));
    }
}

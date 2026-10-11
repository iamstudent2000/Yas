using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Tests;

/// <summary>Return to a chosen earlier step (spec §13.2): steps in between are superseded and must approve again.</summary>
public class WorkflowReturnToSpecificStepTests
{
    private static readonly Guid[] Positions = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    private static WorkflowRequest FourStepRequest() =>
        new(WorkflowTypeCode.Loan, Guid.NewGuid(), Guid.NewGuid(), "{}",
            Enumerable.Range(1, 4).Select(i => (Guid.NewGuid(), i, $"مرحله {i}", Positions[i - 1])).ToList());

    private static WorkflowRequest AtStep(int order)
    {
        var request = FourStepRequest();
        while (request.CurrentStepOrder < order)
            request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        return request;
    }

    private static WorkflowRequestStep Step(WorkflowRequest r, int order) => r.Steps.Single(x => x.Round == r.CurrentRound && x.Order == order);

    [Fact]
    public void Returning_to_the_first_step_supersedes_the_approvals_in_between()
    {
        var request = AtStep(4);
        var approverOfTwo = Step(request, 2).ActedByEmployeeId;

        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 1, "از اول");

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Pending, Step(request, 1).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, Step(request, 2).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, Step(request, 3).Status);
        Assert.Equal(WorkflowStepStatus.ReturnedToSpecificStep, Step(request, 4).Status);
        // The voided approval still says who gave it.
        Assert.Equal(approverOfTwo, Step(request, 2).ActedByEmployeeId);
    }

    [Fact]
    public void Returning_to_a_middle_step_keeps_the_approvals_before_it()
    {
        var request = AtStep(4);
        var first = Step(request, 1).ActedByEmployeeId;

        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 2, null);

        Assert.Equal(2, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Approved, Step(request, 1).Status);
        Assert.Equal(first, Step(request, 1).ActedByEmployeeId);
        Assert.Equal(WorkflowStepStatus.Pending, Step(request, 2).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, Step(request, 3).Status);
    }

    [Fact]
    public void Every_superseded_step_must_approve_again_before_the_request_is_approved()
    {
        var request = AtStep(4);
        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 1, null);

        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        Assert.Equal(2, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Pending, Step(request, 2).Status);
        Assert.Null(Step(request, 2).ActedByEmployeeId);

        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        Assert.Equal(4, request.CurrentStepOrder);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);

        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void The_decision_log_keeps_the_old_approvals_and_records_the_target()
    {
        var request = AtStep(4);
        var returner = Guid.NewGuid();

        request.ReturnToSpecificStep(request.CurrentStep.Id, returner, 2, "  بررسی  ");

        Assert.Equal(4, request.StepDecisions.Count);
        var decision = request.StepDecisions.Single(d => d.Outcome == WorkflowStepStatus.ReturnedToSpecificStep);
        Assert.Equal(returner, decision.ActedByEmployeeId);
        Assert.Equal(4, decision.Order);
        Assert.Equal(2, decision.ReturnTargetOrder);
        Assert.Equal("بررسی", decision.Comment);
        Assert.Equal(3, request.StepDecisions.Count(d => d.Outcome == WorkflowStepStatus.Approved));
    }

    [Fact]
    public void The_target_must_be_an_earlier_step_of_this_round()
    {
        var request = AtStep(3);

        Assert.Throws<InvalidOperationException>(() => request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 3, null));
        Assert.Throws<InvalidOperationException>(() => request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 4, null));
        Assert.Throws<InvalidOperationException>(() => request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 9, null));
        Assert.Throws<InvalidOperationException>(() => request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 0, null));
        Assert.Equal(3, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Pending, request.CurrentStep.Status);
    }

    [Fact]
    public void The_first_step_has_nothing_to_return_to()
    {
        var request = FourStepRequest();

        Assert.Throws<InvalidOperationException>(() => request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 1, null));
    }

    [Fact]
    public void Only_the_current_step_can_return_and_only_while_pending()
    {
        var request = AtStep(3);
        var notCurrent = Step(request, 1).Id;
        Assert.Throws<InvalidOperationException>(() => request.ReturnToSpecificStep(notCurrent, Guid.NewGuid(), 1, null));

        var rejected = AtStep(3);
        rejected.Reject(rejected.CurrentStep.Id, Guid.NewGuid(), null);
        Assert.Throws<InvalidOperationException>(() => rejected.ReturnToSpecificStep(Step(rejected, 3).Id, Guid.NewGuid(), 1, null));
    }

    [Fact]
    public void A_request_returned_to_a_step_can_no_longer_be_cancelled_by_the_requester()
    {
        var request = AtStep(4);
        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 1, null);

        Assert.False(request.CanBeCancelled);
    }

    [Fact]
    public void A_second_return_after_the_first_works_too()
    {
        var request = AtStep(4);
        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 2, null);
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), null);
        Assert.Equal(4, request.CurrentStepOrder);

        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 1, null);

        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(2, request.StepDecisions.Count(d => d.Outcome == WorkflowStepStatus.ReturnedToSpecificStep));
    }

    [Fact]
    public void The_target_steps_position_is_notified_by_the_returned_to_step_rule()
    {
        var request = AtStep(4);
        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 2, null);

        var target = Assert.Single(WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.ReturnedToPreviousStep));

        Assert.False(target.IsRequester);
        Assert.Equal(Positions[1], target.PositionId);
        Assert.Equal(NotificationKind.ReturnedToStep, target.Kind);
    }

    [Fact]
    public async Task A_return_to_a_specific_step_is_saved_with_its_target()
    {
        var name = Guid.NewGuid().ToString();
        Guid id;
        await using (var setup = Create(name))
        {
            var request = AtStep(3);
            setup.WorkflowRequests.Add(request);
            await setup.SaveChangesAsync();
            id = request.Id;
        }

        await using (var act = Create(name))
        {
            var tracked = await act.WorkflowRequests.Include(x => x.Steps).SingleAsync(x => x.Id == id);
            tracked.ReturnToSpecificStep(tracked.CurrentStep.Id, Guid.NewGuid(), 1, "از اول");
            await act.SaveChangesAsync();
        }

        await using var read = Create(name);
        var saved = await read.WorkflowRequests.Include(x => x.Steps).Include(x => x.StepDecisions).SingleAsync(x => x.Id == id);
        Assert.Equal(1, saved.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Superseded, saved.Steps.Single(s => s.Order == 2).Status);
        Assert.Equal(1, saved.StepDecisions.Single(d => d.Outcome == WorkflowStepStatus.ReturnedToSpecificStep).ReturnTargetOrder);
    }

    private static ApplicationDbContext Create(string databaseName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(databaseName).Options);
}

using Xunit;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>Parallel steps (spec §12.2, §12.4, §14): sibling steps sharing one order, ANY/ALL, immediate or wait-for-all reject.</summary>
public class ParallelStepsTests
{
    private static readonly Guid Pa = Guid.NewGuid();
    private static readonly Guid Pb = Guid.NewGuid();
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly Guid Pnext = Guid.NewGuid();
    private static readonly Guid Pfirst = Guid.NewGuid();

    private static WorkflowResolvedStep Step(int order, string name, Guid position, ParallelApprovalMode mode = ParallelApprovalMode.All, ParallelRejectPolicy policy = ParallelRejectPolicy.Immediate) =>
        new(Guid.NewGuid(), order, name, position, mode, policy);

    /// <summary>One parallel group (A, B) at order 1 followed by a single step at order 2.</summary>
    private static WorkflowRequest GroupThenNext(ParallelApprovalMode mode, ParallelRejectPolicy policy) =>
        new(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa, mode, policy), Step(1, "ب", Pb, mode, policy), Step(2, "بعدی", Pnext) });

    private static WorkflowRequestStep At(WorkflowRequest r, Guid position) =>
        r.Steps.Single(x => x.Round == r.CurrentRound && x.ApproverPositionId == position);

    private static void Approve(WorkflowRequest r, Guid position) => r.Approve(At(r, position).Id, Guid.NewGuid(), null);
    private static void Reject(WorkflowRequest r, Guid position) => r.Reject(At(r, position).Id, Guid.NewGuid(), "نه");

    // ----- ALL / ANY -----

    [Fact]
    public void All_mode_waits_for_every_position_before_moving_on()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Assert.Equal(2, request.CurrentSteps.Count);

        Approve(request, Pa);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Pending, At(request, Pb).Status);

        Approve(request, Pb);
        Assert.Equal(2, request.CurrentStepOrder);
        Assert.Equal(Pnext, request.CurrentStep.ApproverPositionId);
    }

    [Fact]
    public void Any_mode_first_approval_moves_on_and_the_others_are_no_longer_needed()
    {
        var request = GroupThenNext(ParallelApprovalMode.Any, ParallelRejectPolicy.Immediate);

        Approve(request, Pb);

        Assert.Equal(2, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Approved, At(request, Pb).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, At(request, Pa).Status);
        // The one who did not answer can no longer act on it.
        Assert.Throws<InvalidOperationException>(() => request.Approve(At(request, Pa).Id, Guid.NewGuid(), null));
    }

    [Fact]
    public void The_last_group_completing_approves_the_request()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa), Step(1, "ب", Pb) });

        Approve(request, Pa);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Approve(request, Pb);
        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
    }

    [Fact]
    public void A_position_cannot_decide_twice_in_the_same_group()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Approve(request, Pa);
        Assert.Throws<InvalidOperationException>(() => request.Approve(At(request, Pa).Id, Guid.NewGuid(), null));
    }

    [Fact]
    public void Every_decision_in_a_group_is_recorded_with_the_position_that_made_it()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Approve(request, Pa);
        Approve(request, Pb);

        var decisions = request.StepDecisions.Where(d => d.Order == 1).ToList();
        Assert.Equal(2, decisions.Count);
        Assert.Equal(new[] { Pa, Pb }.OrderBy(x => x), decisions.Select(d => d.ApproverPositionId).OrderBy(x => x));
    }

    // ----- reject policy -----

    [Fact]
    public void Immediate_reject_closes_the_request_at_once()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);

        Reject(request, Pa);

        Assert.Equal(WorkflowRequestStatus.Rejected, request.Status);
        Assert.Equal(WorkflowStepStatus.Superseded, At(request, Pb).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, request.Steps.Single(s => s.Order == 2).Status);
    }

    [Fact]
    public void Wait_for_all_reject_keeps_the_request_open_until_everyone_answered()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.WaitForAll);

        Reject(request, Pa);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(WorkflowStepStatus.Pending, At(request, Pb).Status);

        Approve(request, Pb);
        Assert.Equal(WorkflowRequestStatus.Rejected, request.Status);
        Assert.Equal(WorkflowStepStatus.Superseded, request.Steps.Single(s => s.Order == 2).Status);
    }

    [Fact]
    public void Wait_for_all_with_everyone_rejecting_rejects_after_the_last_answer()
    {
        var request = GroupThenNext(ParallelApprovalMode.Any, ParallelRejectPolicy.WaitForAll);

        Reject(request, Pa);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Reject(request, Pb);
        Assert.Equal(WorkflowRequestStatus.Rejected, request.Status);
    }

    [Fact]
    public void Wait_for_all_with_any_mode_lets_a_later_approval_complete_the_group()
    {
        var request = GroupThenNext(ParallelApprovalMode.Any, ParallelRejectPolicy.WaitForAll);

        Reject(request, Pa);
        Approve(request, Pb);

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(2, request.CurrentStepOrder);
    }

    // ----- reopen -----

    [Fact]
    public void Reopening_after_a_wait_for_all_rejection_asks_only_the_rejecting_position_again()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.WaitForAll);
        Approve(request, Pa);
        Reject(request, Pb);
        Assert.Equal(WorkflowRequestStatus.Rejected, request.Status);

        request.Reopen(Guid.NewGuid(), "اشتباه");

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(WorkflowStepStatus.Approved, At(request, Pa).Status);
        Assert.Equal(WorkflowStepStatus.Pending, At(request, Pb).Status);
        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.Single(s => s.Order == 2).Status);

        Approve(request, Pb);
        Assert.Equal(2, request.CurrentStepOrder);
    }

    [Fact]
    public void Reopening_after_an_immediate_rejection_restores_the_positions_that_never_answered()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Reject(request, Pa);

        request.Reopen(Guid.NewGuid(), "اشتباه");

        Assert.All(request.Steps.Where(s => s.Round == 1), s => Assert.Equal(WorkflowStepStatus.Pending, s.Status));
    }

    // ----- returns -----

    [Fact]
    public void Returning_to_a_previous_parallel_group_makes_the_whole_group_decide_again()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa), Step(1, "ب", Pb), Step(2, "بعدی", Pnext) });
        Approve(request, Pa);
        Approve(request, Pb);

        request.ReturnToPreviousStep(request.CurrentStep.Id, Guid.NewGuid(), "دوباره");

        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(2, request.CurrentSteps.Count);
        Assert.All(request.CurrentSteps, s => Assert.Equal(WorkflowStepStatus.Pending, s.Status));
    }

    [Fact]
    public void A_group_member_returning_to_an_earlier_step_resets_its_siblings_approvals()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "اول", Pfirst), Step(2, "الف", Pa), Step(2, "ب", Pb) });
        Approve(request, Pfirst);
        Approve(request, Pa);

        request.ReturnToPreviousStep(At(request, Pb).Id, Guid.NewGuid(), "بازنگری");
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Superseded, At(request, Pa).Status);

        Approve(request, Pfirst);
        Assert.Equal(2, request.CurrentStepOrder);
        Assert.All(request.CurrentSteps, s => Assert.Equal(WorkflowStepStatus.Pending, s.Status));
    }

    [Fact]
    public void Return_to_a_specific_parallel_group_is_allowed_once_it_has_approved()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa, ParallelApprovalMode.Any), Step(1, "ب", Pb, ParallelApprovalMode.Any), Step(2, "میانی", Pfirst), Step(3, "آخر", Pnext) });
        Approve(request, Pa);
        Approve(request, Pfirst);

        request.ReturnToSpecificStep(request.CurrentStep.Id, Guid.NewGuid(), 1, "برگردید");

        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(2, request.CurrentSteps.Count);
        Assert.All(request.CurrentSteps, s => Assert.Equal(WorkflowStepStatus.Pending, s.Status));
    }

    [Fact]
    public void CurrentStep_refuses_to_pick_one_of_a_parallel_group()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Assert.Throws<InvalidOperationException>(() => request.CurrentStep);
        Assert.Equal(2, request.CurrentSteps.Count);
    }

    [Fact]
    public void Resubmitting_after_a_return_opens_a_new_round_with_the_group_again()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        request.ReturnToRequester(At(request, Pa).Id, Guid.NewGuid(), "اصلاح");
        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);

        request.Resubmit("{}", new[] { Step(1, "الف", Pa), Step(1, "ب", Pb), Step(2, "بعدی", Pnext) });

        Assert.Equal(2, request.CurrentRound);
        Assert.Equal(2, request.CurrentSteps.Count);
        Assert.Equal(WorkflowStepStatus.Superseded, request.Steps.Single(s => s.Round == 1 && s.ApproverPositionId == Pb).Status);
    }

    // ----- construction -----

    [Fact]
    public void A_group_with_mixed_policies_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowRequest(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa, ParallelApprovalMode.All), Step(1, "ب", Pb, ParallelApprovalMode.Any) }));
    }

    [Fact]
    public void A_group_naming_one_position_twice_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowRequest(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa), Step(1, "ب", Pa) }));
    }

    [Fact]
    public void Cancelling_is_blocked_once_any_member_of_the_group_has_approved()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Assert.True(request.CanBeCancelled);
        Approve(request, Pa);
        Assert.False(request.CanBeCancelled);
    }

    // ----- resolver -----

    private static WorkflowStepDefinition Fixed(int order, string name, Guid position) => new(WorkflowTypeCode.Leave, order, name, position);

    private static readonly Dictionary<Guid, Guid?> NoParents = new();

    [Fact]
    public void Resolver_keeps_sibling_definitions_in_one_shared_order()
    {
        var a = Fixed(2, "الف", Pa);
        var b = Fixed(2, "ب", Pb);
        a.SetParallelPolicy(ParallelApprovalMode.Any, ParallelRejectPolicy.WaitForAll);
        b.SetParallelPolicy(ParallelApprovalMode.Any, ParallelRejectPolicy.WaitForAll);

        var result = WorkflowStepResolver.Resolve([Fixed(1, "اول", Pfirst), a, b, Fixed(3, "آخر", Pnext)], Guid.NewGuid(), NoParents);

        Assert.True(result.Success);
        Assert.Equal(new[] { 1, 2, 2, 3 }, result.Steps.Select(s => s.Order));
        var group = result.Steps.Where(s => s.Order == 2).ToList();
        Assert.All(group, s => Assert.Equal(ParallelApprovalMode.Any, s.Mode));
        Assert.All(group, s => Assert.Equal(ParallelRejectPolicy.WaitForAll, s.RejectPolicy));
    }

    [Fact]
    public void Resolver_numbers_orders_consecutively_around_a_group()
    {
        // Configured orders 1, 4, 4, 9 (gaps from inactive steps) become 1, 2, 2, 3.
        var result = WorkflowStepResolver.Resolve([Fixed(1, "اول", Pfirst), Fixed(4, "الف", Pa), Fixed(4, "ب", Pb), Fixed(9, "آخر", Pnext)], Guid.NewGuid(), NoParents);
        Assert.Equal(new[] { 1, 2, 2, 3 }, result.Steps.Select(s => s.Order));
    }

    [Fact]
    public void Resolver_refuses_an_escalation_inside_a_group()
    {
        var escalation = WorkflowStepDefinition.EscalatingToTreeLevel(WorkflowTypeCode.Leave, 1, "ارجاع", 1);
        var result = WorkflowStepResolver.Resolve([escalation, Fixed(1, "ب", Pb)], Guid.NewGuid(), NoParents);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Kind == WorkflowResolutionErrorKind.ParallelEscalationNotAllowed);
    }

    [Fact]
    public void Resolver_refuses_a_group_whose_members_disagree_on_policy()
    {
        var a = Fixed(1, "الف", Pa);
        var b = Fixed(1, "ب", Pb);
        a.SetParallelPolicy(ParallelApprovalMode.Any, ParallelRejectPolicy.Immediate);

        var result = WorkflowStepResolver.Resolve([a, b], Guid.NewGuid(), NoParents);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Kind == WorkflowResolutionErrorKind.ParallelPolicyMismatch);
    }

    [Fact]
    public void Resolver_refuses_a_group_that_lands_on_one_position_twice()
    {
        // A fixed position and "1 level up" that resolve to the same position for this requester.
        var requester = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var parents = new Dictionary<Guid, Guid?> { [manager] = null, [requester] = manager };
        var byLevel = new WorkflowStepDefinition(WorkflowTypeCode.Leave, 1, "مدیر", 1);

        var result = WorkflowStepResolver.Resolve([byLevel, Fixed(1, "ثابت", manager)], requester, parents);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Kind == WorkflowResolutionErrorKind.ParallelDuplicatePosition);
    }

    [Fact]
    public void Resolver_ignores_an_inactive_member_so_the_rest_is_a_plain_step()
    {
        var a = Fixed(1, "الف", Pa);
        var b = Fixed(1, "ب", Pb);
        b.Deactivate();

        var result = WorkflowStepResolver.Resolve([a, b], Guid.NewGuid(), NoParents);

        Assert.True(result.Success);
        Assert.Single(result.Steps);
    }

    // ----- notifications -----

    [Fact]
    public void Submitting_notifies_every_position_of_the_first_group()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        var targets = WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.Submitted);

        Assert.Equal(new[] { Pa, Pb }.OrderBy(x => x), targets.Select(t => t.PositionId!.Value).OrderBy(x => x));
        Assert.All(targets, t => Assert.Equal(NotificationKind.StepReached, t.Kind));
        Assert.All(targets, t => Assert.NotNull(t.StepDefinitionId));
    }

    [Fact]
    public void A_partial_approval_in_an_all_group_notifies_nobody()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        var before = request.CurrentStepOrder;
        Approve(request, Pa);

        Assert.Empty(WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.StepApproved, before));
    }

    [Fact]
    public void Completing_a_group_notifies_the_next_step()
    {
        var request = GroupThenNext(ParallelApprovalMode.Any, ParallelRejectPolicy.Immediate);
        var before = request.CurrentStepOrder;
        Approve(request, Pa);

        var target = Assert.Single(WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.StepApproved, before));
        Assert.Equal(Pnext, target.PositionId);
    }

    [Fact]
    public void A_wait_for_all_rejection_tells_the_requester_only_when_the_request_really_closes()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.WaitForAll);

        Reject(request, Pa);
        Assert.Empty(WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.Rejected));

        Approve(request, Pb);
        var target = Assert.Single(WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.Rejected));
        Assert.True(target.IsRequester);
        Assert.Equal(NotificationKind.RequestRejected, target.Kind);
    }

    [Fact]
    public void Reopening_notifies_only_the_positions_that_still_have_to_decide()
    {
        var request = GroupThenNext(ParallelApprovalMode.All, ParallelRejectPolicy.WaitForAll);
        Approve(request, Pa);
        Reject(request, Pb);
        request.Reopen(Guid.NewGuid(), "اشتباه");

        var target = Assert.Single(WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.Reopened));
        Assert.Equal(Pb, target.PositionId);
    }
}

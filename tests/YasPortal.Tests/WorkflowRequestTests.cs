using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowRequestTests
{
    private static WorkflowRequest CreateTwoStepRequest(out Guid step1Id, out Guid step2Id)
    {
        var step1DefId = Guid.NewGuid();
        var step2DefId = Guid.NewGuid();
        var request = new WorkflowRequest(
            WorkflowTypeCode.Leave,
            requesterEmployeeId: Guid.NewGuid(),
            requesterPositionId: Guid.NewGuid(),
            fieldValuesJson: "{\"startDate\":\"2026-10-01\"}",
            resolvedSteps: new[]
            {
                (step1DefId, 1, "مدیر مستقیم", Guid.NewGuid()),
                (step2DefId, 2, "مدیر منابع انسانی", Guid.NewGuid()),
            });
        step1Id = request.Steps.First(x => x.Order == 1).Id;
        step2Id = request.Steps.First(x => x.Order == 2).Id;
        return request;
    }

    [Fact]
    public void New_request_starts_pending_on_its_first_step()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(step1Id, request.CurrentStep.Id);
        Assert.All(request.Steps, s => Assert.Equal(WorkflowStepStatus.Pending, s.Status));
    }

    [Fact]
    public void Cannot_create_a_request_with_no_steps()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowRequest(
            WorkflowTypeCode.Helpdesk, Guid.NewGuid(), Guid.NewGuid(), "{}",
            Array.Empty<(Guid, int, string, Guid)>()));
    }

    [Fact]
    public void Approving_the_last_step_completes_the_request()
    {
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();

        request.Approve(step1Id, approver, "تایید شد");
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(2, request.CurrentStepOrder);

        request.Approve(step2Id, approver, null);
        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
        Assert.Equal(WorkflowStepStatus.Approved, request.Steps.First(x => x.Order == 1).Status);
        Assert.Equal(WorkflowStepStatus.Approved, request.Steps.First(x => x.Order == 2).Status);
    }

    [Fact]
    public void Rejecting_a_step_ends_the_request_immediately()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);

        request.Reject(step1Id, Guid.NewGuid(), "رد شد");

        Assert.Equal(WorkflowRequestStatus.Rejected, request.Status);
        Assert.Equal(WorkflowStepStatus.Rejected, request.Steps.First(x => x.Order == 1).Status);
        // The step that was never reached is moot, not "still waiting".
        Assert.Equal(WorkflowStepStatus.Superseded, request.Steps.First(x => x.Order == 2).Status);
    }

    [Fact]
    public void Cancelling_and_force_closing_supersede_unreached_steps_but_keep_decisions()
    {
        var cancelled = CreateTwoStepRequest(out _, out _);
        cancelled.Cancel();
        Assert.All(cancelled.Steps, s => Assert.Equal(WorkflowStepStatus.Superseded, s.Status));

        var forced = CreateTwoStepRequest(out var step1Id, out _);
        forced.Approve(step1Id, Guid.NewGuid(), null);
        forced.ForceClose(Guid.NewGuid(), "دلیل مدیریتی");
        Assert.Equal(WorkflowStepStatus.Approved, forced.Steps.First(x => x.Order == 1).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, forced.Steps.First(x => x.Order == 2).Status);
        Assert.Single(forced.StepDecisions);
    }

    [Fact]
    public void Returning_to_requester_ends_the_request()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);

        request.ReturnToRequester(step1Id, Guid.NewGuid(), "اطلاعات ناقص است");

        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);
        Assert.Equal(WorkflowStepStatus.ReturnedToRequester, request.Steps.First(x => x.Order == 1).Status);
    }

    [Fact]
    public void Returning_to_previous_step_reopens_it_and_moves_current_step_back()
    {
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, null);

        request.ReturnToPreviousStep(step2Id, approver, "لطفا دوباره بررسی شود");

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.ReturnedToPreviousStep, request.Steps.First(x => x.Order == 2).Status);
        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.First(x => x.Order == 1).Status);
        Assert.Null(request.Steps.First(x => x.Order == 1).ActedByEmployeeId);
    }

    [Fact]
    public void Returning_to_previous_step_from_the_first_step_falls_back_to_returning_to_requester()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);

        request.ReturnToPreviousStep(step1Id, Guid.NewGuid(), "بازگشت از اولین مرحله");

        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);
        Assert.Equal(WorkflowStepStatus.ReturnedToRequester, request.Steps.Single(x => x.Order == 1).Status);
    }

    [Fact]
    public void Cannot_act_on_a_step_that_is_not_the_current_step()
    {
        var request = CreateTwoStepRequest(out _, out var step2Id);

        Assert.Throws<InvalidOperationException>(() => request.Approve(step2Id, Guid.NewGuid(), null));
    }

    [Fact]
    public void Cannot_act_twice_on_the_same_request()
    {
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        request.Reject(step1Id, Guid.NewGuid(), null);

        Assert.Throws<InvalidOperationException>(() => request.Approve(step2Id, Guid.NewGuid(), null));
    }

    [Fact]
    public void Pending_request_can_be_cancelled_but_not_twice()
    {
        var request = CreateTwoStepRequest(out _, out _);

        request.Cancel();

        Assert.Equal(WorkflowRequestStatus.Cancelled, request.Status);
        Assert.Throws<InvalidOperationException>(() => request.Cancel());
    }

    [Fact]
    public void A_request_returned_to_the_requester_can_also_be_cancelled()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.ReturnToRequester(step1Id, Guid.NewGuid(), "نیاز به اصلاح دارد");

        request.Cancel();

        Assert.Equal(WorkflowRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void An_already_approved_request_cannot_be_cancelled()
    {
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, null);
        request.Approve(step2Id, approver, null);

        Assert.Throws<InvalidOperationException>(() => request.Cancel());
    }

    [Fact]
    public void Cannot_cancel_once_any_step_has_already_approved_it()
    {
        // Still technically "PendingApproval" (sitting at step 2), but step 1 already
        // approved — cancelling now would silently discard that approval.
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.Approve(step1Id, Guid.NewGuid(), null);

        Assert.False(request.CanBeCancelled);
        Assert.Throws<InvalidOperationException>(() => request.Cancel());
    }

    [Fact]
    public void A_request_returned_to_requester_can_be_cancelled_even_after_an_earlier_approval()
    {
        // Once returned, the requester owns the next move — they may cancel even if
        // an earlier step in this round had already approved.
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, null);
        request.ReturnToRequester(step2Id, approver, "نیاز به اصلاح دارد");

        Assert.True(request.CanBeCancelled);
        request.Cancel();
        Assert.Equal(WorkflowRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void Requester_seen_flag_starts_true_and_flips_on_any_step_action()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        Assert.True(request.RequesterHasSeenLatestUpdate);

        request.Approve(step1Id, Guid.NewGuid(), null);
        Assert.False(request.RequesterHasSeenLatestUpdate);

        request.MarkSeenByRequester();
        Assert.True(request.RequesterHasSeenLatestUpdate);
    }

    [Fact]
    public void Resubmitting_restarts_the_whole_path_as_a_new_round_and_keeps_the_old_round_as_history()
    {
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, "اولین تایید");
        request.ReturnToRequester(step2Id, approver, "لطفا اصلاح شود");

        var newStep1Def = Guid.NewGuid();
        var newStep2Def = Guid.NewGuid();
        request.Resubmit("{\"startDate\":\"2026-11-01\"}", new[]
        {
            (newStep1Def, 1, "مدیر مستقیم", Guid.NewGuid()),
            (newStep2Def, 2, "مدیر منابع انسانی", Guid.NewGuid()),
        });

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(2, request.CurrentRound);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal("{\"startDate\":\"2026-11-01\"}", request.FieldValuesJson);

        // The new round starts completely fresh at step 1, not resuming where it left off.
        var round2Step1 = request.Steps.Single(x => x.Round == 2 && x.Order == 1);
        Assert.Equal(WorkflowStepStatus.Pending, round2Step1.Status);
        Assert.Equal(round2Step1.Id, request.CurrentStep.Id);

        // Round 1 is left exactly as it was — a permanent historical record, not reused.
        Assert.Equal(WorkflowStepStatus.Approved, request.Steps.Single(x => x.Round == 1 && x.Order == 1).Status);
        Assert.Equal(step1Id, request.Steps.Single(x => x.Round == 1 && x.Order == 1).Id);
        Assert.Equal(WorkflowStepStatus.ReturnedToRequester, request.Steps.Single(x => x.Round == 1 && x.Order == 2).Status);
        Assert.Equal(step2Id, request.Steps.Single(x => x.Round == 1 && x.Order == 2).Id);

        // 2 steps from round 1 + 2 fresh steps from round 2.
        Assert.Equal(4, request.Steps.Count);
    }

    [Fact]
    public void Resubmitting_records_a_field_change_for_every_edited_field_and_keeps_the_old_value()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.ReturnToRequester(step1Id, Guid.NewGuid(), null);

        request.Resubmit("{\"startDate\":\"2026-11-01\",\"reason\":\"سفر\"}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        // Ordered by field key so the history is deterministic: "reason" sorts before "startDate".
        var changes = request.FieldChanges.ToList();
        Assert.Equal(2, changes.Count);

        Assert.Equal("reason", changes[0].FieldKey);
        Assert.Null(changes[0].OldValue);
        Assert.Equal("سفر", changes[0].NewValue);

        // The previous value is not lost even though FieldValuesJson now only holds the latest.
        Assert.Equal("startDate", changes[1].FieldKey);
        Assert.Equal("2026-10-01", changes[1].OldValue);
        Assert.Equal("2026-11-01", changes[1].NewValue);

        // Who, from where, and in which round — the §11.3 audit fields.
        Assert.All(changes, c =>
        {
            Assert.Equal(2, c.Round);
            Assert.Equal(request.RequesterEmployeeId, c.ChangedByEmployeeId);
            Assert.Equal(request.RequesterPositionId, c.ChangedByPositionId);
            Assert.Equal(request.Id, c.RequestId);
        });
    }

    [Fact]
    public void Resubmitting_without_changing_anything_records_no_field_changes()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.ReturnToRequester(step1Id, Guid.NewGuid(), null);

        // Same startDate, plus an optional field left blank: a blank and a missing value are the
        // same thing, so neither counts as a change.
        request.Resubmit("{\"startDate\":\"2026-10-01\",\"reason\":\"  \"}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        Assert.Empty(request.FieldChanges);
    }

    [Fact]
    public void Clearing_a_field_while_resubmitting_is_recorded_with_a_null_new_value()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.ReturnToRequester(step1Id, Guid.NewGuid(), null);

        request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        var change = Assert.Single(request.FieldChanges);
        Assert.Equal("startDate", change.FieldKey);
        Assert.Equal("2026-10-01", change.OldValue);
        Assert.Null(change.NewValue);
    }

    [Fact]
    public void Field_history_accumulates_across_rounds_and_is_never_replaced()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.ReturnToRequester(step1Id, Guid.NewGuid(), null);
        request.Resubmit("{\"startDate\":\"2026-11-01\"}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), null);
        request.Resubmit("{\"startDate\":\"2026-12-01\"}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        var changes = request.FieldChanges.OrderBy(c => c.Round).ToList();
        Assert.Equal(2, changes.Count);
        Assert.Equal((2, "2026-10-01", "2026-11-01"), (changes[0].Round, changes[0].OldValue, changes[0].NewValue));
        Assert.Equal((3, "2026-11-01", "2026-12-01"), (changes[1].Round, changes[1].OldValue, changes[1].NewValue));
    }

    [Fact]
    public void Resubmit_marks_unreached_steps_in_the_closed_round_as_superseded()
    {
        // 3 steps; step 1 returns immediately, so steps 2 and 3 were never reached.
        var step1Def = Guid.NewGuid();
        var step2Def = Guid.NewGuid();
        var step3Def = Guid.NewGuid();
        var request = new WorkflowRequest(
            WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[]
            {
                (step1Def, 1, "مدیر مستقیم", Guid.NewGuid()),
                (step2Def, 2, "مالی", Guid.NewGuid()),
                (step3Def, 3, "منابع انسانی", Guid.NewGuid()),
            });
        var step1Id = request.Steps.Single(x => x.Order == 1).Id;
        request.ReturnToRequester(step1Id, Guid.NewGuid(), "اصلاح شود");

        request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()) });

        Assert.Equal(WorkflowStepStatus.ReturnedToRequester, request.Steps.Single(x => x.Round == 1 && x.Order == 1).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, request.Steps.Single(x => x.Round == 1 && x.Order == 2).Status);
        Assert.Equal(WorkflowStepStatus.Superseded, request.Steps.Single(x => x.Round == 1 && x.Order == 3).Status);
    }

    [Fact]
    public void Cannot_resubmit_a_request_that_was_not_returned_to_the_requester()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.Reject(step1Id, Guid.NewGuid(), null);

        Assert.Throws<InvalidOperationException>(() => request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "x", Guid.NewGuid()) }));
    }

    [Fact]
    public void Cancelling_a_second_round_is_independent_of_what_happened_in_the_first_round()
    {
        // Round 1: step 1 approved, then returned at step 2 — round 1 has an Approved step.
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, null);
        request.ReturnToRequester(step2Id, approver, "اصلاح شود");
        request.Resubmit("{}", new[]
        {
            (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()),
            (Guid.NewGuid(), 2, "مدیر منابع انسانی", Guid.NewGuid()),
        });

        // Round 2 hasn't had anything approved yet, so it should be cancellable even though
        // round 1 (now history) did have an approval in it.
        Assert.True(request.CanBeCancelled);
        request.Cancel();
        Assert.Equal(WorkflowRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void Reapproving_after_a_bounce_back_reopens_the_step_ahead_instead_of_leaving_it_stuck()
    {
        // 3 steps: approve 1, approve 2, step 3 sends it back to step 2, step 2 re-approves.
        // Step 3 must become Pending again — not stay stuck in ReturnedToPreviousStep forever.
        var step1Def = Guid.NewGuid();
        var step2Def = Guid.NewGuid();
        var step3Def = Guid.NewGuid();
        var request = new WorkflowRequest(
            WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[]
            {
                (step1Def, 1, "مدیر مستقیم", Guid.NewGuid()),
                (step2Def, 2, "مالی", Guid.NewGuid()),
                (step3Def, 3, "منابع انسانی", Guid.NewGuid()),
            });
        var step1Id = request.Steps.Single(x => x.Order == 1).Id;
        var step2Id = request.Steps.Single(x => x.Order == 2).Id;
        var step3Id = request.Steps.Single(x => x.Order == 3).Id;
        var approver = Guid.NewGuid();

        request.Approve(step1Id, approver, null);
        request.Approve(step2Id, approver, null);
        request.ReturnToPreviousStep(step3Id, approver, "بررسی دوباره لازم است");

        Assert.Equal(2, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.Single(x => x.Order == 2).Status);
        Assert.Equal(WorkflowStepStatus.ReturnedToPreviousStep, request.Steps.Single(x => x.Order == 3).Status);

        // Step 2 re-approves — step 3 must come back to life as Pending, not stay bounced.
        request.Approve(step2Id, approver, "دوباره تایید شد");

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(3, request.CurrentStepOrder);
        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.Single(x => x.Order == 3).Status);
        Assert.Null(request.Steps.Single(x => x.Order == 3).ActedByEmployeeId);
    }
    private static WorkflowRequest CreateRequestFiledBy(Guid requesterId, out Guid step1Id, out Guid step2Id)
    {
        var request = new WorkflowRequest(
            WorkflowTypeCode.Leave,
            requesterEmployeeId: requesterId,
            requesterPositionId: Guid.NewGuid(),
            fieldValuesJson: "{}",
            resolvedSteps: new[]
            {
                (Guid.NewGuid(), 1, "مدیر مستقیم", Guid.NewGuid()),
                (Guid.NewGuid(), 2, "مدیر منابع انسانی", Guid.NewGuid()),
            });
        step1Id = request.Steps.First(x => x.Order == 1).Id;
        step2Id = request.Steps.First(x => x.Order == 2).Id;
        return request;
    }

    [Fact]
    public void Requester_may_act_on_their_own_request_when_they_hold_the_approver_position()
    {
        // Deliberate policy: authority comes from the position, so the requester is not blocked
        // from deciding on their own request. The acting employee is still recorded on the step.
        var requester = Guid.NewGuid();
        var request = CreateRequestFiledBy(requester, out var step1Id, out var step2Id);

        request.Approve(step1Id, requester, null);
        request.Approve(step2Id, requester, null);

        Assert.Equal(WorkflowRequestStatus.Approved, request.Status);
        Assert.Equal(requester, request.Steps.First(x => x.Order == 1).ActedByEmployeeId);
    }

    [Fact]
    public void Revision_increases_on_every_state_changing_action()
    {
        var request = CreateTwoStepRequest(out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        var r0 = request.Revision;

        request.Approve(step1Id, approver, null);
        var r1 = request.Revision;
        request.ReturnToRequester(step2Id, approver, "fix");
        var r2 = request.Revision;
        request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "s", Guid.NewGuid()) });
        var r3 = request.Revision;
        request.Cancel();
        var r4 = request.Revision;

        Assert.True(r0 < r1 && r1 < r2 && r2 < r3 && r3 < r4);
    }

    [Fact]
    public void Rejecting_and_returning_to_previous_step_also_bump_the_revision()
    {
        var reject = CreateTwoStepRequest(out var rejectStep1, out _);
        var before = reject.Revision;
        reject.Reject(rejectStep1, Guid.NewGuid(), null);
        Assert.True(reject.Revision > before);

        var back = CreateTwoStepRequest(out var backStep1, out var backStep2);
        back.Approve(backStep1, Guid.NewGuid(), null);
        before = back.Revision;
        back.ReturnToPreviousStep(backStep2, Guid.NewGuid(), null);
        Assert.True(back.Revision > before);
    }

    [Fact]
    public void Marking_seen_does_not_change_the_revision()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.Approve(step1Id, Guid.NewGuid(), null);
        var before = request.Revision;

        request.MarkSeenByRequester();

        Assert.Equal(before, request.Revision);
    }

    [Fact]
    public void Cancel_fails_closed_when_the_steps_were_not_loaded()
    {
        // Simulates a query without .Include(x => x.Steps): the request is pending but has an
        // empty Steps collection. That must never be read as "no step has approved yet".
        var request = CreateTwoStepRequest(out _, out _);
        request.Steps.Clear();

        Assert.False(request.CanBeCancelled);
        Assert.Throws<InvalidOperationException>(() => request.Cancel());
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
    }

    [Fact]
    public void Submitting_records_a_submitted_action_for_the_requester_in_round_one()
    {
        var requester = Guid.NewGuid();
        var request = CreateRequestFiledBy(requester, out _, out _);

        var action = Assert.Single(request.RequesterActions);
        Assert.Equal(WorkflowRequesterActionKind.Submitted, action.Kind);
        Assert.Equal(requester, action.EmployeeId);
        Assert.Equal(1, action.Round);
        Assert.Equal(request.Id, action.RequestId);
        Assert.True(action.AtUtc <= DateTime.UtcNow && action.AtUtc > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void Resubmitting_appends_a_resubmitted_action_for_the_new_round_and_keeps_the_old_ones()
    {
        var requester = Guid.NewGuid();
        var request = CreateRequestFiledBy(requester, out var step1Id, out _);
        request.ReturnToRequester(step1Id, Guid.NewGuid(), "fix");
        request.Resubmit("{}", new[] { (Guid.NewGuid(), 1, "s", Guid.NewGuid()) });

        var actions = request.RequesterActions.OrderBy(x => x.AtUtc).ToList();
        Assert.Equal(2, actions.Count);
        Assert.Equal(WorkflowRequesterActionKind.Submitted, actions[0].Kind);
        Assert.Equal(1, actions[0].Round);
        Assert.Equal(WorkflowRequesterActionKind.Resubmitted, actions[1].Kind);
        Assert.Equal(2, actions[1].Round);
        Assert.Equal(requester, actions[1].EmployeeId);
    }

    [Fact]
    public void Cancelling_appends_a_cancelled_action_but_a_refused_cancel_records_nothing()
    {
        var requester = Guid.NewGuid();
        var cancelled = CreateRequestFiledBy(requester, out _, out _);
        cancelled.Cancel();
        Assert.Equal(WorkflowRequesterActionKind.Cancelled, cancelled.RequesterActions.Last().Kind);
        Assert.Equal(requester, cancelled.RequesterActions.Last().EmployeeId);
        Assert.Equal(2, cancelled.RequesterActions.Count);

        var approved = CreateRequestFiledBy(requester, out var step1Id, out _);
        approved.Approve(step1Id, Guid.NewGuid(), null);
        Assert.Throws<InvalidOperationException>(() => approved.Cancel());
        Assert.Single(approved.RequesterActions);
    }

    [Fact]
    public void Approver_actions_do_not_add_requester_actions()
    {
        var request = CreateRequestFiledBy(Guid.NewGuid(), out var step1Id, out var step2Id);
        var approver = Guid.NewGuid();
        request.Approve(step1Id, approver, null);
        request.ReturnToPreviousStep(step2Id, approver, null);
        request.Reject(step1Id, approver, null);

        Assert.Single(request.RequesterActions);
    }

    [Fact]
    public void Approving_records_a_step_decision_that_survives_the_step_being_reopened()
    {
        var request = CreateRequestFiledBy(Guid.NewGuid(), out var step1Id, out var step2Id);
        var firstApprover = Guid.NewGuid();
        var secondPosition = request.Steps.First(x => x.Order == 2).ApproverPositionId;

        request.Approve(step1Id, firstApprover, "اولین تایید");
        request.ReturnToPreviousStep(step2Id, Guid.NewGuid(), "برگشت به مرحله قبل");

        // The live step was reset by Reopen() for its second pass...
        var step1 = request.Steps.Single(x => x.Id == step1Id);
        Assert.Equal(WorkflowStepStatus.Pending, step1.Status);
        Assert.Null(step1.ActedByEmployeeId);
        Assert.Null(step1.Comment);

        // ...but the original decision is still there, exactly as it was made.
        var firstDecision = Assert.Single(request.StepDecisions, d => d.StepId == step1Id);
        Assert.Equal(WorkflowStepStatus.Approved, firstDecision.Outcome);
        Assert.Equal(firstApprover, firstDecision.ActedByEmployeeId);
        Assert.Equal("اولین تایید", firstDecision.Comment);
        Assert.Equal(1, firstDecision.Round);
        Assert.Equal(1, firstDecision.Order);
    }

    [Fact]
    public void A_step_acted_on_twice_in_the_same_round_has_two_ordered_decisions()
    {
        var request = CreateRequestFiledBy(Guid.NewGuid(), out var step1Id, out var step2Id);
        var firstApprover = Guid.NewGuid();
        var secondApprover = Guid.NewGuid();

        request.Approve(step1Id, firstApprover, "بار اول");
        request.ReturnToPreviousStep(step2Id, Guid.NewGuid(), null);
        request.Approve(step1Id, secondApprover, "بار دوم");

        var decisions = request.StepDecisions.Where(d => d.StepId == step1Id).OrderBy(d => d.ActedAtUtc).ToList();
        Assert.Equal(2, decisions.Count);
        Assert.Equal(firstApprover, decisions[0].ActedByEmployeeId);
        Assert.Equal("بار اول", decisions[0].Comment);
        Assert.Equal(secondApprover, decisions[1].ActedByEmployeeId);
        Assert.Equal("بار دوم", decisions[1].Comment);
        // The live step only ever shows the most recent pass.
        Assert.Equal(secondApprover, request.Steps.Single(x => x.Id == step1Id).ActedByEmployeeId);
    }

    [Fact]
    public void Reject_and_return_to_requester_are_also_recorded_as_step_decisions()
    {
        var rejected = CreateRequestFiledBy(Guid.NewGuid(), out var rejectStep, out _);
        var rejector = Guid.NewGuid();
        rejected.Reject(rejectStep, rejector, "رد شد");
        var rejectDecision = Assert.Single(rejected.StepDecisions);
        Assert.Equal(WorkflowStepStatus.Rejected, rejectDecision.Outcome);
        Assert.Equal(rejector, rejectDecision.ActedByEmployeeId);

        var returned = CreateRequestFiledBy(Guid.NewGuid(), out var returnStep, out _);
        var returner = Guid.NewGuid();
        returned.ReturnToRequester(returnStep, returner, "اصلاح شود");
        var returnDecision = Assert.Single(returned.StepDecisions);
        Assert.Equal(WorkflowStepStatus.ReturnedToRequester, returnDecision.Outcome);
        Assert.Equal(returner, returnDecision.ActedByEmployeeId);
    }

    [Fact]
    public void Returning_from_the_first_step_to_the_requester_records_the_position_that_did_it()
    {
        var request = CreateRequestFiledBy(Guid.NewGuid(), out var step1Id, out _);
        var step1Position = request.Steps.Single(x => x.Id == step1Id).ApproverPositionId;
        var actor = Guid.NewGuid();

        request.ReturnToPreviousStep(step1Id, actor, "بازگشت از اولین مرحله");

        var decision = Assert.Single(request.StepDecisions);
        Assert.Equal(WorkflowStepStatus.ReturnedToRequester, decision.Outcome);
        Assert.Equal(step1Position, decision.ApproverPositionId);
        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);
    }

    [Fact]
    public void ForceClose_closes_a_request_from_any_non_terminal_state_with_a_reason()
    {
        var pending = CreateTwoStepRequest(out _, out _);
        var admin = Guid.NewGuid();
        pending.ForceClose(admin, "سمت خالی است و راهی برای پیشروی وجود ندارد");

        Assert.Equal(WorkflowRequestStatus.Cancelled, pending.Status);
        Assert.Equal(admin, pending.ForceClosedByEmployeeId);
        Assert.Equal("سمت خالی است و راهی برای پیشروی وجود ندارد", pending.ForceCloseReason);
        Assert.NotNull(pending.ForceClosedAtUtc);
    }

    [Fact]
    public void ForceClose_works_even_after_a_step_has_already_approved_unlike_Cancel()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        request.Approve(step1Id, Guid.NewGuid(), null);
        Assert.False(request.CanBeCancelled);

        request.ForceClose(Guid.NewGuid(), "دلیل مدیریتی");

        Assert.Equal(WorkflowRequestStatus.Cancelled, request.Status);
    }

    [Fact]
    public void ForceClose_requires_a_reason_and_a_real_admin_and_cannot_reclose_a_terminal_request()
    {
        var noReason = CreateTwoStepRequest(out _, out _);
        Assert.Throws<ArgumentException>(() => noReason.ForceClose(Guid.NewGuid(), ""));
        Assert.Throws<ArgumentException>(() => noReason.ForceClose(Guid.NewGuid(), "   "));
        Assert.Throws<ArgumentException>(() => noReason.ForceClose(Guid.Empty, "دلیل"));

        var alreadyClosed = CreateTwoStepRequest(out _, out _);
        alreadyClosed.ForceClose(Guid.NewGuid(), "بار اول");
        Assert.Throws<InvalidOperationException>(() => alreadyClosed.ForceClose(Guid.NewGuid(), "بار دوم"));
    }

    [Fact]
    public void IsTerminal_matches_exactly_the_three_closed_statuses()
    {
        var approved = CreateTwoStepRequest(out var s1, out var s2);
        approved.Approve(s1, Guid.NewGuid(), null);
        approved.Approve(s2, Guid.NewGuid(), null);
        Assert.True(approved.IsTerminal);

        var rejected = CreateTwoStepRequest(out var rs, out _);
        rejected.Reject(rs, Guid.NewGuid(), null);
        Assert.True(rejected.IsTerminal);

        var pending = CreateTwoStepRequest(out _, out _);
        Assert.False(pending.IsTerminal);

        var returned = CreateTwoStepRequest(out var retS, out _);
        returned.ReturnToRequester(retS, Guid.NewGuid(), null);
        Assert.False(returned.IsTerminal);
    }

    [Fact]
    public void Requester_cannot_cancel_after_a_step_approved_even_once_and_was_then_reopened()
    {
        // The exact scenario: step 1 approves, step 2 sends it back to step 1 (reopening it),
        // and now step 1 is Pending again — indistinguishable from "never touched" if you only
        // look at the live step. The requester must not be able to cancel and erase that approval.
        var request = CreateRequestFiledBy(Guid.NewGuid(), out var step1Id, out var step2Id);
        request.Approve(step1Id, Guid.NewGuid(), null);
        request.ReturnToPreviousStep(step2Id, Guid.NewGuid(), null);

        // Confirm the setup actually reproduces the deceptive state before asserting the fix.
        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.Single(x => x.Id == step1Id).Status);

        Assert.False(request.CanBeCancelled);
        Assert.Throws<InvalidOperationException>(() => request.Cancel());
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
    }

    [Fact]
    public void Requester_can_still_cancel_when_a_step_only_ever_returned_and_never_approved()
    {
        // Contrast case: step 2 returns to step 1 WITHOUT step 1 ever having approved anything
        // (falls back to ReturnToRequester when there's no earlier step, or simply nothing has
        // been decided yet on step 1) — this must remain cancellable.
        var request = CreateRequestFiledBy(Guid.NewGuid(), out var step1Id, out _);
        Assert.True(request.CanBeCancelled);

        request.ReturnToPreviousStep(step1Id, Guid.NewGuid(), null); // no earlier step -> falls back to ReturnToRequester
        Assert.Equal(WorkflowRequestStatus.ReturnedToRequester, request.Status);
        Assert.True(request.CanBeCancelled);
    }

}

public class WorkflowStepDefinitionTests
{
    [Fact]
    public void Manager_level_step_rejects_invalid_levels()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowStepDefinition(WorkflowTypeCode.Leave, 1, "مدیر مستقیم", managerLevel: 0));
    }

    [Fact]
    public void Specific_position_step_requires_a_real_position_id()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowStepDefinition(WorkflowTypeCode.Leave, 1, "مدیر منابع انسانی", approverPositionId: Guid.Empty));
    }

    [Fact]
    public void Switching_rule_kind_clears_the_other_kind_data()
    {
        var step = new WorkflowStepDefinition(WorkflowTypeCode.Leave, 1, "مدیر مستقیم", managerLevel: 1);
        Assert.Equal(1, step.ManagerLevel);

        var positionId = Guid.NewGuid();
        step.UseSpecificPosition(positionId);

        Assert.Equal(ApproverRuleKind.SpecificPosition, step.ApproverRuleKind);
        Assert.Equal(positionId, step.ApproverPositionId);
        Assert.Null(step.ManagerLevel);
    }
}

public class WorkflowFieldCatalogTests
{
    [Fact]
    public void Every_workflow_type_has_at_least_one_field()
    {
        foreach (var type in WorkflowFieldCatalog.AllTypes)
            Assert.NotEmpty(WorkflowFieldCatalog.GetFields(type));
    }

    [Fact]
    public void Missing_required_fields_are_reported_by_label()
    {
        var missing = WorkflowFieldCatalog.ValidateRequiredFields(
            WorkflowTypeCode.Purchase,
            new Dictionary<string, string?> { ["itemName"] = "لپ‌تاپ" });

        Assert.Contains("تعداد", missing);
        Assert.Contains("برآورد هزینه (ریال)", missing);
        Assert.Contains("توجیه درخواست", missing);
        Assert.DoesNotContain("کالا/خدمت", missing);
    }

    private static Dictionary<string, string?> ValidLeave() => new()
    {
        ["startDate"] = "2026-10-01",
        ["endDate"] = "2026-10-05",
        // Taken from the catalog itself so this can never drift from the real option text.
        ["leaveType"] = WorkflowFieldCatalog.GetFields(WorkflowTypeCode.Leave).Single(f => f.Key == "leaveType").Options![0],
    };

    [Fact]
    public void Valid_values_produce_no_validation_errors()
    {
        Assert.Empty(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Leave, ValidLeave()));
    }

    [Fact]
    public void Validation_errors_name_the_specific_field_and_reason()
    {
        var values = ValidLeave();
        values["startDate"] = "";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Leave, values));
        Assert.Equal("startDate", error.Key);
        Assert.Equal("تاریخ شروع", error.Label);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Select_values_must_be_one_of_the_fields_options()
    {
        var values = ValidLeave();
        values["leaveType"] = "یک گزینه ساختگی";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Leave, values));
        Assert.Equal("leaveType", error.Key);
    }

    [Fact]
    public void Dates_must_be_real_iso_dates()
    {
        var values = ValidLeave();
        values["endDate"] = "2026-13-45";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Leave, values));
        Assert.Equal("endDate", error.Key);
    }

    [Fact]
    public void Leave_cannot_end_before_it_starts()
    {
        var values = ValidLeave();
        values["startDate"] = "2026-10-10";
        values["endDate"] = "2026-10-05";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Leave, values));
        Assert.Equal("endDate", error.Key);
    }

    [Fact]
    public void Numbers_must_be_numeric_and_not_negative()
    {
        var values = new Dictionary<string, string?>
        {
            ["itemName"] = "لپ‌تاپ",
            ["quantity"] = "abc",
            ["estimatedCost"] = "-5",
            ["justification"] = "نیاز واحد",
        };

        var errors = WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Purchase, values);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Key == "quantity");
        Assert.Contains(errors, e => e.Key == "estimatedCost");
    }

    [Fact]
    public void Keys_outside_the_types_schema_are_rejected()
    {
        var values = ValidLeave();
        values["approvedByManager"] = "true";

        var error = Assert.Single(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Leave, values));
        Assert.Equal("approvedByManager", error.Key);
    }

    [Fact]
    public void Blank_optional_fields_are_valid_and_not_format_checked()
    {
        var values = new Dictionary<string, string?>
        {
            ["amount"] = "1000000",
            ["installments"] = "12",
            ["reason"] = null,
        };

        Assert.Empty(WorkflowFieldCatalog.ValidateValues(WorkflowTypeCode.Loan, values));
    }
}

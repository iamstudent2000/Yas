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
        Assert.Equal(WorkflowStepStatus.Pending, request.Steps.First(x => x.Order == 2).Status);
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
    public void Requester_cannot_approve_reject_or_return_their_own_request()
    {
        var requester = Guid.NewGuid();
        var request = CreateRequestFiledBy(requester, out var step1Id, out _);

        Assert.Throws<InvalidOperationException>(() => request.Approve(step1Id, requester, null));
        Assert.Throws<InvalidOperationException>(() => request.Reject(step1Id, requester, null));
        Assert.Throws<InvalidOperationException>(() => request.ReturnToRequester(step1Id, requester, null));
        Assert.Throws<InvalidOperationException>(() => request.ReturnToPreviousStep(step1Id, requester, null));

        // Nothing changed, and someone else can still decide.
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(WorkflowStepStatus.Pending, request.CurrentStep.Status);
        request.Approve(step1Id, Guid.NewGuid(), null);
        Assert.Equal(2, request.CurrentStepOrder);
    }

    [Fact]
    public void Requester_cannot_approve_a_later_step_of_their_own_request_either()
    {
        var requester = Guid.NewGuid();
        var request = CreateRequestFiledBy(requester, out var step1Id, out var step2Id);
        request.Approve(step1Id, Guid.NewGuid(), null);

        Assert.Throws<InvalidOperationException>(() => request.Approve(step2Id, requester, null));
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
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
}

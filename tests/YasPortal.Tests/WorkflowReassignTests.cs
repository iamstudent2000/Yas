using System.Text.Json;
using Xunit;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

/// <summary>SuperAdmin workflow reassignment (spec §22.1): restart on another workflow, history kept.</summary>
public class WorkflowReassignTests
{
    private static readonly Guid P1 = Guid.NewGuid();
    private static readonly Guid P2 = Guid.NewGuid();
    private static readonly Guid P3 = Guid.NewGuid();
    private static readonly Guid RequesterPosition = Guid.NewGuid();

    private static readonly (Guid, int, string, Guid)[] NewPath =
    {
        (Guid.NewGuid(), 1, "پشتیبانی", P3),
    };

    private static WorkflowRequest PurchaseAtSecondStep()
    {
        var request = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), RequesterPosition,
            JsonSerializer.Serialize(new Dictionary<string, string?> { ["itemName"] = "لپ‌تاپ", ["quantity"] = "2" }),
            new[]
            {
                (Guid.NewGuid(), 1, "مدیر مستقیم", P1),
                (Guid.NewGuid(), 2, "مالی", P2),
            });
        request.Approve(request.CurrentStep.Id, Guid.NewGuid(), "ok");
        return request;
    }

    private static string HelpdeskValues() =>
        JsonSerializer.Serialize(new Dictionary<string, string?> { ["subject"] = "خرابی", ["priority"] = "زیاد", ["description"] = "کار نمی‌کند" });

    [Fact]
    public void Reassigning_restarts_on_the_new_workflows_first_step_in_a_new_round()
    {
        var request = PurchaseAtSecondStep();
        var admin = Guid.NewGuid();

        request.Reassign(admin, WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "گردش‌کار اشتباه بود");

        Assert.Equal(WorkflowTypeCode.Helpdesk, request.WorkflowType);
        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(2, request.CurrentRound);
        Assert.Equal(1, request.CurrentStepOrder);
        Assert.Equal(P3, request.CurrentStep.ApproverPositionId);
        Assert.Equal(WorkflowStepStatus.Pending, request.CurrentStep.Status);
    }

    [Fact]
    public void Earlier_steps_and_approvals_are_superseded_but_the_decisions_stay()
    {
        var request = PurchaseAtSecondStep();

        request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "انتقال");

        Assert.All(request.Steps.Where(s => s.Round == 1), s => Assert.Equal(WorkflowStepStatus.Superseded, s.Status));
        var decision = Assert.Single(request.StepDecisions);
        Assert.Equal(WorkflowStepStatus.Approved, decision.Outcome);
        Assert.Equal(1, decision.Round);
    }

    [Fact]
    public void The_reassignment_is_recorded_with_its_reason_and_the_admin()
    {
        var request = PurchaseAtSecondStep();
        var admin = Guid.NewGuid();

        request.Reassign(admin, WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "  نیاز به پشتیبانی  ");

        var action = request.RequesterActions.Single(a => a.Kind == WorkflowRequesterActionKind.Reassigned);
        Assert.Equal(admin, action.EmployeeId);
        Assert.Equal(2, action.Round);
        Assert.Contains("نیاز به پشتیبانی", action.Reason);
    }

    [Fact]
    public void Every_changed_field_is_recorded_with_the_admin_and_no_position()
    {
        var request = PurchaseAtSecondStep();
        var admin = Guid.NewGuid();

        request.Reassign(admin, WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "انتقال");

        var itemName = request.FieldChanges.Single(c => c.FieldKey == "itemName");
        Assert.Equal("لپ‌تاپ", itemName.OldValue);
        Assert.Null(itemName.NewValue);
        Assert.Equal(admin, itemName.ChangedByEmployeeId);
        Assert.Null(itemName.ChangedByPositionId);
        Assert.Equal("خرابی", request.FieldChanges.Single(c => c.FieldKey == "subject").NewValue);
        Assert.All(request.FieldChanges, c => Assert.Equal(2, c.Round));
    }

    [Fact]
    public void The_requester_and_their_position_stay_as_created()
    {
        var request = PurchaseAtSecondStep();
        var requester = request.RequesterEmployeeId;

        request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "انتقال");

        Assert.Equal(requester, request.RequesterEmployeeId);
        Assert.Equal(RequesterPosition, request.RequesterPositionId);
    }

    [Fact]
    public void A_request_returned_to_the_requester_can_be_reassigned()
    {
        var request = PurchaseAtSecondStep();
        request.ReturnToRequester(request.CurrentStep.Id, Guid.NewGuid(), "اصلاح شود");

        request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "انتقال");

        Assert.Equal(WorkflowRequestStatus.PendingApproval, request.Status);
        Assert.Equal(2, request.CurrentRound);
    }

    [Fact]
    public void A_reason_is_required_and_the_workflow_must_differ()
    {
        var request = PurchaseAtSecondStep();
        Assert.Throws<ArgumentException>(() => request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "  "));
        Assert.Throws<InvalidOperationException>(() => request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Purchase, request.FieldValuesJson, NewPath, "same"));
    }

    [Fact]
    public void A_closed_request_cannot_be_reassigned()
    {
        var request = PurchaseAtSecondStep();
        request.Reject(request.CurrentStep.Id, Guid.NewGuid(), "no");

        Assert.Throws<InvalidOperationException>(() => request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "انتقال"));
    }

    [Fact]
    public void Notifications_go_to_the_requester_and_the_new_first_step()
    {
        var request = PurchaseAtSecondStep();
        request.Reassign(Guid.NewGuid(), WorkflowTypeCode.Helpdesk, HelpdeskValues(), NewPath, "انتقال");

        var targets = WorkflowNotificationPlanner.Plan(request, WorkflowNotificationEvent.Reassigned);

        Assert.Contains(targets, t => t.IsRequester && t.Kind == NotificationKind.RequestReassigned);
        Assert.Contains(targets, t => !t.IsRequester && t.PositionId == P3 && t.Kind == NotificationKind.StepReached);
    }
}

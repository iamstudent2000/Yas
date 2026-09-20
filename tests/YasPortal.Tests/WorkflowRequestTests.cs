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
    public void Cannot_return_to_previous_step_from_the_first_step()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);

        Assert.Throws<InvalidOperationException>(() => request.ReturnToPreviousStep(step1Id, Guid.NewGuid(), null));
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
    public void Requester_seen_flag_starts_true_and_flips_on_any_step_action()
    {
        var request = CreateTwoStepRequest(out var step1Id, out _);
        Assert.True(request.RequesterHasSeenLatestUpdate);

        request.Approve(step1Id, Guid.NewGuid(), null);
        Assert.False(request.RequesterHasSeenLatestUpdate);

        request.MarkSeenByRequester();
        Assert.True(request.RequesterHasSeenLatestUpdate);
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

using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowRequestEndStateTests
{
    private static WorkflowRequest Make() =>
        new(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { new WorkflowResolvedStep(Guid.NewGuid(), 1, "مدیر", Guid.NewGuid()) });

    [Fact]
    public void Pending_request_has_no_end_state()
    {
        Assert.Null(WorkflowRequestEndState.Of(Make()));
    }

    [Fact]
    public void Cancelled_by_requester_is_with_the_requester()
    {
        var r = Make();
        r.Cancel();
        var end = WorkflowRequestEndState.Of(r)!;
        Assert.True(end.WithRequester);
        Assert.Null(end.DeciderEmployeeId);
    }

    [Fact]
    public void Force_closed_names_the_admin()
    {
        var r = Make();
        var admin = Guid.NewGuid();
        r.ForceClose(admin, "گیر کرده");
        var end = WorkflowRequestEndState.Of(r)!;
        Assert.Equal(admin, end.DeciderEmployeeId);
        Assert.False(end.WithRequester);
    }

    [Fact]
    public void Approved_names_the_last_approver()
    {
        var r = Make();
        var approver = Guid.NewGuid();
        r.Approve(r.Steps.Single().Id, approver, null);
        var end = WorkflowRequestEndState.Of(r)!;
        Assert.Equal(approver, end.DeciderEmployeeId);
        Assert.Contains("تایید", end.StageLabel);
    }

    [Fact]
    public void Rejected_names_the_step_and_the_rejecter()
    {
        var r = Make();
        var rejecter = Guid.NewGuid();
        r.Reject(r.Steps.Single().Id, rejecter, "نه");
        var end = WorkflowRequestEndState.Of(r)!;
        Assert.Equal(rejecter, end.DeciderEmployeeId);
        Assert.Contains("مدیر", end.StageLabel);
    }
}

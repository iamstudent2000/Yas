using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowStageSummaryTests
{
    private static readonly Guid Pa = Guid.NewGuid();
    private static readonly Guid Pb = Guid.NewGuid();
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly Guid Pnext = Guid.NewGuid();

    private static WorkflowResolvedStep Step(int order, string name, Guid position, ParallelApprovalMode mode = ParallelApprovalMode.All, ParallelRejectPolicy policy = ParallelRejectPolicy.Immediate) =>
        new(Guid.NewGuid(), order, name, position, mode, policy);

    private static WorkflowRequest Group(ParallelApprovalMode mode, ParallelRejectPolicy policy) =>
        new(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "الف", Pa, mode, policy), Step(1, "ب", Pb, mode, policy), Step(1, "ج", Pc, mode, policy), Step(2, "بعدی", Pnext) });

    private static WorkflowStageSummary Summary(WorkflowRequest r, int order) =>
        WorkflowStageSummary.Of(r.StepsAt(order), isLive: r.Status == WorkflowRequestStatus.PendingApproval && r.CurrentStepOrder == order);

    private static void Do(WorkflowRequest r, Guid position, Action<WorkflowRequest, Guid> act) =>
        act(r, r.Steps.Single(s => s.Round == r.CurrentRound && s.ApproverPositionId == position).Id);

    [Fact]
    public void Live_stage_with_nobody_answered_is_awaiting_and_the_next_is_queued()
    {
        var r = Group(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Assert.Equal(WorkflowStageState.Awaiting, Summary(r, 1).State);
        Assert.Equal(WorkflowStageState.Queued, Summary(r, 2).State);
    }

    [Fact]
    public void All_mode_counts_progress_and_stays_awaiting_until_everyone_approved()
    {
        var r = Group(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Do(r, Pa, (req, id) => req.Approve(id, Guid.NewGuid(), null));

        var s = Summary(r, 1);
        Assert.Equal(WorkflowStageState.Awaiting, s.State);
        Assert.Equal((3, 1, 0, 2), (s.Total, s.Approved, s.Rejected, s.Pending));
    }

    [Fact]
    public void Any_mode_is_approved_after_one_approval_even_though_the_others_never_answered()
    {
        var r = Group(ParallelApprovalMode.Any, ParallelRejectPolicy.Immediate);
        Do(r, Pb, (req, id) => req.Approve(id, Guid.NewGuid(), null));

        Assert.Equal(WorkflowStageState.Approved, Summary(r, 1).State);
        Assert.Equal(WorkflowStageState.Awaiting, Summary(r, 2).State);
    }

    [Fact]
    public void An_immediate_rejection_makes_the_stage_rejected()
    {
        var r = Group(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Do(r, Pa, (req, id) => req.Reject(id, Guid.NewGuid(), "نه"));

        Assert.Equal(WorkflowStageState.Rejected, Summary(r, 1).State);
        Assert.Equal(WorkflowStageState.Skipped, Summary(r, 2).State);
    }

    [Fact]
    public void A_wait_for_all_rejection_keeps_the_stage_awaiting_until_the_rest_answered()
    {
        var r = Group(ParallelApprovalMode.All, ParallelRejectPolicy.WaitForAll);
        Do(r, Pa, (req, id) => req.Reject(id, Guid.NewGuid(), "نه"));

        var s = Summary(r, 1);
        Assert.Equal(WorkflowStageState.Awaiting, s.State);
        Assert.Equal(1, s.Rejected);
    }

    [Fact]
    public void A_member_sending_the_request_back_marks_the_stage_returned()
    {
        var r = Group(ParallelApprovalMode.All, ParallelRejectPolicy.Immediate);
        Do(r, Pa, (req, id) => req.ReturnToRequester(id, Guid.NewGuid(), "اصلاح"));

        Assert.Equal(WorkflowStageState.Returned, Summary(r, 1).State);
    }

    [Fact]
    public void A_single_step_behaves_like_a_group_of_one()
    {
        var r = new WorkflowRequest(WorkflowTypeCode.Leave, Guid.NewGuid(), Guid.NewGuid(), "{}", new[] { (Guid.NewGuid(), 1, "مدیر", Pa) });
        Assert.Equal(WorkflowStageState.Awaiting, Summary(r, 1).State);
        r.Approve(r.CurrentStep.Id, Guid.NewGuid(), null);
        Assert.Equal(WorkflowStageState.Approved, Summary(r, 1).State);
    }

    [Fact]
    public void A_stage_whose_approvals_were_voided_by_a_return_is_skipped_not_approved()
    {
        var r = new WorkflowRequest(WorkflowTypeCode.Purchase, Guid.NewGuid(), Guid.NewGuid(), "{}",
            new[] { Step(1, "اول", Pa), Step(2, "میانی", Pb), Step(3, "آخر", Pc) });
        r.Approve(r.CurrentStep.Id, Guid.NewGuid(), null);
        r.Approve(r.CurrentStep.Id, Guid.NewGuid(), null);
        r.ReturnToSpecificStep(r.CurrentStep.Id, Guid.NewGuid(), 1, "برگردید");

        Assert.Equal(WorkflowStageState.Skipped, Summary(r, 2).State);
    }

    [Fact]
    public void An_empty_stage_is_refused()
    {
        Assert.Throws<ArgumentException>(() => WorkflowStageSummary.Of([], isLive: true));
    }
}

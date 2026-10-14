using Xunit;
using YasPortal.Domain.Workflows;

namespace YasPortal.Tests;

public class WorkflowPathPreviewTests
{
    private static readonly Guid Pa = Guid.NewGuid();
    private static readonly Guid Pb = Guid.NewGuid();
    private static readonly Guid Pc = Guid.NewGuid();
    private static readonly Dictionary<Guid, Guid?> NoParents = new();

    private static WorkflowStepDefinition Fixed(int order, string name, Guid position) => new(WorkflowTypeCode.Leave, order, name, position);

    [Fact]
    public void Lays_the_path_out_stage_by_stage_with_parallel_groups_together()
    {
        var b = Fixed(2, "ب", Pb);
        var c = Fixed(2, "ج", Pc);
        b.SetParallelPolicy(ParallelApprovalMode.Any, ParallelRejectPolicy.WaitForAll);
        c.SetParallelPolicy(ParallelApprovalMode.Any, ParallelRejectPolicy.WaitForAll);

        var preview = WorkflowPathPreview.From(WorkflowStepResolver.Resolve([Fixed(1, "الف", Pa), b, c], Guid.NewGuid(), NoParents));

        Assert.True(preview.Success);
        Assert.Equal(2, preview.Stages.Count);
        Assert.False(preview.Stages[0].IsParallel);
        var group = preview.Stages[1];
        Assert.True(group.IsParallel);
        Assert.Equal(ParallelApprovalMode.Any, group.Mode);
        Assert.Equal(ParallelRejectPolicy.WaitForAll, group.RejectPolicy);
        Assert.Equal(new[] { Pb, Pc }.OrderBy(x => x), group.Approvers.Select(a => a.PositionId).OrderBy(x => x));
    }

    [Fact]
    public void Numbers_stages_consecutively_like_the_real_path()
    {
        var preview = WorkflowPathPreview.From(WorkflowStepResolver.Resolve([Fixed(1, "الف", Pa), Fixed(5, "ب", Pb), Fixed(9, "ج", Pc)], Guid.NewGuid(), NoParents));
        Assert.Equal(new[] { 1, 2, 3 }, preview.Stages.Select(s => s.Number));
    }

    [Fact]
    public void Carries_the_reasons_a_submission_would_be_refused()
    {
        var escalation = WorkflowStepDefinition.EscalatingToTreeLevel(WorkflowTypeCode.Leave, 1, "ارجاع", 1);
        var preview = WorkflowPathPreview.From(WorkflowStepResolver.Resolve([escalation, Fixed(1, "ب", Pb)], Guid.NewGuid(), NoParents));

        Assert.False(preview.Success);
        Assert.Empty(preview.Stages);
        Assert.Contains(preview.Errors, e => e.Kind == WorkflowResolutionErrorKind.ParallelEscalationNotAllowed);
    }

    [Fact]
    public void A_workflow_with_no_active_step_reports_that()
    {
        var preview = WorkflowPathPreview.From(WorkflowStepResolver.Resolve([], Guid.NewGuid(), NoParents));
        Assert.False(preview.Success);
        Assert.Contains(preview.Errors, e => e.Kind == WorkflowResolutionErrorKind.NoActiveSteps);
    }
}

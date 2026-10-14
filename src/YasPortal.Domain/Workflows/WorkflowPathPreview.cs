namespace YasPortal.Domain.Workflows;

/// <summary>One position asked to decide in a <see cref="PreviewStage"/>.</summary>
public sealed record PreviewApprover(string Name, Guid PositionId);

/// <summary>
/// One stage of a resolved approval path: a single approver (ordinary step) or several that decide
/// side by side (parallel group, spec §12.2) under one <see cref="Mode"/> and <see cref="RejectPolicy"/>.
/// </summary>
public sealed record PreviewStage(int Number, ParallelApprovalMode Mode, ParallelRejectPolicy RejectPolicy, IReadOnlyList<PreviewApprover> Approvers)
{
    public bool IsParallel => Approvers.Count > 1;
}

/// <summary>
/// What a workflow's approval path would be for one requester position, laid out stage by stage for
/// the admin "try it" view. Built from a <see cref="WorkflowResolution"/> — the very resolver submissions
/// use — so the preview can never disagree with what a real request would get, including the reasons
/// a submission would be refused.
/// </summary>
public sealed record WorkflowPathPreview(IReadOnlyList<PreviewStage> Stages, IReadOnlyList<WorkflowResolutionError> Errors)
{
    public bool Success => Errors.Count == 0;

    public static WorkflowPathPreview From(WorkflowResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var stages = resolution.Steps
            .GroupBy(x => x.Order)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var members = g.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
                return new PreviewStage(
                    g.Key,
                    members[0].Mode,
                    members[0].RejectPolicy,
                    members.Select(x => new PreviewApprover(x.Name, x.ApproverPositionId)).ToList());
            })
            .ToList();
        return new WorkflowPathPreview(stages, resolution.Errors);
    }
}

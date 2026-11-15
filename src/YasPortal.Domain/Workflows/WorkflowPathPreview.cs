namespace YasPortal.Domain.Workflows;

// Path preview: the admin-only "try the path" view of a workflow. It shows, stage by stage, who would be
// asked to approve
// for a chosen requester position, or why a real submission would be refused. It reuses the real resolver
// so the
// preview always matches what a real request would get. A record = a small read-only bundle of values.
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
// The preview as a whole: the ordered stages plus any problems found (such as a vacant or missing approver
// position).
public sealed record WorkflowPathPreview(IReadOnlyList<PreviewStage> Stages, IReadOnlyList<WorkflowResolutionError> Errors)
{
    public bool Success => Errors.Count == 0;

    // Groups the resolved steps by their order number: steps sharing a number are parallel approvers in the
    // same stage.
    public static WorkflowPathPreview From(WorkflowResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        var stages = resolution.Steps
            .GroupBy(x => x.Order)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                // Approvers inside a stage are listed alphabetically so the display is stable. Mode and
                // reject policy are shared by the stage.
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

using YasPortal.Domain.Organization;

namespace YasPortal.Domain.Workflows;

/// <summary>Why a request's approval path could not be resolved (spec §23, §24).</summary>
public enum WorkflowResolutionErrorKind
{
    /// <summary>The type has no active approval step at all.</summary>
    NoActiveSteps,

    /// <summary>A "N levels up" step needs a manager that the requester's position does not have that far up.</summary>
    ManagerLevelUnavailable,

    /// <summary>An "escalate to tree level N" step, but the requester's position sits above that level of the tree.</summary>
    TreeLevelUnavailable,
}

/// <summary>One reason resolution failed, naming the step it concerns (null for <see cref="WorkflowResolutionErrorKind.NoActiveSteps"/>).</summary>
public sealed record WorkflowResolutionError(WorkflowResolutionErrorKind Kind, string? StepName, int? ManagerLevel = null, int? TreeLevel = null);

public sealed record WorkflowResolvedStep(Guid StepDefinitionId, int Order, string Name, Guid ApproverPositionId);

public sealed record WorkflowResolution(IReadOnlyList<WorkflowResolvedStep> Steps, IReadOnlyList<WorkflowResolutionError> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Turns a type's admin-configured <see cref="WorkflowStepDefinition"/>s into a concrete approval
/// path for one requester position. Deliberately strict: the path the admin configured is the path
/// the request gets, or the submission is refused with every reason listed — never silently
/// shortened. The previous behaviour (clamping an unreachable "level 3" step to a lower manager,
/// and skipping a step when the requester had no manager) let a short hierarchy quietly weaken the
/// approval chain; per spec §23/§24 such cases need an explicit SuperAdmin decision (change the
/// workflow or the hierarchy), not implicit automation. Steps may land on the requester's own
/// position, or on the same position more than once — the path is honoured as configured.
///
/// One definition can produce several steps: an <see cref="ApproverRuleKind.EscalateToTreeLevel"/>
/// definition expands into one step per position between the requester and the target level.
/// </summary>
public static class WorkflowStepResolver
{
    public static WorkflowResolution Resolve(
        IEnumerable<WorkflowStepDefinition> definitions,
        Guid requesterPositionId,
        IReadOnlyDictionary<Guid, Guid?> parentByPosition)
    {
        var active = definitions.Where(x => x.IsActive).OrderBy(x => x.Order).ToList();
        if (active.Count == 0)
            return new WorkflowResolution([], [new WorkflowResolutionError(WorkflowResolutionErrorKind.NoActiveSteps, null)]);

        var errors = new List<WorkflowResolutionError>();
        var resolved = new List<(Guid DefinitionId, string Name, Guid PositionId)>();

        foreach (var definition in active)
        {
            switch (definition.ApproverRuleKind)
            {
                case ApproverRuleKind.SpecificPosition:
                    resolved.Add((definition.Id, definition.Name, definition.ApproverPositionId!.Value));
                    break;

                case ApproverRuleKind.RequesterManagerLevel:
                {
                    // Exact level or nothing: no clamping to "the highest manager that exists".
                    var approver = PositionHierarchy.GetAncestorPositionId(requesterPositionId, definition.ManagerLevel!.Value, parentByPosition);
                    if (approver is Guid positionId)
                        resolved.Add((definition.Id, definition.Name, positionId));
                    else
                        errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ManagerLevelUnavailable, definition.Name, ManagerLevel: definition.ManagerLevel));
                    break;
                }

                case ApproverRuleKind.EscalateToTreeLevel:
                {
                    var chain = ResolveEscalationChain(requesterPositionId, definition.TreeLevel!.Value, parentByPosition);
                    if (chain is null)
                    {
                        errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.TreeLevelUnavailable, definition.Name, TreeLevel: definition.TreeLevel));
                        break;
                    }

                    // Several positions can sit between the requester and the target level, so the
                    // approvals are numbered "(1/3)", "(2/3)", ... to tell them apart in the trail.
                    for (var i = 0; i < chain.Count; i++)
                    {
                        var name = chain.Count == 1 ? definition.Name : $"{definition.Name} ({i + 1}/{chain.Count})";
                        resolved.Add((definition.Id, name, chain[i]));
                    }
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unsupported approver rule kind '{definition.ApproverRuleKind}'.");
            }
        }

        if (errors.Count > 0)
            return new WorkflowResolution([], errors);

        // Orders are 1..N by construction of the admin page, but an inactive step in the middle
        // leaves a gap and an escalation expands into several steps, so the live trail is
        // renumbered consecutively.
        var steps = resolved
            .Select((x, i) => new WorkflowResolvedStep(x.DefinitionId, i + 1, x.Name, x.PositionId))
            .ToList();
        return new WorkflowResolution(steps, []);
    }

    /// <summary>
    /// The positions that must approve, in order, for an escalation to <paramref name="treeLevel"/>
    /// counted from the top of the tree (the top position is level 1): the requester's direct
    /// manager, then each manager above them, ending with the position at that level. Returns null
    /// when the requester sits above the target level, so there is nobody to escalate to.
    /// If the requester is already at exactly the target level the chain is just their own
    /// position — a step may land on the requester's own position.
    /// </summary>
    private static IReadOnlyList<Guid>? ResolveEscalationChain(
        Guid requesterPositionId,
        int treeLevel,
        IReadOnlyDictionary<Guid, Guid?> parentByPosition)
    {
        var ancestors = PositionHierarchy.GetAncestorChain(requesterPositionId, parentByPosition);
        var requesterLevel = ancestors.Count + 1;

        if (treeLevel > requesterLevel)
            return null;
        if (treeLevel == requesterLevel)
            return [requesterPositionId];

        // ancestors[0] is one level above the requester, so the ancestor at the target level is
        // requesterLevel - treeLevel steps up; take everything from the direct manager to there.
        return ancestors.Take(requesterLevel - treeLevel).ToList();
    }
}

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

    /// <summary>An escalation definition shares its order with other definitions; an escalation expands into its own sequence of steps, so it must stand alone.</summary>
    ParallelEscalationNotAllowed,

    /// <summary>Definitions sharing one order disagree on approval mode or reject policy — a parallel group has exactly one of each.</summary>
    ParallelPolicyMismatch,

    /// <summary>Two definitions in one parallel group resolve to the same position for this requester; that position would have to approve twice.</summary>
    ParallelDuplicatePosition,
}

/// <summary>One reason resolution failed, naming the step it concerns (null for <see cref="WorkflowResolutionErrorKind.NoActiveSteps"/>).</summary>
public sealed record WorkflowResolutionError(WorkflowResolutionErrorKind Kind, string? StepName, int? ManagerLevel = null, int? TreeLevel = null);

/// <summary>
/// One approval the request needs. Steps that share an <see cref="Order"/> form a parallel group
/// (spec §12.2) and carry the group's <see cref="Mode"/> and <see cref="RejectPolicy"/>.
/// </summary>
public sealed record WorkflowResolvedStep(
    Guid StepDefinitionId,
    int Order,
    string Name,
    Guid ApproverPositionId,
    ParallelApprovalMode Mode = ParallelApprovalMode.All,
    ParallelRejectPolicy RejectPolicy = ParallelRejectPolicy.Immediate);

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
        var resolved = new List<(Guid DefinitionId, int Slot, string Name, Guid PositionId, ParallelApprovalMode Mode, ParallelRejectPolicy RejectPolicy)>();

        // Definitions sharing one Order are a parallel group (spec §12.2): they resolve to steps that
        // share one live order. A definition alone at its order is an ordinary sequential step.
        // "Slot" counts live orders: one per sequential step, one per whole group, and one per
        // position an escalation expands into. It is renumbered 1..N below.
        var slot = 0;
        foreach (var group in active.GroupBy(x => x.Order).OrderBy(g => g.Key))
        {
            var members = group.ToList();
            if (members.Count > 1)
            {
                slot++;
                ResolveParallelGroup(members, slot, requesterPositionId, parentByPosition, resolved, errors);
                continue;
            }

            var definition = members[0];
            switch (definition.ApproverRuleKind)
            {
                case ApproverRuleKind.SpecificPosition:
                    slot++;
                    resolved.Add((definition.Id, slot, definition.Name, definition.ApproverPositionId!.Value, definition.ApprovalMode, definition.RejectPolicy));
                    break;

                case ApproverRuleKind.RequesterManagerLevel:
                {
                    // Exact level or nothing: no clamping to "the highest manager that exists".
                    var approver = PositionHierarchy.GetAncestorPositionId(requesterPositionId, definition.ManagerLevel!.Value, parentByPosition);
                    if (approver is Guid positionId)
                    {
                        slot++;
                        resolved.Add((definition.Id, slot, definition.Name, positionId, definition.ApprovalMode, definition.RejectPolicy));
                    }
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
                    // Each is its own sequential step.
                    for (var i = 0; i < chain.Count; i++)
                    {
                        slot++;
                        var name = chain.Count == 1 ? definition.Name : $"{definition.Name} ({i + 1}/{chain.Count})";
                        resolved.Add((definition.Id, slot, name, chain[i], definition.ApprovalMode, definition.RejectPolicy));
                    }
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unsupported approver rule kind '{definition.ApproverRuleKind}'.");
            }
        }

        if (errors.Count > 0)
            return new WorkflowResolution([], errors);

        // An inactive step in the middle leaves a gap in the configured orders and an escalation
        // expands into several steps, so the live trail is numbered consecutively (slots already are).
        var steps = resolved
            .Select(x => new WorkflowResolvedStep(x.DefinitionId, x.Slot, x.Name, x.PositionId, x.Mode, x.RejectPolicy))
            .ToList();
        return new WorkflowResolution(steps, []);
    }

    /// <summary>
    /// Resolves one parallel group: every member becomes one step at the same live order. Refused —
    /// with the reason listed, never quietly "fixed" — when a member is an escalation (it expands
    /// into a sequence, which cannot also be parallel), when members disagree on approval mode or
    /// reject policy, or when two members land on the same position for this requester.
    /// </summary>
    private static void ResolveParallelGroup(
        List<WorkflowStepDefinition> members,
        int slot,
        Guid requesterPositionId,
        IReadOnlyDictionary<Guid, Guid?> parentByPosition,
        List<(Guid DefinitionId, int Slot, string Name, Guid PositionId, ParallelApprovalMode Mode, ParallelRejectPolicy RejectPolicy)> resolved,
        List<WorkflowResolutionError> errors)
    {
        var groupName = string.Join(" / ", members.Select(m => m.Name));
        var failed = false;

        if (members.Any(m => m.ApproverRuleKind == ApproverRuleKind.EscalateToTreeLevel))
        {
            errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ParallelEscalationNotAllowed, groupName));
            failed = true;
        }

        var mode = members[0].ApprovalMode;
        var rejectPolicy = members[0].RejectPolicy;
        if (members.Any(m => m.ApprovalMode != mode || m.RejectPolicy != rejectPolicy))
        {
            errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ParallelPolicyMismatch, groupName));
            failed = true;
        }

        if (failed)
            return;

        var positions = new List<(WorkflowStepDefinition Definition, Guid PositionId)>();
        foreach (var member in members)
        {
            if (member.ApproverRuleKind == ApproverRuleKind.SpecificPosition)
            {
                positions.Add((member, member.ApproverPositionId!.Value));
                continue;
            }

            var approver = PositionHierarchy.GetAncestorPositionId(requesterPositionId, member.ManagerLevel!.Value, parentByPosition);
            if (approver is Guid positionId)
                positions.Add((member, positionId));
            else
                errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ManagerLevelUnavailable, member.Name, ManagerLevel: member.ManagerLevel));
        }

        // A member that could not resolve was already reported; don't pile a second error on top.
        if (positions.Count != members.Count)
            return;

        if (positions.Select(x => x.PositionId).Distinct().Count() != positions.Count)
        {
            errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ParallelDuplicatePosition, groupName));
            return;
        }

        foreach (var (definition, positionId) in positions)
            resolved.Add((definition.Id, slot, definition.Name, positionId, mode, rejectPolicy));
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

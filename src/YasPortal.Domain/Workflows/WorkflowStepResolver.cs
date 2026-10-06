namespace YasPortal.Domain.Workflows;

/// <summary>Why a request's approval path could not be resolved (spec §23, §24).</summary>
public enum WorkflowResolutionErrorKind
{
    /// <summary>The type has no active approval step at all.</summary>
    NoActiveSteps,

    /// <summary>A "N levels up" step needs a manager that the requester's position does not have that far up.</summary>
    ManagerLevelUnavailable,

    /// <summary>A step resolves to the requester's own position — they would be approving their own request.</summary>
    ApproverIsRequesterPosition,

    /// <summary>Two steps resolve to the same position, so one holder would approve the same request twice.</summary>
    DuplicateApprover,
}

/// <summary>One reason resolution failed, naming the step it concerns (null for <see cref="WorkflowResolutionErrorKind.NoActiveSteps"/>).</summary>
public sealed record WorkflowResolutionError(WorkflowResolutionErrorKind Kind, string? StepName, int? ManagerLevel = null);

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
/// workflow or the hierarchy), not implicit automation.
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
        var resolved = new List<(WorkflowStepDefinition Definition, Guid PositionId)>();

        foreach (var definition in active)
        {
            Guid? approver = definition.ApproverRuleKind == ApproverRuleKind.SpecificPosition
                ? definition.ApproverPositionId
                // Exact level or nothing: no clamping to "the highest manager that exists".
                : Organization.PositionHierarchy.GetAncestorPositionId(requesterPositionId, definition.ManagerLevel!.Value, parentByPosition);

            if (approver is not Guid positionId)
            {
                errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ManagerLevelUnavailable, definition.Name, definition.ManagerLevel));
                continue;
            }

            if (positionId == requesterPositionId)
            {
                errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.ApproverIsRequesterPosition, definition.Name));
                continue;
            }

            if (resolved.Any(x => x.PositionId == positionId))
            {
                errors.Add(new WorkflowResolutionError(WorkflowResolutionErrorKind.DuplicateApprover, definition.Name));
                continue;
            }

            resolved.Add((definition, positionId));
        }

        if (errors.Count > 0)
            return new WorkflowResolution([], errors);

        // Orders are 1..N by construction of the admin page, but an inactive step in the middle
        // leaves a gap, so the live trail is renumbered consecutively.
        var steps = resolved
            .Select((x, i) => new WorkflowResolvedStep(x.Definition.Id, i + 1, x.Definition.Name, x.PositionId))
            .ToList();
        return new WorkflowResolution(steps, []);
    }
}

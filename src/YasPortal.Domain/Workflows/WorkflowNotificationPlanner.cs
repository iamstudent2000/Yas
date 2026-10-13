using YasPortal.Domain.Notifications;

namespace YasPortal.Domain.Workflows;

/// <summary>What just happened to a request, from the point of view of who needs to be told (spec §17.2).</summary>
public enum WorkflowNotificationEvent
{
    Submitted,
    Resubmitted,
    StepApproved,
    Rejected,
    ReturnedToRequester,
    ReturnedToPreviousStep,
    ForceClosed,
    Reopened,
}

/// <summary>Who should hear about it: the requester, or whoever holds a position.</summary>
/// <param name="StepDefinitionId">For a position target: the workflow step it concerns, so delegation rules of that very step apply (a parallel group has several).</param>
public sealed record NotificationTarget(bool IsRequester, Guid? PositionId, string? StepName, NotificationKind Kind, Guid? StepDefinitionId = null);

/// <summary>
/// Pure rules for "which notifications does this event produce". Looks at the request
/// <b>after</b> the action was applied (so <see cref="WorkflowRequest.Status"/> and the current
/// step are already the new ones) and needs <see cref="WorkflowRequest.Steps"/> loaded.
/// Turning a position into actual employees — and raising the vacancy alert when nobody holds it —
/// is the caller's job, because that needs the database.
/// </summary>
public static class WorkflowNotificationPlanner
{
    /// <param name="stepOrderBefore">
    /// The request's <see cref="WorkflowRequest.CurrentStepOrder"/> from <b>before</b> the action. Only
    /// <see cref="WorkflowNotificationEvent.StepApproved"/> needs it: in a parallel group an approval
    /// that does not complete the group leaves the request where it was, and nobody new should be told
    /// "a step was reached". Omit it and the request is treated as having moved on.
    /// </param>
    public static IReadOnlyList<NotificationTarget> Plan(WorkflowRequest request, WorkflowNotificationEvent evt, int? stepOrderBefore = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (evt)
        {
            case WorkflowNotificationEvent.Submitted:
            case WorkflowNotificationEvent.Resubmitted:
                return AtCurrentStep(request, NotificationKind.StepReached);

            case WorkflowNotificationEvent.StepApproved:
                if (request.Status == WorkflowRequestStatus.Approved)
                    return [ToRequester(NotificationKind.RequestApproved)];
                // Still waiting on the rest of the same parallel group: nothing new to announce.
                if (stepOrderBefore is int before && before == request.CurrentStepOrder)
                    return [];
                return AtCurrentStep(request, NotificationKind.StepReached);

            case WorkflowNotificationEvent.Rejected:
                // A rejection under the wait-for-all policy (§14.3) leaves the request open until the
                // rest of the group has answered; the requester hears about it when it really closes.
                return request.Status == WorkflowRequestStatus.Rejected
                    ? [ToRequester(NotificationKind.RequestRejected)]
                    : [];

            case WorkflowNotificationEvent.ReturnedToRequester:
                return [ToRequester(NotificationKind.RequestReturned)];

            case WorkflowNotificationEvent.ReturnedToPreviousStep:
                // The first step has nothing before it, so the domain falls back to returning
                // the request to the requester — the notification follows what really happened.
                return request.Status == WorkflowRequestStatus.ReturnedToRequester
                    ? [ToRequester(NotificationKind.RequestReturned)]
                    : AtCurrentStep(request, NotificationKind.ReturnedToStep);

            case WorkflowNotificationEvent.Reopened:
                return AtCurrentStep(request, NotificationKind.RequestReopened);

            case WorkflowNotificationEvent.ForceClosed:
                return [ToRequester(NotificationKind.RequestForceClosed)];

            default:
                throw new ArgumentOutOfRangeException(nameof(evt), evt, null);
        }
    }

    private static NotificationTarget ToRequester(NotificationKind kind) => new(true, null, null, kind);

    /// <summary>
    /// One target per position that now has something to decide: the single step of an ordinary step,
    /// or every position of the live parallel group that has not answered yet (after a reopen, the
    /// positions that already approved are not asked again).
    /// </summary>
    private static IReadOnlyList<NotificationTarget> AtCurrentStep(WorkflowRequest request, NotificationKind kind) =>
        request.CurrentSteps
            .Where(step => step.Status == WorkflowStepStatus.Pending)
            .Select(step => new NotificationTarget(false, step.ApproverPositionId, step.Name, kind, step.StepDefinitionId))
            .ToList();
}

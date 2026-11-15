using YasPortal.Domain.Notifications;

namespace YasPortal.Domain.Workflows;

// Notification planner: decides WHO must be told when something happens to a request (spec section 17).
// Example: when a step is approved, the next approver is told that a request is waiting for them; when the
// last step
// is approved, the requester is told it is approved. This file only decides who; sending the messages
// happens elsewhere.
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

// A record = a small read-only bundle of values. A notification target is either the requester or the
// holder(s)
// of a position (positions, not people, hold workflow steps).
/// <summary>Who should hear about it: the requester, or whoever holds a position.</summary>
/// <param name="StepDefinitionId">For a position target: the workflow step it concerns, so delegation rules of that very step apply (a parallel group has several).</param>
public sealed record NotificationTarget(bool IsRequester, Guid? PositionId, string? StepName, NotificationKind Kind, Guid? StepDefinitionId = null);

// Planner = the class holding the rules. Notification kind = the type of message shown to the person.
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
    // Entry point: given the request (already updated) and what just happened, returns the list of
    // people/positions to notify.
    public static IReadOnlyList<NotificationTarget> Plan(WorkflowRequest request, WorkflowNotificationEvent evt, int? stepOrderBefore = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (evt)
        {
            // Submitted: the first approver(s) are told a request reached them.
            case WorkflowNotificationEvent.Submitted:
            {
                var atStep = AtCurrentStep(request, NotificationKind.StepReached);
                // If an administrator created the request on behalf of the requester, the requester gets a
                // message too.
                // An administrator submitted it for the requester: tell the requester too.
                return request.CreatedByEmployeeId is not null
                    ? [ToRequester(NotificationKind.RequestCreatedForYou), .. atStep]
                    : atStep;
            }

            case WorkflowNotificationEvent.Resubmitted:
                return AtCurrentStep(request, NotificationKind.StepReached);

            // Step approved: if that was the last step the requester is told it is approved; otherwise the
            // next approver(s) are told.
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

    // Shortcut for a message addressed to the requester.
    private static NotificationTarget ToRequester(NotificationKind kind) => new(true, null, null, kind);

    /// <summary>
    /// One target per position that now has something to decide: the single step of an ordinary step,
    /// or every position of the live parallel group that has not answered yet (after a reopen, the
    /// positions that already approved are not asked again).
    /// </summary>
    // Everyone still expected to decide at the current stage: the one approver of an ordinary step, or each
    // parallel approver who has not answered.
    private static IReadOnlyList<NotificationTarget> AtCurrentStep(WorkflowRequest request, NotificationKind kind) =>
        request.CurrentSteps
            .Where(step => step.Status == WorkflowStepStatus.Pending)
            .Select(step => new NotificationTarget(false, step.ApproverPositionId, step.Name, kind, step.StepDefinitionId))
            .ToList();
}

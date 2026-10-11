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
public sealed record NotificationTarget(bool IsRequester, Guid? PositionId, string? StepName, NotificationKind Kind);

/// <summary>
/// Pure rules for "which notifications does this event produce". Looks at the request
/// <b>after</b> the action was applied (so <see cref="WorkflowRequest.Status"/> and the current
/// step are already the new ones) and needs <see cref="WorkflowRequest.Steps"/> loaded.
/// Turning a position into actual employees — and raising the vacancy alert when nobody holds it —
/// is the caller's job, because that needs the database.
/// </summary>
public static class WorkflowNotificationPlanner
{
    public static IReadOnlyList<NotificationTarget> Plan(WorkflowRequest request, WorkflowNotificationEvent evt)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (evt)
        {
            case WorkflowNotificationEvent.Submitted:
            case WorkflowNotificationEvent.Resubmitted:
                return [AtCurrentStep(request, NotificationKind.StepReached)];

            case WorkflowNotificationEvent.StepApproved:
                return request.Status == WorkflowRequestStatus.Approved
                    ? [ToRequester(NotificationKind.RequestApproved)]
                    : [AtCurrentStep(request, NotificationKind.StepReached)];

            case WorkflowNotificationEvent.Rejected:
                return [ToRequester(NotificationKind.RequestRejected)];

            case WorkflowNotificationEvent.ReturnedToRequester:
                return [ToRequester(NotificationKind.RequestReturned)];

            case WorkflowNotificationEvent.ReturnedToPreviousStep:
                // The first step has nothing before it, so the domain falls back to returning
                // the request to the requester — the notification follows what really happened.
                return request.Status == WorkflowRequestStatus.ReturnedToRequester
                    ? [ToRequester(NotificationKind.RequestReturned)]
                    : [AtCurrentStep(request, NotificationKind.ReturnedToStep)];

            case WorkflowNotificationEvent.Reopened:
                return [AtCurrentStep(request, NotificationKind.RequestReopened)];

            case WorkflowNotificationEvent.ForceClosed:
                return [ToRequester(NotificationKind.RequestForceClosed)];

            default:
                throw new ArgumentOutOfRangeException(nameof(evt), evt, null);
        }
    }

    private static NotificationTarget ToRequester(NotificationKind kind) => new(true, null, null, kind);

    private static NotificationTarget AtCurrentStep(WorkflowRequest request, NotificationKind kind)
    {
        var step = request.CurrentStep;
        return new NotificationTarget(false, step.ApproverPositionId, step.Name, kind);
    }
}

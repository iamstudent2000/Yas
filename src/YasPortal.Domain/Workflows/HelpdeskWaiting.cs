using System.Text.Json;

namespace YasPortal.Domain.Workflows;

/// <summary>How long a pending helpdesk request has been waiting, and its priority label.</summary>
public sealed record HelpdeskWaitingStatus(string? Priority, DateTime SinceUtc, TimeSpan Waited);

/// <summary>
/// Waiting time of a helpdesk request that is still pending approval: the time since the current round was submitted
/// (a request returned to the requester and sent again starts over, so the time spent with the requester is not counted).
/// There are no target times or overdue states. Anything not pending, and any other request type, has no status.
/// </summary>
public static class HelpdeskWaiting
{
    public const string PriorityKey = "priority";

    public static HelpdeskWaitingStatus? For(WorkflowRequest request, DateTime nowUtc)
    {
        if (request.WorkflowType != WorkflowTypeCode.Helpdesk || request.Status != WorkflowRequestStatus.PendingApproval)
            return null;

        string? priority = null;
        try
        {
            priority = JsonSerializer.Deserialize<Dictionary<string, string?>>(request.FieldValuesJson)?.GetValueOrDefault(PriorityKey)?.Trim();
        }
        catch (JsonException)
        {
            // unreadable values: still show the waiting time, just without a priority
        }

        // The current round's start; the request's own creation time when the requester actions were not loaded.
        var since = request.RequesterActions
            .Where(a => a.Round == request.CurrentRound && a.Kind is WorkflowRequesterActionKind.Submitted or WorkflowRequesterActionKind.Resubmitted)
            .Select(a => (DateTime?)a.AtUtc)
            .Max() ?? request.CreatedAtUtc;

        var waited = nowUtc - since;
        return new HelpdeskWaitingStatus(string.IsNullOrEmpty(priority) ? null : priority, since, waited < TimeSpan.Zero ? TimeSpan.Zero : waited);
    }
}

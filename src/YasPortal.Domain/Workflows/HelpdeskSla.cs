using System.Text.Json;

namespace YasPortal.Domain.Workflows;

/// <summary>Where a pending helpdesk request stands against its priority's target time.</summary>
public sealed record HelpdeskSlaStatus(string Priority, DateTime DueAtUtc, TimeSpan Remaining)
{
    public bool IsOverdue => Remaining < TimeSpan.Zero;
}

/// <summary>
/// Target resolution time per helpdesk priority, in plain clock hours (no working-hours calendar). The clock starts when the
/// current round was submitted, so a request returned to the requester and sent again does not count the time it waited for them.
/// Only a request still pending approval has a status; once it is approved, rejected, returned or closed the target no longer applies.
/// </summary>
public static class HelpdeskSla
{
    public const string PriorityKey = "priority";

    private static readonly IReadOnlyDictionary<string, int> TargetHours = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["کم"] = 72,
        ["متوسط"] = 24,
        ["زیاد"] = 8,
        ["بحرانی"] = 4,
    };

    /// <summary>Target hours for a priority label, or null when the label is not one of the catalog's options.</summary>
    public static int? TargetHoursFor(string? priority) =>
        priority is not null && TargetHours.TryGetValue(priority.Trim(), out var hours) ? hours : null;

    /// <summary>The due time for a priority from a start time, or null for an unknown priority.</summary>
    public static DateTime? DueAtUtc(string? priority, DateTime startUtc) =>
        TargetHoursFor(priority) is int hours ? startUtc.AddHours(hours) : null;

    /// <summary>
    /// The SLA status of a request, or null when it is not a helpdesk request, is not pending, or has no known priority
    /// (requests saved before the priority list existed, or hand-edited data). Reads the priority from the stored field values.
    /// </summary>
    public static HelpdeskSlaStatus? For(WorkflowRequest request, DateTime nowUtc)
    {
        if (request.WorkflowType != WorkflowTypeCode.Helpdesk || request.Status != WorkflowRequestStatus.PendingApproval)
            return null;

        string? priority;
        try
        {
            priority = JsonSerializer.Deserialize<Dictionary<string, string?>>(request.FieldValuesJson)?.GetValueOrDefault(PriorityKey);
        }
        catch (JsonException)
        {
            return null;
        }

        // The current round's start; the request's own creation time when the requester actions were not loaded.
        var start = request.RequesterActions
            .Where(a => a.Round == request.CurrentRound && a.Kind is WorkflowRequesterActionKind.Submitted or WorkflowRequesterActionKind.Resubmitted)
            .Select(a => (DateTime?)a.AtUtc)
            .DefaultIfEmpty(null)
            .Max() ?? request.CreatedAtUtc;

        return DueAtUtc(priority, start) is DateTime due ? new HelpdeskSlaStatus(priority!.Trim(), due, due - nowUtc) : null;
    }
}

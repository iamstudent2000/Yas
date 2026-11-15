using System.Text.Json;

namespace YasPortal.Domain.Workflows;

// Helpdesk report: statistics for the IT/support helpdesk request type, shown on the helpdesk report page.
// A record = a small read-only bundle of values. The rows below are plain data, not database entities, so
// the maths
// can be checked without a database.
/// <summary>One helpdesk request as the report needs it (no entities, so it can be tested without a database).</summary>
public sealed record HelpdeskReportRow(Guid Id, WorkflowRequestStatus Status, DateTime CreatedAtUtc, DateTime WaitingSinceUtc, DateTime? FinishedAtUtc, string FieldValuesJson);

/// <summary>Counts for one category or priority.</summary>
public sealed record HelpdeskGroupStat(string Name, int Total, int Open, int Resolved, TimeSpan? AverageResolution);

/// <summary>An open request, for the "waiting longest" list.</summary>
public sealed record HelpdeskWaitingItem(Guid Id, string Subject, string? Category, string? Priority, TimeSpan Waited);

public sealed record HelpdeskReportResult(
    int Total, int Open, int Resolved, int Rejected, int Other,
    TimeSpan? AverageResolution, double? AverageMinutesSpent,
    IReadOnlyList<HelpdeskGroupStat> ByCategory, IReadOnlyList<HelpdeskGroupStat> ByPriority,
    IReadOnlyList<HelpdeskWaitingItem> LongestWaiting);

/// <summary>
/// Statistics over helpdesk requests. "Resolved" means approved (the handler approves after filling the support-action part);
/// "open" means still pending approval; rejected stays separate; returned and cancelled count as other.
/// Average resolution time is from creation to the last decision, over resolved requests only.
/// </summary>
// Builds the whole report in one pass: totals, averages, per-category and per-priority figures and the
// longest waiters.
public static class HelpdeskReport
{
    public const string NoCategory = "بدون دسته‌بندی";
    public const string NoPriority = "بدون اولویت";

    // Entry point. Takes the helpdesk requests and the current time and returns every figure the report
    // page shows.
    public static HelpdeskReportResult Build(IEnumerable<HelpdeskReportRow> rows, DateTime nowUtc, int longestWaitingCount = 10)
    {
        // Each request stores its form answers as one JSON text; read them into a lookup first (unreadable
        // text gives an empty lookup).
        var items = rows.Select(r => (Row: r, Values: Parse(r.FieldValuesJson))).ToList();

        // Resolved = approved. Only these count towards the resolution-time and time-spent averages.
        var resolved = items.Where(x => x.Row.Status == WorkflowRequestStatus.Approved).ToList();
        // Time spent: the handler records minutes on the support-action part of the form. Missing or non-
        // positive values are ignored.
        var minutes = resolved
            .Select(x => double.TryParse(x.Values.GetValueOrDefault("timeSpentMinutes"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m) ? m : (double?)null)
            .Where(m => m is > 0).Select(m => m!.Value).ToList();

        // Requests per category, biggest category first; requests without a category are grouped under a
        // fallback label.
        var byCategory = items
            .GroupBy(x => Clean(x.Values.GetValueOrDefault("category")) ?? NoCategory)
            .Select(g => Stat(g.Key, g.Select(x => x.Row)))
            .OrderByDescending(s => s.Total).ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        // Requests per priority (highest first, requests without a priority last).
        // Highest priority first, "no priority" last.
        var byPriority = items
            .GroupBy(x => WorkflowPriority.RankOf(x.Values.GetValueOrDefault(WorkflowPriority.FieldKey)))
            .OrderBy(g => g.Key is null).ThenByDescending(g => g.Key)
            .Select(g => Stat(WorkflowPriority.LabelOf(g.Key) ?? NoPriority, g.Select(x => x.Row)))
            .ToList();

        // The oldest still-pending requests, using the time since their current round started (the helpdesk
        // waiting rule).
        var longest = items
            .Where(x => x.Row.Status == WorkflowRequestStatus.PendingApproval)
            .OrderBy(x => x.Row.WaitingSinceUtc)
            .Take(longestWaitingCount)
            .Select(x => new HelpdeskWaitingItem(
                x.Row.Id,
                Clean(x.Values.GetValueOrDefault("subject")) ?? "—",
                Clean(x.Values.GetValueOrDefault("category")),
                WorkflowPriority.LabelOf(WorkflowPriority.RankOf(x.Values.GetValueOrDefault(WorkflowPriority.FieldKey))),
                NonNegative(nowUtc - x.Row.WaitingSinceUtc)))
            .ToList();

        // Headline numbers: total, open (pending approval), resolved, rejected, and other (returned to
        // requester or cancelled).
        return new HelpdeskReportResult(
            items.Count,
            items.Count(x => x.Row.Status == WorkflowRequestStatus.PendingApproval),
            resolved.Count,
            items.Count(x => x.Row.Status == WorkflowRequestStatus.Rejected),
            items.Count(x => x.Row.Status is WorkflowRequestStatus.ReturnedToRequester or WorkflowRequestStatus.Cancelled),
            AverageResolution(resolved.Select(x => x.Row)),
            minutes.Count == 0 ? null : minutes.Average(),
            byCategory, byPriority, longest);
    }

    // Counts for one group (a category or a priority): total, still open, resolved and average time to
    // resolve.
    private static HelpdeskGroupStat Stat(string name, IEnumerable<HelpdeskReportRow> rows)
    {
        var list = rows.ToList();
        var resolved = list.Where(r => r.Status == WorkflowRequestStatus.Approved).ToList();
        return new HelpdeskGroupStat(name, list.Count, list.Count(r => r.Status == WorkflowRequestStatus.PendingApproval), resolved.Count, AverageResolution(resolved));
    }

    // Average time from creation to the last decision, over resolved requests that have a finish time.
    private static TimeSpan? AverageResolution(IEnumerable<HelpdeskReportRow> resolved)
    {
        var spans = resolved.Where(r => r.FinishedAtUtc is not null).Select(r => NonNegative(r.FinishedAtUtc!.Value - r.CreatedAtUtc)).ToList();
        return spans.Count == 0 ? null : TimeSpan.FromTicks((long)spans.Average(s => s.Ticks));
    }

    // Guards against clock differences: a negative time span is shown as zero.
    private static TimeSpan NonNegative(TimeSpan span) => span < TimeSpan.Zero ? TimeSpan.Zero : span;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Reads the stored form answers; if the stored text is damaged the request is still counted, just
    // without answers.
    private static Dictionary<string, string?> Parse(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json ?? "{}") ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

public sealed record StatusCount(WorkflowRequestStatus Status, int Count);
public sealed record LabeledCount(string Label, int Count, string? Sub = null);
public sealed record PositionDelay(string Position, int Decisions, double AverageHours);
public sealed record WaitingRequest(string Number, string Type, string Requester, string Stage, string Holder, double WaitHours);

/// <summary>Counts of one set of requests (mine, my team's, or all) over a time range.</summary>
public sealed class RequestOverview
{
    public int Total { get; init; }
    public List<StatusCount> ByStatus { get; init; } = [];
    public List<LabeledCount> ByType { get; init; } = [];
    /// <summary>The last twelve Persian months, oldest first, regardless of the chosen range.</summary>
    public List<LabeledCount> ByMonth { get; init; } = [];
    /// <summary>Average time from submission to the last approval, over approved requests; null when there are none.</summary>
    public double? AverageHoursToApproval { get; init; }
    public int ApprovedSampleSize { get; init; }
}

/// <summary>System-wide figures for administrators.</summary>
public sealed class AdminInsights
{
    public List<PositionDelay> SlowestPositions { get; init; } = [];
    public List<WaitingRequest> LongestWaiting { get; init; } = [];
    public List<LabeledCount> ByOrganization { get; init; } = [];
    public List<LabeledCount> TopRequesters { get; init; } = [];
    public int ActiveEmployees { get; init; }
    public int ActiveOrganizations { get; init; }
    public int ActivePositions { get; init; }
    public int VacantPositions { get; init; }
}

/// <summary>
/// The numbers behind the dashboard. Counting and grouping happen in SQL; only the figures that need the decision
/// log (waiting times) are computed in memory over a bounded set (<see cref="MaxDecisions"/>).
/// </summary>
public sealed class DashboardService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows)
{
    private const int MaxDecisions = 30000;
    private static readonly string[] PersianMonths = ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];

    /// <summary>The start of a range such as "30" (days), "90", "365", or null for "all".</summary>
    public static DateTime? RangeStartUtc(string range) =>
        int.TryParse(range, NumberStyles.None, CultureInfo.InvariantCulture, out var days) ? DateTime.UtcNow.AddDays(-days) : null;

    public async Task<RequestOverview> GetOverviewAsync(Func<ApplicationDbContext, IQueryable<WorkflowRequest>> scope, DateTime? fromUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var all = scope(db);
        var inRange = fromUtc is DateTime from ? all.Where(x => x.CreatedAtUtc >= from) : all;

        var statuses = await inRange.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var types = await inRange.GroupBy(x => x.WorkflowType).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        // Twelve Persian months ending with the current one.
        var calendar = new PersianCalendar();
        var local = DateTime.Now;
        var currentIndex = calendar.GetYear(local) * 12 + calendar.GetMonth(local) - 1;
        var firstIndex = currentIndex - 11;
        var firstLocal = calendar.ToDateTime(firstIndex / 12, firstIndex % 12 + 1, 1, 0, 0, 0, 0);
        var firstUtc = DateTime.SpecifyKind(firstLocal, DateTimeKind.Local).ToUniversalTime();
        var created = await all.Where(x => x.CreatedAtUtc >= firstUtc).Select(x => x.CreatedAtUtc).ToListAsync(ct);
        var perMonth = created.GroupBy(d => { var l = d.ToLocalTime(); return calendar.GetYear(l) * 12 + calendar.GetMonth(l) - 1; }).ToDictionary(g => g.Key, g => g.Count());
        var months = Enumerable.Range(firstIndex, 12)
            .Select(i => new LabeledCount(PersianMonths[i % 12], perMonth.GetValueOrDefault(i), WorkflowDisplay.ToPersianDigits((i / 12).ToString())))
            .ToList();

        var approved = await inRange.Where(x => x.Status == WorkflowRequestStatus.Approved)
            .Select(x => new { x.CreatedAtUtc, Last = x.StepDecisions.Max(d => (DateTime?)d.ActedAtUtc) })
            .Take(5000).ToListAsync(ct);
        var durations = approved.Where(x => x.Last is not null).Select(x => (x.Last!.Value - x.CreatedAtUtc).TotalHours).Where(h => h >= 0).ToList();

        return new RequestOverview
        {
            Total = statuses.Sum(x => x.Count),
            ByStatus = statuses.Select(x => new StatusCount(x.Key, x.Count)).ToList(),
            ByType = types.Select(x => new LabeledCount(WorkflowFieldCatalog.GetDisplayName(x.Key), x.Count)).OrderByDescending(x => x.Count).ToList(),
            ByMonth = months,
            AverageHoursToApproval = durations.Count > 0 ? durations.Average() : null,
            ApprovedSampleSize = durations.Count,
        };
    }

    public async Task<AdminInsights> GetAdminInsightsAsync(DateTime? fromUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var requests = db.WorkflowRequests.AsNoTracking();
        var inRange = fromUtc is DateTime from ? requests.Where(x => x.CreatedAtUtc >= from) : requests;

        // ---- where requests wait: for every approval, the time since the previous approval of its round (or since submission)
        var decisions = await inRange
            .SelectMany(r => r.StepDecisions.Where(d => d.Outcome == WorkflowStepStatus.Approved)
                .Select(d => new { d.RequestId, d.Round, d.Order, d.ApproverPositionId, d.ActedAtUtc, r.CreatedAtUtc }))
            .Take(MaxDecisions).ToListAsync(ct);
        var waits = new List<(Guid Position, double Hours)>();
        foreach (var group in decisions.GroupBy(d => (d.RequestId, d.Round)))
        {
            var ordered = group.OrderBy(d => d.Order).ThenBy(d => d.ActedAtUtc).ToList();
            foreach (var d in ordered)
            {
                var previous = ordered.Where(p => p.Order < d.Order).Select(p => (DateTime?)p.ActedAtUtc).Max();
                var start = previous ?? (d.Round == 1 ? d.CreatedAtUtc : null);
                if (start is DateTime since && d.ActedAtUtc >= since)
                    waits.Add((d.ApproverPositionId, (d.ActedAtUtc - since).TotalHours));
            }
        }
        var slow = waits.GroupBy(w => w.Position)
            .Select(g => (Position: g.Key, Count: g.Count(), Average: g.Average(w => w.Hours)))
            .OrderByDescending(x => x.Average).Take(8).ToList();
        var slowIds = slow.Select(x => x.Position).ToList();
        var positionNames = await db.Positions.AsNoTracking().Where(p => slowIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        // ---- open requests that have waited longest: the oldest 100 pending ones, ranked by time on their current step
        var pending = await requests.Where(x => x.Status == WorkflowRequestStatus.PendingApproval)
            .OrderBy(x => x.CreatedAtUtc).Take(100)
            .Include(x => x.Steps).Include(x => x.StepDecisions).AsSplitQuery().ToListAsync(ct);
        var waiting = pending.Select(r =>
        {
            var lastApproval = r.StepDecisions.Where(d => d.Round == r.CurrentRound && d.Outcome == WorkflowStepStatus.Approved).Select(d => (DateTime?)d.ActedAtUtc).Max();
            return (Request: r, Since: lastApproval ?? r.CreatedAtUtc);
        }).OrderBy(x => x.Since).Take(5).ToList();
        var holderIds = waiting.SelectMany(x => x.Request.CurrentSteps).Where(s => s.Status == WorkflowStepStatus.Pending).Select(s => s.ApproverPositionId).Distinct().ToList();
        var holders = holderIds.Count == 0 ? [] : await workflows.GetCurrentHolderNamesAsync(holderIds, ct);
        var requesterIds = waiting.Select(x => x.Request.RequesterEmployeeId).Distinct().ToList();
        var requesterNames = await db.Employees.AsNoTracking().Where(e => requesterIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var longest = waiting.Select(x =>
        {
            var steps = x.Request.CurrentSteps.Where(s => s.Status == WorkflowStepStatus.Pending).ToList();
            return new WaitingRequest(
                "#" + x.Request.Id.ToString("N")[..8],
                WorkflowFieldCatalog.GetDisplayName(x.Request.WorkflowType),
                requesterNames.GetValueOrDefault(x.Request.RequesterEmployeeId, "—"),
                string.Join(" / ", steps.Select(s => s.Name)),
                string.Join("، ", steps.Select(s => holders.GetValueOrDefault(s.ApproverPositionId, "سمت خالی"))),
                (DateTime.UtcNow - x.Since).TotalHours);
        }).ToList();

        // ---- organizations and people
        var perOrganization = await inRange.Join(db.Positions.AsNoTracking(), r => r.RequesterPositionId, p => p.Id, (r, p) => p.OrganizationId)
            .GroupBy(o => o).Select(g => new { Organization = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(8).ToListAsync(ct);
        var organizationIds = perOrganization.Where(x => x.Organization != null).Select(x => x.Organization!.Value).ToList();
        var organizationNames = await db.Organizations.AsNoTracking().Where(o => organizationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var perRequester = await inRange.GroupBy(x => x.RequesterEmployeeId).Select(g => new { Employee = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(5).ToListAsync(ct);
        var topIds = perRequester.Select(x => x.Employee).ToList();
        var topNames = await db.Employees.AsNoTracking().Where(e => topIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        var activePositions = await db.Positions.AsNoTracking().CountAsync(p => p.IsActive, ct);
        var vacant = await db.Positions.AsNoTracking().CountAsync(p => p.IsActive && !db.EmployeePositions.Any(ep => ep.PositionId == p.Id && ep.EndedAt == null), ct);

        return new AdminInsights
        {
            SlowestPositions = slow.Select(x => new PositionDelay(positionNames.GetValueOrDefault(x.Position, "—"), x.Count, x.Average)).ToList(),
            LongestWaiting = longest,
            ByOrganization = perOrganization.Select(x => new LabeledCount(x.Organization is Guid id ? organizationNames.GetValueOrDefault(id, "—") : "بدون سازمان", x.Count)).ToList(),
            TopRequesters = perRequester.Select(x => new LabeledCount(topNames.GetValueOrDefault(x.Employee, "—"), x.Count)).ToList(),
            ActiveEmployees = await db.Employees.AsNoTracking().CountAsync(e => e.IsActive, ct),
            ActiveOrganizations = await db.Organizations.AsNoTracking().CountAsync(o => o.IsActive, ct),
            ActivePositions = activePositions,
            VacantPositions = vacant,
        };
    }

    /// <summary>"۳ روز و ۴ ساعت", "۵ ساعت و ۱۰ دقیقه" or "۲۵ دقیقه", in Persian digits.</summary>
    public static string Duration(double hours)
    {
        var span = TimeSpan.FromHours(Math.Max(0, hours));
        var text = span.TotalDays >= 1 ? $"{(int)span.TotalDays} روز و {span.Hours} ساعت"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} ساعت و {span.Minutes} دقیقه"
            : $"{(int)span.TotalMinutes} دقیقه";
        return WorkflowDisplay.ToPersianDigits(text);
    }
}

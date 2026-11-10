using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>
/// Sends the "waiting too long" reminders: for every employee who set their own number of hours, one in-app notification for each
/// request waiting for their decision (at a position they hold, or one delegated to them) for at least that long. A request is
/// reminded about once per time it reaches a step; people who chose "repeat" get it again every threshold until it is handled. At most <see cref="MaxPerEmployeePerRun"/> per person per run, oldest first; the rest follow on the next run.
/// </summary>
public sealed class WaitingReminderService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows)
{
    public const int MaxPerEmployeePerRun = 20;

    /// <summary>Creates the due reminders and returns how many were created.</summary>
    public async Task<int> RunAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var settings = await db.WaitingReminderSettings.AsNoTracking().ToListAsync(ct);
        if (settings.Count == 0)
            return 0;

        var settingIds = settings.Select(s => s.EmployeeId).ToList();
        // Active, non-admin employees only: administrators have no inbox.
        var eligible = (await db.Employees.AsNoTracking().Where(e => settingIds.Contains(e.Id) && e.IsActive && !e.IsAdmin).Select(e => e.Id).ToListAsync(ct)).ToHashSet();

        var created = 0;
        foreach (var setting in settings.Where(s => eligible.Contains(s.EmployeeId)))
        {
            var positions = await db.EmployeePositions.AsNoTracking()
                .Where(p => p.EmployeeId == setting.EmployeeId && p.EndedAt == null).Select(p => p.PositionId).ToListAsync(ct);
            var delegations = await workflows.GetActiveDelegationsAsync(setting.EmployeeId, DateTime.Now, ct);

            var requests = new Dictionary<Guid, WorkflowRequest>();
            foreach (var position in positions.Cast<Guid?>().DefaultIfEmpty(null))
            {
                if (position is null && delegations.Count == 0)
                    continue;
                var found = await WorkflowService.PendingFor(db, position, delegations).AsNoTracking()
                    .Include(x => x.RequesterActions).Include(x => x.StepDecisions).ToListAsync(ct);
                foreach (var request in found)
                    requests[request.Id] = request;
            }

            var due = requests.Values
                .Select(r => (Request: r, Since: WaitingReminders.WaitingSince(r)))
                .Where(x => WaitingReminders.IsDue(x.Since, setting.AfterHours, nowUtc))
                .OrderBy(x => x.Since)
                .ToList();
            if (due.Count == 0)
                continue;

            var dueIds = due.Select(x => x.Request.Id).ToList();
            var earlier = await db.Notifications.AsNoTracking()
                .Where(n => n.Kind == NotificationKind.WaitingReminder && n.RecipientEmployeeId == setting.EmployeeId && n.RequestId != null && dueIds.Contains(n.RequestId.Value))
                .Select(n => new { n.RequestId, n.CreatedAtUtc }).ToListAsync(ct);

            var toSend = due
                .Where(x => WaitingReminders.ShouldSend(x.Since,
                    earlier.Where(n => n.RequestId == x.Request.Id).Select(n => (DateTime?)n.CreatedAtUtc).Max(),
                    setting.AfterHours, setting.Repeat, nowUtc))
                .Take(MaxPerEmployeePerRun)
                .ToList();
            if (toSend.Count == 0)
                continue;

            var requesterIds = toSend.Select(x => x.Request.RequesterEmployeeId).Distinct().ToList();
            var names = await db.Employees.AsNoTracking().Where(e => requesterIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

            foreach (var (request, _) in toSend)
            {
                var typeName = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType);
                var requester = names.GetValueOrDefault(request.RequesterEmployeeId) ?? "—";
                db.Notifications.Add(new Notification(setting.EmployeeId, NotificationKind.WaitingReminder, request.Id, typeName,
                    $"{typeName} #{request.Id.ToString("N")[..8]} از طرف {requester} بیش از {HoursText(setting.AfterHours)} است که منتظر تصمیم شماست.",
                    "/requests/inbox"));
                created++;
            }
        }

        if (created > 0)
            await db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>«۱۲ ساعت» or «۲ روز» for a whole number of days.</summary>
    public static string HoursText(int hours) =>
        WorkflowDisplay.ToPersianDigits(hours >= 24 && hours % 24 == 0 ? $"{hours / 24} روز" : $"{hours} ساعت");
}

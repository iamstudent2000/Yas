using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Delegations;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>In-app notifications (spec §17): creating them as part of a workflow action, and reading/marking them for the recipient.</summary>
public sealed class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory, IPermissionChecker permissions)
{
    /// <summary>
    /// Adds the notifications an event produces to <paramref name="db"/> <b>without saving</b>, so they
    /// are written in the same transaction as the action itself — either both happen or neither.
    /// The request must be the tracked instance the action was applied to, with its steps loaded.
    /// A target position nobody holds produces a vacancy alert to every active SuperAdmin (§5.4).
    /// </summary>
    public async Task StageAsync(ApplicationDbContext db, WorkflowRequest request, WorkflowNotificationEvent evt, CancellationToken ct = default) =>
        await StageAsync(db, request, evt, null, ct);

    /// <param name="stepOrderBefore">The request's current step order before the action — see <see cref="WorkflowNotificationPlanner.Plan"/>.</param>
    public async Task StageAsync(ApplicationDbContext db, WorkflowRequest request, WorkflowNotificationEvent evt, int? stepOrderBefore, CancellationToken ct = default)
    {
        var typeName = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType);
        var shortId = request.Id.ToString("N")[..8];
        var requestLabel = $"{typeName} #{shortId}";

        foreach (var target in WorkflowNotificationPlanner.Plan(request, evt, stepOrderBefore))
        {
            if (target.IsRequester)
            {
                db.Notifications.Add(new Notification(request.RequesterEmployeeId, target.Kind, request.Id, typeName,
                    RequesterMessage(target.Kind, requestLabel), "/requests/mine"));
                continue;
            }

            var positionId = target.PositionId!.Value;
            var holders = await HoldersOfAsync(db, positionId, ct);
            // Delegates covering the position today hear about it too (spec §15) — otherwise a holder
            // on leave would leave the delegate waiting for work they are never told about.
            var delegates = (await DelegatesOfAsync(db, positionId, target.StepDefinitionId!.Value, ct)).Except(holders).ToList();
            if (holders.Count > 0 || delegates.Count > 0)
            {
                var message = target.Kind switch
                {
                    NotificationKind.ReturnedToStep => $"درخواست {requestLabel} به مرحله «{target.StepName}» بازگردانده شد و دوباره منتظر تصمیم شماست.",
                    NotificationKind.RequestReopened => $"درخواست ردشده {requestLabel} بازگشایی شد و در مرحله «{target.StepName}» دوباره منتظر تصمیم شماست.",
                    _ => $"درخواست {requestLabel} به مرحله «{target.StepName}» رسید و منتظر تصمیم شماست.",
                };
                foreach (var holder in holders)
                    db.Notifications.Add(new Notification(holder, target.Kind, request.Id, typeName, message, "/requests/inbox"));
                foreach (var delegateId in delegates)
                    db.Notifications.Add(new Notification(delegateId, target.Kind, request.Id, typeName, message + " (به‌عنوان نماینده)", "/requests/inbox"));
                continue;
            }

            var positionName = await db.Positions.AsNoTracking().Where(x => x.Id == positionId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "نامشخص";
            var admins = await db.Employees.AsNoTracking().Where(x => x.IsAdmin && x.IsActive).Select(x => x.Id).ToListAsync(ct);
            foreach (var admin in admins)
                db.Notifications.Add(new Notification(admin, NotificationKind.VacantPosition, request.Id, typeName,
                    $"سمت «{positionName}» خالی است؛ درخواست {requestLabel} در مرحله «{target.StepName}» نمی‌تواند پیش برود.",
                    "/admin/active-requests"));
        }
    }

    /// <summary>Tells the delegate that authority over a position was handed to them (spec §17.2). Added to <paramref name="db"/> without saving.</summary>
    public async Task StageDelegationReceivedAsync(ApplicationDbContext db, PositionDelegation delegation, CancellationToken ct = default)
    {
        var positionName = await db.Positions.AsNoTracking().Where(x => x.Id == delegation.PositionId).Select(x => x.Name).SingleOrDefaultAsync(ct) ?? "نامشخص";
        foreach (var holder in await HoldersOfAsync(db, delegation.DelegatePositionId, ct))
            db.Notifications.Add(new Notification(holder, NotificationKind.DelegationReceived, null, "تفویض اختیار",
                $"اختیار سمت «{positionName}» از {WorkflowDisplay.ToPersianDate(delegation.StartDate)} تا {WorkflowDisplay.ToPersianDate(delegation.EndDate)} به سمت شما تفویض شد.",
                "/requests/inbox"));
    }

    /// <summary>Delegates whose delegation covers the position today and the step: active employees, in-effect dates, holder still in place (admin arrangements excepted), step allowing delegation (admin arrangements excepted).</summary>
    private static async Task<List<Guid>> DelegatesOfAsync(ApplicationDbContext db, Guid positionId, Guid stepDefinitionId, CancellationToken ct)
    {
        var today = DateTime.Now.Date;
        return await db.PositionDelegations.AsNoTracking()
            .Where(d => d.PositionId == positionId
                        && d.RevokedAtUtc == null
                        && d.StartDate <= today && d.EndDate >= today
                        && (d.ArrangedByAdmin
                            || (db.EmployeePositions.Any(ep => ep.EmployeeId == d.DelegatorEmployeeId && ep.PositionId == d.PositionId && ep.EndedAt == null)
                                && db.WorkflowStepDefinitions.Any(sd => sd.Id == stepDefinitionId && sd.AllowDelegation)))
                        )
            .Join(db.EmployeePositions.Where(ep => ep.EndedAt == null), d => d.DelegatePositionId, ep => ep.PositionId, (d, ep) => ep.EmployeeId)
            .Where(id => db.Employees.Any(e => e.Id == id && e.IsActive))
            .Distinct()
            .ToListAsync(ct);
    }

    private static string RequesterMessage(NotificationKind kind, string requestLabel) => kind switch
    {
        NotificationKind.RequestApproved => $"درخواست {requestLabel} تایید نهایی شد.",
        NotificationKind.RequestRejected => $"درخواست {requestLabel} رد شد.",
        NotificationKind.RequestReturned => $"درخواست {requestLabel} به شما بازگردانده شد؛ پس از اصلاح دوباره ارسال کنید.",
        NotificationKind.RequestForceClosed => $"درخواست {requestLabel} توسط مدیر سامانه بسته شد.",
        NotificationKind.RequestCreatedForYou => $"مدیر سامانه درخواست {requestLabel} را برای شما ثبت کرد.",
        _ => $"وضعیت درخواست {requestLabel} تغییر کرد.",
    };

    /// <summary>Employees who currently, actively hold the position (inactive employees count as vacant, §4.3).</summary>
    private static async Task<List<Guid>> HoldersOfAsync(ApplicationDbContext db, Guid positionId, CancellationToken ct) =>
        await db.EmployeePositions.AsNoTracking()
            .Where(x => x.PositionId == positionId && x.EndedAt == null)
            .Join(db.Employees.AsNoTracking().Where(e => e.IsActive), ep => ep.EmployeeId, e => e.Id, (ep, e) => e.Id)
            .ToListAsync(ct);

    /// <summary>
    /// Sends a hand-written notification from <paramref name="senderId"/> to one active employee. The sender must hold
    /// <see cref="Perm.NotificationsMessageSend"/> (checked here as well as on the page). Returns the Persian reason when it cannot be sent.
    /// </summary>
    public async Task<(bool Ok, string? Error)> SendAsync(Guid senderId, Guid recipientId, string? message, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.NotificationsMessageSend, ct))
            return (false, "شما مجوز ارسال اعلان ندارید.");
        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
            return (false, "متن اعلان را بنویسید.");
        if (text.Length > Notification.MaxMessageLength)
            return (false, $"متن اعلان نباید بیش از {Notification.MaxMessageLength} نویسه باشد.");
        if (senderId == recipientId)
            return (false, "ارسال اعلان به خودتان ممکن نیست.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var recipient = await db.Employees.AsNoTracking().Where(x => x.Id == recipientId).Select(x => new { x.IsActive, x.IsAdmin }).SingleOrDefaultAsync(ct);
        if (recipient is null || !recipient.IsActive)
            return (false, "گیرنده پیدا نشد یا غیرفعال است.");
        var senderName = await db.Employees.AsNoTracking().Where(x => x.Id == senderId).Select(x => x.FullName).SingleOrDefaultAsync(ct);
        if (senderName is null)
            return (false, "فرستنده شناسایی نشد.");

        // Administrators read their notifications on their own page (they have no position and no personal request pages).
        db.Notifications.Add(Notification.FromSender(senderId, recipientId, senderName, text, recipient.IsAdmin ? "/admin/notifications" : "/notifications"));
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public sealed record SentRow(Guid Id, string RecipientName, string Message, DateTime CreatedAtUtc, bool IsRead, DateTime? ReadAtUtc);

    /// <summary>The hand-written notifications this employee sent, newest first, with whether each was read.</summary>
    public async Task<(List<SentRow> Items, int Total)> ListSentAsync(Guid senderId, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Notifications.AsNoTracking().Where(x => x.SenderEmployeeId == senderId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Join(db.Employees.AsNoTracking(), n => n.RecipientEmployeeId, e => e.Id, (n, e) => new SentRow(n.Id, e.FullName, n.Message, n.CreatedAtUtc, n.IsRead, n.ReadAtUtc))
            .ToListAsync(ct);
        return (items, total);
    }

    // ---- the all-notifications report (Admin.Notifications.View) ----

    /// <param name="ReadState">"read", "unread", or null for both.</param>
    /// <param name="Search">Text that must appear in the message or in the "request type / sender" line.</param>
    /// <param name="From">First day (local) to include.</param>
    /// <param name="To">Last day (local) to include.</param>
    public sealed record ReportFilter(Guid? RecipientId = null, Guid? SenderId = null, NotificationKind? Kind = null, string? ReadState = null,
        string? Search = null, DateTime? From = null, DateTime? To = null)
    {
        public int Count => (RecipientId is null ? 0 : 1) + (SenderId is null ? 0 : 1) + (Kind is null ? 0 : 1) + (ReadState is null ? 0 : 1)
            + (string.IsNullOrWhiteSpace(Search) ? 0 : 1) + (From is null ? 0 : 1) + (To is null ? 0 : 1);
    }

    public sealed record ReportRow(Guid Id, DateTime CreatedAtUtc, NotificationKind Kind, string RecipientName, string RecipientUsername, string? SenderName,
        string Message, string RequestType, bool IsRead, DateTime? ReadAtUtc);

    private static DateTime LocalDayStartUtc(DateTime day) => DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Notifications matching the filter (not yet joined to names); shared by the page's list, its counters and the CSV export.</summary>
    public static IQueryable<Notification> Filtered(ApplicationDbContext db, ReportFilter filter)
    {
        var query = db.Notifications.AsNoTracking().AsQueryable();
        if (filter.RecipientId is Guid recipient) query = query.Where(x => x.RecipientEmployeeId == recipient);
        if (filter.SenderId is Guid sender) query = query.Where(x => x.SenderEmployeeId == sender);
        if (filter.Kind is NotificationKind kind) query = query.Where(x => x.Kind == kind);
        if (filter.ReadState == "read") query = query.Where(x => x.IsRead);
        else if (filter.ReadState == "unread") query = query.Where(x => !x.IsRead);
        if (filter.From is DateTime from) { var start = LocalDayStartUtc(from); query = query.Where(x => x.CreatedAtUtc >= start); }
        if (filter.To is DateTime to) { var end = LocalDayStartUtc(to.AddDays(1)); query = query.Where(x => x.CreatedAtUtc < end); }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            query = query.Where(x => x.Message.Contains(text) || x.RequestType.Contains(text));
        }
        return query;
    }

    /// <summary>The filtered notifications with the recipient's and sender's names, in the requested order (ends with the Id so batches never overlap).</summary>
    public static IQueryable<ReportRow> Report(ApplicationDbContext db, ReportFilter filter, string sortBy, bool descending)
    {
        var rows = from n in Filtered(db, filter)
                   join r in db.Employees.AsNoTracking() on n.RecipientEmployeeId equals r.Id
                   join s in db.Employees.AsNoTracking() on n.SenderEmployeeId equals s.Id into senders
                   from s in senders.DefaultIfEmpty()
                   select new ReportRow(n.Id, n.CreatedAtUtc, n.Kind, r.FullName, r.Username, s == null ? null : s.FullName, n.Message, n.RequestType, n.IsRead, n.ReadAtUtc);
        IOrderedQueryable<ReportRow> ordered = (sortBy, descending) switch
        {
            ("recipient", false) => rows.OrderBy(x => x.RecipientName),
            ("recipient", true) => rows.OrderByDescending(x => x.RecipientName),
            ("sender", false) => rows.OrderBy(x => x.SenderName ?? ""),
            ("sender", true) => rows.OrderByDescending(x => x.SenderName ?? ""),
            ("kind", false) => rows.OrderBy(x => x.Kind),
            ("kind", true) => rows.OrderByDescending(x => x.Kind),
            ("read", false) => rows.OrderBy(x => x.IsRead),
            ("read", true) => rows.OrderByDescending(x => x.IsRead),
            ("created", false) => rows.OrderBy(x => x.CreatedAtUtc),
            _ => rows.OrderByDescending(x => x.CreatedAtUtc),
        };
        return ordered.ThenBy(x => x.Id);
    }

    public async Task<int> CountUnreadAsync(Guid? employeeId, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.AsNoTracking().CountAsync(x => x.RecipientEmployeeId == id && !x.IsRead, ct);
    }

    public async Task<(List<Notification> Items, int Total)> ListAsync(Guid employeeId, bool unreadOnly, int page, int pageSize, NotificationKind? kind = null, bool oldestFirst = false, bool unreadFirst = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Notifications.AsNoTracking().Where(x => x.RecipientEmployeeId == employeeId);
        if (unreadOnly)
            query = query.Where(x => !x.IsRead);
        if (kind is NotificationKind kindFilter)
            query = query.Where(x => x.Kind == kindFilter);
        var total = await query.CountAsync(ct);
        // Unread-first keeps the chosen date order inside each group; the ID makes equal timestamps page stably.
        var ordered = unreadFirst
            ? (oldestFirst ? query.OrderBy(x => x.IsRead).ThenBy(x => x.CreatedAtUtc) : query.OrderBy(x => x.IsRead).ThenByDescending(x => x.CreatedAtUtc))
            : (oldestFirst ? query.OrderBy(x => x.CreatedAtUtc) : query.OrderByDescending(x => x.CreatedAtUtc));
        var items = await ordered.ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items, total);
    }

    /// <summary>Marks one of the employee's own notifications as read; other people's are ignored.</summary>
    public async Task MarkReadAsync(Guid employeeId, Guid notificationId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId && x.RecipientEmployeeId == employeeId, ct);
        if (item is null || item.IsRead)
            return;
        item.MarkRead();
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Marks the employee's unread notifications about these requests as read. Used when the requester has
    /// looked at the request itself, so the same news is not counted twice (sidebar "my requests" and "notifications").
    /// Pass null to cover every request.
    /// </summary>
    public async Task MarkRequestsReadAsync(Guid employeeId, IReadOnlyCollection<Guid>? requestIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Notifications.Where(x => x.RecipientEmployeeId == employeeId && !x.IsRead && x.RequestId != null
            && x.Kind != NotificationKind.StepReached && x.Kind != NotificationKind.ReturnedToStep && x.Kind != NotificationKind.VacantPosition);
        if (requestIds is not null)
            query = query.Where(x => requestIds.Contains(x.RequestId!.Value));
        var unread = await query.ToListAsync(ct);
        if (unread.Count == 0)
            return;
        foreach (var item in unread)
            item.MarkRead();
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unread = await db.Notifications.Where(x => x.RecipientEmployeeId == employeeId && !x.IsRead).ToListAsync(ct);
        foreach (var item in unread)
            item.MarkRead();
        if (unread.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}

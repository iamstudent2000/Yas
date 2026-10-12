using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Delegations;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>In-app notifications (spec §17): creating them as part of a workflow action, and reading/marking them for the recipient.</summary>
public sealed class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    /// <summary>
    /// Adds the notifications an event produces to <paramref name="db"/> <b>without saving</b>, so they
    /// are written in the same transaction as the action itself — either both happen or neither.
    /// The request must be the tracked instance the action was applied to, with its steps loaded.
    /// A target position nobody holds produces a vacancy alert to every active SuperAdmin (§5.4).
    /// </summary>
    public async Task StageAsync(ApplicationDbContext db, WorkflowRequest request, WorkflowNotificationEvent evt, CancellationToken ct = default)
    {
        var typeName = WorkflowFieldCatalog.GetDisplayName(request.WorkflowType);
        var shortId = request.Id.ToString("N")[..8];
        var requestLabel = $"{typeName} #{shortId}";

        foreach (var target in WorkflowNotificationPlanner.Plan(request, evt))
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
            var delegates = (await DelegatesOfAsync(db, positionId, request.CurrentStep.StepDefinitionId, ct)).Except(holders).ToList();
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
        db.Notifications.Add(new Notification(delegation.DelegateEmployeeId, NotificationKind.DelegationReceived, null, "تفویض اختیار",
            $"اختیار سمت «{positionName}» از {WorkflowDisplay.ToPersianDate(delegation.StartDate)} تا {WorkflowDisplay.ToPersianDate(delegation.EndDate)} به شما تفویض شد.",
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
                        && db.Employees.Any(e => e.Id == d.DelegateEmployeeId && e.IsActive))
            .Select(d => d.DelegateEmployeeId)
            .Distinct()
            .ToListAsync(ct);
    }

    private static string RequesterMessage(NotificationKind kind, string requestLabel) => kind switch
    {
        NotificationKind.RequestApproved => $"درخواست {requestLabel} تایید نهایی شد.",
        NotificationKind.RequestRejected => $"درخواست {requestLabel} رد شد.",
        NotificationKind.RequestReturned => $"درخواست {requestLabel} به شما بازگردانده شد؛ پس از اصلاح دوباره ارسال کنید.",
        NotificationKind.RequestForceClosed => $"درخواست {requestLabel} توسط مدیر سامانه بسته شد.",
        _ => $"وضعیت درخواست {requestLabel} تغییر کرد.",
    };

    /// <summary>Employees who currently, actively hold the position (inactive employees count as vacant, §4.3).</summary>
    private static async Task<List<Guid>> HoldersOfAsync(ApplicationDbContext db, Guid positionId, CancellationToken ct) =>
        await db.EmployeePositions.AsNoTracking()
            .Where(x => x.PositionId == positionId && x.EndedAt == null)
            .Join(db.Employees.AsNoTracking().Where(e => e.IsActive), ep => ep.EmployeeId, e => e.Id, (ep, e) => e.Id)
            .ToListAsync(ct);

    public async Task<int> CountUnreadAsync(Guid? employeeId, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.AsNoTracking().CountAsync(x => x.RecipientEmployeeId == id && !x.IsRead, ct);
    }

    public async Task<(List<Notification> Items, int Total)> ListAsync(Guid employeeId, bool unreadOnly, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Notifications.AsNoTracking().Where(x => x.RecipientEmployeeId == employeeId);
        if (unreadOnly)
            query = query.Where(x => !x.IsRead);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
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

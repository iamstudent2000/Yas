using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Auditing;
using YasPortal.Domain.Delegations;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>In-app notifications (spec §17): creating them as part of a workflow action, and reading/marking them for the recipient.</summary>
public sealed class NotificationService(IDbContextFactory<ApplicationDbContext> dbFactory, IPermissionChecker permissions, AuditEventWriter? audit = null)
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

    // ---- sending by hand ----

    /// <summary>
    /// Who a hand-written message goes to: the union of everyone picked by hand, every member of the chosen organizations, and every member of
    /// the sender's own saved groups; or every active employee when <see cref="Everyone"/> is set. Inactive employees and the sender are always left out.
    /// </summary>
    /// <param name="ExcludedEmployeeIds">People taken out of the audience before sending, whichever way they were included (ticked off in the review list).</param>
    public sealed record Audience(bool Everyone = false, IReadOnlyCollection<Guid>? EmployeeIds = null, IReadOnlyCollection<Guid>? OrganizationIds = null,
        IReadOnlyCollection<Guid>? GroupIds = null, IReadOnlyCollection<Guid>? ExcludedEmployeeIds = null)
    {
        public bool IsEmpty => !Everyone && (EmployeeIds?.Count ?? 0) == 0 && (OrganizationIds?.Count ?? 0) == 0 && (GroupIds?.Count ?? 0) == 0;
    }

    public sealed record SendResult(bool Ok, string? Error, int Sent = 0);

    public sealed record ChangeResult(bool Ok, string? Error, int Count = 0);

    /// <summary>Most people that can be picked by hand for a single message.</summary>
    public const int MaxSelectedRecipients = 500;

    /// <summary>Most recipients of a single message, however they were chosen (a guard against a runaway broadcast).</summary>
    public const int MaxRecipients = 5000;

    /// <summary>Active employees the audience resolves to, never including the sender.</summary>
    private static IQueryable<Employee> Recipients(ApplicationDbContext db, Audience audience, Guid senderId, bool applyExclusions = true)
    {
        var query = db.Employees.AsNoTracking().Where(e => e.IsActive && e.Id != senderId);
        if (applyExclusions && audience.ExcludedEmployeeIds is { Count: > 0 })
        {
            var excluded = audience.ExcludedEmployeeIds.Distinct().ToList();
            query = query.Where(e => !excluded.Contains(e.Id));
        }
        if (audience.Everyone)
            return query;
        var employeeIds = (audience.EmployeeIds ?? []).Distinct().ToList();
        var organizationIds = (audience.OrganizationIds ?? []).Distinct().ToList();
        var groupIds = (audience.GroupIds ?? []).Distinct().ToList();
        // Only the sender's own groups count, whatever ids the caller passes.
        var memberIds = db.RecipientGroupMembers.AsNoTracking()
            .Where(m => groupIds.Contains(m.GroupId) && db.RecipientGroups.Any(g => g.Id == m.GroupId && g.OwnerEmployeeId == senderId))
            .Select(m => m.EmployeeId);
        return query.Where(e => employeeIds.Contains(e.Id) || organizationIds.Contains(e.OrganizationId) || memberIds.Contains(e.Id));
    }

    /// <summary>How many people the audience would reach right now (for the preview before sending).</summary>
    public async Task<int> CountRecipientsAsync(Guid senderId, Audience audience, CancellationToken ct = default)
    {
        if (audience.IsEmpty)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await Recipients(db, audience, senderId).CountAsync(ct);
    }

    /// <summary>
    /// The people the audience reaches, before any exclusions, for the review list where some can be ticked off. Optional text filter on name, code or username.
    /// </summary>
    public async Task<(List<AdminQueryService.LookupItem> Items, int Total)> ListRecipientsAsync(Guid senderId, Audience audience, string? search, int skip, int take, CancellationToken ct = default)
    {
        if (audience.IsEmpty)
            return ([], 0);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = Recipients(db, audience, senderId, applyExclusions: false);
        var text = (search ?? string.Empty).Trim();
        if (text.Length > 0)
            query = query.Where(e => e.FullName.Contains(text) || e.Username.Contains(text) || e.Code.Contains(text));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(e => e.FullName).ThenBy(e => e.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 200))
            .Select(e => new AdminQueryService.LookupItem(e.Id, e.Code + " — " + e.FullName + " — " + e.Username + (e.IsAdmin ? " (مدیر سامانه)" : " (کارمند)"))).ToListAsync(ct);
        return (items, total);
    }

    /// <summary>
    /// Sends one hand-written notification from <paramref name="senderId"/> to every active employee in the audience (one copy each, tied together
    /// by a batch id). One permission covers all of it: <see cref="Perm.NotificationsMessageSend"/>, checked here as well as on the page.
    /// Returns the Persian reason when nothing was sent.
    /// </summary>
    public async Task<SendResult> SendAsync(Guid senderId, Audience audience, string? message, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.NotificationsMessageSend, ct))
            return new(false, "شما مجوز ارسال اعلان ندارید.");

        var text = (message ?? string.Empty).Trim();
        if (text.Length == 0)
            return new(false, "متن اعلان را بنویسید.");
        if (text.Length > Notification.MaxMessageLength)
            return new(false, $"متن اعلان نباید بیش از {Notification.MaxMessageLength} نویسه باشد.");
        if (audience.IsEmpty)
            return new(false, "گیرنده را انتخاب کنید.");
        var pickedCount = audience.Everyone ? 0 : (audience.EmployeeIds ?? []).Distinct().Count();
        if (pickedCount > MaxSelectedRecipients)
            return new(false, $"حداکثر {MaxSelectedRecipients} نفر را می‌توان یکی‌یکی انتخاب کرد؛ برای تعداد بیشتر سازمان، گروه یا «همه» را انتخاب کنید.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var senderName = await db.Employees.AsNoTracking().Where(x => x.Id == senderId).Select(x => x.FullName).SingleOrDefaultAsync(ct);
        if (senderName is null)
            return new(false, "فرستنده شناسایی نشد.");

        var recipients = await Recipients(db, audience, senderId).Select(e => new { e.Id, e.IsAdmin }).Take(MaxRecipients + 1).ToListAsync(ct);
        if (recipients.Count == 0)
            return new(false, "گیرنده‌ی فعالی پیدا نشد.");
        if (recipients.Count > MaxRecipients)
            return new(false, $"حداکثر {MaxRecipients} گیرنده در هر ارسال مجاز است.");

        var withoutExclusions = audience.ExcludedEmployeeIds is { Count: > 0 } ? await Recipients(db, audience, senderId, applyExclusions: false).CountAsync(ct) : recipients.Count;
        var audienceLabel = await DescribeAsync(db, audience, senderId, recipients.Count, withoutExclusions - recipients.Count, ct);
        var batchId = recipients.Count > 1 ? Guid.NewGuid() : (Guid?)null;

        // Administrators read their notifications on their own page (they have no position and no personal request pages).
        var done = 0;
        foreach (var chunk in recipients.Chunk(500))
        {
            foreach (var recipient in chunk)
                db.Notifications.Add(Notification.FromSender(senderId, recipient.Id, senderName, text, recipient.IsAdmin ? "/admin/notifications" : "/notifications", batchId, audienceLabel));
            await db.SaveChangesAsync(ct);
            done += chunk.Length;
        }
        // Who sent it and to whom (the audience wording and head-count); the message text itself is not copied into the log.
        if (audit is not null)
            await audit.WriteAsync(AuditActions.NotificationSent, AuditEntityTypes.Notification, batchId?.ToString(),
                new { Audience = audienceLabel, Recipients = done, RecipientId = done == 1 ? recipients[0].Id : (Guid?)null }, employeeId: senderId);
        return new(true, null, done);
    }

    /// <summary>The audience in words, kept with the message so the sender (and the report) can see who it was meant for.</summary>
    private static async Task<string?> DescribeAsync(ApplicationDbContext db, Audience audience, Guid senderId, int recipientCount, int leftOut, CancellationToken ct)
    {
        var exclusion = leftOut > 0 ? $" (بدون {leftOut} نفر)" : string.Empty;
        if (audience.Everyone)
            return "همه کارکنان" + exclusion;
        var parts = new List<string>();
        var picked = (audience.EmployeeIds ?? []).Distinct().Count();
        var organizationIds = (audience.OrganizationIds ?? []).Distinct().ToList();
        var groupIds = (audience.GroupIds ?? []).Distinct().ToList();
        if (picked > 0 && (organizationIds.Count > 0 || groupIds.Count > 0 || recipientCount > 1))
            parts.Add($"{picked} نفر منتخب");
        if (organizationIds.Count > 0)
            parts.Add("سازمان‌ها: " + string.Join("، ", await db.Organizations.AsNoTracking().Where(o => organizationIds.Contains(o.Id)).OrderBy(o => o.Name).Select(o => o.Name).ToListAsync(ct)));
        if (groupIds.Count > 0)
            parts.Add("گروه‌ها: " + string.Join("، ", await db.RecipientGroups.AsNoTracking().Where(g => g.OwnerEmployeeId == senderId && groupIds.Contains(g.Id)).OrderBy(g => g.Name).Select(g => g.Name).ToListAsync(ct)));
        return parts.Count == 0 ? null : string.Join(" + ", parts) + exclusion;
    }

    // ---- saved recipient groups (the sender's own; need the same send permission) ----

    public sealed record GroupSummary(Guid Id, string Name, int MemberCount);

    public async Task<List<GroupSummary>> ListGroupsAsync(Guid ownerId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var counts = db.RecipientGroupMembers.AsNoTracking().GroupBy(m => m.GroupId).Select(g => new { GroupId = g.Key, Count = g.Count() });
        return await (from g in db.RecipientGroups.AsNoTracking()
                      where g.OwnerEmployeeId == ownerId
                      join c in counts on g.Id equals c.GroupId into cs
                      from c in cs.DefaultIfEmpty()
                      orderby g.Name
                      select new GroupSummary(g.Id, g.Name, c == null ? 0 : c.Count)).ToListAsync(ct);
    }

    /// <summary>The members of one of the owner's groups (active or not), for loading them back into the hand-picked list.</summary>
    public async Task<List<AdminQueryService.LookupItem>> GetGroupMembersAsync(Guid ownerId, Guid groupId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await (from m in db.RecipientGroupMembers.AsNoTracking()
                      join g in db.RecipientGroups.AsNoTracking() on m.GroupId equals g.Id
                      join e in db.Employees.AsNoTracking() on m.EmployeeId equals e.Id
                      where g.Id == groupId && g.OwnerEmployeeId == ownerId && e.IsActive
                      orderby e.FullName
                      select new AdminQueryService.LookupItem(e.Id, e.Code + " — " + e.FullName + " — " + e.Username + (e.IsAdmin ? " (مدیر سامانه)" : " (کارمند)"))).ToListAsync(ct);
    }

    /// <summary>
    /// Saves what the audience resolves to right now as a group under <paramref name="name"/>: a new group, or, when the owner already has one with
    /// that name, its members are replaced. The group is a snapshot of people (not of the organizations behind it).
    /// </summary>
    public async Task<ChangeResult> SaveGroupAsync(Guid ownerId, string? name, Audience audience, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.NotificationsMessageSend, ct))
            return new(false, "شما مجوز ارسال اعلان ندارید.");
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
            return new(false, "نام گروه را بنویسید.");
        if (value.Length > RecipientGroup.MaxNameLength)
            return new(false, $"نام گروه نباید بیش از {RecipientGroup.MaxNameLength} نویسه باشد.");
        if (audience.IsEmpty)
            return new(false, "ابتدا گیرندگان را انتخاب کنید.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var memberIds = await Recipients(db, audience, ownerId).Select(e => e.Id).Take(MaxRecipients + 1).ToListAsync(ct);
        if (memberIds.Count == 0)
            return new(false, "گیرنده‌ی فعالی پیدا نشد.");
        if (memberIds.Count > MaxRecipients)
            return new(false, $"یک گروه حداکثر {MaxRecipients} عضو دارد.");

        var group = await db.RecipientGroups.SingleOrDefaultAsync(g => g.OwnerEmployeeId == ownerId && g.Name == value, ct);
        if (group is null)
        {
            if (await db.RecipientGroups.CountAsync(g => g.OwnerEmployeeId == ownerId, ct) >= RecipientGroup.MaxGroupsPerOwner)
                return new(false, $"حداکثر {RecipientGroup.MaxGroupsPerOwner} گروه می‌توانید داشته باشید؛ یکی را حذف کنید.");
            group = new RecipientGroup(ownerId, value);
            db.RecipientGroups.Add(group);
        }
        else
        {
            group.Touch();
            await db.RecipientGroupMembers.Where(m => m.GroupId == group.Id).ExecuteDeleteAsync(ct);
        }
        foreach (var id in memberIds)
            db.RecipientGroupMembers.Add(new RecipientGroupMember(group.Id, id));
        await db.SaveChangesAsync(ct);
        return new(true, null, memberIds.Count);
    }

    public async Task<ChangeResult> DeleteGroupAsync(Guid ownerId, Guid groupId, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.NotificationsMessageSend, ct))
            return new(false, "شما مجوز ارسال اعلان ندارید.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var count = await db.RecipientGroups.Where(g => g.Id == groupId && g.OwnerEmployeeId == ownerId).ExecuteDeleteAsync(ct);
        return new(count > 0, count > 0 ? null : "گروه پیدا نشد.", count);
    }

    // ---- what a sender sent, and taking it back ----

    /// <summary>One message the person sent: a single notification, or all the copies of a message sent to several people.</summary>
    /// <param name="Key">The batch id, or the notification's own id for a message sent to one person.</param>
    /// <param name="RecipientName">Set only when the message went to one person.</param>
    public sealed record SentGroup(Guid Key, string Message, DateTime CreatedAtUtc, int Total, int Read, int Removed, string? RecipientName, string? AudienceLabel);

    /// <summary>The hand-written notifications this employee sent, newest first, one row per message (not per recipient).</summary>
    public async Task<(List<SentGroup> Items, int Total)> ListSentAsync(Guid senderId, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = db.Notifications.AsNoTracking().Where(x => x.SenderEmployeeId == senderId)
            .GroupBy(x => x.BatchId ?? x.Id)
            .Select(g => new
            {
                Key = g.Key,
                CreatedAtUtc = g.Min(x => x.CreatedAtUtc),
                Total = g.Count(),
                Read = g.Count(x => x.IsRead),
                Removed = g.Count(x => x.RemovedAtUtc != null),
                Message = g.Max(x => x.Message),
                AudienceLabel = g.Max(x => x.AudienceLabel),
            });
        var total = await groups.CountAsync(ct);
        var rows = await groups.OrderByDescending(g => g.CreatedAtUtc).ThenBy(g => g.Key).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        // A message to one person has a single row; look up that person's name for the groups that need it.
        var singleKeys = rows.Where(g => g.Total == 1).Select(g => g.Key).ToList();
        var names = singleKeys.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Notifications.AsNoTracking().Where(x => x.SenderEmployeeId == senderId && singleKeys.Contains(x.BatchId ?? x.Id))
                .Join(db.Employees.AsNoTracking(), n => n.RecipientEmployeeId, e => e.Id, (n, e) => new { Key = n.BatchId ?? n.Id, e.FullName })
                .ToDictionaryAsync(x => x.Key, x => x.FullName, ct);
        return (rows.Select(g => new SentGroup(g.Key, g.Message, g.CreatedAtUtc, g.Total, g.Read, g.Removed,
            g.Total == 1 && names.TryGetValue(g.Key, out var name) ? name : null, g.AudienceLabel)).ToList(), total);
    }

    /// <summary>
    /// The sender takes back a message they sent: every copy of it disappears from the recipients' lists (the records stay in the report,
    /// marked removed). Only the sender's own messages are touched.
    /// </summary>
    public async Task<ChangeResult> RetractAsync(Guid senderId, Guid groupKey, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.NotificationsMessageSend, ct))
            return new(false, "شما مجوز ارسال اعلان ندارید.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var count = await db.Notifications
            .Where(x => x.SenderEmployeeId == senderId && x.RemovedAtUtc == null && (x.BatchId == groupKey || (x.BatchId == null && x.Id == groupKey)))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAtUtc, (DateTime?)now).SetProperty(x => x.RemovedByEmployeeId, (Guid?)senderId), ct);
        if (audit is not null && count > 0)
            await audit.WriteAsync(AuditActions.NotificationRetracted, AuditEntityTypes.Notification, groupKey.ToString(), new { Count = count }, employeeId: senderId);
        return new(true, null, count);
    }

    // ---- removing and restoring other people's notifications (Admin.Notifications.Manage) ----

    private async Task<ChangeResult> ApplyRemovalAsync(Guid actorId, Func<ApplicationDbContext, IQueryable<Notification>> select, bool removed, CancellationToken ct)
    {
        if (!await permissions.HasPermissionAsync(Perm.AdminNotificationsManage, ct))
            return new(false, "شما مجوز مدیریت اعلان‌ها را ندارید.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var matching = select(db);
        var count = removed
            ? await matching.ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAtUtc, (DateTime?)now).SetProperty(x => x.RemovedByEmployeeId, (Guid?)actorId), ct)
            : await matching.ExecuteUpdateAsync(s => s.SetProperty(x => x.RemovedAtUtc, (DateTime?)null).SetProperty(x => x.RemovedByEmployeeId, (Guid?)null), ct);
        if (audit is not null && count > 0)
            await audit.WriteAsync(removed ? AuditActions.NotificationRemoved : AuditActions.NotificationRestored, AuditEntityTypes.Notification, null, new { Count = count }, employeeId: actorId);
        return new(true, null, count);
    }

    /// <summary>Removes (or restores) specific notifications of any kind and any recipient.</summary>
    public Task<ChangeResult> SetRemovedAsync(Guid actorId, IReadOnlyCollection<Guid> ids, bool removed, CancellationToken ct = default)
    {
        var list = ids.Distinct().ToList();
        return ApplyRemovalAsync(actorId, db => db.Notifications.Where(x => list.Contains(x.Id) && (x.RemovedAtUtc == null) == removed), removed, ct);
    }

    /// <summary>
    /// Removes (or restores) every notification the report currently matches. At least one filter is required, so a stray click can never
    /// remove every notification in the system.
    /// </summary>
    public Task<ChangeResult> SetRemovedForFilterAsync(Guid actorId, ReportFilter filter, bool removed, CancellationToken ct = default)
    {
        if (filter.Count == 0)
            return Task.FromResult(new ChangeResult(false, "برای عملیات گروهی دست‌کم یک فیلتر لازم است."));
        var scoped = filter with { RemovedState = removed ? "active" : "removed" };
        return ApplyRemovalAsync(actorId, db => Filtered(db, scoped), removed, ct);
    }

    // ---- the all-notifications report (Admin.Notifications.View) ----

    /// <param name="ReadState">"read", "unread", or null for both.</param>
    /// <param name="Search">Text that must appear in the message or in the "request type / sender" line.</param>
    /// <param name="From">First day (local) to include.</param>
    /// <param name="To">Last day (local) to include.</param>
    /// <param name="RemovedState">"active" (still shown), "removed", or null for both.</param>
    public sealed record ReportFilter(Guid? RecipientId = null, Guid? SenderId = null, NotificationKind? Kind = null, string? ReadState = null,
        string? Search = null, DateTime? From = null, DateTime? To = null, string? RemovedState = null)
    {
        public int Count => (RecipientId is null ? 0 : 1) + (SenderId is null ? 0 : 1) + (Kind is null ? 0 : 1) + (ReadState is null ? 0 : 1)
            + (string.IsNullOrWhiteSpace(Search) ? 0 : 1) + (From is null ? 0 : 1) + (To is null ? 0 : 1) + (RemovedState is null ? 0 : 1);
    }

    public sealed record ReportRow(Guid Id, DateTime CreatedAtUtc, NotificationKind Kind, string RecipientName, string RecipientUsername, string? SenderName,
        string Message, string RequestType, bool IsRead, DateTime? ReadAtUtc, DateTime? RemovedAtUtc = null, string? RemovedByName = null);

    private static DateTime LocalDayStartUtc(DateTime day) => DateTime.SpecifyKind(day.Date, DateTimeKind.Local).ToUniversalTime();

    /// <summary>Notifications matching the filter (not yet joined to names); shared by the page's list, its counters, the bulk removal and the CSV export.</summary>
    public static IQueryable<Notification> Filtered(ApplicationDbContext db, ReportFilter filter)
    {
        var query = db.Notifications.AsNoTracking().AsQueryable();
        if (filter.RecipientId is Guid recipient) query = query.Where(x => x.RecipientEmployeeId == recipient);
        if (filter.SenderId is Guid sender) query = query.Where(x => x.SenderEmployeeId == sender);
        if (filter.Kind is NotificationKind kind) query = query.Where(x => x.Kind == kind);
        if (filter.ReadState == "read") query = query.Where(x => x.IsRead);
        else if (filter.ReadState == "unread") query = query.Where(x => !x.IsRead);
        if (filter.RemovedState == "active") query = query.Where(x => x.RemovedAtUtc == null);
        else if (filter.RemovedState == "removed") query = query.Where(x => x.RemovedAtUtc != null);
        if (filter.From is DateTime from) { var start = LocalDayStartUtc(from); query = query.Where(x => x.CreatedAtUtc >= start); }
        if (filter.To is DateTime to) { var end = LocalDayStartUtc(to.AddDays(1)); query = query.Where(x => x.CreatedAtUtc < end); }
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var text = filter.Search.Trim();
            query = query.Where(x => x.Message.Contains(text) || x.RequestType.Contains(text));
        }
        return query;
    }

    /// <summary>The filtered notifications with the recipient's, sender's and remover's names, in the requested order (ends with the Id so batches never overlap).</summary>
    /// <remarks>Sorting happens on the joined rows and the <see cref="ReportRow"/> is projected last: EF Core cannot translate a sort on the properties of a constructor projection.</remarks>
    public static IQueryable<ReportRow> Report(ApplicationDbContext db, ReportFilter filter, string sortBy, bool descending)
    {
        var joined = from n in Filtered(db, filter)
                     join r in db.Employees.AsNoTracking() on n.RecipientEmployeeId equals r.Id
                     join s in db.Employees.AsNoTracking() on n.SenderEmployeeId equals s.Id into senders
                     from s in senders.DefaultIfEmpty()
                     join m in db.Employees.AsNoTracking() on n.RemovedByEmployeeId equals m.Id into removers
                     from m in removers.DefaultIfEmpty()
                     select new { n, r, s, m };
        var ordered = (sortBy, descending) switch
        {
            ("recipient", false) => joined.OrderBy(x => x.r.FullName),
            ("recipient", true) => joined.OrderByDescending(x => x.r.FullName),
            ("sender", false) => joined.OrderBy(x => x.s == null ? "" : x.s.FullName),
            ("sender", true) => joined.OrderByDescending(x => x.s == null ? "" : x.s.FullName),
            ("kind", false) => joined.OrderBy(x => x.n.Kind),
            ("kind", true) => joined.OrderByDescending(x => x.n.Kind),
            ("read", false) => joined.OrderBy(x => x.n.IsRead),
            ("read", true) => joined.OrderByDescending(x => x.n.IsRead),
            ("unread", _) => joined.OrderBy(x => x.n.IsRead).ThenByDescending(x => x.n.CreatedAtUtc),
            ("created", false) => joined.OrderBy(x => x.n.CreatedAtUtc),
            _ => joined.OrderByDescending(x => x.n.CreatedAtUtc),
        };
        return ordered.ThenBy(x => x.n.Id).Select(x => new ReportRow(x.n.Id, x.n.CreatedAtUtc, x.n.Kind, x.r.FullName, x.r.Username,
            x.s == null ? null : x.s.FullName, x.n.Message, x.n.RequestType, x.n.IsRead, x.n.ReadAtUtc, x.n.RemovedAtUtc, x.m == null ? null : x.m.FullName));
    }

    public async Task<int> CountUnreadAsync(Guid? employeeId, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return 0;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Notifications.AsNoTracking().CountAsync(x => x.RecipientEmployeeId == id && !x.IsRead && x.RemovedAtUtc == null, ct);
    }

    public async Task<(List<Notification> Items, int Total)> ListAsync(Guid employeeId, bool unreadOnly, int page, int pageSize, NotificationKind? kind = null, bool oldestFirst = false, bool unreadFirst = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Notifications.AsNoTracking().Where(x => x.RecipientEmployeeId == employeeId && x.RemovedAtUtc == null);
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
        // The person is looking at these notifications now. One row per page of the list (not per reload within a minute).
        if (audit is not null && await audit.ShouldLogAsync($"notifications:{page}:{pageSize}:{unreadOnly}:{kind}:{oldestFirst}:{unreadFirst}", TimeSpan.FromMinutes(1), employeeId))
            await audit.WriteAsync(AuditActions.NotificationsViewed, AuditEntityTypes.Notification, null,
                new { Page = page, Shown = items.Count, UnreadShown = items.Count(x => !x.IsRead), UnreadOnly = unreadOnly, Ids = string.Join(", ", items.Select(x => x.Id)) },
                employeeId: employeeId);
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
        // When it was seen, and how long after it was sent.
        if (audit is not null)
            await audit.WriteAsync(AuditActions.NotificationRead, AuditEntityTypes.Notification, item.Id.ToString(),
                new { NotificationKind = item.Kind, item.RequestId, SentAtUtc = item.CreatedAtUtc, WaitedSeconds = (long)((item.ReadAtUtc ?? DateTime.UtcNow) - item.CreatedAtUtc).TotalSeconds },
                employeeId: employeeId);
    }

    /// <summary>
    /// Marks the employee's unread notifications about these requests as read. Used when the requester has
    /// looked at the request itself, so the same news is not counted twice (sidebar "my requests" and "notifications").
    /// Pass null to cover every request.
    /// </summary>
    public async Task MarkRequestsReadAsync(Guid employeeId, IReadOnlyCollection<Guid>? requestIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Notifications.Where(x => x.RecipientEmployeeId == employeeId && x.RemovedAtUtc == null && !x.IsRead && x.RequestId != null
            && x.Kind != NotificationKind.StepReached && x.Kind != NotificationKind.ReturnedToStep && x.Kind != NotificationKind.VacantPosition);
        if (requestIds is not null)
            query = query.Where(x => requestIds.Contains(x.RequestId!.Value));
        var unread = await query.ToListAsync(ct);
        if (unread.Count == 0)
            return;
        foreach (var item in unread)
            item.MarkRead();
        await db.SaveChangesAsync(ct);
        if (audit is not null)
            await audit.WriteAsync(AuditActions.NotificationsMarkedRead, AuditEntityTypes.Notification, null,
                new { Scope = requestIds is null ? "AllRequests" : "Requests", Count = unread.Count, Ids = string.Join(", ", unread.Select(x => x.Id)) }, employeeId: employeeId);
    }

    public async Task MarkAllReadAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var unread = await db.Notifications.Where(x => x.RecipientEmployeeId == employeeId && x.RemovedAtUtc == null && !x.IsRead).ToListAsync(ct);
        foreach (var item in unread)
            item.MarkRead();
        if (unread.Count == 0)
            return;
        await db.SaveChangesAsync(ct);
        if (audit is not null)
            await audit.WriteAsync(AuditActions.NotificationsMarkedRead, AuditEntityTypes.Notification, null,
                new { Scope = "All", Count = unread.Count, Ids = string.Join(", ", unread.Select(x => x.Id)) }, employeeId: employeeId);
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Store;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;
using YasPortal.Infrastructure.Store;

namespace YasPortal.Web.Services;

public sealed record LotteryMineRow(Guid EntryId, Guid ItemId, string ItemName, string Unit, decimal Quantity, LotteryEntryStatus Status, DateTime AppliedAtUtc, Guid? OrderId);

/// <summary>A lottery item with applications waiting: what the storekeeper sees before drawing.</summary>
public sealed record LotteryItemRow(Guid ItemId, string Code, string Name, string Unit, decimal Stock, StoreRequestWindow Window, DateTime? ClosesOn, int Applicants, decimal TotalQuantity);

public sealed record LotteryApplicantRow(string EmployeeName, string? PositionName, decimal Quantity, DateTime AppliedAtUtc);

public sealed record LotteryDrawResult(bool Ok, string? Error = null, int Winners = 0, int Losers = 0);

/// <summary>
/// Lottery items. People apply while the item's request period is open (one application per person, the quantity they would like); once the period
/// has closed the storekeeper runs one random draw limited by the stock. Every winner gets an ordinary store order that goes through approval from the
/// position they applied in; everybody else is told they did not win.
/// </summary>
public sealed class StoreLotteryService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows, NotificationService notifications)
{
    private const string AlreadyApplied = "شما قبلاً برای این کالا ثبت‌نام کرده‌اید.";

    public async Task<StoreResult> ApplyAsync(Guid itemId, Guid employeeId, Guid positionId, decimal quantity, DateTime today, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.StoreItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, ct);
        var categoryActive = item is not null && await db.StoreCategories.AnyAsync(c => c.Id == item.CategoryId && c.IsActive, ct);
        if (item is null || !item.IsListed || !categoryActive) return StoreResult.Fail("این کالا دیگر در فروشگاه نیست.");
        if (!item.IsLottery) return StoreResult.Fail("این کالا قرعه‌کشی ندارد؛ آن را از سبد خرید سفارش دهید.");
        var name = $"«{item.Name}»";
        switch (item.WindowOn(today))
        {
            case StoreRequestWindow.NotYet: return StoreResult.Fail($"ثبت‌نام {name} هنوز شروع نشده است.");
            case StoreRequestWindow.Closed: return StoreResult.Fail($"مهلت ثبت‌نام {name} تمام شده است.");
        }
        if (quantity <= 0) return StoreResult.Fail($"مقدار {name} باید بیشتر از صفر باشد.");
        if (!StoreUnits.IsValidQuantity(item.Unit, quantity)) return StoreResult.Fail(StoreUnits.AllowsFraction(item.Unit) ? "مقدار حداکثر سه رقم اعشار دارد." : $"واحد «{item.Unit}» شمارشی است؛ مقدار باید عدد صحیح باشد.");
        if (item.MaxPerRequest > 0 && quantity > item.MaxPerRequest) return StoreResult.Fail($"حداکثر مقدار {name} برای هر نفر {item.MaxPerRequest:0.###} {item.Unit} است.");
        if (await db.StoreLotteryEntries.AnyAsync(e => e.ItemId == itemId && e.EmployeeId == employeeId && e.Status == LotteryEntryStatus.Applied, ct)) return StoreResult.Fail(AlreadyApplied);

        db.StoreLotteryEntries.Add(new StoreLotteryEntry(itemId, employeeId, positionId, quantity));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return StoreResult.Fail(AlreadyApplied); } // the filtered unique index caught a double click
        return StoreResult.Success;
    }

    /// <summary>Takes an application back. Only the owner can, and only before the draw.</summary>
    public async Task<StoreResult> WithdrawAsync(Guid entryId, Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var entry = await db.StoreLotteryEntries.FirstOrDefaultAsync(e => e.Id == entryId && e.EmployeeId == employeeId, ct);
        if (entry is null) return StoreResult.Fail("ثبت‌نام پیدا نشد.");
        if (entry.Status != LotteryEntryStatus.Applied) return StoreResult.Fail("قرعه‌کشی انجام شده و دیگر نمی‌توانید انصراف دهید.");
        entry.Withdraw();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return StoreResult.Fail("وضعیت ثبت‌نام تغییر کرد؛ صفحه را دوباره باز کنید."); }
        return StoreResult.Success;
    }

    /// <summary>The person's applications, newest first.</summary>
    public async Task<IReadOnlyList<LotteryMineRow>> ListMineAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.StoreLotteryEntries.AsNoTracking().Where(e => e.EmployeeId == employeeId)
            .OrderByDescending(e => e.AppliedAtUtc).Take(200)
            .Select(e => new { e.Id, e.ItemId, e.Quantity, e.Status, e.AppliedAtUtc, e.OrderId,
                Name = db.StoreItems.Where(i => i.Id == e.ItemId).Select(i => i.Name).FirstOrDefault(),
                Unit = db.StoreItems.Where(i => i.Id == e.ItemId).Select(i => i.Unit).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(r => new LotteryMineRow(r.Id, r.ItemId, r.Name ?? "", r.Unit ?? "", r.Quantity, r.Status, r.AppliedAtUtc, r.OrderId)).ToList();
    }

    /// <summary>Item id → (entry id, quantity) of the person's applications that still wait for a draw. Used to show «ثبت‌نام شده» on the shop.</summary>
    public async Task<IReadOnlyDictionary<Guid, (Guid EntryId, decimal Quantity)>> WaitingOfAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.StoreLotteryEntries.AsNoTracking().Where(e => e.EmployeeId == employeeId && e.Status == LotteryEntryStatus.Applied)
            .Select(e => new { e.ItemId, e.Id, e.Quantity }).ToListAsync(ct);
        return rows.ToDictionary(r => r.ItemId, r => (r.Id, r.Quantity));
    }

    /// <summary>Lottery items that have applications waiting, with the numbers needed to decide whether to draw.</summary>
    public async Task<IReadOnlyList<LotteryItemRow>> WaitingItemsAsync(DateTime today, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var groups = await db.StoreLotteryEntries.AsNoTracking().Where(e => e.Status == LotteryEntryStatus.Applied)
            .GroupBy(e => e.ItemId).Select(g => new { ItemId = g.Key, Count = g.Count(), Total = g.Sum(e => e.Quantity) }).ToListAsync(ct);
        var ids = groups.Select(g => g.ItemId).ToList();
        var items = await db.StoreItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
        var stock = await db.StoreStockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Delta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        return groups.Select(g => (g, item: items.FirstOrDefault(i => i.Id == g.ItemId))).Where(x => x.item is not null)
            .Select(x => new LotteryItemRow(x.item!.Id, x.item.Code, x.item.Name, x.item.Unit, stock.GetValueOrDefault(x.item.Id), x.item.WindowOn(today), x.item.RequestClosesOn, x.g.Count, x.g.Total))
            .OrderBy(r => r.Name).ToList();
    }

    public async Task<IReadOnlyList<LotteryApplicantRow>> ApplicantsAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.StoreLotteryEntries.AsNoTracking().Where(e => e.ItemId == itemId && e.Status == LotteryEntryStatus.Applied).OrderBy(e => e.AppliedAtUtc)
            .Select(e => new { e.Quantity, e.AppliedAtUtc,
                Who = db.Employees.Where(x => x.Id == e.EmployeeId).Select(x => x.FullName).FirstOrDefault(),
                Position = db.Positions.Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefault() }).ToListAsync(ct);
        return rows.Select(r => new LotteryApplicantRow(r.Who ?? "", r.Position, r.Quantity, r.AppliedAtUtc)).ToList();
    }

    /// <summary>
    /// Runs the draw for one item, once the request period is over. All or nothing: the winners' orders are created, every application is marked
    /// won or lost and everybody is notified in one transaction. If an order cannot be routed for approval (for example the winner's position has no
    /// approver), nothing is drawn and the message names the person, so the draw can be repeated after the fix.
    /// </summary>
    public async Task<LotteryDrawResult> DrawAsync(Guid itemId, Guid byEmployeeId, DateTime today, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var item = await db.StoreItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null || !item.IsLottery) return new(false, "این کالا قرعه‌کشی ندارد.");
        if (item.RequestClosesOn is null) return new(false, "برای این کالا تاریخ پایان دوره درخواست مشخص نشده است. آن را در «کالاها و گروه‌ها» ثبت کنید.");
        var window = item.WindowOn(today);
        if (window != StoreRequestWindow.Closed) return new(false, "قرعه‌کشی فقط بعد از پایان دوره درخواست کالا ممکن است.");

        var entries = await db.StoreLotteryEntries.Where(e => e.ItemId == itemId && e.Status == LotteryEntryStatus.Applied).ToListAsync(ct);
        if (entries.Count == 0) return new(false, "برای این کالا ثبت‌نامی در انتظار قرعه‌کشی نیست.");
        var stock = await db.StoreStockMovements.Where(m => m.ItemId == itemId).SumAsync(m => (decimal?)m.Delta, ct) ?? 0m;
        if (stock <= 0) return new(false, "موجودی این کالا صفر است؛ ابتدا موجودی را ثبت کنید.");

        var winnerIds = StoreLottery.Draw(entries.Select(e => (e.Id, e.Quantity)).ToList(), stock).ToHashSet();
        var typeName = WorkflowFieldCatalog.GetDisplayName(WorkflowTypeCode.StoreOrder);
        var entryEmployeeIds = entries.Select(x => x.EmployeeId).Distinct().ToList();
        var employeeNames = await db.Employees.AsNoTracking().Where(e => entryEmployeeIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);

        // route every winner first, so one problem stops the whole draw before anything is written
        var routed = new List<(StoreLotteryEntry Entry, WorkflowService.ResolveResult Resolved)>();
        foreach (var entry in entries.Where(e => winnerIds.Contains(e.Id)))
        {
            var resolved = await workflows.ResolveStepsAsync(WorkflowTypeCode.StoreOrder, entry.PositionId, ct);
            if (!resolved.Success)
                return new(false, $"مسیر تأیید سفارش «{employeeNames.GetValueOrDefault(entry.EmployeeId, "؟")}» تعیین نشد: {resolved.Error} قرعه‌کشی انجام نشد.");
            routed.Add((entry, resolved));
        }

        foreach (var (entry, resolved) in routed)
        {
            var line = new StoreOrderLine(item.Id, item.Code, item.Name, item.Unit, entry.Quantity);
            var summary = StoreOrderService.Summarize([new StoreOrderLineView(item.Code, item.Name, item.Unit, entry.Quantity)]);
            var fields = new Dictionary<string, string?> { ["summary"] = summary, ["note"] = "برنده قرعه‌کشی" };
            var request = new WorkflowRequest(WorkflowTypeCode.StoreOrder, entry.EmployeeId, entry.PositionId, JsonSerializer.Serialize(fields), resolved.Steps.Select(s => s.ToDomain()).ToList());
            var order = new StoreOrder(request.Id, entry.EmployeeId, entry.PositionId, "برنده قرعه‌کشی", [line]);
            db.WorkflowRequests.Add(request);
            db.StoreOrders.Add(order);
            await notifications.StageAsync(db, request, WorkflowNotificationEvent.Submitted, ct);
            entry.MarkWon(order.Id);
            db.Notifications.Add(new Notification(entry.EmployeeId, NotificationKind.StoreLotteryResult, request.Id, typeName,
                $"شما در قرعه‌کشی «{item.Name}» برنده شدید. سفارش شما برای تأیید ارسال شد.", "/store/orders"));
        }
        foreach (var entry in entries.Where(e => !winnerIds.Contains(e.Id)))
        {
            entry.MarkLost();
            db.Notifications.Add(new Notification(entry.EmployeeId, NotificationKind.StoreLotteryResult, null, typeName,
                $"در قرعه‌کشی «{item.Name}» این بار نام شما درنیامد.", "/store"));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(true, null, winnerIds.Count, entries.Count - winnerIds.Count);
    }
}

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Notifications;
using YasPortal.Domain.Store;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;
using YasPortal.Infrastructure.Store;

namespace YasPortal.Web.Services;

/// <summary>One line of an order as shown on screen. <paramref name="UnitPrice"/> is the price copied onto the line when the order was sent (empty = no price).</summary>
public sealed record StoreOrderLineView(string ItemCode, string ItemName, string Unit, decimal Quantity, decimal? UnitPrice = null)
{
    /// <summary>Unit price x quantity; empty when the item had no price.</summary>
    public decimal? LineTotal => StorePricing.LineTotal(UnitPrice, Quantity);
}

/// <summary><paramref name="Total"/> is the final price of the order (zero when nothing in it had a price).</summary>
public sealed record StoreOrderRow(Guid Id, Guid RequestId, DateTime CreatedAtUtc, WorkflowRequestStatus Status, DateTime? IssuedAtUtc, string Summary, string? RequesterName = null, string? IssuedByName = null, decimal Total = 0);

/// <summary>
/// One order with everything the slip shows. The salary-deduction fields say who agreed to the total being taken from the salary, when, and for how much
/// (all empty when the order costs nothing).
/// </summary>
public sealed record StoreOrderDetail(Guid Id, Guid RequestId, Guid EmployeeId, string EmployeeName, string? PositionName, DateTime CreatedAtUtc, WorkflowRequestStatus Status,
    DateTime? IssuedAtUtc, string? IssuedByName, string? Note, string? IssueNote, IReadOnlyList<StoreOrderLineView> Lines,
    DateTime? DeductionAcceptedAtUtc = null, string? DeductionAcceptedByName = null, decimal? DeductionAcceptedAmount = null)
{
    public string Number => Id.ToString("N")[..8].ToUpperInvariant();

    /// <summary>The final price of the order: the sum of the line totals.</summary>
    public decimal Total => StorePricing.Total(Lines.Select(l => (l.UnitPrice, l.Quantity)));

    /// <summary>True when the person (or an administrator for them) agreed to the salary deduction, so the slip can say who.</summary>
    public bool HasDeductionConsent => DeductionAcceptedAtUtc is not null;
}

/// <summary>
/// How one cart line stands right now: what is wrong with it (null = nothing), the current stock and the current unit price of the item
/// (the price an order sent now would be charged; empty = no price). The page multiplies the price by the quantity to show the line total.
/// </summary>
public sealed record StoreCartCheck(string? Problem, decimal Stock, decimal? UnitPrice = null);

public sealed record StorePlaceResult(bool Ok, string? Error = null, Guid? OrderId = null);

/// <summary>
/// Store orders. An order is owned by the person who sent it (listed by employee, so it is visible under every position they hold) and is
/// approved as an ordinary workflow request of type StoreOrder routed from the position they were acting in. Once the request is approved the
/// storekeeper hands the goods over, which writes the Issue movements and closes the order.
/// </summary>
public sealed class StoreOrderService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows, NotificationService notifications)
{
    // Wording for an order's state. An approved order reads as "waiting for hand-over" until the goods are actually issued.
    public static string StatusText(WorkflowRequestStatus status, bool issued) => status switch
    {
        WorkflowRequestStatus.PendingApproval => "در انتظار تأیید",
        WorkflowRequestStatus.Approved => issued ? "تحویل داده شد" : "تأیید شده، در انتظار تحویل",
        WorkflowRequestStatus.Rejected => "رد شد",
        WorkflowRequestStatus.ReturnedToRequester => "بازگشت به درخواست‌کننده",
        WorkflowRequestStatus.Cancelled => "لغو شد",
        _ => status.ToString(),
    };

    /// <summary>
    /// The text the approvers read in the request. Items that have a price show their line total, and an order that costs money ends with the final
    /// price, so the person approving can see what will be taken from the requester's salary.
    /// </summary>
    public static string Summarize(IEnumerable<StoreOrderLineView> lines)
    {
        var list = lines.ToList();
        var text = string.Join("\n", list.Select(l => $"• {l.ItemName} ({l.ItemCode}) — {l.Quantity:0.###} {l.Unit}"
            + (l.LineTotal is decimal t ? $" — {StoreMoney.Plain(l.UnitPrice!.Value)} × {l.Quantity:0.###} = {StoreMoney.Plain(t)} تومان" : "")));
        var total = StorePricing.Total(list.Select(l => (l.UnitPrice, l.Quantity)));
        return StorePricing.NeedsDeductionConsent(total) ? text + $"\nمبلغ نهایی: {StoreMoney.Plain(total)} تومان (با موافقت درخواست‌کننده از حقوق کسر می‌شود)" : text;
    }

    /// <summary>The message shown when an order that costs money is sent without the agreement to the salary deduction.</summary>
    public static string DeductionRequiredMessage(decimal total) =>
        $"مبلغ این سفارش {StoreMoney.Format(total)} است و از حقوق کسر می‌شود؛ برای ادامه باید با کسر مبلغ از حقوق موافقت کنید.";

    /// <summary>
    /// The person agrees to the amount they were shown. If the amount worked out now is different (the item's price was changed while the page was open),
    /// the agreement does not count: they agreed to another amount. Returns the message to show, or null when the amounts match or no amount was shown.
    /// </summary>
    public static string? ChangedAmountMessage(decimal total, decimal? shownTotal) =>
        shownTotal is decimal shown && shown != total
            ? $"قیمت کالا در این فاصله تغییر کرده است؛ مبلغ نهایی اکنون {StoreMoney.Format(total)} است (در صفحه {StoreMoney.Format(shown)} نشان داده شده بود). مبلغ جدید را ببینید و دوباره موافقت کنید."
            : null;

    /// <summary>The final price of the lines the person wants, as an order sent now would be charged (empty lines or items without a price add nothing).</summary>
    private static decimal TotalOf(IEnumerable<StoreOrderLine> lines) => StorePricing.Total(lines.Select(l => (l.UnitPrice, l.Quantity)));

    /// <summary>The one place the ordering rules live: the cart page shows these messages before sending, and sending checks them again. Returns null when the line is fine.</summary>
    private static string? Validate(StoreItem? item, bool categoryActive, decimal quantity, decimal available, DateTime today, bool alreadyOnOrder = false, decimal outstanding = 0)
    {
        // Rules applied in order: the item must still be in the shop, the quantity must make sense for its unit, the request period
        // must be open, lottery items cannot be ordered directly, the per-person limit and the current stock must allow it.
        if (item is null || !item.IsListed || !categoryActive) return "این کالا دیگر در فروشگاه نیست؛ دیگر نمی‌توان آن را درخواست داد.";
        var name = $"«{item.Name}»";
        if (quantity <= 0) return $"مقدار {name} باید بیشتر از صفر باشد.";
        if (decimal.Round(quantity, 3) != quantity) return $"مقدار {name} حداکثر سه رقم اعشار دارد.";
        if (!StoreUnits.AllowsFraction(item.Unit) && quantity != decimal.Truncate(quantity)) return $"واحد «{item.Unit}» شمارشی است؛ مقدار {name} باید عدد صحیح باشد.";
        // an item that is already on the order being corrected (for example a lottery win, drawn after the period closed) keeps its place as long as the quantity does not grow
        if (!alreadyOnOrder)
        {
            switch (item.WindowOn(today))
            {
                case StoreRequestWindow.NotYet: return $"دوره درخواست {name} هنوز شروع نشده است.";
                case StoreRequestWindow.Closed: return $"دوره درخواست {name} پایان یافته است.";
            }
        }
        if (item.IsLottery && !alreadyOnOrder) return $"{name} با قرعه‌کشی توزیع می‌شود و درخواست مستقیم ندارد؛ در قرعه‌کشی شرکت کنید.";
        if (item.MaxPerRequest > 0 && outstanding + quantity > item.MaxPerRequest)
        {
            var left = Math.Max(0, item.MaxPerRequest - outstanding);
            return outstanding > 0
                ? $"از {name} این مقدار هنوز تحویل نگرفته‌اید: {outstanding:0.###} {item.Unit}. سقف مجاز برای هر نفر {item.MaxPerRequest:0.###} {item.Unit} است، پس تا تحویل گرفتن آن‌ها فقط {left:0.###} {item.Unit} دیگر می‌توانید درخواست دهید."
                : $"حداکثر مقدار {name} در هر درخواست {item.MaxPerRequest:0.###} {item.Unit} است.";
        }
        if (quantity > available) return $"موجودی {name} کافی نیست (موجودی: {available:0.###} {item.Unit}).";
        return null;
    }

    /// <summary>Checks every cart line as it would be checked when sending, so the person sees what to fix before pressing send. Key = item id; a missing key means the line is fine.</summary>
    public async Task<IReadOnlyDictionary<Guid, StoreCartCheck>> CheckAsync(Guid employeeId, IReadOnlyList<(Guid ItemId, decimal Quantity)> wanted, DateTime today, Guid? correctingRequestId = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await ExistingQuantitiesAsync(db, correctingRequestId, ct);
        var outstanding = await OutstandingAsync(db, employeeId, correctingRequestId, ct);
        var ids = wanted.Select(x => x.ItemId).ToList();
        var items = await db.StoreItems.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var activeCategories = await db.StoreCategories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(ct);
        var stock = await db.StoreStockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Delta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var result = new Dictionary<Guid, StoreCartCheck>();
        foreach (var (itemId, quantity) in wanted)
        {
            var item = items.FirstOrDefault(x => x.Id == itemId);
            var available = stock.GetValueOrDefault(itemId);
            result[itemId] = new StoreCartCheck(Validate(item, item is not null && activeCategories.Contains(item.CategoryId), quantity, available, today, existing.TryGetValue(itemId, out var had) && quantity <= had, outstanding.GetValueOrDefault(itemId)), available, item?.Price);
        }
        return result;
    }

    /// <summary>
    /// What the person has asked for but not yet received, per item: the lines of their orders that are still alive (waiting for approval, returned for
    /// correction, or approved but not handed over). Handed-over, rejected and cancelled orders do not count. <paramref name="exceptRequestId"/> leaves out the order being corrected.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, decimal>> OutstandingOfAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await OutstandingAsync(db, employeeId, null, ct);
    }

    // Outstanding = quantities the person already asked for but has not received yet, which count towards the per-person limit.
    private static async Task<Dictionary<Guid, decimal>> OutstandingAsync(ApplicationDbContext db, Guid employeeId, Guid? exceptRequestId, CancellationToken ct)
    {
        var rows = await (from o in db.StoreOrders.AsNoTracking()
                          where o.EmployeeId == employeeId && o.IssuedAtUtc == null && o.RequestId != exceptRequestId
                              && db.WorkflowRequests.Any(r => r.Id == o.RequestId && (r.Status == WorkflowRequestStatus.PendingApproval || r.Status == WorkflowRequestStatus.Approved || r.Status == WorkflowRequestStatus.ReturnedToRequester))
                          from l in o.Lines
                          select new { l.ItemId, l.Quantity }).ToListAsync(ct);
        return rows.GroupBy(r => r.ItemId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
    }

    private static async Task<Dictionary<Guid, decimal>> ExistingQuantitiesAsync(ApplicationDbContext db, Guid? requestId, CancellationToken ct) =>
        requestId is null ? [] : await db.StoreOrderLines.AsNoTracking().Where(l => db.StoreOrders.Any(o => o.Id == l.OrderId && o.RequestId == requestId)).ToDictionaryAsync(l => l.ItemId, l => l.Quantity, ct);

    // Checks every wanted line and turns it into order lines; the first problem found stops everything and is reported.
    private async Task<(List<StoreOrderLine>? Lines, string? Error)> BuildLinesAsync(ApplicationDbContext db, Guid employeeId, IReadOnlyList<(Guid ItemId, decimal Quantity)> wanted, DateTime today, Guid? correctingRequestId, CancellationToken ct)
    {
        var existing = await ExistingQuantitiesAsync(db, correctingRequestId, ct);
        var outstanding = await OutstandingAsync(db, employeeId, correctingRequestId, ct);
        var ids = wanted.Select(x => x.ItemId).ToList();
        var items = await db.StoreItems.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var activeCategories = await db.StoreCategories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(ct);
        var stock = await db.StoreStockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Delta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var lines = new List<StoreOrderLine>();
        foreach (var (itemId, quantity) in wanted)
        {
            var item = items.FirstOrDefault(x => x.Id == itemId);
            var problem = Validate(item, item is not null && activeCategories.Contains(item.CategoryId), quantity, stock.GetValueOrDefault(itemId), today, existing.TryGetValue(itemId, out var had) && quantity <= had, outstanding.GetValueOrDefault(itemId));
            if (problem is not null) return (null, problem);
            // the price is copied now: a later price change in the catalogue never changes an order that was already sent
            lines.Add(new StoreOrderLine(item!.Id, item.Code, item.Name, item.Unit, quantity, item.Price));
        }
        return (lines, null);
    }

    /// <summary>The id of the order that belongs to an approval request (the request lists know the request id), or null.</summary>
    public async Task<Guid?> OrderIdOfRequestAsync(Guid requestId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.StoreOrders.AsNoTracking().Where(o => o.RequestId == requestId).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Sends an order that an approver returned for correction again, with the corrected items. The approval chain is worked out again from
    /// the position the order was first sent from (that is organizational), while the order stays the person's own.
    /// </summary>
    /// <param name="acceptSalaryDeduction">The person ticked the agreement to the salary deduction for the new total. The old agreement was for the old amount, so it has to be given again.</param>
    /// <param name="shownTotal">The final price the page showed next to the tick box; if the price changed meanwhile the order is refused so the person can agree to the new amount.</param>
    public async Task<StorePlaceResult> ResubmitAsync(Guid requestId, Guid employeeId, IReadOnlyList<(Guid ItemId, decimal Quantity)> wanted, string? note, DateTime today, bool acceptSalaryDeduction = false, decimal? shownTotal = null, CancellationToken ct = default)
    {
        if (wanted.Count == 0) return new(false, "سفارش باید دست‌کم یک کالا داشته باشد؛ اگر دیگر نیازی ندارید، درخواست را لغو کنید.");
        if (note is { Length: > StoreOrder.NoteMax }) return new(false, $"توضیحات حداکثر {StoreOrder.NoteMax} نویسه باشد.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var request = await db.WorkflowRequests.Include(x => x.Steps)
            .FirstOrDefaultAsync(x => x.Id == requestId && x.RequesterEmployeeId == employeeId && x.WorkflowType == WorkflowTypeCode.StoreOrder, ct);
        if (request is null) return new(false, "سفارش پیدا نشد.");
        if (request.Status != WorkflowRequestStatus.ReturnedToRequester) return new(false, "این سفارش دیگر در وضعیت بازگشتی نیست.");
        var order = await db.StoreOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.RequestId == requestId, ct);
        if (order is null || order.IsIssued) return new(false, "سفارش پیدا نشد.");

        var (lines, error) = await BuildLinesAsync(db, employeeId, wanted, today, requestId, ct);
        if (lines is null) return new(false, error);
        var total = TotalOf(lines);
        if (ChangedAmountMessage(total, shownTotal) is string changed) return new(false, changed);
        if (StorePricing.NeedsDeductionConsent(total) && !acceptSalaryDeduction) return new(false, DeductionRequiredMessage(total));
        var resolved = await workflows.ResolveStepsAsync(WorkflowTypeCode.StoreOrder, request.RequesterPositionId, ct);
        if (!resolved.Success) return new(false, resolved.Error);

        var fields = new Dictionary<string, string?> { ["summary"] = Summarize(lines.Select(l => new StoreOrderLineView(l.ItemCode, l.ItemName, l.Unit, l.Quantity, l.UnitPrice))) };
        if (!string.IsNullOrWhiteSpace(note)) fields["note"] = note.Trim();

        db.StoreOrderLines.RemoveRange(order.Lines.ToList());
        order.ReplaceLines(note, lines, acceptSalaryDeduction ? employeeId : null);
        request.Resubmit(JsonSerializer.Serialize(fields), resolved.Steps.Select(s => s.ToDomain()).ToList());
        await notifications.StageAsync(db, request, WorkflowNotificationEvent.Resubmitted, ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return new(false, "وضعیت سفارش هم‌زمان تغییر کرد؛ صفحه را دوباره باز کنید."); }
        return new(true, null, order.Id);
    }

    /// <summary>Checks the lines against the catalogue (active item, request period open today, not a lottery item, per-request maximum, stock) and sends the order for approval.</summary>
    /// <param name="shownTotal">The final price the page showed next to the tick box; if the price changed meanwhile the order is refused so the person can agree to the new amount.</param>
    /// <param name="acceptSalaryDeduction">The salary-deduction box was ticked. It is only needed when the order costs money (an item with a price). When an administrator places the order for someone, the administrator is recorded as the one who accepted.</param>
    public async Task<StorePlaceResult> PlaceAsync(Guid employeeId, Guid positionId, IReadOnlyList<(Guid ItemId, decimal Quantity)> wanted, string? note, DateTime today, Guid? createdByEmployeeId = null, bool acceptSalaryDeduction = false, decimal? shownTotal = null, CancellationToken ct = default)
    {
        if (wanted.Count == 0) return new(false, "کالایی برای درخواست انتخاب نشده است.");
        if (note is { Length: > StoreOrder.NoteMax }) return new(false, $"توضیحات حداکثر {StoreOrder.NoteMax} نویسه باشد.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var (lines, error) = await BuildLinesAsync(db, employeeId, wanted, today, null, ct);
        if (lines is null) return new(false, error);
        var total = TotalOf(lines);
        if (ChangedAmountMessage(total, shownTotal) is string changed) return new(false, changed);
        if (StorePricing.NeedsDeductionConsent(total) && !acceptSalaryDeduction) return new(false, DeductionRequiredMessage(total));

        // a double click (or a second tab) must not send the same order twice: refuse an identical order sent in the last two minutes
        var since = DateTime.UtcNow.AddMinutes(-2);
        var recent = await db.StoreOrders.AsNoTracking().Include(o => o.Lines).Where(o => o.EmployeeId == employeeId && o.CreatedAtUtc >= since).ToListAsync(ct);
        var signature = string.Join("|", lines.OrderBy(l => l.ItemId).Select(l => $"{l.ItemId}:{l.Quantity}"));
        if (recent.Any(o => string.Join("|", o.Lines.OrderBy(l => l.ItemId).Select(l => $"{l.ItemId}:{l.Quantity}")) == signature))
            return new(false, "همین سفارش چند لحظه پیش ثبت شده است. فهرست «سفارش‌های من» را ببینید.");

        // Work out the approvers from the position the person is acting in; if no valid path exists the order is refused.
        var resolved = await workflows.ResolveStepsAsync(WorkflowTypeCode.StoreOrder, positionId, ct);
        if (!resolved.Success) return new(false, resolved.Error);

        var summary = Summarize(lines.Select(l => new StoreOrderLineView(l.ItemCode, l.ItemName, l.Unit, l.Quantity, l.UnitPrice)));
        var fields = new Dictionary<string, string?> { ["summary"] = summary };
        if (!string.IsNullOrWhiteSpace(note)) fields["note"] = note.Trim();
        var request = new WorkflowRequest(WorkflowTypeCode.StoreOrder, employeeId, positionId, JsonSerializer.Serialize(fields), resolved.Steps.Select(s => s.ToDomain()).ToList(), createdByEmployeeId: createdByEmployeeId);
        // who agreed: the person themselves, or the administrator who recorded the agreement for them
        var order = new StoreOrder(request.Id, employeeId, positionId, note, lines, acceptSalaryDeduction ? createdByEmployeeId ?? employeeId : null);

        // The order and its approval request are saved together with the "request submitted" notifications, all or nothing.
        db.WorkflowRequests.Add(request);
        db.StoreOrders.Add(order);
        await notifications.StageAsync(db, request, WorkflowNotificationEvent.Submitted, ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, order.Id);
    }

    /// <summary>The person's own orders, newest first. Filtered by employee, so orders sent while acting in any position are listed.</summary>
    public async Task<(IReadOnlyList<StoreOrderRow> Rows, int Total)> ListMineAsync(Guid employeeId, int page, int pageSize, string? sort = null, bool descending = false, string? search = null, string? status = null, DateTime? createdFromUtc = null, DateTime? createdToUtcExclusive = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreOrders.AsNoTracking().Where(o => o.EmployeeId == employeeId);
        if (createdFromUtc is DateTime from) q = q.Where(o => o.CreatedAtUtc >= from);
        if (createdToUtcExclusive is DateTime to) q = q.Where(o => o.CreatedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => o.Lines.Any(l => l.ItemName.Contains(s) || l.ItemCode.Contains(s)));
        }
        q = ApplyStatus(db, q, status);
        return await PageAsync(db, q, page, pageSize, ct, sort: sort, descending: descending);
    }

    /// <summary>status: pending | waiting (approved, not handed over yet) | issued | rejected | returned | cancelled; anything else keeps every order.</summary>
    private static IQueryable<StoreOrder> ApplyStatus(ApplicationDbContext db, IQueryable<StoreOrder> q, string? status) =>
        status switch
        {
            "pending" => q.Where(o => db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.PendingApproval)),
            "waiting" => q.Where(o => o.IssuedAtUtc == null && db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.Approved)),
            "issued" => q.Where(o => o.IssuedAtUtc != null),
            "rejected" => q.Where(o => db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.Rejected)),
            "returned" => q.Where(o => db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.ReturnedToRequester)),
            "cancelled" => q.Where(o => db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.Cancelled)),
            _ => q,
        };

    /// <summary>Every store order, any employee and any status (the store administrator's overview); the same filters as «سفارش‌های من».</summary>
    public async Task<(IReadOnlyList<StoreOrderRow> Rows, int Total)> ListAllAsync(int page, int pageSize, string? sort = null, bool descending = false, string? search = null, string? status = null, DateTime? createdFromUtc = null, DateTime? createdToUtcExclusive = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        IQueryable<StoreOrder> q = db.StoreOrders.AsNoTracking();
        if (createdFromUtc is DateTime from) q = q.Where(o => o.CreatedAtUtc >= from);
        if (createdToUtcExclusive is DateTime to) q = q.Where(o => o.CreatedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => db.Employees.Any(e => e.Id == o.EmployeeId && e.FullName.Contains(s)) || o.Lines.Any(l => l.ItemName.Contains(s) || l.ItemCode.Contains(s)));
        }
        q = ApplyStatus(db, q, status);
        return await PageAsync(db, q, page, pageSize, ct, sort: sort, descending: descending);
    }

    /// <summary>
    /// Approved orders still waiting for the goods to be handed over, oldest first. <paramref name="search"/> looks at the requester's name and at the
    /// item name or code; the dates filter on the day the order was sent (the same filters as the hand-over history).
    /// </summary>
    public async Task<(IReadOnlyList<StoreOrderRow> Rows, int Total)> ListPendingIssueAsync(int page, int pageSize, string? sort = null, bool descending = false, string? search = null, DateTime? createdFromUtc = null, DateTime? createdToUtcExclusive = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreOrders.AsNoTracking().Where(o => o.IssuedAtUtc == null && db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.Approved));
        if (createdFromUtc is DateTime from) q = q.Where(o => o.CreatedAtUtc >= from);
        if (createdToUtcExclusive is DateTime to) q = q.Where(o => o.CreatedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => db.Employees.Any(e => e.Id == o.EmployeeId && e.FullName.Contains(s)) || o.Lines.Any(l => l.ItemName.Contains(s) || l.ItemCode.Contains(s)));
        }
        return await PageAsync(db, q, page, pageSize, ct, oldestFirst: true, sort: sort, descending: descending);
    }

    /// <summary>Orders already handed over, newest hand-over first (the storekeeper's history).</summary>
    public async Task<(IReadOnlyList<StoreOrderRow> Rows, int Total)> ListIssuedAsync(int page, int pageSize, string? search = null, DateTime? issuedFromUtc = null, DateTime? issuedToUtcExclusive = null, string? sort = null, bool descending = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreOrders.AsNoTracking().Where(o => o.IssuedAtUtc != null);
        if (issuedFromUtc is DateTime from) q = q.Where(o => o.IssuedAtUtc >= from);
        if (issuedToUtcExclusive is DateTime to) q = q.Where(o => o.IssuedAtUtc < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(o => db.Employees.Any(e => e.Id == o.EmployeeId && e.FullName.Contains(s)) || o.Lines.Any(l => l.ItemName.Contains(s) || l.ItemCode.Contains(s)));
        }
        return await PageAsync(db, q, page, pageSize, ct, byIssueDate: true, sort: sort, descending: descending);
    }

    // Shared paging helper for the order lists: counts the matches, applies the chosen column sort, then returns one page of rows.
    private static async Task<(IReadOnlyList<StoreOrderRow>, int)> PageAsync(ApplicationDbContext db, IQueryable<StoreOrder> q, int page, int pageSize, CancellationToken ct, bool oldestFirst = false, bool byIssueDate = false, string? sort = null, bool descending = false)
    {
        var total = await q.CountAsync(ct);
        var ordered = (sort, descending) switch
        {
            ("number", false) => q.OrderBy(o => o.Id),
            ("number", true) => q.OrderByDescending(o => o.Id),
            ("who", false) => q.OrderBy(o => db.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefault()).ThenBy(o => o.CreatedAtUtc),
            ("who", true) => q.OrderByDescending(o => db.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefault()).ThenBy(o => o.CreatedAtUtc),
            ("by", false) => q.OrderBy(o => db.Employees.Where(e => e.Id == o.IssuedByEmployeeId).Select(e => e.FullName).FirstOrDefault()).ThenBy(o => o.CreatedAtUtc),
            ("by", true) => q.OrderByDescending(o => db.Employees.Where(e => e.Id == o.IssuedByEmployeeId).Select(e => e.FullName).FirstOrDefault()).ThenBy(o => o.CreatedAtUtc),
            ("status", false) => q.OrderBy(o => db.WorkflowRequests.Where(r => r.Id == o.RequestId).Select(r => r.Status).FirstOrDefault()).ThenBy(o => o.CreatedAtUtc),
            ("status", true) => q.OrderByDescending(o => db.WorkflowRequests.Where(r => r.Id == o.RequestId).Select(r => r.Status).FirstOrDefault()).ThenBy(o => o.CreatedAtUtc),
            ("issued", false) => q.OrderBy(o => o.IssuedAtUtc),
            ("issued", true) => q.OrderByDescending(o => o.IssuedAtUtc),
            ("date", false) => q.OrderBy(o => o.CreatedAtUtc),
            ("date", true) => q.OrderByDescending(o => o.CreatedAtUtc),
            _ => byIssueDate ? q.OrderByDescending(o => o.IssuedAtUtc)
                : oldestFirst ? q.OrderBy(o => o.CreatedAtUtc) : q.OrderByDescending(o => o.CreatedAtUtc),
        };
        var rows = await ordered.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id, o.RequestId, o.CreatedAtUtc, o.IssuedAtUtc,
                Status = db.WorkflowRequests.Where(r => r.Id == o.RequestId).Select(r => r.Status).FirstOrDefault(),
                Lines = o.Lines.Select(l => new { l.ItemName, l.Unit, l.Quantity, l.UnitPrice }).ToList(),
                Who = db.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefault(),
                By = db.Employees.Where(e => e.Id == o.IssuedByEmployeeId).Select(e => e.FullName).FirstOrDefault(),
            }).ToListAsync(ct);
        return (rows.Select(r => new StoreOrderRow(r.Id, r.RequestId, r.CreatedAtUtc, r.Status, r.IssuedAtUtc,
            string.Join("، ", r.Lines.Select(l => $"{l.ItemName} {l.Quantity:0.###} {l.Unit}")), r.Who, r.By,
            StorePricing.Total(r.Lines.Select(l => (l.UnitPrice, l.Quantity))))).ToList(), total);
    }

    // Loads one order with the requester, position, hand-over person and lines for the detail and printable slip pages.
    public async Task<StoreOrderDetail?> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.StoreOrders.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (o is null) return null;
        var status = await db.WorkflowRequests.Where(r => r.Id == o.RequestId).Select(r => r.Status).FirstOrDefaultAsync(ct);
        var who = await db.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "";
        var position = await db.Positions.Where(p => p.Id == o.PositionId).Select(p => p.Name).FirstOrDefaultAsync(ct);
        var by = o.IssuedByEmployeeId is Guid bid ? await db.Employees.Where(e => e.Id == bid).Select(e => e.FullName).FirstOrDefaultAsync(ct) : null;
        var acceptedBy = o.SalaryDeductionAcceptedByEmployeeId is Guid aid ? await db.Employees.Where(e => e.Id == aid).Select(e => e.FullName).FirstOrDefaultAsync(ct) : null;
        return new StoreOrderDetail(o.Id, o.RequestId, o.EmployeeId, who, position, o.CreatedAtUtc, status, o.IssuedAtUtc, by, o.Note, o.IssueNote,
            o.Lines.OrderBy(l => l.ItemName).Select(l => new StoreOrderLineView(l.ItemCode, l.ItemName, l.Unit, l.Quantity, l.UnitPrice)).ToList(),
            o.SalaryDeductionAcceptedAtUtc, acceptedBy, o.SalaryDeductionAcceptedAmount);
    }

    /// <summary>
    /// Hands over several orders. Each order is its own all-or-nothing step: one that cannot be issued (not enough stock, already handed over)
    /// does not stop the others, and the result says what happened to each.
    /// </summary>
    public async Task<IReadOnlyList<(Guid OrderId, StoreResult Result)>> IssueManyAsync(IReadOnlyList<Guid> orderIds, Guid byEmployeeId, string? note, CancellationToken ct = default)
    {
        var results = new List<(Guid, StoreResult)>();
        // oldest first, so when stock runs short the people who asked first are served first
        foreach (var id in orderIds.Distinct())
        {
            try { results.Add((id, await IssueAsync(id, byEmployeeId, note, ct))); }
            catch (Exception) { results.Add((id, StoreResult.Fail("تحویل این سفارش انجام نشد."))); }
        }
        return results;
    }

    /// <summary>
    /// Undoes a hand-over (wrong order, goods brought back): one Return movement per line puts the quantities back into stock and the order waits
    /// for hand-over again. The ledger is never edited; the original Issue rows stay and the Return rows cancel them. A reason is required.
    /// </summary>
    public async Task<StoreResult> UndoIssueAsync(Guid orderId, Guid byEmployeeId, string? reason, CancellationToken ct = default)
    {
        reason = reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return StoreResult.Fail("دلیل برگشت تحویل را بنویسید.");
        if (reason.Length > StoreStockMovement.NoteMax) return StoreResult.Fail($"دلیل حداکثر {StoreStockMovement.NoteMax} نویسه باشد.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await db.StoreOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return StoreResult.Fail("سفارش پیدا نشد.");
        if (!order.IsIssued) return StoreResult.Fail("این سفارش تحویل داده نشده یا قبلاً برگشت خورده است.");
        var number = order.Id.ToString("N")[..8].ToUpperInvariant();
        foreach (var line in order.Lines)
            db.StoreStockMovements.Add(new StoreStockMovement(line.ItemId, StockMovementKind.Return, line.Quantity, "برگشت تحویل سفارش " + number, reason, byEmployeeId, order.Id));
        order.UndoIssue();
        db.Notifications.Add(new Notification(order.EmployeeId, NotificationKind.StoreOrderIssued, order.RequestId, WorkflowFieldCatalog.GetDisplayName(WorkflowTypeCode.StoreOrder),
            $"تحویل کالاهای سفارش {number} برگشت خورد و سفارش دوباره منتظر تحویل است. دلیل: {reason}", $"/store/orders/{order.Id}"));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StoreResult.Success;
    }

    /// <summary>
    /// Hands an approved order over: one Issue movement per line and the order is closed. Nothing is written unless every line is in stock;
    /// the stock check and the writes share one serializable transaction.
    /// </summary>
    // Stock is the sum of the movement rows, so handing over means adding one negative "Issue" row per line.
    public async Task<StoreResult> IssueAsync(Guid orderId, Guid byEmployeeId, string? note, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await db.StoreOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return StoreResult.Fail("سفارش پیدا نشد.");
        if (order.IsIssued) return StoreResult.Fail("این سفارش قبلاً تحویل داده شده است.");
        var status = await db.WorkflowRequests.Where(r => r.Id == order.RequestId).Select(r => r.Status).FirstOrDefaultAsync(ct);
        if (status != WorkflowRequestStatus.Approved) return StoreResult.Fail("این سفارش هنوز تأیید نهایی نشده است.");
        // an order that costs money is never handed over without the agreement to the salary deduction (for exactly its current total)
        if (!order.HasValidDeductionConsent) return StoreResult.Fail("موافقت با کسر مبلغ از حقوق برای این سفارش ثبت نشده است؛ تحویل ممکن نیست.");

        var ids = order.Lines.Select(l => l.ItemId).ToList();
        var stock = await db.StoreStockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Delta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var short_ = order.Lines.Where(l => stock.GetValueOrDefault(l.ItemId) < l.Quantity).ToList();
        if (short_.Count > 0)
            return StoreResult.Fail("موجودی کافی نیست: " + string.Join("، ", short_.Select(l => $"{l.ItemName} (نیاز {l.Quantity:0.###}، موجودی {stock.GetValueOrDefault(l.ItemId):0.###})")));

        var reference = "سفارش " + order.Id.ToString("N")[..8].ToUpperInvariant();
        foreach (var line in order.Lines)
            db.StoreStockMovements.Add(new StoreStockMovement(line.ItemId, StockMovementKind.Issue, line.Quantity, reference, null, byEmployeeId, order.Id));
        db.Notifications.Add(new Notification(order.EmployeeId, NotificationKind.StoreOrderIssued, order.RequestId, WorkflowFieldCatalog.GetDisplayName(WorkflowTypeCode.StoreOrder),
            $"کالاهای سفارش {order.Id.ToString("N")[..8].ToUpperInvariant()} تحویل داده شد.", $"/store/orders/{order.Id}"));
        try { order.MarkIssued(byEmployeeId, note); }
        catch (ArgumentException) { return StoreResult.Fail($"توضیحات حداکثر {StoreOrder.NoteMax} نویسه باشد."); }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StoreResult.Success;
    }
}

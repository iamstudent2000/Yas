using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Store;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;
using YasPortal.Infrastructure.Store;

namespace YasPortal.Web.Services;

public sealed record StoreOrderLineView(string ItemCode, string ItemName, string Unit, decimal Quantity);

public sealed record StoreOrderRow(Guid Id, Guid RequestId, DateTime CreatedAtUtc, WorkflowRequestStatus Status, DateTime? IssuedAtUtc, string Summary, string? RequesterName = null);

public sealed record StoreOrderDetail(Guid Id, Guid RequestId, Guid EmployeeId, string EmployeeName, string? PositionName, DateTime CreatedAtUtc, WorkflowRequestStatus Status,
    DateTime? IssuedAtUtc, string? IssuedByName, string? Note, string? IssueNote, IReadOnlyList<StoreOrderLineView> Lines)
{
    public string Number => Id.ToString("N")[..8].ToUpperInvariant();
}

public sealed record StorePlaceResult(bool Ok, string? Error = null, Guid? OrderId = null);

/// <summary>
/// Store orders. An order is owned by the person who sent it (listed by employee, so it is visible under every position they hold) and is
/// approved as an ordinary workflow request of type StoreOrder routed from the position they were acting in. Once the request is approved the
/// storekeeper hands the goods over, which writes the Issue movements and closes the order.
/// </summary>
public sealed class StoreOrderService(IDbContextFactory<ApplicationDbContext> dbFactory, WorkflowService workflows, NotificationService notifications)
{
    public static string StatusText(WorkflowRequestStatus status, bool issued) => status switch
    {
        WorkflowRequestStatus.PendingApproval => "در انتظار تأیید",
        WorkflowRequestStatus.Approved => issued ? "تحویل داده شد" : "تأیید شده، در انتظار تحویل",
        WorkflowRequestStatus.Rejected => "رد شد",
        WorkflowRequestStatus.ReturnedToRequester => "بازگشت به درخواست‌کننده",
        WorkflowRequestStatus.Cancelled => "لغو شد",
        _ => status.ToString(),
    };

    public static string Summarize(IEnumerable<StoreOrderLineView> lines) =>
        string.Join("\n", lines.Select(l => $"• {l.ItemName} ({l.ItemCode}) — {l.Quantity:0.###} {l.Unit}"));

    /// <summary>Checks the lines against the catalogue (active item, request period open today, not a lottery item, per-request maximum, stock) and sends the order for approval.</summary>
    public async Task<StorePlaceResult> PlaceAsync(Guid employeeId, Guid positionId, IReadOnlyList<(Guid ItemId, decimal Quantity)> wanted, string? note, DateTime today, CancellationToken ct = default)
    {
        if (wanted.Count == 0) return new(false, "سبد خرید خالی است.");
        if (note is { Length: > StoreOrder.NoteMax }) return new(false, $"توضیحات حداکثر {StoreOrder.NoteMax} نویسه باشد.");

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = wanted.Select(x => x.ItemId).ToList();
        var items = await db.StoreItems.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        var activeCategories = await db.StoreCategories.AsNoTracking().Where(c => c.IsActive).Select(c => c.Id).ToListAsync(ct);
        var stock = await db.StoreStockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Delta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

        var lines = new List<StoreOrderLine>();
        foreach (var (itemId, quantity) in wanted)
        {
            var item = items.FirstOrDefault(x => x.Id == itemId);
            if (item is null || !item.IsActive || !activeCategories.Contains(item.CategoryId)) return new(false, "یکی از کالاهای سبد دیگر در فروشگاه نیست؛ آن را از سبد حذف کنید.");
            var name = $"«{item.Name}»";
            if (quantity <= 0 || decimal.Round(quantity, 3) != quantity) return new(false, $"مقدار {name} باید عددی مثبت با حداکثر سه رقم اعشار باشد.");
            switch (item.WindowOn(today))
            {
                case StoreRequestWindow.NotYet: return new(false, $"دوره درخواست {name} هنوز شروع نشده است.");
                case StoreRequestWindow.Closed: return new(false, $"دوره درخواست {name} پایان یافته است.");
            }
            if (item.IsLottery) return new(false, $"{name} با قرعه‌کشی توزیع می‌شود و از سبد خرید قابل سفارش نیست.");
            if (item.MaxPerRequest > 0 && quantity > item.MaxPerRequest) return new(false, $"حداکثر مقدار {name} در هر درخواست {item.MaxPerRequest:0.###} {item.Unit} است.");
            var available = stock.GetValueOrDefault(itemId);
            if (quantity > available) return new(false, $"موجودی {name} کافی نیست (موجودی: {available:0.###} {item.Unit}).");
            lines.Add(new StoreOrderLine(item.Id, item.Code, item.Name, item.Unit, quantity));
        }

        var resolved = await workflows.ResolveStepsAsync(WorkflowTypeCode.StoreOrder, positionId, ct);
        if (!resolved.Success) return new(false, resolved.Error);

        var summary = Summarize(lines.Select(l => new StoreOrderLineView(l.ItemCode, l.ItemName, l.Unit, l.Quantity)));
        var fields = new Dictionary<string, string?> { ["summary"] = summary };
        if (!string.IsNullOrWhiteSpace(note)) fields["note"] = note.Trim();
        var request = new WorkflowRequest(WorkflowTypeCode.StoreOrder, employeeId, positionId, JsonSerializer.Serialize(fields), resolved.Steps.Select(s => s.ToDomain()).ToList());
        var order = new StoreOrder(request.Id, employeeId, positionId, note, lines);

        db.WorkflowRequests.Add(request);
        db.StoreOrders.Add(order);
        await notifications.StageAsync(db, request, WorkflowNotificationEvent.Submitted, ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, order.Id);
    }

    /// <summary>The person's own orders, newest first. Filtered by employee, so orders sent while acting in any position are listed.</summary>
    public async Task<(IReadOnlyList<StoreOrderRow> Rows, int Total)> ListMineAsync(Guid employeeId, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreOrders.AsNoTracking().Where(o => o.EmployeeId == employeeId);
        return await PageAsync(db, q, page, pageSize, ct);
    }

    /// <summary>Approved orders still waiting for the goods to be handed over, oldest first.</summary>
    public async Task<(IReadOnlyList<StoreOrderRow> Rows, int Total)> ListPendingIssueAsync(int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreOrders.AsNoTracking().Where(o => o.IssuedAtUtc == null && db.WorkflowRequests.Any(r => r.Id == o.RequestId && r.Status == WorkflowRequestStatus.Approved));
        return await PageAsync(db, q, page, pageSize, ct, oldestFirst: true);
    }

    private static async Task<(IReadOnlyList<StoreOrderRow>, int)> PageAsync(ApplicationDbContext db, IQueryable<StoreOrder> q, int page, int pageSize, CancellationToken ct, bool oldestFirst = false)
    {
        var total = await q.CountAsync(ct);
        var ordered = oldestFirst ? q.OrderBy(o => o.CreatedAtUtc) : q.OrderByDescending(o => o.CreatedAtUtc);
        var rows = await ordered.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(o => new
            {
                o.Id, o.RequestId, o.CreatedAtUtc, o.IssuedAtUtc,
                Status = db.WorkflowRequests.Where(r => r.Id == o.RequestId).Select(r => r.Status).FirstOrDefault(),
                Lines = o.Lines.Select(l => new { l.ItemName, l.Unit, l.Quantity }).ToList(),
                Who = db.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefault(),
            }).ToListAsync(ct);
        return (rows.Select(r => new StoreOrderRow(r.Id, r.RequestId, r.CreatedAtUtc, r.Status, r.IssuedAtUtc,
            string.Join("، ", r.Lines.Select(l => $"{l.ItemName} {l.Quantity:0.###} {l.Unit}")), r.Who)).ToList(), total);
    }

    public async Task<StoreOrderDetail?> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var o = await db.StoreOrders.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (o is null) return null;
        var status = await db.WorkflowRequests.Where(r => r.Id == o.RequestId).Select(r => r.Status).FirstOrDefaultAsync(ct);
        var who = await db.Employees.Where(e => e.Id == o.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "";
        var position = await db.Positions.Where(p => p.Id == o.PositionId).Select(p => p.Name).FirstOrDefaultAsync(ct);
        var by = o.IssuedByEmployeeId is Guid bid ? await db.Employees.Where(e => e.Id == bid).Select(e => e.FullName).FirstOrDefaultAsync(ct) : null;
        return new StoreOrderDetail(o.Id, o.RequestId, o.EmployeeId, who, position, o.CreatedAtUtc, status, o.IssuedAtUtc, by, o.Note, o.IssueNote,
            o.Lines.OrderBy(l => l.ItemName).Select(l => new StoreOrderLineView(l.ItemCode, l.ItemName, l.Unit, l.Quantity)).ToList());
    }

    /// <summary>
    /// Hands an approved order over: one Issue movement per line and the order is closed. Nothing is written unless every line is in stock;
    /// the stock check and the writes share one serializable transaction.
    /// </summary>
    public async Task<StoreResult> IssueAsync(Guid orderId, Guid byEmployeeId, string? note, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await db.StoreOrders.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct);
        if (order is null) return StoreResult.Fail("سفارش پیدا نشد.");
        if (order.IsIssued) return StoreResult.Fail("این سفارش قبلاً تحویل داده شده است.");
        var status = await db.WorkflowRequests.Where(r => r.Id == order.RequestId).Select(r => r.Status).FirstOrDefaultAsync(ct);
        if (status != WorkflowRequestStatus.Approved) return StoreResult.Fail("این سفارش هنوز تأیید نهایی نشده است.");

        var ids = order.Lines.Select(l => l.ItemId).ToList();
        var stock = await db.StoreStockMovements.Where(m => ids.Contains(m.ItemId)).GroupBy(m => m.ItemId)
            .Select(g => new { g.Key, Sum = g.Sum(m => m.Delta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var short_ = order.Lines.Where(l => stock.GetValueOrDefault(l.ItemId) < l.Quantity).ToList();
        if (short_.Count > 0)
            return StoreResult.Fail("موجودی کافی نیست: " + string.Join("، ", short_.Select(l => $"{l.ItemName} (نیاز {l.Quantity:0.###}، موجودی {stock.GetValueOrDefault(l.ItemId):0.###})")));

        var reference = "سفارش " + order.Id.ToString("N")[..8].ToUpperInvariant();
        foreach (var line in order.Lines)
            db.StoreStockMovements.Add(new StoreStockMovement(line.ItemId, StockMovementKind.Issue, line.Quantity, reference, null, byEmployeeId, order.Id));
        try { order.MarkIssued(byEmployeeId, note); }
        catch (ArgumentException) { return StoreResult.Fail($"توضیحات حداکثر {StoreOrder.NoteMax} نویسه باشد."); }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StoreResult.Success;
    }
}

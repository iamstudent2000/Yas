using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Store;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Persistence;
using YasPortal.Infrastructure.Store;

namespace YasPortal.Web.Services;

public sealed record StoreReportTable(string Title, string Description, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>The store's printable reports. Every report is a plain table so the page can show it, print it and export it the same way.</summary>
public sealed class StoreReportService(IDbContextFactory<ApplicationDbContext> dbFactory, StoreService store)
{
    public static readonly IReadOnlyList<(string Key, string Name, bool UsesDates, bool UsesDays)> Kinds =
    [
        ("categories", "خلاصه گروه‌ها", false, false),
        ("outofstock", "کالاهای ناموجود", false, false),
        ("issued", "کالاهای تحویل‌شده", true, false),
        ("dormant", "کالاهای راکد", false, true),
        ("orders", "وضعیت سفارش‌ها", true, false),
        ("requesters", "پرسفارش‌ترین کاربران", true, false),
    ];

    private static string Num(decimal v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    public async Task<StoreReportTable> BuildAsync(string key, DateTime? from, DateTime? to, int dormantDays, CancellationToken ct = default)
    {
        var fromUtc = from?.Date.ToUniversalTime();
        var toUtc = to?.Date.AddDays(1).ToUniversalTime();
        return key switch
        {
            "categories" => await CategoriesAsync(ct),
            "outofstock" => await OutOfStockAsync(ct),
            "issued" => await IssuedAsync(fromUtc, toUtc, ct),
            "dormant" => await DormantAsync(dormantDays, ct),
            "orders" => await OrdersAsync(fromUtc, toUtc, ct),
            "requesters" => await RequestersAsync(fromUtc, toUtc, ct),
            _ => new StoreReportTable("گزارش", "", [], []),
        };
    }

    private static string Row(DateTime? utc) => utc is DateTime d ? WorkflowDisplay.ToPersianDate(d.ToLocalTime()) : "—";

    private async Task<StoreReportTable> CategoriesAsync(CancellationToken ct)
    {
        var items = await store.AllItemsAsync(new StoreItemFilter(), ct);
        var categories = await store.ListCategoriesAsync(true, ct);
        var rows = categories.Select(c =>
        {
            var mine = items.Where(i => i.CategoryId == c.Id).ToList();
            return (IReadOnlyList<string>)[c.Name + (c.IsActive ? "" : " (غیرفعال)"), mine.Count.ToString(), mine.Count(i => i.IsActive).ToString(), mine.Count(i => i.IsActive && i.Stock <= 0).ToString(), mine.Count(i => i.IsLottery).ToString()];
        }).ToList();
        return new("خلاصه گروه‌ها", "تعداد کالاها در هر گروه و چند تا از کالاهای فعال ناموجودند.", ["گروه", "تعداد کالا", "کالای فعال", "فعال و ناموجود", "قرعه‌کشی"], rows);
    }

    private async Task<StoreReportTable> OutOfStockAsync(CancellationToken ct)
    {
        var items = await store.AllItemsAsync(new StoreItemFilter { Active = true, InStock = false, ActiveCategoriesOnly = true }, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = await db.StoreStockMovements.GroupBy(m => m.ItemId).Select(g => new { g.Key, Last = g.Max(m => m.CreatedAtUtc) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var rows = items.Select(i => (IReadOnlyList<string>)[i.Code, i.Name, i.CategoryName, i.Unit, Row(last.TryGetValue(i.Id, out var d) ? d : null)]).ToList();
        return new("کالاهای ناموجود", "کالاهای فعالی که موجودی‌شان صفر است و باید تأمین شوند.", ["کد", "کالا", "گروه", "واحد", "آخرین گردش"], rows);
    }

    private async Task<StoreReportTable> IssuedAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreStockMovements.AsNoTracking().Where(m => m.Kind == StockMovementKind.Issue || (m.Kind == StockMovementKind.Return && m.OrderId != null)); // a hand-over that was undone nets out
        if (fromUtc is DateTime f) q = q.Where(m => m.CreatedAtUtc >= f);
        if (toUtc is DateTime t) q = q.Where(m => m.CreatedAtUtc < t);
        var groups = await q.GroupBy(m => m.ItemId).Select(g => new { g.Key, Qty = -g.Sum(m => m.Delta), Times = g.Count(m => m.Kind == StockMovementKind.Issue) }).ToListAsync(ct);
        var items = (await store.AllItemsAsync(new StoreItemFilter(), ct)).ToDictionary(i => i.Id);
        var rows = groups.Where(g => items.ContainsKey(g.Key)).OrderByDescending(g => g.Qty)
            .Select(g => { var i = items[g.Key]; return (IReadOnlyList<string>)[i.Code, i.Name, i.CategoryName, i.Unit, Num(g.Qty), g.Times.ToString()]; }).ToList();
        return new("کالاهای تحویل‌شده", "مقدار خارج‌شده از انبار برای هر کالا در بازهٔ انتخاب‌شده (بیشترین در بالا). واحدها فرق دارند، پس مقدارها را با هم جمع نکنید.", ["کد", "کالا", "گروه", "واحد", "مقدار تحویل", "تعداد دفعات"], rows);
    }

    private async Task<StoreReportTable> DormantAsync(int days, CancellationToken ct)
    {
        days = Math.Clamp(days, 1, 3650);
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var items = await store.AllItemsAsync(new StoreItemFilter { Active = true, InStock = true }, ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = await db.StoreStockMovements.GroupBy(m => m.ItemId).Select(g => new { g.Key, Last = g.Max(m => m.CreatedAtUtc) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        var rows = items.Select(i => new { Item = i, Last = last.TryGetValue(i.Id, out var d) ? d : (DateTime?)null })
            .Where(x => x.Last is null || x.Last < cutoff).OrderBy(x => x.Last ?? DateTime.MinValue)
            .Select(x => (IReadOnlyList<string>)[x.Item.Code, x.Item.Name, x.Item.CategoryName, Num(x.Item.Stock), x.Item.Unit, Row(x.Last), x.Last is DateTime l ? ((int)(DateTime.UtcNow - l).TotalDays).ToString() : "—"]).ToList();
        return new("کالاهای راکد", $"کالاهای موجودی که {days} روز یا بیشتر هیچ ورود و خروجی نداشته‌اند.", ["کد", "کالا", "گروه", "موجودی", "واحد", "آخرین گردش", "روز از آخرین گردش"], rows);
    }

    private async Task<List<(Guid EmployeeId, WorkflowRequestStatus Status, bool Issued)>> OrderRowsAsync(DateTime? fromUtc, DateTime? toUtc, ApplicationDbContext db, CancellationToken ct)
    {
        var q = db.StoreOrders.AsNoTracking().AsQueryable();
        if (fromUtc is DateTime f) q = q.Where(o => o.CreatedAtUtc >= f);
        if (toUtc is DateTime t) q = q.Where(o => o.CreatedAtUtc < t);
        var rows = await (from o in q join r in db.WorkflowRequests.AsNoTracking() on o.RequestId equals r.Id select new { o.EmployeeId, r.Status, Issued = o.IssuedAtUtc != null }).ToListAsync(ct);
        return rows.Select(x => (x.EmployeeId, x.Status, x.Issued)).ToList();
    }

    private async Task<StoreReportTable> OrdersAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await OrderRowsAsync(fromUtc, toUtc, db, ct);
        var table = new List<IReadOnlyList<string>>();
        foreach (var status in Enum.GetValues<WorkflowRequestStatus>())
        {
            if (status == WorkflowRequestStatus.Approved)
            {
                table.Add([StoreOrderService.StatusText(status, false), rows.Count(x => x.Status == status && !x.Issued).ToString()]);
                table.Add([StoreOrderService.StatusText(status, true), rows.Count(x => x.Status == status && x.Issued).ToString()]);
            }
            else table.Add([StoreOrderService.StatusText(status, false), rows.Count(x => x.Status == status).ToString()]);
        }
        table.Add(["جمع", rows.Count.ToString()]);
        return new("وضعیت سفارش‌ها", "تعداد سفارش‌های ثبت‌شده در بازهٔ انتخاب‌شده به تفکیک وضعیت.", ["وضعیت", "تعداد سفارش"], table);
    }

    private async Task<StoreReportTable> RequestersAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await OrderRowsAsync(fromUtc, toUtc, db, ct);
        var ids = rows.Select(x => x.EmployeeId).Distinct().ToList();
        var names = await db.Employees.AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var table = rows.GroupBy(x => x.EmployeeId).Select(g => new { g.Key, Total = g.Count(), Issued = g.Count(x => x.Issued), Rejected = g.Count(x => x.Status == WorkflowRequestStatus.Rejected) })
            .OrderByDescending(x => x.Total).Take(50)
            .Select(x => (IReadOnlyList<string>)[names.GetValueOrDefault(x.Key, "—"), x.Total.ToString(), x.Issued.ToString(), x.Rejected.ToString()]).ToList();
        return new("پرسفارش‌ترین کاربران", "۵۰ نفری که بیشترین سفارش را در بازهٔ انتخاب‌شده ثبت کرده‌اند.", ["نام", "تعداد سفارش", "تحویل‌شده", "ردشده"], table);
    }
}

using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Store;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Store;

public sealed record StoreItemRow(Guid Id, string Code, string Name, Guid CategoryId, string CategoryName, string Unit, string? Description, bool IsActive, decimal Stock, decimal MaxPerRequest = 0, DateTime? RequestOpensOn = null, DateTime? RequestClosesOn = null, bool IsLottery = false, bool IsVisible = true)
{
    public StoreRequestWindow WindowOn(DateTime today) => StoreItem.WindowOf(RequestOpensOn, RequestClosesOn, today);
}

public sealed record StoreMovementRow(Guid Id, DateTime CreatedAtUtc, Guid ItemId, string ItemCode, string ItemName, string Unit, StockMovementKind Kind, decimal Delta, string? Reference, string? Note, string? CreatedBy);

public enum StoreWindowFilter { Any, Always, Open, NotYet, Closed }

/// <summary>The filters of the item lists. A value left empty does not filter.</summary>
public sealed class StoreItemFilter
{
    public string? Search { get; set; }
    public Guid? CategoryId { get; set; }
    public bool? Active { get; set; }
    /// <summary>Hide items whose group is switched off.</summary>
    public bool ActiveCategoriesOnly { get; set; }
    public bool? InStock { get; set; }
    public bool? Lottery { get; set; }
    /// <summary>Shown in the shop (true) or hidden by the admin (false).</summary>
    public bool? Visible { get; set; }
    public StoreWindowFilter Window { get; set; }
    /// <summary>Only items that can be put in a cart on this day: in stock, not a lottery item, request period open.</summary>
    public DateTime? RequestableOn { get; set; }
    /// <summary>The day "open", "not yet" and "closed" are judged on.</summary>
    public DateTime Today { get; set; } = DateTime.Today;
    /// <summary>name, code, stock or category (default).</summary>
    public string? Sort { get; set; }
    public bool Descending { get; set; }
}

public sealed record StorePage<T>(IReadOnlyList<T> Rows, int Total);

/// <summary>The result of a store operation that can fail for a reason the person should read (duplicate code, not enough stock, ...).</summary>
/// <summary>Reads a quantity typed by a person: Persian and Arabic digits, the Persian decimal point (٫) and a comma are all accepted.</summary>
public static class StoreNumber
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return true;   // an empty box means 0
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text.Trim())
        {
            if (ch is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));
            else if (ch is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));
            else if (ch is '\u066B' or '\u066C' or ',' or '/' or '\u060C') sb.Append(ch == '\u066C' ? "" : ".");
            else if (ch == '\u2212') sb.Append('-');
            else sb.Append(ch);
        }
        return decimal.TryParse(sb.ToString(), System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}

public sealed record StoreResult(bool Ok, string? Error = null)
{
    public static StoreResult Success { get; } = new(true);
    public static StoreResult Fail(string error) => new(false, error);
}

/// <summary>Catalogue and stock ledger of the internal store. Stock is always the sum of the movements and never goes below zero.</summary>
public sealed class StoreService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    // ---------- categories ----------

    public async Task<IReadOnlyList<StoreCategory>> ListCategoriesAsync(bool includeInactive, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.StoreCategories.AsNoTracking();
        if (!includeInactive) q = q.Where(x => x.IsActive);
        return await q.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(ct);
    }

    public async Task<StoreResult> SaveCategoryAsync(Guid? id, string name, int sortOrder, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        name = (name ?? "").Trim();
        if (name.Length == 0) return StoreResult.Fail("نام گروه را وارد کنید.");
        if (name.Length > StoreCategory.NameMax) return StoreResult.Fail("نام گروه بیش از حد طولانی است.");
        if (await db.StoreCategories.AnyAsync(x => x.Name == name && x.Id != id, ct)) return StoreResult.Fail("گروهی با این نام وجود دارد.");
        if (id is null) db.StoreCategories.Add(new StoreCategory(name, sortOrder));
        else
        {
            var category = await db.StoreCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (category is null) return StoreResult.Fail("گروه پیدا نشد.");
            category.Update(name, sortOrder);
        }
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    public async Task<StoreResult> SetCategoryActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var category = await db.StoreCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return StoreResult.Fail("گروه پیدا نشد.");
        if (active) category.Activate(); else category.Deactivate();
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    /// <summary>Deletes a group that has no items. A group with items can only be switched off.</summary>
    public async Task<StoreResult> DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var category = await db.StoreCategories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (category is null) return StoreResult.Success;
        if (await db.StoreItems.AnyAsync(x => x.CategoryId == id, ct)) return StoreResult.Fail("این گروه کالا دارد و حذف نمی‌شود؛ ابتدا کالاهای آن را حذف یا به گروه دیگری منتقل کنید، یا گروه را غیرفعال کنید.");
        db.StoreCategories.Remove(category);
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    /// <summary>Deletes an item that never had any stock movement. An item with history stays (the ledger is never rewritten) and can only be switched off.</summary>
    public async Task<StoreResult> DeleteItemAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.StoreItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return StoreResult.Success;
        if (await db.StoreStockMovements.AnyAsync(x => x.ItemId == id, ct)) return StoreResult.Fail("این کالا گردش انبار دارد و حذف نمی‌شود (تاریخچه انبار پاک نمی‌شود)؛ آن را غیرفعال کنید.");
        db.StoreItems.Remove(item);
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    // ---------- items ----------

    // A member-init projection (not a record constructor) so EF can still filter and order on its properties in SQL.
    private sealed class ItemProjection
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
        public string Name { get; set; } = "";
        public Guid CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public string Unit { get; set; } = "";
        public string? Description { get; set; }
        public bool IsActive { get; set; }
        public decimal Stock { get; set; }
        public decimal MaxPerRequest { get; set; }
        public DateTime? RequestOpensOn { get; set; }
        public DateTime? RequestClosesOn { get; set; }
        public bool IsLottery { get; set; }
        public bool IsVisible { get; set; }

        public StoreItemRow ToRow() => new(Id, Code, Name, CategoryId, CategoryName, Unit, Description, IsActive, Stock, MaxPerRequest, RequestOpensOn, RequestClosesOn, IsLottery, IsVisible);
    }

    private static IQueryable<ItemProjection> ItemRows(ApplicationDbContext db) =>
        from i in db.StoreItems.AsNoTracking()
        join c in db.StoreCategories.AsNoTracking() on i.CategoryId equals c.Id
        select new ItemProjection
        {
            Id = i.Id, Code = i.Code, Name = i.Name, CategoryId = i.CategoryId, CategoryName = c.Name, Unit = i.Unit, Description = i.Description,
            MaxPerRequest = i.MaxPerRequest, RequestOpensOn = i.RequestOpensOn, RequestClosesOn = i.RequestClosesOn, IsLottery = i.IsLottery, IsVisible = i.IsVisible, IsActive = i.IsActive,
            Stock = db.StoreStockMovements.Where(m => m.ItemId == i.Id).Sum(m => (decimal?)m.Delta) ?? 0m,
        };

    /// <summary>Items with their current stock. <paramref name="activeOnly"/> hides items and categories that are switched off.</summary>
    public Task<StorePage<StoreItemRow>> SearchItemsAsync(string? search, Guid? categoryId, bool activeOnly, int page, int pageSize, bool? inStock = null, CancellationToken ct = default) =>
        SearchItemsAsync(new StoreItemFilter { Search = search, CategoryId = categoryId, Active = activeOnly ? true : null, Visible = activeOnly ? true : null, ActiveCategoriesOnly = activeOnly, InStock = inStock }, page, pageSize, ct);

    /// <summary>Items matching every filter that is set, in the chosen order (default: group, then name).</summary>
    public async Task<StorePage<StoreItemRow>> SearchItemsAsync(StoreItemFilter f, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = ApplyFilter(db, ItemRows(db), f);
        var total = await q.CountAsync(ct);
        var ordered = Order(q, f.Sort, f.Descending);
        var rows = await ordered.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new StorePage<StoreItemRow>(rows.Select(r => r.ToRow()).ToList(), total);
    }

    /// <summary>Every item matching the filter, no paging (for exports and reports).</summary>
    public async Task<IReadOnlyList<StoreItemRow>> AllItemsAsync(StoreItemFilter f, CancellationToken ct = default) =>
        (await SearchItemsAsync(f, 1, 100_000, ct)).Rows;

    private static IQueryable<ItemProjection> ApplyFilter(ApplicationDbContext db, IQueryable<ItemProjection> q, StoreItemFilter f)
    {
        if (f.Active is bool active) q = q.Where(x => x.IsActive == active);
        if (f.ActiveCategoriesOnly) q = q.Where(x => db.StoreCategories.Any(c => c.Id == x.CategoryId && c.IsActive));
        if (f.CategoryId is Guid cid) q = q.Where(x => x.CategoryId == cid);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(x => x.Name.Contains(s) || x.Code.Contains(s));
        }
        if (f.InStock is true) q = q.Where(x => x.Stock > 0);
        else if (f.InStock is false) q = q.Where(x => x.Stock <= 0);
        if (f.Lottery is bool lottery) q = q.Where(x => x.IsLottery == lottery);
        if (f.Visible is bool visible) q = q.Where(x => x.IsVisible == visible);
        var today = f.Today.Date;
        if (f.RequestableOn is DateTime on)
        {
            var day = on.Date;
            q = q.Where(x => x.Stock > 0 && !x.IsLottery && (x.RequestOpensOn == null || x.RequestOpensOn <= day) && (x.RequestClosesOn == null || x.RequestClosesOn >= day));
        }
        switch (f.Window)
        {
            case StoreWindowFilter.Always: q = q.Where(x => x.RequestOpensOn == null && x.RequestClosesOn == null); break;
            case StoreWindowFilter.Open: q = q.Where(x => (x.RequestOpensOn == null || x.RequestOpensOn <= today) && (x.RequestClosesOn == null || x.RequestClosesOn >= today)); break;
            case StoreWindowFilter.NotYet: q = q.Where(x => x.RequestOpensOn != null && x.RequestOpensOn > today); break;
            case StoreWindowFilter.Closed: q = q.Where(x => x.RequestClosesOn != null && x.RequestClosesOn < today); break;
        }
        return q;
    }

    private static IOrderedQueryable<ItemProjection> Order(IQueryable<ItemProjection> q, string? sort, bool desc) => (sort, desc) switch
    {
        ("name", false) => q.OrderBy(x => x.Name),
        ("name", true) => q.OrderByDescending(x => x.Name),
        ("code", false) => q.OrderBy(x => x.Code),
        ("code", true) => q.OrderByDescending(x => x.Code),
        ("stock", false) => q.OrderBy(x => x.Stock).ThenBy(x => x.Name),
        ("stock", true) => q.OrderByDescending(x => x.Stock).ThenBy(x => x.Name),
        (_, true) => q.OrderByDescending(x => x.CategoryName).ThenBy(x => x.Name),
        _ => q.OrderBy(x => x.CategoryName).ThenBy(x => x.Name),
    };

    /// <summary>All items matching the filters with no paging, for printing.</summary>
    public async Task<IReadOnlyList<StoreItemRow>> AllItemsAsync(string? search, Guid? categoryId, CancellationToken ct = default) =>
        (await SearchItemsAsync(search, categoryId, false, 1, 100_000, ct: ct)).Rows;

    public async Task<StoreItem?> GetItemAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.StoreItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StoreResult> SaveItemAsync(Guid? id, string code, string name, Guid categoryId, string unit, string? description, decimal maxPerRequest, DateTime? requestOpensOn, DateTime? requestClosesOn, bool isLottery, decimal initialStock, Guid? byEmployeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        code = (code ?? "").Trim();
        if (await db.StoreItems.AnyAsync(x => x.Code == code && x.Id != id, ct)) return StoreResult.Fail("کالایی با این کد وجود دارد.");
        if (!await db.StoreCategories.AnyAsync(x => x.Id == categoryId, ct)) return StoreResult.Fail("گروه کالا را انتخاب کنید.");
        unit = (unit ?? "").Trim();
        if (initialStock < 0 || decimal.Round(initialStock, 3) != initialStock) return StoreResult.Fail("موجودی اولیه باید عدد صفر یا بیشتر باشد.");
        // a unit outside the list is accepted only when the item already has it (older data)
        if (!StoreUnits.All.Contains(unit) && !(id is Guid existing && await db.StoreItems.AnyAsync(x => x.Id == existing && x.Unit == unit, ct)))
            return StoreResult.Fail("واحد را از فهرست انتخاب کنید.");
        try
        {
            if (id is null)
            {
                var created = new StoreItem(code, name, categoryId, unit, description, maxPerRequest, requestOpensOn, requestClosesOn, isLottery);
                db.StoreItems.Add(created);
                if (initialStock > 0) db.StoreStockMovements.Add(new StoreStockMovement(created.Id, StockMovementKind.Receive, initialStock, "موجودی اولیه", null, byEmployeeId));
            }
            else
            {
                var item = await db.StoreItems.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (item is null) return StoreResult.Fail("کالا پیدا نشد.");
                item.Update(code, name, categoryId, unit, description, maxPerRequest, requestOpensOn, requestClosesOn, isLottery);
            }
        }
        catch (ArgumentException)
        {
            return StoreResult.Fail("اطلاعات کالا کامل یا معتبر نیست (کد، نام، واحد و گروه لازم‌اند و پایان دوره درخواست نباید پیش از شروع آن باشد).");
        }
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    /// <summary>Shows or hides several items in the shop at once. Hidden items stay in the admin lists and keep their stock.</summary>
    public async Task<StoreResult> SetItemsVisibleAsync(IReadOnlyCollection<Guid> ids, bool visible, CancellationToken ct = default)
    {
        if (ids.Count == 0) return StoreResult.Fail("هیچ کالایی انتخاب نشده است.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.StoreItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        foreach (var item in items) item.SetVisible(visible);
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    public async Task<StoreResult> SetItemActiveAsync(Guid id, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.StoreItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return StoreResult.Fail("کالا پیدا نشد.");
        if (active) item.Activate(); else item.Deactivate();
        await db.SaveChangesAsync(ct);
        return StoreResult.Success;
    }

    // ---------- stock ----------

    public async Task<decimal> StockOfAsync(Guid itemId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.StoreStockMovements.Where(m => m.ItemId == itemId).SumAsync(m => (decimal?)m.Delta, ct) ?? 0m;
    }

    /// <summary>
    /// Adds one row to the ledger. An issue or a negative correction is refused when it would take the stock below zero.
    /// The check and the insert run in one serializable transaction so two storekeepers cannot both take the last unit.
    /// </summary>
    public async Task<StoreResult> RecordMovementAsync(Guid itemId, StockMovementKind kind, decimal quantity, string? reference, string? note, Guid? byEmployeeId, CancellationToken ct = default)
    {
        StoreStockMovement movement;
        try { movement = new StoreStockMovement(itemId, kind, quantity, reference, note, byEmployeeId); }
        catch (ArgumentException) { return StoreResult.Fail("مقدار باید عددی غیر صفر با حداکثر سه رقم اعشار باشد (برای ورود، خروج و برگشت مثبت)."); }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.StoreItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (item is null) return StoreResult.Fail("کالا پیدا نشد.");
        if (!item.IsActive && movement.Delta > 0 && kind == StockMovementKind.Receive) return StoreResult.Fail("این کالا غیرفعال است.");
        if (!StoreUnits.AllowsFraction(item.Unit) && quantity != decimal.Truncate(quantity))
            return StoreResult.Fail($"واحد «{item.Unit}» شمارشی است و مقدار باید عدد صحیح باشد (مثلاً ۵، نه ۵٫۵).");

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        if (movement.Delta < 0)
        {
            var stock = await db.StoreStockMovements.Where(m => m.ItemId == itemId).SumAsync(m => (decimal?)m.Delta, ct) ?? 0m;
            if (stock + movement.Delta < 0) return StoreResult.Fail($"موجودی کافی نیست؛ موجودی فعلی {stock:0.###} {item.Unit} است.");
        }
        db.StoreStockMovements.Add(movement);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return StoreResult.Success;
    }

    public async Task<StorePage<StoreMovementRow>> SearchMovementsAsync(Guid? itemId, StockMovementKind? kind, DateTime? fromUtc, DateTime? toUtcExclusive, int page, int pageSize, string? text = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = from m in db.StoreStockMovements.AsNoTracking()
                join i in db.StoreItems.AsNoTracking() on m.ItemId equals i.Id
                select new { m, i };
        if (itemId is Guid id) q = q.Where(x => x.m.ItemId == id);
        if (kind is StockMovementKind k) q = q.Where(x => x.m.Kind == k);
        if (fromUtc is DateTime f) q = q.Where(x => x.m.CreatedAtUtc >= f);
        if (toUtcExclusive is DateTime t) q = q.Where(x => x.m.CreatedAtUtc < t);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var s = text.Trim();
            q = q.Where(x => (x.m.Reference != null && x.m.Reference.Contains(s)) || (x.m.Note != null && x.m.Note.Contains(s)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.m.CreatedAtUtc)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.m.Id, x.m.CreatedAtUtc, x.m.ItemId, ItemCode = x.i.Code, ItemName = x.i.Name, x.i.Unit, x.m.Kind, x.m.Delta, x.m.Reference, x.m.Note,
                By = db.Employees.Where(e => e.Id == x.m.CreatedByEmployeeId).Select(e => e.FullName).FirstOrDefault()
            }).ToListAsync(ct);
        return new StorePage<StoreMovementRow>(rows.Select(r => new StoreMovementRow(r.Id, r.CreatedAtUtc, r.ItemId, r.ItemCode, r.ItemName, r.Unit, r.Kind, r.Delta, r.Reference, r.Note, r.By)).ToList(), total);
    }

    public static string KindLabel(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Receive => "ورود به انبار",
        StockMovementKind.Issue => "خروج از انبار",
        StockMovementKind.Adjustment => "اصلاح موجودی",
        StockMovementKind.Return => "برگشت به انبار",
        _ => kind.ToString(),
    };
}

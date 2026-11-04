using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Store;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Store;

public sealed record StoreItemRow(Guid Id, string Code, string Name, Guid CategoryId, string CategoryName, string Unit, string? Description, decimal MinStock, bool IsActive, decimal Stock)
{
    /// <summary>Low = an alert level is set and the stock has reached it.</summary>
    public bool IsLow => MinStock > 0 && Stock <= MinStock;
}

public sealed record StoreMovementRow(Guid Id, DateTime CreatedAtUtc, Guid ItemId, string ItemCode, string ItemName, string Unit, StockMovementKind Kind, decimal Delta, string? Reference, string? Note, string? CreatedBy);

public sealed record StorePage<T>(IReadOnlyList<T> Rows, int Total);

/// <summary>The result of a store operation that can fail for a reason the person should read (duplicate code, not enough stock, ...).</summary>
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
        public decimal MinStock { get; set; }
        public bool IsActive { get; set; }
        public decimal Stock { get; set; }

        public StoreItemRow ToRow() => new(Id, Code, Name, CategoryId, CategoryName, Unit, Description, MinStock, IsActive, Stock);
    }

    private static IQueryable<ItemProjection> ItemRows(ApplicationDbContext db) =>
        from i in db.StoreItems.AsNoTracking()
        join c in db.StoreCategories.AsNoTracking() on i.CategoryId equals c.Id
        select new ItemProjection
        {
            Id = i.Id, Code = i.Code, Name = i.Name, CategoryId = i.CategoryId, CategoryName = c.Name, Unit = i.Unit, Description = i.Description,
            MinStock = i.MinStock, IsActive = i.IsActive,
            Stock = db.StoreStockMovements.Where(m => m.ItemId == i.Id).Sum(m => (decimal?)m.Delta) ?? 0m,
        };

    /// <summary>Items with their current stock. <paramref name="activeOnly"/> hides items and categories that are switched off.</summary>
    public async Task<StorePage<StoreItemRow>> SearchItemsAsync(string? search, Guid? categoryId, bool activeOnly, bool lowOnly, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = ItemRows(db);
        if (activeOnly) q = q.Where(x => x.IsActive && db.StoreCategories.Any(c => c.Id == x.CategoryId && c.IsActive));
        if (categoryId is Guid cid) q = q.Where(x => x.CategoryId == cid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Name.Contains(s) || x.Code.Contains(s));
        }
        if (lowOnly) q = q.Where(x => x.MinStock > 0 && x.Stock <= x.MinStock);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.CategoryName).ThenBy(x => x.Name)
            .Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new StorePage<StoreItemRow>(rows.Select(r => r.ToRow()).ToList(), total);
    }

    /// <summary>All items matching the filters with no paging, for printing.</summary>
    public async Task<IReadOnlyList<StoreItemRow>> AllItemsAsync(string? search, Guid? categoryId, bool lowOnly, CancellationToken ct = default) =>
        (await SearchItemsAsync(search, categoryId, false, lowOnly, 1, 100_000, ct)).Rows;

    public async Task<StoreItem?> GetItemAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.StoreItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<StoreResult> SaveItemAsync(Guid? id, string code, string name, Guid categoryId, string unit, string? description, decimal minStock, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        code = (code ?? "").Trim();
        if (await db.StoreItems.AnyAsync(x => x.Code == code && x.Id != id, ct)) return StoreResult.Fail("کالایی با این کد وجود دارد.");
        if (!await db.StoreCategories.AnyAsync(x => x.Id == categoryId, ct)) return StoreResult.Fail("گروه کالا را انتخاب کنید.");
        try
        {
            if (id is null) db.StoreItems.Add(new StoreItem(code, name, categoryId, unit, description, minStock));
            else
            {
                var item = await db.StoreItems.FirstOrDefaultAsync(x => x.Id == id, ct);
                if (item is null) return StoreResult.Fail("کالا پیدا نشد.");
                item.Update(code, name, categoryId, unit, description, minStock);
            }
        }
        catch (ArgumentException)
        {
            return StoreResult.Fail("اطلاعات کالا کامل یا معتبر نیست (کد، نام، واحد و گروه لازم‌اند).");
        }
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

    public async Task<StorePage<StoreMovementRow>> SearchMovementsAsync(Guid? itemId, StockMovementKind? kind, DateTime? fromUtc, DateTime? toUtcExclusive, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = from m in db.StoreStockMovements.AsNoTracking()
                join i in db.StoreItems.AsNoTracking() on m.ItemId equals i.Id
                select new { m, i };
        if (itemId is Guid id) q = q.Where(x => x.m.ItemId == id);
        if (kind is StockMovementKind k) q = q.Where(x => x.m.Kind == k);
        if (fromUtc is DateTime f) q = q.Where(x => x.m.CreatedAtUtc >= f);
        if (toUtcExclusive is DateTime t) q = q.Where(x => x.m.CreatedAtUtc < t);
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

namespace YasPortal.Domain.Store;

/// <summary>A group of store items (stationery, IT supplies, ...).</summary>
public sealed class StoreCategory
{
    public const int NameMax = 100;

    private StoreCategory()
    {
    }

    public StoreCategory(string name, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(name, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string name, int sortOrder)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > NameMax)
            throw new ArgumentException($"Name is required and at most {NameMax} characters.", nameof(name));
        Name = name;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

/// <summary>An item the store keeps. Its stock is never stored on the item: it is the sum of its <see cref="StoreStockMovement"/> rows.</summary>
public sealed class StoreItem
{
    public const int CodeMax = 50, NameMax = 200, UnitMax = 30, DescriptionMax = 500;

    private StoreItem()
    {
    }

    public StoreItem(string code, string name, Guid categoryId, string unit, string? description, decimal minStock)
    {
        Id = Guid.NewGuid();
        Update(code, name, categoryId, unit, description, minStock);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = "";
    public string Name { get; private set; } = "";
    public Guid CategoryId { get; private set; }
    public string Unit { get; private set; } = "";
    public string? Description { get; private set; }
    /// <summary>Stock at or below this number is shown as low (0 means "no alert").</summary>
    public decimal MinStock { get; private set; }
    public bool IsActive { get; private set; } = true;

    public void Update(string code, string name, Guid categoryId, string unit, string? description, decimal minStock)
    {
        code = (code ?? "").Trim();
        name = (name ?? "").Trim();
        unit = (unit ?? "").Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (code.Length == 0 || code.Length > CodeMax) throw new ArgumentException($"Code is required and at most {CodeMax} characters.", nameof(code));
        if (name.Length == 0 || name.Length > NameMax) throw new ArgumentException($"Name is required and at most {NameMax} characters.", nameof(name));
        if (unit.Length == 0 || unit.Length > UnitMax) throw new ArgumentException($"Unit is required and at most {UnitMax} characters.", nameof(unit));
        if (categoryId == Guid.Empty) throw new ArgumentException("Category is required.", nameof(categoryId));
        if (description is { Length: > DescriptionMax }) throw new ArgumentException($"Description is at most {DescriptionMax} characters.", nameof(description));
        if (minStock < 0) throw new ArgumentException("Minimum stock cannot be negative.", nameof(minStock));
        Code = code;
        Name = name;
        CategoryId = categoryId;
        Unit = unit;
        Description = description;
        MinStock = minStock;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public enum StockMovementKind
{
    /// <summary>Goods came in (positive).</summary>
    Receive = 0,
    /// <summary>Goods handed out (negative).</summary>
    Issue = 1,
    /// <summary>A count correction (either sign).</summary>
    Adjustment = 2,
    /// <summary>Goods handed back (positive).</summary>
    Return = 3,
}

/// <summary>One line of the stock ledger. Rows are only ever added, never edited or deleted; a mistake is corrected by a new row.</summary>
public sealed class StoreStockMovement
{
    public const int ReferenceMax = 200, NoteMax = 300;

    private StoreStockMovement()
    {
    }

    /// <summary><paramref name="quantity"/> is always positive for Receive, Issue and Return; for Adjustment it is the signed change.</summary>
    public StoreStockMovement(Guid itemId, StockMovementKind kind, decimal quantity, string? reference, string? note, Guid? createdByEmployeeId, Guid? orderId = null)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item is required.", nameof(itemId));
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown movement kind.", nameof(kind));
        if (quantity == 0) throw new ArgumentException("Quantity cannot be zero.", nameof(quantity));
        if (kind != StockMovementKind.Adjustment && quantity < 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (reference is { Length: > ReferenceMax }) throw new ArgumentException($"Reference is at most {ReferenceMax} characters.", nameof(reference));
        if (note is { Length: > NoteMax }) throw new ArgumentException($"Note is at most {NoteMax} characters.", nameof(note));
        if (decimal.Round(quantity, 3) != quantity) throw new ArgumentException("At most three decimals.", nameof(quantity));

        Id = Guid.NewGuid();
        ItemId = itemId;
        Kind = kind;
        Delta = kind == StockMovementKind.Issue ? -quantity : quantity;
        Reference = reference;
        Note = note;
        CreatedByEmployeeId = createdByEmployeeId == Guid.Empty ? null : createdByEmployeeId;
        OrderId = orderId == Guid.Empty ? null : orderId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public StockMovementKind Kind { get; private set; }
    /// <summary>Signed change of the stock: positive adds, negative removes.</summary>
    public decimal Delta { get; private set; }
    public string? Reference { get; private set; }
    public string? Note { get; private set; }
    public Guid? CreatedByEmployeeId { get; private set; }
    /// <summary>The store order this movement belongs to (set by ordering, later).</summary>
    public Guid? OrderId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}

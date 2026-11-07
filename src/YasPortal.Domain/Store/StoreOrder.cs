namespace YasPortal.Domain.Store;

/// <summary>
/// What a person asked for from the store. It belongs to the person (<see cref="EmployeeId"/>, so it is visible under every position they
/// hold) while its approval is organizational: the linked workflow request was routed from <see cref="PositionId"/>, the position the
/// person was acting in when they sent it. Whether the order is waiting, approved or refused is the status of that request; the store only
/// adds the hand-over (<see cref="IssuedAtUtc"/>).
/// </summary>
public sealed class StoreOrder
{
    public const int NoteMax = 500;

    private StoreOrder()
    {
    }

    public StoreOrder(Guid requestId, Guid employeeId, Guid positionId, string? note, IEnumerable<StoreOrderLine> lines)
    {
        if (requestId == Guid.Empty) throw new ArgumentException("Request is required.", nameof(requestId));
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee is required.", nameof(employeeId));
        if (positionId == Guid.Empty) throw new ArgumentException("Position is required.", nameof(positionId));
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > NoteMax }) throw new ArgumentException($"Note is at most {NoteMax} characters.", nameof(note));
        var list = lines?.ToList() ?? [];
        if (list.Count == 0) throw new ArgumentException("An order needs at least one line.", nameof(lines));
        if (list.Select(x => x.ItemId).Distinct().Count() != list.Count) throw new ArgumentException("An item can appear once.", nameof(lines));

        Id = Guid.NewGuid();
        RequestId = requestId;
        EmployeeId = employeeId;
        PositionId = positionId;
        Note = note;
        CreatedAtUtc = DateTime.UtcNow;
        foreach (var line in list) { line.Attach(Id); Lines.Add(line); }
    }

    public Guid Id { get; private set; }
    /// <summary>The approval request of this order (one to one).</summary>
    public Guid RequestId { get; private set; }
    /// <summary>The person the order belongs to. Lists of "my orders" filter on this, never on a position.</summary>
    public Guid EmployeeId { get; private set; }
    /// <summary>The position the person acted in when they sent the order (used only to route the approval).</summary>
    public Guid PositionId { get; private set; }
    public string? Note { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? IssuedAtUtc { get; private set; }
    public Guid? IssuedByEmployeeId { get; private set; }
    public string? IssueNote { get; private set; }
    public List<StoreOrderLine> Lines { get; private set; } = [];

    public bool IsIssued => IssuedAtUtc is not null;

    public void MarkIssued(Guid byEmployeeId, string? note)
    {
        if (IsIssued) throw new InvalidOperationException("The order was already handed over.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > NoteMax }) throw new ArgumentException($"Note is at most {NoteMax} characters.", nameof(note));
        IssuedAtUtc = DateTime.UtcNow;
        IssuedByEmployeeId = byEmployeeId == Guid.Empty ? null : byEmployeeId;
        IssueNote = note;
    }
}

/// <summary>One item and quantity of an order. The code, name and unit are copied so the order still reads correctly if the item is renamed later.</summary>
public sealed class StoreOrderLine
{
    private StoreOrderLine()
    {
    }

    public StoreOrderLine(Guid itemId, string itemCode, string itemName, string unit, decimal quantity)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item is required.", nameof(itemId));
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity) throw new ArgumentException("Quantity must be positive with at most three decimals.", nameof(quantity));
        Id = Guid.NewGuid();
        ItemId = itemId;
        ItemCode = itemCode;
        ItemName = itemName;
        Unit = unit;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public string ItemCode { get; private set; } = "";
    public string ItemName { get; private set; } = "";
    public string Unit { get; private set; } = "";
    public decimal Quantity { get; private set; }

    internal void Attach(Guid orderId) => OrderId = orderId;
}

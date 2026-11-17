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

    /// <param name="deductionAcceptedBy">Who agreed that the total is taken from the salary (the employee, or an administrator recording it for them). Required when the order costs money.</param>
    /// <param name="deductionAcceptedAtUtc">When they agreed; empty means "now". A lottery winner agreed when they applied, which is earlier than the order itself.</param>
    public StoreOrder(Guid requestId, Guid employeeId, Guid positionId, string? note, IEnumerable<StoreOrderLine> lines, Guid? deductionAcceptedBy = null, DateTime? deductionAcceptedAtUtc = null)
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
        RecordDeductionConsent(deductionAcceptedBy, deductionAcceptedAtUtc);
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

    /// <summary>When the person agreed that the total is taken from their salary. Empty when the order costs nothing.</summary>
    public DateTime? SalaryDeductionAcceptedAtUtc { get; private set; }
    /// <summary>Who agreed. Normally the owner of the order; when an administrator placed the order for someone, the administrator who recorded the agreement.</summary>
    public Guid? SalaryDeductionAcceptedByEmployeeId { get; private set; }
    /// <summary>The exact amount that was agreed to. If the lines change later, this no longer matches the total and the agreement has to be given again.</summary>
    public decimal? SalaryDeductionAcceptedAmount { get; private set; }

    public bool IsIssued => IssuedAtUtc is not null;

    /// <summary>The final price of the order (the sum of its line totals). Zero when no item had a price.</summary>
    public decimal TotalPrice => StorePricing.Total(Lines.Select(l => (l.UnitPrice, l.Quantity)));

    /// <summary>True when the order costs money, so an agreement to the salary deduction is needed.</summary>
    public bool RequiresSalaryDeduction => StorePricing.NeedsDeductionConsent(TotalPrice);

    /// <summary>True when nothing needs agreeing to, or the agreement on record is for exactly the current total.</summary>
    public bool HasValidDeductionConsent => !RequiresSalaryDeduction || (SalaryDeductionAcceptedAtUtc is not null && SalaryDeductionAcceptedAmount == TotalPrice);

    /// <summary>
    /// Writes down the salary-deduction agreement for the current lines. An order that costs money cannot be created or changed without it;
    /// an order that costs nothing carries no agreement (any old one is cleared, because it was for a different amount).
    /// </summary>
    private void RecordDeductionConsent(Guid? acceptedBy, DateTime? acceptedAtUtc)
    {
        var total = TotalPrice;
        if (!StorePricing.NeedsDeductionConsent(total))
        {
            SalaryDeductionAcceptedAtUtc = null;
            SalaryDeductionAcceptedByEmployeeId = null;
            SalaryDeductionAcceptedAmount = null;
            return;
        }
        if (acceptedBy is not Guid who || who == Guid.Empty)
            throw new ArgumentException("An order that costs money needs the agreement to the salary deduction.", nameof(acceptedBy));
        SalaryDeductionAcceptedAtUtc = acceptedAtUtc ?? DateTime.UtcNow;
        SalaryDeductionAcceptedByEmployeeId = who;
        SalaryDeductionAcceptedAmount = total;
    }

    /// <summary>
    /// Replaces the items (and the note) of an order that came back for correction. The same rules as a new order: at least one line, an item once,
    /// and the salary-deduction agreement must be given again for the new total (the old agreement was for the old amount).
    /// </summary>
    public void ReplaceLines(string? note, IEnumerable<StoreOrderLine> lines, Guid? deductionAcceptedBy = null, DateTime? deductionAcceptedAtUtc = null)
    {
        if (IsIssued) throw new InvalidOperationException("An order that was handed over cannot be changed.");
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (note is { Length: > NoteMax }) throw new ArgumentException($"Note is at most {NoteMax} characters.", nameof(note));
        var list = lines?.ToList() ?? [];
        if (list.Count == 0) throw new ArgumentException("An order needs at least one line.", nameof(lines));
        if (list.Select(x => x.ItemId).Distinct().Count() != list.Count) throw new ArgumentException("An item can appear once.", nameof(lines));
        var oldLines = Lines.ToList();
        Lines.Clear();
        foreach (var line in list) { line.Attach(Id); Lines.Add(line); }
        try { RecordDeductionConsent(deductionAcceptedBy, deductionAcceptedAtUtc); }
        catch (ArgumentException)
        {
            // refused: put the old items back so a failed change leaves the order exactly as it was
            Lines.Clear();
            Lines.AddRange(oldLines);
            throw;
        }
        Note = note;
    }

    /// <summary>Takes a hand-over back (the goods returned to the store). The order is approved and waiting again; the stock side is a Return movement written by the service.</summary>
    public void UndoIssue()
    {
        if (!IsIssued) throw new InvalidOperationException("The order was not handed over.");
        IssuedAtUtc = null;
        IssuedByEmployeeId = null;
        IssueNote = null;
    }

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

/// <summary>
/// One item and quantity of an order. The code, name, unit and unit price are copied so the order still reads (and costs) the same
/// if the item is renamed or its price is changed later.
/// </summary>
public sealed class StoreOrderLine
{
    private StoreOrderLine()
    {
    }

    /// <param name="unitPrice">The price of one unit (Toman) when the order was sent; empty when the item had no price.</param>
    public StoreOrderLine(Guid itemId, string itemCode, string itemName, string unit, decimal quantity, decimal? unitPrice = null)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item is required.", nameof(itemId));
        if (quantity <= 0 || decimal.Round(quantity, 3) != quantity) throw new ArgumentException("Quantity must be positive with at most three decimals.", nameof(quantity));
        if (unitPrice is < 0 || (unitPrice is decimal pv && decimal.Round(pv, 2) != pv)) throw new ArgumentException("Price cannot be negative (two decimals at most).", nameof(unitPrice));
        Id = Guid.NewGuid();
        ItemId = itemId;
        ItemCode = itemCode;
        ItemName = itemName;
        Unit = unit;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ItemId { get; private set; }
    public string ItemCode { get; private set; } = "";
    public string ItemName { get; private set; } = "";
    public string Unit { get; private set; } = "";
    public decimal Quantity { get; private set; }
    /// <summary>The price of one unit (Toman) at the moment the order was sent; empty when the item had no price.</summary>
    public decimal? UnitPrice { get; private set; }
    /// <summary>Unit price x quantity; empty when there was no price.</summary>
    public decimal? LineTotal => StorePricing.LineTotal(UnitPrice, Quantity);

    internal void Attach(Guid orderId) => OrderId = orderId;
}

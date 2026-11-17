using System.Security.Cryptography;

namespace YasPortal.Domain.Store;

// Lottery items in the store: some scarce items are not first-come-first-served. Employees apply during a period,
// then a random draw picks the winners, and each winner automatically gets a store order for approval.
// Entity = a row-shaped business object stored in the database; this file holds the application row and the draw rule.
public enum LotteryEntryStatus
{
    /// <summary>Waiting for the draw.</summary>
    Applied = 0,
    Won = 1,
    Lost = 2,
    /// <summary>The person took their application back before the draw.</summary>
    Withdrawn = 3,
}

/// <summary>
/// One person's application for a lottery item. It is owned by the employee (not by a position); the position they were acting in is kept only
/// to route the approval of the order a win produces.
/// </summary>
public sealed class StoreLotteryEntry
{
    private StoreLotteryEntry()
    {
    }

    /// <param name="unitPrice">The price of one unit shown to the person when they applied (Toman); empty when the item has no price. A winner pays this price, even if the item's price changes before the draw.</param>
    /// <param name="deductionAccepted">The person ticked the agreement to the salary deduction. Required when the application would cost money.</param>
    public StoreLotteryEntry(Guid itemId, Guid employeeId, Guid positionId, decimal quantity, decimal? unitPrice = null, bool deductionAccepted = false)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item is required.", nameof(itemId));
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee is required.", nameof(employeeId));
        if (positionId == Guid.Empty) throw new ArgumentException("Position is required.", nameof(positionId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (decimal.Round(quantity, 3) != quantity) throw new ArgumentException("At most three decimals.", nameof(quantity));
        if (unitPrice is < 0 || (unitPrice is decimal pv && decimal.Round(pv, 2) != pv)) throw new ArgumentException("Price cannot be negative (two decimals at most).", nameof(unitPrice));
        if (StorePricing.NeedsDeductionConsent(StorePricing.LineTotal(unitPrice, quantity) ?? 0m) && !deductionAccepted)
            throw new ArgumentException("An application that costs money needs the agreement to the salary deduction.", nameof(deductionAccepted));
        Id = Guid.NewGuid();
        ItemId = itemId;
        EmployeeId = employeeId;
        PositionId = positionId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DeductionAcceptedAtUtc = StorePricing.NeedsDeductionConsent(StorePricing.LineTotal(unitPrice, quantity) ?? 0m) ? DateTime.UtcNow : null;
        Status = LotteryEntryStatus.Applied;
        AppliedAtUtc = DateTime.UtcNow;
    }

    // Stored fields of the application: who applied, for which item, how much, and what happened after the draw.
    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public decimal Quantity { get; private set; }
    /// <summary>The price of one unit shown when the person applied (Toman); empty when the item had no price. The winner's order uses it.</summary>
    public decimal? UnitPrice { get; private set; }
    /// <summary>When the person agreed to the salary deduction for this application. Empty when it costs nothing.</summary>
    public DateTime? DeductionAcceptedAtUtc { get; private set; }
    /// <summary>What the application would cost (unit price x quantity); empty when there was no price.</summary>
    public decimal? EstimatedTotal => StorePricing.LineTotal(UnitPrice, Quantity);
    public LotteryEntryStatus Status { get; private set; }
    public DateTime AppliedAtUtc { get; private set; }
    public DateTime? DrawnAtUtc { get; private set; }
    /// <summary>The store order a win produced.</summary>
    public Guid? OrderId { get; private set; }

    // Status changes are one-way: an application can only be withdrawn, won or lost while it is still waiting for the draw.
    public void Withdraw()
    {
        if (Status != LotteryEntryStatus.Applied) throw new InvalidOperationException("Only an application waiting for the draw can be withdrawn.");
        Status = LotteryEntryStatus.Withdrawn;
    }

    public void MarkWon(Guid orderId)
    {
        if (Status != LotteryEntryStatus.Applied) throw new InvalidOperationException("The application was already drawn.");
        if (orderId == Guid.Empty) throw new ArgumentException("Order is required.", nameof(orderId));
        Status = LotteryEntryStatus.Won;
        OrderId = orderId;
        DrawnAtUtc = DateTime.UtcNow;
    }

    public void MarkLost()
    {
        if (Status != LotteryEntryStatus.Applied) throw new InvalidOperationException("The application was already drawn.");
        Status = LotteryEntryStatus.Lost;
        DrawnAtUtc = DateTime.UtcNow;
    }
}

/// <summary>The draw itself, free of storage so it can be tested.</summary>
// The draw is pure logic with no database access, so the fairness rule can be tested and explained on its own.
public static class StoreLottery
{
    public static string StatusText(LotteryEntryStatus s) => s switch
    {
        LotteryEntryStatus.Applied => "در انتظار قرعه‌کشی",
        LotteryEntryStatus.Won => "برنده شد",
        LotteryEntryStatus.Lost => "برنده نشد",
        LotteryEntryStatus.Withdrawn => "انصراف داده شد",
        _ => s.ToString(),
    };

    /// <summary>
    /// Puts the applicants in a random order and goes down the list: an applicant wins when the whole quantity they asked for is still left,
    /// otherwise they are skipped and the next one is tried (so a big request cannot block smaller ones). Returns the winners' ids.
    /// <paramref name="next"/> returns a number in [0, n); the default is cryptographically random.
    /// </summary>
    public static IReadOnlyList<Guid> Draw(IReadOnlyList<(Guid Id, decimal Quantity)> applicants, decimal stock, Func<int, int>? next = null)
    {
        // Shuffle the applicants (random order), then walk through them handing out stock while enough remains.
        next ??= RandomNumberGenerator.GetInt32;
        var order = applicants.ToList();
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        var winners = new List<Guid>();
        var left = stock;
        foreach (var (id, quantity) in order)
        {
            if (quantity <= 0 || quantity > left) continue;
            winners.Add(id);
            left -= quantity;
        }
        return winners;
    }
}

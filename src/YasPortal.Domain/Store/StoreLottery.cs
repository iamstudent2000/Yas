using System.Security.Cryptography;

namespace YasPortal.Domain.Store;

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

    public StoreLotteryEntry(Guid itemId, Guid employeeId, Guid positionId, decimal quantity)
    {
        if (itemId == Guid.Empty) throw new ArgumentException("Item is required.", nameof(itemId));
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee is required.", nameof(employeeId));
        if (positionId == Guid.Empty) throw new ArgumentException("Position is required.", nameof(positionId));
        if (quantity <= 0) throw new ArgumentException("Quantity must be positive.", nameof(quantity));
        if (decimal.Round(quantity, 3) != quantity) throw new ArgumentException("At most three decimals.", nameof(quantity));
        Id = Guid.NewGuid();
        ItemId = itemId;
        EmployeeId = employeeId;
        PositionId = positionId;
        Quantity = quantity;
        Status = LotteryEntryStatus.Applied;
        AppliedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public decimal Quantity { get; private set; }
    public LotteryEntryStatus Status { get; private set; }
    public DateTime AppliedAtUtc { get; private set; }
    public DateTime? DrawnAtUtc { get; private set; }
    /// <summary>The store order a win produced.</summary>
    public Guid? OrderId { get; private set; }

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

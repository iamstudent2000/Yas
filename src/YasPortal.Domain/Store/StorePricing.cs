namespace YasPortal.Domain.Store;

/// <summary>
/// The money rules of the store in one place, so every page and the server work out the same amount.
/// Prices are in Toman. A line costs "unit price x quantity", rounded to two decimals; an order costs the sum of its (already rounded) lines,
/// so the lines shown on screen always add up to the total shown.
/// </summary>
public static class StorePricing
{
    /// <summary>
    /// Price of one line, or null when the item has no price (it is free, so nothing is deducted).
    /// Halves are rounded up, the way people round by hand.
    /// </summary>
    public static decimal? LineTotal(decimal? unitPrice, decimal quantity) =>
        unitPrice is decimal price ? decimal.Round(price * quantity, 2, MidpointRounding.AwayFromZero) : null;

    /// <summary>The final price of a set of lines; lines without a price add nothing.</summary>
    public static decimal Total(IEnumerable<(decimal? UnitPrice, decimal Quantity)> lines) =>
        lines.Sum(l => LineTotal(l.UnitPrice, l.Quantity) ?? 0m);

    /// <summary>
    /// True when the amount is above zero. Such an amount is taken from the person's salary, so they must agree first.
    /// An order that costs nothing (no prices, or a price of zero) needs no agreement.
    /// </summary>
    public static bool NeedsDeductionConsent(decimal total) => total > 0;
}

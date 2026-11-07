using YasPortal.Infrastructure.Store;

namespace YasPortal.Web.Services;

public sealed record StoreCartLine(Guid ItemId, string Code, string Name, string Unit, decimal Quantity, decimal MaxPerRequest);

/// <summary>The person's shopping cart for this browser session (one per circuit). It only remembers what was chosen; every rule is checked again when the order is sent.</summary>
public sealed class StoreCart
{
    private readonly Dictionary<Guid, StoreCartLine> _lines = [];

    public event Action? Changed;

    public IReadOnlyList<StoreCartLine> Lines => _lines.Values.OrderBy(l => l.Name).ToList();
    public int Count => _lines.Count;

    public decimal QuantityOf(Guid itemId) => _lines.TryGetValue(itemId, out var l) ? l.Quantity : 0;

    /// <summary>Adds the quantity to the line (or sets it when <paramref name="replace"/> is true).</summary>
    public void Add(StoreItemRow item, decimal quantity, bool replace = false)
    {
        if (quantity <= 0) return;
        var total = replace ? quantity : QuantityOf(item.Id) + quantity;
        _lines[item.Id] = new StoreCartLine(item.Id, item.Code, item.Name, item.Unit, total, item.MaxPerRequest);
        Changed?.Invoke();
    }

    public void Remove(Guid itemId) { if (_lines.Remove(itemId)) Changed?.Invoke(); }
    public void Clear() { if (_lines.Count == 0) return; _lines.Clear(); Changed?.Invoke(); }
}

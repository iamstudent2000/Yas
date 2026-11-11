namespace YasPortal.Web.Services;

/// <summary>Click-a-header sorting for small in-memory tables (the big ones sort in the query).</summary>
public static class TableSort
{
    /// <summary>Same header again flips the direction; another header starts ascending.</summary>
    public static (string? Key, bool Desc) Toggle(string? current, bool currentDesc, string clicked) =>
        current == clicked ? (clicked, !currentDesc) : (clicked, false);

    /// <summary>Orders <paramref name="rows"/> by the column <paramref name="key"/>; no key (or an unknown one) keeps the original order.</summary>
    public static IReadOnlyList<T> Apply<T>(IEnumerable<T> rows, string? key, bool desc, params (string Key, Func<T, object?> Select)[] columns)
    {
        var list = rows.ToList();
        var col = columns.FirstOrDefault(c => c.Key == key);
        if (col.Select is null) return list;
        var cmp = Comparer<object?>.Create(Compare);
        var ordered = desc ? list.OrderByDescending(col.Select, cmp) : list.OrderBy(col.Select, cmp);
        return ordered.ToList();
    }

    /// <summary>Numbers compare as numbers (also text like "12" or "1,200"), everything else as text; empty values go last.</summary>
    public static int Compare(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        if (a is string sa && b is string sb)
        {
            if (string.IsNullOrEmpty(sa) != string.IsNullOrEmpty(sb)) return string.IsNullOrEmpty(sa) ? 1 : -1;
            if (decimal.TryParse(sa.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var da)
                && decimal.TryParse(sb.Replace(",", ""), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var db))
                return da.CompareTo(db);
            return string.Compare(sa, sb, StringComparison.CurrentCultureIgnoreCase);
        }
        return Comparer<object>.Default.Compare(a, b);
    }
}

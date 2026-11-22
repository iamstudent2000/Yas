namespace YasPortal.Domain.Paging;

// Paging for lists that are already loaded completely in the page (small admin lists such as links, slides and guides).
// The big lists are paged inside the database query instead; this does the same job in memory so every list page behaves alike.
/// <summary>Cuts an already filtered and sorted list into pages.</summary>
public static class ListPaging
{
    /// <summary>The page sizes offered under such lists, the same as on the other admin lists.</summary>
    public static readonly IReadOnlyList<int> PageSizes = [5, 10, 20, 50, 100];

    /// <summary>How many pages <paramref name="total"/> rows make with <paramref name="pageSize"/> rows each (never less than one).</summary>
    public static int PageCount(int total, int pageSize) => Math.Max(1, (int)Math.Ceiling(Math.Max(0, total) / (double)Math.Max(1, pageSize)));

    /// <summary>
    /// Keeps a page number inside what exists. Needed because rows can disappear while a later page is open
    /// (deleting the last row of the last page, or a search that matches fewer rows): the person then lands on the last page that exists.
    /// </summary>
    public static int ClampPage(int page, int total, int pageSize) => Math.Clamp(page, 1, PageCount(total, pageSize));

    /// <summary>The rows of one page (the page number is clamped first, so the result is empty only when there are no rows at all).</summary>
    public static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> rows, int page, int pageSize)
    {
        var size = Math.Max(1, pageSize);
        var current = ClampPage(page, rows.Count, size);
        return rows.Skip((current - 1) * size).Take(size).ToList();
    }
}

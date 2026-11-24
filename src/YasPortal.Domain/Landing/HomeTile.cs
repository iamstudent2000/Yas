namespace YasPortal.Domain.Landing;

// The tiles are the big buttons in the last section of the home page. Administrators who may manage the home page links
// define them (at most four), each with a title, a short text, an icon and the address it opens.
/// <summary>
/// One tile of the last section of the home page. At most <see cref="MaxCount"/> tiles exist in total (switched on or off);
/// the home page shows the switched-on ones in <see cref="SortOrder"/> order.
/// </summary>
public sealed class HomeTile
{
    // How many tiles may exist, and the length limits of the fields.
    public const int MaxCount = 4, TitleMax = 60, TextMax = 150, UrlMax = PortalLink.UrlMax;

    private HomeTile()
    {
    }

    public HomeTile(string title, string? text, string icon, string url, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(title, text, icon, url, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Text { get; private set; }
    public string Icon { get; private set; } = "ti-sparkles";
    public string Url { get; private set; } = "";
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Used when creating and editing. The title and the address are required; a blank text means no text; the icon must be one of
    // the slide icons (the same picker list); the address follows the same rules as the portal links (a page of this site, http, https,
    // ftp, or a bare server name).
    public void Update(string title, string? text, string icon, string url, int sortOrder)
    {
        title = (title ?? "").Trim();
        text = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        if (title.Length == 0 || title.Length > TitleMax)
            throw new ArgumentException($"Title is required and at most {TitleMax} characters.", nameof(title));
        if (text is { Length: > TextMax })
            throw new ArgumentException($"Text is at most {TextMax} characters.", nameof(text));
        if (!HomeSlide.IconChoices.Any(x => x.Key == icon))
            throw new ArgumentException("Unknown icon.", nameof(icon));
        var normalized = PortalLink.NormalizeUrl(url);
        if (normalized is null)
            throw new ArgumentException("The address is not acceptable.", nameof(url));
        Title = title;
        Text = text;
        Icon = icon;
        Url = normalized;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>A Persian reason that another tile cannot be added when this many already exist, or null when there is room.</summary>
    public static string? ValidateCanAdd(int existing) =>
        existing >= MaxCount ? $"حداکثر {MaxCount} کاشی ممکن است. یکی از کاشی‌های فعلی را ویرایش یا حذف کنید." : null;

    /// <summary>The tiles the home page shows: the switched-on ones in their order (then by title), never more than <see cref="MaxCount"/>.</summary>
    public static IReadOnlyList<HomeTile> ForHomePage(IEnumerable<HomeTile> tiles) =>
        tiles.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Title, StringComparer.CurrentCulture).Take(MaxCount).ToList();
}

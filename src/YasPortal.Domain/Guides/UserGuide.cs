namespace YasPortal.Domain.Guides;

/// <summary>
/// A user guide written by an administrator: a title, an optional summary, a reading direction and an ordered list of
/// <see cref="GuideBlock"/>s. A guide may name a permission; then only people holding it see the guide (an empty permission means
/// every signed-in person). A guide that is off is hidden from readers but stays in the admin list.
/// </summary>
public sealed class UserGuide
{
    public const int TitleMax = 200, SummaryMax = 300, PermissionMax = 150, MaxBlocks = 100;

    private UserGuide()
    {
    }

    public UserGuide(string title, string? summary, GuideDirection direction, string? relatedPermissionCode, int sortOrder)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
        Update(title, summary, direction, relatedPermissionCode, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Summary { get; private set; }
    public GuideDirection Direction { get; private set; }
    /// <summary>Permission code that decides who sees the guide; empty = every signed-in person.</summary>
    public string? RelatedPermissionCode { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string title, string? summary, GuideDirection direction, string? relatedPermissionCode, int sortOrder)
    {
        title = (title ?? "").Trim();
        summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        relatedPermissionCode = string.IsNullOrWhiteSpace(relatedPermissionCode) ? null : relatedPermissionCode.Trim();
        if (title.Length == 0 || title.Length > TitleMax)
            throw new ArgumentException($"Title is required and at most {TitleMax} characters.", nameof(title));
        if (summary is { Length: > SummaryMax })
            throw new ArgumentException($"Summary is at most {SummaryMax} characters.", nameof(summary));
        if (!Enum.IsDefined(direction))
            throw new ArgumentException("Unknown direction.", nameof(direction));
        if (relatedPermissionCode is { Length: > PermissionMax })
            throw new ArgumentException("Permission code is too long.", nameof(relatedPermissionCode));

        Title = title;
        Summary = summary;
        Direction = direction;
        RelatedPermissionCode = relatedPermissionCode;
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    /// <summary>
    /// True when a reader may open the guide: it is on and the reader holds its permission (or it names none).
    /// <paramref name="hasPermission"/> answers for one permission code.
    /// </summary>
    public bool IsReadableBy(Func<string, bool> hasPermission) =>
        IsActive && (RelatedPermissionCode is null || hasPermission(RelatedPermissionCode));
}

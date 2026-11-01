using System.Text.RegularExpressions;

namespace YasPortal.Domain.Landing;

/// <summary>
/// A link to a site or page, shown on the public home page. A link marked <see cref="IsQuickAccess"/> also appears in the quick-access row
/// (in <see cref="QuickOrder"/> order). The address may be http, https or ftp (https is not required), a bare host such as
/// <c>intranet/app</c> (stored as http://intranet/app) or a page of this site (<c>/login</c>).
/// </summary>
public sealed partial class PortalLink
{
    public const int TitleMax = 150, UrlMax = 500, DescriptionMax = 300;

    private PortalLink()
    {
    }

    public PortalLink(string title, string url, string? description, Guid? groupId, int sortOrder, bool isQuickAccess, int quickOrder)
    {
        Id = Guid.NewGuid();
        Update(title, url, description, groupId, sortOrder, isQuickAccess, quickOrder);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Url { get; private set; } = "";
    public string? Description { get; private set; }
    /// <summary>The group it is in; empty = not in any group (listed under "سایر").</summary>
    public Guid? GroupId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsQuickAccess { get; private set; }
    public int QuickOrder { get; private set; }

    public void Update(string title, string url, string? description, Guid? groupId, int sortOrder, bool isQuickAccess, int quickOrder)
    {
        title = (title ?? "").Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (title.Length == 0 || title.Length > TitleMax)
            throw new ArgumentException($"Title is required and at most {TitleMax} characters.", nameof(title));
        var normalized = NormalizeUrl(url);
        if (normalized is null)
            throw new ArgumentException("The address must be an http, https or ftp address, a host name, or a page of this site (starting with /).", nameof(url));
        if (description is { Length: > DescriptionMax })
            throw new ArgumentException($"Description is at most {DescriptionMax} characters.", nameof(description));
        Title = title;
        Url = normalized;
        Description = description;
        GroupId = groupId;
        SortOrder = sortOrder;
        IsQuickAccess = isQuickAccess;
        QuickOrder = quickOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.\-]*[A-Za-z0-9])?(:\d{1,5})?([/?#]\S*)?$")]
    private static partial Regex HostLike();

    [GeneratedRegex(@"^[A-Za-z0-9]([A-Za-z0-9.\-]*[A-Za-z0-9])?:\d{1,5}([/?#]\S*)?$")]
    private static partial Regex HostPort();

    /// <summary>
    /// The address to store, or null when it is not acceptable. Accepted: "/page" (not "//host"), http://, https:// and ftp:// addresses,
    /// and a bare host ("intranet", "10.0.0.5:8080/app") which gets "http://" in front. Anything with another scheme
    /// (javascript:, data:, file:, mailto:) is refused.
    /// </summary>
    public static string? NormalizeUrl(string? url)
    {
        url = (url ?? "").Trim();
        if (url.Length == 0 || url.Length > UrlMax || url.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return null;
        if (url.StartsWith('/'))
            return url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("/\\", StringComparison.Ordinal) ? null : url;
        if (url.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
                return null;
            return uri.Scheme is "http" or "https" or "ftp" ? url : null;
        }
        // "host:8080/x" looks like a scheme to a URI parser; a bare host (with an optional port and path) is taken as http.
        if (HostPort().IsMatch(url) || HostLike().IsMatch(url))
            return "http://" + url;
        return null;
    }

    public static bool IsExternal(string url) => !url.StartsWith('/');

    /// <summary>The quick-access links a fresh system starts with (pages of this site); an administrator can change or delete them.</summary>
    public static IReadOnlyList<PortalLink> StarterQuickLinks() =>
    [
        new("ورود به سامانه", "/login", "با نام کاربری و رمز عبور", null, 1, true, 1),
        new("ثبت درخواست جدید", "/requests/new", "پس از ورود", null, 2, true, 2),
        new("کارتابل تاییدات", "/requests/inbox", "پس از ورود", null, 3, true, 3),
        new("اعلان‌ها", "/notifications", "پس از ورود", null, 4, true, 4),
        new("پروفایل من", "/my-profile", "پس از ورود", null, 5, true, 5),
    ];
}

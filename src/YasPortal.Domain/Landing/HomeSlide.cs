namespace YasPortal.Domain.Landing;

/// <summary>
/// A slide of the public landing page, written by an administrator. It is shown from <see cref="StartsOn"/> to
/// <see cref="ExpiresOn"/> (both days inclusive; either may be empty) while it is active, in <see cref="SortOrder"/> order.
/// </summary>
public sealed class HomeSlide
{
    public const int TitleMax = 200, TextMax = 600, IconMax = 50, LinkTextMax = 100, LinkUrlMax = 300;

    /// <summary>The icons an administrator can pick (Tabler icon classes).</summary>
    public static readonly IReadOnlyList<(string Key, string Text)> IconChoices =
    [
        ("ti-sparkles", "ستاره‌ها"), ("ti-building", "ساختمان"), ("ti-bell-ringing", "زنگ اعلان"), ("ti-speakerphone", "اطلاعیه"),
        ("ti-calendar-event", "رویداد"), ("ti-file-check", "درخواست"), ("ti-shield-lock", "امنیت"), ("ti-users", "کارکنان"),
        ("ti-rocket", "راه‌اندازی"), ("ti-tools", "تعمیرات و سرویس"), ("ti-gift", "هدیه"), ("ti-flag", "پرچم"),
    ];

    private HomeSlide()
    {
    }

    public HomeSlide(string title, string text, string icon, string? linkText, string? linkUrl, int sortOrder, DateTime? startsOn, DateTime? expiresOn)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTime.UtcNow;
        Update(title, text, icon, linkText, linkUrl, sortOrder, startsOn, expiresOn);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Text { get; private set; } = "";
    public string Icon { get; private set; } = "ti-sparkles";
    public string? LinkText { get; private set; }
    public string? LinkUrl { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>First day it is shown (a date; empty = from now).</summary>
    public DateTime? StartsOn { get; private set; }
    /// <summary>Last day it is shown (a date, that day included; empty = never expires).</summary>
    public DateTime? ExpiresOn { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Update(string title, string text, string icon, string? linkText, string? linkUrl, int sortOrder, DateTime? startsOn, DateTime? expiresOn)
    {
        title = (title ?? "").Trim();
        text = (text ?? "").Trim();
        if (title.Length == 0 || title.Length > TitleMax)
            throw new ArgumentException($"Title is required and at most {TitleMax} characters.", nameof(title));
        if (text.Length > TextMax)
            throw new ArgumentException($"Text is at most {TextMax} characters.", nameof(text));
        if (!IconChoices.Any(x => x.Key == icon))
            throw new ArgumentException("Unknown icon.", nameof(icon));
        linkText = string.IsNullOrWhiteSpace(linkText) ? null : linkText.Trim();
        linkUrl = string.IsNullOrWhiteSpace(linkUrl) ? null : linkUrl.Trim();
        if (linkText is { Length: > LinkTextMax })
            throw new ArgumentException($"Link text is at most {LinkTextMax} characters.", nameof(linkText));
        if (linkUrl is not null && !IsSafeLink(linkUrl))
            throw new ArgumentException("The link must be a page of this site (starting with /) or an http(s) address.", nameof(linkUrl));
        if (linkUrl is not null && linkText is null)
            throw new ArgumentException("A link needs its text.", nameof(linkText));
        if (startsOn is not null && expiresOn is not null && expiresOn.Value.Date < startsOn.Value.Date)
            throw new ArgumentException("The expiry date cannot be before the start date.", nameof(expiresOn));

        Title = title;
        Text = text;
        Icon = icon;
        LinkText = linkUrl is null ? null : linkText;
        LinkUrl = linkUrl;
        SortOrder = sortOrder;
        StartsOn = startsOn?.Date;
        ExpiresOn = expiresOn?.Date;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>True when the slide should be on the landing page on <paramref name="today"/> (a local date).</summary>
    public bool IsShownOn(DateTime today) =>
        IsActive && (StartsOn is null || StartsOn.Value.Date <= today.Date) && (ExpiresOn is null || ExpiresOn.Value.Date >= today.Date);

    /// <summary>True when at least one of the slides is shown on <paramref name="today"/>. The rule the admin page enforces: this must stay true.</summary>
    public static bool HasValid(IEnumerable<HomeSlide> slides, DateTime today) => slides.Any(x => x.IsShownOn(today));

    /// <summary>The slides a fresh system starts with (the seeder and the migration script add them when the table is empty); an administrator can change or delete them.</summary>
    public static IReadOnlyList<HomeSlide> StarterSet() =>
    [
        new("به یاس پورتال خوش آمدید", "سامانه یکپارچه مدیریت سازمان، سمت‌ها، درخواست‌ها و دسترسی‌های کارکنان در یک بستر واحد.", "ti-building", "ورود به سامانه", "/login", 1, null, null),
        new("درخواست‌ها بدون کاغذ", "ثبت، پیگیری و تأیید درخواست‌ها به‌صورت برخط و با مسیر تأیید مشخص برای هر سمت.", "ti-file-check", null, null, 2, null, null),
        new("اعلان‌ها و رویدادها", "از تصمیم‌ها و پیام‌های مهم بلافاصله باخبر شوید؛ همه رویدادهای سامانه ثبت و قابل پیگیری است.", "ti-bell-ringing", null, null, 3, null, null),
    ];

    /// <summary>A page of this site ("/…", not "//…") or an absolute http/https address. Nothing else (no javascript: links).</summary>
    public static bool IsSafeLink(string url)
    {
        if (url.Length > LinkUrlMax || url.Any(char.IsWhiteSpace))
            return false;
        if (url.StartsWith('/'))
            return !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal);
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}

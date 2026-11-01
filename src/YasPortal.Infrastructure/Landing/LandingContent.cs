using Microsoft.Extensions.Configuration;

namespace YasPortal.Infrastructure.Landing;

/// <summary>One slide of the public landing page.</summary>
public sealed class LandingSlide
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>A Tabler icon class such as <c>ti-building</c>.</summary>
    public string Icon { get; set; } = "ti-sparkles";
    public string? LinkText { get; set; }
    public string? LinkUrl { get; set; }
    /// <summary>The slide's attached file, when it has one (served from <c>/landing/files/{slide id}</c>).</summary>
    public Guid? FileSlideId { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
}

public sealed class LandingLink
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "/login";
    public string Icon { get; set; } = "ti-link";
    public string? Hint { get; set; }
}

public sealed class LandingInfo
{
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public string Icon { get; set; } = "ti-info-circle";
}

/// <summary>
/// What the public landing page shows besides the slides (those are managed by administrators, see <c>HomeSlide</c>): quick links and general information cards. Nothing here comes from the database,
/// so nothing private can leak to a visitor who has not signed in. The built-in texts can be replaced from appsettings: a
/// <c>Landing</c> section with <c>QuickLinks</c> and <c>Info</c> lists (a list that is present replaces the built-in one).
/// </summary>
public sealed class LandingContent(IConfiguration configuration)
{
    public IReadOnlyList<LandingLink> QuickLinks => Read("Landing:QuickLinks", DefaultLinks);
    public IReadOnlyList<LandingInfo> Info => Read("Landing:Info", DefaultInfo);

    private List<T> Read<T>(string section, List<T> fallback) where T : class
    {
        var items = configuration.GetSection(section).Get<List<T>>();
        return items is { Count: > 0 } ? items : fallback;
    }

    private static readonly List<LandingLink> DefaultLinks =
    [
        new() { Title = "ورود به سامانه", Url = "/login", Icon = "ti-login", Hint = "با نام کاربری و رمز عبور" },
        new() { Title = "ثبت درخواست جدید", Url = "/requests/new", Icon = "ti-file-plus", Hint = "پس از ورود" },
        new() { Title = "کارتابل تاییدات", Url = "/requests/inbox", Icon = "ti-inbox", Hint = "پس از ورود" },
        new() { Title = "اعلان‌ها", Url = "/notifications", Icon = "ti-bell", Hint = "پس از ورود" },
        new() { Title = "پروفایل من", Url = "/my-profile", Icon = "ti-user-circle", Hint = "پس از ورود" },
    ];

    private static readonly List<LandingInfo> DefaultInfo =
    [
        new() { Title = "دسترسی بر اساس مجوز", Text = "پس از ورود، فقط بخش‌هایی را می‌بینید که مجوز آن‌ها را دارید. برای دسترسی بیشتر با مدیر سامانه تماس بگیرید.", Icon = "ti-shield-lock" },
        new() { Title = "چند سمت، یک حساب", Text = "اگر بیش از یک سمت دارید، پس از ورود سمت فعال را انتخاب کنید و در صورت نیاز آن را تغییر دهید.", Icon = "ti-users" },
        new() { Title = "امنیت و ثبت رویدادها", Text = "ورود و اقدامات مهم در سامانه ثبت می‌شود. رمز عبور خود را به کسی ندهید.", Icon = "ti-lock-check" },
    ];
}

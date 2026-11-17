namespace YasPortal.Domain.Preferences;

// Appearance choices an employee can make for their own screen: light or dark theme, accent color, font,
// text size and spacing. Each list below is a fixed set of choices.
public enum UiTheme { System, Light, Dark }
public enum UiAccent { Blue, Teal, Violet, Rose, Amber, Green }
public enum UiFont { Vazirmatn, System, Sahel, Shabnam }
public enum UiTextSize { Small, Normal, Large, ExtraLarge }
public enum UiDensity { Comfortable, Compact }

// Stored settings for one employee. Empty means the defaults.
/// <summary>
/// One employee's appearance settings (theme, accent color, font, text size, density). Purely cosmetic:
/// nothing here affects permissions or workflow. One row per employee, created on first save.
/// </summary>
public sealed class UserUiPreference
{
    private UserUiPreference()
    {
    }

    public UserUiPreference(Guid employeeId)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(employeeId));
        EmployeeId = employeeId;
    }

    /// <summary>The employee the settings belong to; also the key (one row per employee).</summary>
    public Guid EmployeeId { get; private set; }

    public UiTheme Theme { get; private set; } = UiTheme.System;
    public UiAccent Accent { get; private set; } = UiAccent.Blue;
    public UiFont Font { get; private set; } = UiFont.Vazirmatn;
    public UiTextSize TextSize { get; private set; } = UiTextSize.Normal;
    public UiDensity Density { get; private set; } = UiDensity.Comfortable;
    public DateTime UpdatedAtUtc { get; private set; } = DateTime.UtcNow;

    // All five values are checked to be real choices before saving.
    public void Update(UiTheme theme, UiAccent accent, UiFont font, UiTextSize textSize, UiDensity density)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (!Enum.IsDefined(accent)) throw new ArgumentOutOfRangeException(nameof(accent));
        if (!Enum.IsDefined(font)) throw new ArgumentOutOfRangeException(nameof(font));
        if (!Enum.IsDefined(textSize)) throw new ArgumentOutOfRangeException(nameof(textSize));
        if (!Enum.IsDefined(density)) throw new ArgumentOutOfRangeException(nameof(density));
        Theme = theme;
        Accent = accent;
        Font = font;
        TextSize = textSize;
        Density = density;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    // Reset puts back the default look.
    public void Reset() => Update(UiTheme.System, UiAccent.Blue, UiFont.Vazirmatn, UiTextSize.Normal, UiDensity.Comfortable);
}

// The page reads these short words to pick the matching styles in the theme stylesheet.
/// <summary>The values written to the page's &lt;html data-*&gt; attributes; the CSS in theme.css keys off exactly these strings.</summary>
public sealed record UiPreferenceAttributes(string Theme, string Accent, string Font, string Size, string Density)
{
    public static readonly UiPreferenceAttributes Default = From(null);

    public static UiPreferenceAttributes From(UserUiPreference? p) => p is null
        ? new("system", "blue", "vazirmatn", "md", "comfortable")
        : new(
            p.Theme switch { UiTheme.Light => "light", UiTheme.Dark => "dark", _ => "system" },
            p.Accent.ToString().ToLowerInvariant(),
            p.Font switch { UiFont.System => "system", UiFont.Sahel => "sahel", UiFont.Shabnam => "shabnam", _ => "vazirmatn" },
            p.TextSize switch { UiTextSize.Small => "sm", UiTextSize.Large => "lg", UiTextSize.ExtraLarge => "xl", _ => "md" },
            p.Density == UiDensity.Compact ? "compact" : "comfortable");
}

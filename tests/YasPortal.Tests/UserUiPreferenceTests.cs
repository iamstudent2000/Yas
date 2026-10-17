using Xunit;
using YasPortal.Domain.Preferences;

namespace YasPortal.Tests;

public class UserUiPreferenceTests
{
    [Fact]
    public void New_preference_uses_the_app_defaults()
    {
        var p = new UserUiPreference(Guid.NewGuid());
        Assert.Equal((UiTheme.System, UiAccent.Blue, UiFont.Vazirmatn, UiTextSize.Normal, UiDensity.Comfortable),
            (p.Theme, p.Accent, p.Font, p.TextSize, p.Density));
        Assert.Equal(UiPreferenceAttributes.Default, UiPreferenceAttributes.From(p));
    }

    [Fact]
    public void Attributes_use_the_strings_the_stylesheet_expects()
    {
        var p = new UserUiPreference(Guid.NewGuid());
        p.Update(UiTheme.Dark, UiAccent.Violet, UiFont.System, UiTextSize.ExtraLarge, UiDensity.Compact);
        Assert.Equal(new UiPreferenceAttributes("dark", "violet", "system", "xl", "compact"), UiPreferenceAttributes.From(p));
    }

    [Theory]
    [InlineData(UiTextSize.Small, "sm")]
    [InlineData(UiTextSize.Normal, "md")]
    [InlineData(UiTextSize.Large, "lg")]
    [InlineData(UiTextSize.ExtraLarge, "xl")]
    public void Text_size_maps_to_short_codes(UiTextSize size, string code)
    {
        var p = new UserUiPreference(Guid.NewGuid());
        p.Update(UiTheme.Light, UiAccent.Blue, UiFont.Vazirmatn, size, UiDensity.Comfortable);
        Assert.Equal(code, UiPreferenceAttributes.From(p).Size);
    }

    [Theory]
    [InlineData(UiFont.Vazirmatn, "vazirmatn")]
    [InlineData(UiFont.Sahel, "sahel")]
    [InlineData(UiFont.Shabnam, "shabnam")]
    [InlineData(UiFont.System, "system")]
    public void Every_font_maps_to_its_stylesheet_code(UiFont font, string code)
    {
        var p = new UserUiPreference(Guid.NewGuid());
        p.Update(UiTheme.Light, UiAccent.Blue, font, UiTextSize.Normal, UiDensity.Comfortable);
        Assert.Equal(code, UiPreferenceAttributes.From(p).Font);
    }

    [Fact]
    public void Reset_returns_to_defaults()
    {
        var p = new UserUiPreference(Guid.NewGuid());
        p.Update(UiTheme.Dark, UiAccent.Rose, UiFont.System, UiTextSize.Large, UiDensity.Compact);
        p.Reset();
        Assert.Equal(UiPreferenceAttributes.Default, UiPreferenceAttributes.From(p));
    }

    [Fact]
    public void Undefined_values_and_empty_employee_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new UserUiPreference(Guid.Empty));
        var p = new UserUiPreference(Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() => p.Update((UiTheme)99, UiAccent.Blue, UiFont.Vazirmatn, UiTextSize.Normal, UiDensity.Comfortable));
    }
}

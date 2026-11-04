using YasPortal.Infrastructure.Guides;
using Xunit;

namespace YasPortal.Tests;

public sealed class GuideHtmlSanitizerTests
{
    [Fact]
    public void Editor_output_is_kept()
    {
        var html = "<h2>عنوان</h2><p class=\"ql-align-center\">این <strong>مهم</strong> است <span style=\"color: rgb(230, 0, 0);\">قرمز</span></p>"
                 + "<p class=\"ql-direction-ltr\">See <a href=\"https://example.com/x?a=1&amp;b=2\" rel=\"noopener noreferrer\" target=\"_blank\">docs</a></p>"
                 + "<ul><li>یک</li><li class=\"ql-indent-1\">دو</li></ul><blockquote>نقل</blockquote>";
        var clean = GuideHtmlSanitizer.Sanitize(html);

        Assert.Contains("<h2>", clean);
        Assert.Contains("ql-align-center", clean);
        Assert.Contains("ql-direction-ltr", clean);
        Assert.Contains("ql-indent-1", clean);
        Assert.Contains("<strong>", clean);
        Assert.Contains("<ul>", clean);
        Assert.Contains("<blockquote>", clean);
        Assert.Contains("https://example.com/x?a=1", clean);
        Assert.Contains("color", clean);
    }

    [Fact]
    public void Scripts_handlers_and_script_links_are_removed()
    {
        var clean = GuideHtmlSanitizer.Sanitize(
            "<p onclick=\"steal()\">سلام</p><script>alert(1)</script><a href=\"javascript:alert(2)\">x</a><img src=\"x\" onerror=\"alert(3)\"><iframe src=\"https://evil.example\"></iframe>");

        Assert.Contains("سلام", clean);
        Assert.DoesNotContain("script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("iframe", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_known_classes_and_colour_styles_survive()
    {
        var clean = GuideHtmlSanitizer.Sanitize("<p class=\"ql-align-right evil-class\" style=\"color: red; position: fixed; background-image: url(x)\">x</p>");

        Assert.Contains("ql-align-right", clean);
        Assert.DoesNotContain("evil-class", clean);
        Assert.DoesNotContain("position", clean);
        Assert.DoesNotContain("background-image", clean);
        Assert.Contains("color", clean);
    }

    [Fact]
    public void Links_to_this_site_are_kept()
    {
        Assert.Contains("href=\"/phone-book\"", GuideHtmlSanitizer.Sanitize("<a href=\"/phone-book\">دفترچه</a>"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_gives_an_empty_string(string? html) => Assert.Equal("", GuideHtmlSanitizer.Sanitize(html));
}

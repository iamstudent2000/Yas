using Ganss.Xss;

namespace YasPortal.Infrastructure.Guides;

/// <summary>
/// Cleans the HTML of a user guide down to what the editor can produce: paragraphs, headings, lists, quotes, bold / italic / underline,
/// text colour, alignment and direction classes, and links (http, https, mailto, tel or a page of this site). Everything else
/// (scripts, event handlers, iframes, images, forms, other styles) is removed. It runs when a guide is saved and again when it is shown,
/// because public pages are read by people who are not signed in.
/// </summary>
public static class GuideHtmlSanitizer
{
    private static readonly HtmlSanitizer Sanitizer = Build();
    private static readonly object Gate = new();

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        lock (Gate)
            return Sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer Build()
    {
        var s = new HtmlSanitizer();

        s.AllowedTags.Clear();
        foreach (var tag in new[] { "p", "br", "h1", "h2", "h3", "h4", "strong", "b", "em", "i", "u", "s", "ul", "ol", "li", "blockquote", "a", "span" })
            s.AllowedTags.Add(tag);

        s.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "class", "style", "href", "rel", "target" })
            s.AllowedAttributes.Add(attribute);

        s.AllowedClasses.Clear();
        foreach (var name in new[] { "ql-align-center", "ql-align-right", "ql-align-justify", "ql-direction-rtl", "ql-direction-ltr" })
            s.AllowedClasses.Add(name);
        for (var i = 1; i <= 8; i++)
            s.AllowedClasses.Add($"ql-indent-{i}");

        s.AllowedCssProperties.Clear();
        s.AllowedCssProperties.Add("color");
        s.AllowedCssProperties.Add("background-color");

        s.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto", "tel" })
            s.AllowedSchemes.Add(scheme);

        s.AllowDataAttributes = false;
        return s;
    }
}

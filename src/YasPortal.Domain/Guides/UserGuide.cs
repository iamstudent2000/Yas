using System.Net;
using System.Text.RegularExpressions;

namespace YasPortal.Domain.Guides;

/// <summary>Who may read a guide.</summary>
public enum GuideVisibility
{
    /// <summary>Everyone, also visitors who have not signed in.</summary>
    Public = 0,
    /// <summary>Only signed-in people.</summary>
    SignedIn = 1,
}

/// <summary>
/// A user guide written by an administrator in the rich-text editor. <see cref="ContentHtml"/> is HTML that the web layer has already
/// cleaned down to a small safe set of tags (see GuideHtmlSanitizer); the domain only checks that there is text and how long it is.
/// A guide that is off is hidden from readers but stays in the admin list.
/// </summary>
public sealed partial class UserGuide
{
    public const int TitleMax = 200, SummaryMax = 300, ContentMax = 200_000;

    private UserGuide()
    {
    }

    public UserGuide(string title, string? summary, string contentHtml, GuideVisibility visibility, int sortOrder)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
        Update(title, summary, contentHtml, visibility, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Summary { get; private set; }
    public string ContentHtml { get; private set; } = "";
    public GuideVisibility Visibility { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Update(string title, string? summary, string contentHtml, GuideVisibility visibility, int sortOrder)
    {
        title = (title ?? "").Trim();
        summary = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim();
        contentHtml = (contentHtml ?? "").Trim();
        if (title.Length == 0 || title.Length > TitleMax)
            throw new ArgumentException($"Title is required and at most {TitleMax} characters.", nameof(title));
        if (summary is { Length: > SummaryMax })
            throw new ArgumentException($"Summary is at most {SummaryMax} characters.", nameof(summary));
        if (!HasText(contentHtml) || contentHtml.Length > ContentMax)
            throw new ArgumentException($"The guide needs some text and at most {ContentMax} characters.", nameof(contentHtml));
        if (!Enum.IsDefined(visibility))
            throw new ArgumentException("Unknown visibility.", nameof(visibility));

        Title = title;
        Summary = summary;
        ContentHtml = contentHtml;
        Visibility = visibility;
        SortOrder = sortOrder;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    /// <summary>True when the guide is on and the reader may see it: a public guide is for everyone, any other only for a signed-in person.</summary>
    public bool IsReadableBy(bool signedIn) => IsActive && (Visibility == GuideVisibility.Public || signedIn);

    /// <summary>True when the HTML holds some visible text (an editor's "empty" content such as &lt;p&gt;&lt;br&gt;&lt;/p&gt; does not count).</summary>
    public static bool HasText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;
        var text = WebUtility.HtmlDecode(TagPattern().Replace(html, " "));
        return !string.IsNullOrWhiteSpace(text);   // char.IsWhiteSpace also covers the non-breaking space
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();
}

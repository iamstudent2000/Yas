using System.Net;
using System.Text.RegularExpressions;

namespace YasPortal.Domain.Guides;

// User guides are help pages written by administrators. Each guide is public (anyone) or for signed-in
// people only.
/// <summary>Who may read a guide.</summary>
public enum GuideVisibility
{
    /// <summary>Everyone, also visitors who have not signed in.</summary>
    Public = 0,
    /// <summary>Only signed-in people.</summary>
    SignedIn = 1,
}

// The guide itself (an entity = a row-shaped business object stored in the database).
/// <summary>
/// A user guide written by an administrator in the rich-text editor. <see cref="ContentHtml"/> is HTML that the web layer has already
/// cleaned down to a small safe set of tags (see GuideHtmlSanitizer); the domain only checks that there is text and how long it is.
/// A guide that is off is hidden from readers but stays in the admin list.
/// </summary>
public sealed partial class UserGuide
{
    // Length limits for the title, the short summary and the body text.
    // GroupMax is the longest name a group (a heading that gathers related guides) may have.
    public const int TitleMax = 200, SummaryMax = 300, ContentMax = 200_000, GroupMax = 100;

    private UserGuide()
    {
    }

    public UserGuide(string title, string? summary, string contentHtml, GuideVisibility visibility, int sortOrder, Guid? createdByEmployeeId = null, string? groupName = null)
    {
        Id = Guid.NewGuid();
        CreatedByEmployeeId = createdByEmployeeId == Guid.Empty ? null : createdByEmployeeId;
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
        Update(title, summary, contentHtml, visibility, sortOrder, groupName);
    }

    public Guid Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? Summary { get; private set; }
    public string ContentHtml { get; private set; } = "";
    public GuideVisibility Visibility { get; private set; }
    public int SortOrder { get; private set; }
    /// <summary>
    /// The group the guide is listed under on the guides page (for example «درخواست‌ها» or «فروشگاه»); empty = no group.
    /// Guides with the same name are gathered under one heading, so there is no separate list of groups to maintain.
    /// </summary>
    public string? GroupName { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    /// <summary>The employee who wrote the guide; empty for a guide written before authors were recorded.</summary>
    public Guid? CreatedByEmployeeId { get; private set; }

    // Used both when creating and when editing. It trims the text and refuses a missing title, over-long
    // fields, or a body with no visible text, so an empty guide can never be saved.
    // The group name is cleaned the same way (see NormalizeGroupName); leaving it out removes the guide from its group.
    public void Update(string title, string? summary, string contentHtml, GuideVisibility visibility, int sortOrder, string? groupName = null)
    {
        var group = NormalizeGroupName(groupName);
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
        if (group is { Length: > GroupMax })
            throw new ArgumentException($"The group name is at most {GroupMax} characters.", nameof(groupName));

        Title = title;
        Summary = summary;
        ContentHtml = contentHtml;
        Visibility = visibility;
        SortOrder = sortOrder;
        GroupName = group;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Cleans a typed group name so the same group is never split in two by a typing difference: spaces at the ends are removed,
    /// runs of spaces become one, and the Arabic forms of «ی» and «ک» become the Persian ones. Blank becomes empty (no group).
    /// </summary>
    public static string? NormalizeGroupName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var text = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');
    }

    // A guide that is switched off is hidden from readers but stays in the admin list.
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    // Who may see and who may edit a guide: readers by visibility, editors by the all-guides or own-guides
    // right.
    /// <summary>True when the guide is on and the reader may see it: a public guide is for everyone, any other only for a signed-in person.</summary>
    public bool IsReadableBy(bool signedIn) => IsActive && (Visibility == GuideVisibility.Public || signedIn);

    public bool IsOwnedBy(Guid? employeeId) => employeeId is Guid id && id != Guid.Empty && CreatedByEmployeeId == id;

    /// <summary>
    /// True when the person may change the guide: whoever may edit all guides, or whoever may edit their own guides and wrote this one
    /// (a guide without a recorded author is never "own").
    /// </summary>
    public bool CanBeEditedBy(bool canEditAll, bool canEditOwn, Guid? employeeId) => canEditAll || (canEditOwn && IsOwnedBy(employeeId));

    // The body is stored as HTML. The check below strips the tags to find out whether any real words
    // remain.
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

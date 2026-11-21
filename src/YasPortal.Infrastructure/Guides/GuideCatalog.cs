using YasPortal.Domain.Guides;

namespace YasPortal.Infrastructure.Guides;

/// <summary>One heading of the guides page with the guides under it. <paramref name="GroupName"/> is empty for the guides that have no group.</summary>
public sealed record GuideSection(string? GroupName, IReadOnlyList<GuideListItem> Guides);

/// <summary>
/// Searching and grouping the guides a reader may see. Nothing is read from the database here: the page hands over the list it already has,
/// which is in the order the administrator set.
/// </summary>
public static class GuideCatalog
{
    /// <summary>The heading shown above the guides that belong to no group.</summary>
    public const string UngroupedLabel = "سایر راهنماها";

    /// <summary>
    /// The guides that match the typed words (see <see cref="GuideSearch"/>) and the chosen group.
    /// <paramref name="groupName"/>: null = every group, an empty text = only the guides without a group, otherwise that group.
    /// The display order is kept.
    /// </summary>
    public static IReadOnlyList<GuideListItem> Search(IEnumerable<GuideListItem> guides, string? query, string? groupName) =>
        guides.Where(g => (groupName is null || (groupName.Length == 0 ? g.GroupName is null : g.GroupName == groupName))
                          && GuideSearch.Matches(g.Title, g.Summary, g.GroupName, query)).ToList();

    /// <summary>
    /// The guides in sections, one per group. Groups come in the order of their first guide (so the administrator's guide order also orders
    /// the groups) and the guides without a group come last. A group with no guides is not shown.
    /// </summary>
    public static IReadOnlyList<GuideSection> Group(IEnumerable<GuideListItem> guides)
    {
        var list = guides.ToList();
        var sections = list.Where(g => g.GroupName is not null).GroupBy(g => g.GroupName!)
            .Select(g => new GuideSection(g.Key, g.ToList())).ToList();
        var ungrouped = list.Where(g => g.GroupName is null).ToList();
        if (ungrouped.Count > 0) sections.Add(new GuideSection(null, ungrouped));
        return sections;
    }
}

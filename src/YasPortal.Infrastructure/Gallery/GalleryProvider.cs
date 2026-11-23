using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Domain.Gallery;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Gallery;

/// <summary>One picture of the public gallery, without its bytes. <paramref name="GroupName"/> is the heading it is listed under and <paramref name="GroupOrder"/> that group's order number (both empty = no group).</summary>
public sealed record GalleryListItem(Guid Id, string Title, string? Caption, int Version, string? GroupName = null, int? GroupOrder = null);

/// <summary>One heading of the gallery page with the pictures under it. <paramref name="GroupName"/> is empty for the pictures that have no group.</summary>
public sealed record GallerySection(string? GroupName, IReadOnlyList<GalleryListItem> Images);

/// <summary>The pictures visitors may see (switched on, and not in a switched-off group), from the database. An unreadable database gives an empty gallery.</summary>
public sealed class GalleryProvider(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<GalleryProvider> logger)
{
    public async Task<IReadOnlyList<GalleryListItem>> ListAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Each picture is joined to its group (a picture may have none). A picture whose group is switched off is hidden with it,
            // the same way the guides hide the guides of a switched-off group.
            var joined = from g in db.GalleryImages.AsNoTracking().Where(x => x.IsActive)
                         join gr in db.GalleryGroups.AsNoTracking() on g.GroupId equals (Guid?)gr.Id into groups
                         from gr in groups.DefaultIfEmpty()
                         where gr == null || gr.IsActive
                         orderby g.SortOrder, g.CreatedAtUtc
                         select new GalleryListItem(g.Id, g.Title, g.Caption, g.Version, gr == null ? null : gr.Name, gr == null ? null : (int?)gr.SortOrder);
            // Display order set by the administrator, then oldest first.
            return await joined.ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the gallery pictures.");
            return [];
        }
    }
}

/// <summary>Filtering and grouping the gallery pictures a visitor may see. Nothing is read from the database here: the page hands over the list it already has.</summary>
public static class GalleryCatalog
{
    /// <summary>The heading shown above the pictures that belong to no group.</summary>
    public const string UngroupedLabel = "سایر تصاویر";

    /// <summary>
    /// The pictures that match the typed words (see <see cref="GalleryImage.Matches"/>: title, caption and group name) and the chosen group.
    /// <paramref name="groupName"/>: null = every group, an empty text = only the pictures without a group, otherwise that group. The display order is kept.
    /// </summary>
    public static IReadOnlyList<GalleryListItem> Filter(IEnumerable<GalleryListItem> images, string? query, string? groupName) =>
        images.Where(g => (groupName is null || (groupName.Length == 0 ? g.GroupName is null : g.GroupName == groupName))
                          && GalleryImage.Matches(g.Title, g.Caption, null, g.GroupName, query)).ToList();

    /// <summary>
    /// The pictures in sections, one per group. Groups come in the order the administrators gave them (the group's order number); groups with the
    /// same number keep the order of their first picture. The pictures without a group come last. A group with no pictures is not shown.
    /// </summary>
    public static IReadOnlyList<GallerySection> Group(IEnumerable<GalleryListItem> images)
    {
        var list = images.ToList();
        var sections = list.Where(g => g.GroupName is not null).GroupBy(g => g.GroupName!)
            .OrderBy(g => g.Min(x => x.GroupOrder ?? int.MaxValue))
            .Select(g => new GallerySection(g.Key, g.ToList())).ToList();
        var ungrouped = list.Where(g => g.GroupName is null).ToList();
        if (ungrouped.Count > 0) sections.Add(new GallerySection(null, ungrouped));
        return sections;
    }
}

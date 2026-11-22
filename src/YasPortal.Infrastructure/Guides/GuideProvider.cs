using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Domain.Guides;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Guides;

/// <summary>The guides a reader may see (public ones for a visitor, public and signed-in ones for a signed-in person). An unreadable database gives no guides.</summary>
public sealed class GuideProvider(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<GuideProvider> logger)
{
    /// <summary>The readable guides in display order, without their text.</summary>
    public async Task<IReadOnlyList<GuideListItem>> ListAsync(bool signedIn, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Only guides that an administrator left switched on are ever shown.
            var query = db.UserGuides.AsNoTracking().Where(x => x.IsActive);
            // A visitor who is not signed in sees only public guides.
            if (!signedIn) query = query.Where(x => x.Visibility == GuideVisibility.Public);
            // Each guide is joined to its group (a guide may have none). A guide whose group is switched off is hidden with it,
            // the same way the store hides the items of a switched-off group.
            var joined = from g in query
                         join gr in db.UserGuideGroups.AsNoTracking() on g.GroupId equals (Guid?)gr.Id into groups
                         from gr in groups.DefaultIfEmpty()
                         where gr == null || gr.IsActive
                         orderby g.SortOrder, g.CreatedAtUtc
                         select new GuideListItem(g.Id, g.Title, g.Summary, g.Visibility, gr == null ? null : gr.Name, gr == null ? null : (int?)gr.SortOrder);
            // Display order set by the administrator, then oldest first.
            return await joined.ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the user guides.");
            return [];
        }
    }

    /// <summary>
    /// One guide, or null when it does not exist or the reader may not see it. <paramref name="canPreviewAll"/> (a person who may see every guide in the
    /// workspace) also sees a guide that is off; <paramref name="authorId"/> (set only for a person who may edit their own guides) also sees their own.
    /// </summary>
    public async Task<UserGuide?> GetAsync(Guid id, bool signedIn, bool canPreviewAll, Guid? authorId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            // Load the guide, then show it only if the reader is allowed: full preview rights, a normal reader of its
            // visibility level, or its own author.
            var guide = await db.UserGuides.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return guide is not null && (canPreviewAll || guide.IsReadableBy(signedIn) || guide.IsOwnedBy(authorId)) ? guide : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read a user guide.");
            return null;
        }
    }

    /// <summary>The files attached to a guide (name and size only, never the contents), oldest first. The caller has already checked the reader may see the guide.</summary>
    public async Task<IReadOnlyList<GuideFileItem>> ListAttachmentsAsync(Guid guideId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.UserGuideAttachments.AsNoTracking().Where(x => x.GuideId == guideId).OrderBy(x => x.UploadedAtUtc).ThenBy(x => x.FileName)
                .Select(x => new GuideFileItem(x.Id, x.FileName, x.SizeBytes)).ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the files of a user guide.");
            return [];
        }
    }

    /// <summary>
    /// One attached file with its contents, or null when it does not exist or the reader may not see its guide
    /// (the same rule as <see cref="GetAsync"/>: preview rights, a normal reader of the guide, or its own author).
    /// </summary>
    public async Task<UserGuideAttachment?> GetAttachmentAsync(Guid id, bool signedIn, bool canPreviewAll, Guid? authorId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var file = await db.UserGuideAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (file is null) return null;
            var guide = await db.UserGuides.AsNoTracking().SingleOrDefaultAsync(x => x.Id == file.GuideId, ct);
            return guide is not null && (canPreviewAll || guide.IsReadableBy(signedIn) || guide.IsOwnedBy(authorId)) ? file : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read a user guide file.");
            return null;
        }
    }
}

/// <summary>One file attached to a guide, without its contents.</summary>
public sealed record GuideFileItem(Guid Id, string FileName, long SizeBytes);

/// <summary>One guide in the list, without its text. <paramref name="GroupName"/> is the heading it is listed under and <paramref name="GroupOrder"/> that group's order number (both empty = no group).</summary>
public sealed record GuideListItem(Guid Id, string Title, string? Summary, GuideVisibility Visibility, string? GroupName = null, int? GroupOrder = null);

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
            // Display order set by the administrator, then oldest first.
            return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc)
                .Select(x => new GuideListItem(x.Id, x.Title, x.Summary, x.Visibility, x.GroupName))
                .ToListAsync(ct);
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
}

/// <summary>One guide in the list, without its text. <paramref name="GroupName"/> is the heading it is listed under (empty = no group).</summary>
public sealed record GuideListItem(Guid Id, string Title, string? Summary, GuideVisibility Visibility, string? GroupName = null);

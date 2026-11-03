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
            var query = db.UserGuides.AsNoTracking().Where(x => x.IsActive);
            if (!signedIn) query = query.Where(x => x.Visibility == GuideVisibility.Public);
            return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc)
                .Select(x => new GuideListItem(x.Id, x.Title, x.Summary, x.Visibility))
                .ToListAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the user guides.");
            return [];
        }
    }

    /// <summary>One guide, or null when it does not exist or the reader may not see it. <paramref name="canPreview"/> (an administrator who manages guides) also sees a guide that is off.</summary>
    public async Task<UserGuide?> GetAsync(Guid id, bool signedIn, bool canPreview, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var guide = await db.UserGuides.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            return guide is not null && (canPreview || guide.IsReadableBy(signedIn)) ? guide : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read a user guide.");
            return null;
        }
    }
}

public sealed record GuideListItem(Guid Id, string Title, string? Summary, GuideVisibility Visibility);

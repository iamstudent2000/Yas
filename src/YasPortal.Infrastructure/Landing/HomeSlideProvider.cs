using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Landing;

/// <summary>
/// The slides the landing page shows today: the administrator's slides that are active and inside their dates, in order.
/// When there are none (or the database cannot be read) the built-in slides of <see cref="LandingContent"/> are shown, so the page is never empty.
/// </summary>
public sealed class HomeSlideProvider(IDbContextFactory<ApplicationDbContext> dbFactory, LandingContent content, ILogger<HomeSlideProvider> logger)
{
    public async Task<IReadOnlyList<LandingSlide>> GetSlidesAsync(DateTime? today = null, CancellationToken ct = default)
    {
        var day = (today ?? DateTime.Now).Date;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var rows = await db.HomeSlides.AsNoTracking()
                .Where(x => x.IsActive && (x.StartsOn == null || x.StartsOn <= day) && (x.ExpiresOn == null || x.ExpiresOn >= day))
                .OrderBy(x => x.SortOrder).ThenBy(x => x.CreatedAtUtc)
                .ToListAsync(ct);
            if (rows.Count > 0)
                return rows.Select(x => new LandingSlide { Title = x.Title, Text = x.Text, Icon = x.Icon, LinkText = x.LinkText, LinkUrl = x.LinkUrl }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the landing slides; showing the built-in ones.");
        }
        return content.Slides;
    }
}

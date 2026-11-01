using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Landing;

/// <summary>
/// The slides the landing page shows today: the administrator's slides that are active and inside their dates, in order.
/// Every slide is managed by an administrator; there are no built-in slides. If none is valid (or the database cannot be read) the list is empty.
/// </summary>
public sealed class HomeSlideProvider(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<HomeSlideProvider> logger)
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
            var ids = rows.Select(x => x.Id).ToList();
            // Only the file's name and size (never its contents) are read here.
            var files = ids.Count == 0 ? [] : await db.HomeSlideAttachments.AsNoTracking()
                .Where(a => ids.Contains(a.SlideId)).Select(a => new { a.SlideId, a.FileName, a.SizeBytes }).ToListAsync(ct);
            return rows.Select(x =>
            {
                var file = files.FirstOrDefault(f => f.SlideId == x.Id);
                return new LandingSlide
                {
                    Title = x.Title, Text = x.Text, Icon = x.Icon, LinkText = x.LinkText, LinkUrl = x.LinkUrl,
                    FileSlideId = file is null ? null : x.Id, FileName = file?.FileName, FileSize = file?.SizeBytes ?? 0,
                };
            }).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the landing slides.");
            return [];
        }
    }
}

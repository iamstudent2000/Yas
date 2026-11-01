using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Domain.Landing;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Landing;

/// <summary>The links of the home page (quick access and the group tree), from the database. An unreadable database gives an empty page section.</summary>
public sealed class PortalLinkProvider(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<PortalLinkProvider> logger)
{
    public async Task<LinkTree> GetTreeAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var groups = await db.LinkGroups.AsNoTracking().ToListAsync(ct);
            var links = await db.PortalLinks.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
            return LinkTree.Build(groups, links);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the portal links.");
            return LinkTree.Build([], []);
        }
    }
}

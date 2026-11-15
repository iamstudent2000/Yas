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
            // Link groups form the folders of the tree.
            var groups = await db.LinkGroups.AsNoTracking().ToListAsync(ct);
            // Only links that an administrator left switched on are included.
            var links = await db.PortalLinks.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct);
            // Put links into their groups to form the tree shown on the home page.
            return LinkTree.Build(groups, links);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the portal links.");
            return LinkTree.Build([], []);
        }
    }
}

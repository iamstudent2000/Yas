using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Landing;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Landing;

// Gives the home page its tiles (the big buttons of the last section): the switched-on ones, in their order.
public sealed class HomeTileProvider(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public async Task<IReadOnlyList<HomeTile>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return HomeTile.ForHomePage(await db.HomeTiles.AsNoTracking().Where(x => x.IsActive).ToListAsync(ct));
    }
}

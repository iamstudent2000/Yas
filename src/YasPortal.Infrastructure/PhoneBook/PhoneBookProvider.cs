using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using YasPortal.Domain.PhoneBook;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.PhoneBook;

/// <summary>The phone book for the reading page: active groups with their active contacts. An unreadable database gives an empty book.</summary>
public sealed class PhoneBookProvider(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<PhoneBookProvider> logger)
{
    public async Task<PhoneBookTree> GetTreeAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var groups = await db.ContactGroups.AsNoTracking().ToListAsync(ct);
            var contacts = await db.Contacts.AsNoTracking().ToListAsync(ct);
            return PhoneBookTree.Build(groups, contacts, includeInactive);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the phone book.");
            return PhoneBookTree.Build([], []);
        }
    }
}

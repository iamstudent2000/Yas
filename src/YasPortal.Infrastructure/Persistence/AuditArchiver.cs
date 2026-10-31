using Microsoft.EntityFrameworkCore;

namespace YasPortal.Infrastructure.Persistence;

/// <summary>
/// Moves old audit rows to the archive: nothing is deleted, the rows get an <c>ArchivedAtUtc</c> time and drop out of the
/// default audit view (the audit page can show them with its "source" filter). Used by the daily background job and by the
/// administrator's button.
/// </summary>
public sealed class AuditArchiver(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    private const int BatchSize = 2000;

    /// <summary>How many current (not yet archived) rows are older than <paramref name="cutoffUtc"/>.</summary>
    public async Task<int> CountOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.AuditEntries.AsNoTracking().CountAsync(x => x.ArchivedAtUtc == null && x.OccurredAtUtc < cutoffUtc, ct);
    }

    /// <summary>Archives every current row older than <paramref name="cutoffUtc"/>, a batch at a time; returns how many.</summary>
    public async Task<int> ArchiveOlderThanAsync(DateTime cutoffUtc, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var total = 0;
        while (true)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var batch = await db.AuditEntries
                .Where(x => x.ArchivedAtUtc == null && x.OccurredAtUtc < cutoffUtc)
                .OrderBy(x => x.OccurredAtUtc)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                return total;
            foreach (var entry in batch)
                entry.Archive(now);
            await db.SaveChangesAsync(ct);   // audit rows are never audited themselves, so this adds no new rows
            total += batch.Count;
        }
    }
}

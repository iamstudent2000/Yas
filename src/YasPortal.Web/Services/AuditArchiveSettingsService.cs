using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Auditing;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>Reads and saves the single audit-archive setting (automatic or manual, and the age in months).</summary>
public sealed class AuditArchiveSettingsService(IDbContextFactory<ApplicationDbContext> dbFactory, IPermissionChecker permissions,
    IConfiguration configuration, ILogger<AuditArchiveSettingsService> logger)
{
    // Until an administrator saves the setting, appsettings Audit:ArchiveAfterMonths gives the starting values (0 = manual only).
    private AuditArchiveSetting Defaults()
    {
        var months = configuration.GetValue("Audit:ArchiveAfterMonths", 12);
        return AuditArchiveSetting.CreateDefault(months > 0, months > 0 ? months : 12);
    }

    /// <summary>The saved setting, or the defaults when none is saved yet or it cannot be read.</summary>
    public async Task<AuditArchiveSetting> GetAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.AuditArchiveSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == AuditArchiveSetting.SingletonId, ct) ?? Defaults();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the audit archive setting; using the defaults.");
            return Defaults();
        }
    }

    public async Task SaveAsync(bool automaticEnabled, int afterMonths, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.AdminAuditLogManage, ct))
            throw new UnauthorizedAccessException("Changing the audit archive setting needs " + Perm.AdminAuditLogManage + ".");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.AuditArchiveSettings.SingleOrDefaultAsync(x => x.Id == AuditArchiveSetting.SingletonId, ct);
        var isNew = setting is null;
        setting ??= Defaults();
        setting.Update(automaticEnabled, afterMonths);
        if (isNew)
            db.AuditArchiveSettings.Add(setting);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Remembers that the daily job ran (used by the background job; no permission check).</summary>
    public async Task RecordAutomaticRunAsync(DateTime atUtc, int count, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.AuditArchiveSettings.SingleOrDefaultAsync(x => x.Id == AuditArchiveSetting.SingletonId, ct);
        var isNew = setting is null;
        setting ??= Defaults();
        setting.RecordAutomaticRun(atUtc, count);
        if (isNew)
            db.AuditArchiveSettings.Add(setting);
        await db.SaveChangesAsync(ct);
    }
}

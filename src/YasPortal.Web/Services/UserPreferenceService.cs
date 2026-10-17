using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Preferences;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>Reads and saves each employee's appearance settings (theme, accent color, font, text size, density).</summary>
public sealed class UserPreferenceService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<UserPreferenceService> logger)
{
    /// <summary>The employee's saved settings, or null when none are saved or they cannot be read (the page then uses defaults — looks must never block a page).</summary>
    public async Task<UserUiPreference?> GetAsync(Guid? employeeId, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return null;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.UiPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read appearance settings for {EmployeeId}; using defaults.", id);
            return null;
        }
    }

    public async Task<UserUiPreference> SaveAsync(Guid employeeId, UiTheme theme, UiAccent accent, UiFont font, UiTextSize textSize, UiDensity density, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pref = await db.UiPreferences.SingleOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        if (pref is null)
        {
            pref = new UserUiPreference(employeeId);
            db.UiPreferences.Add(pref);
        }
        pref.Update(theme, accent, font, textSize, density);
        await db.SaveChangesAsync(ct);
        return pref;
    }

    public async Task ResetAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var pref = await db.UiPreferences.SingleOrDefaultAsync(x => x.EmployeeId == employeeId, ct);
        if (pref is null)
            return;
        db.UiPreferences.Remove(pref);
        await db.SaveChangesAsync(ct);
    }
}

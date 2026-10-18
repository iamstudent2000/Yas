using Microsoft.EntityFrameworkCore;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>One predefined avatar: a boxicons icon on a colored circle.</summary>
public sealed record AvatarOption(string Key, string Icon, string Color, string Label);

/// <summary>The fixed set of avatars an employee can choose from. Keys are what gets stored.</summary>
public static class AvatarCatalog
{
    public static readonly IReadOnlyList<AvatarOption> All =
    [
        new("user", "bx-user", "#2563eb", "کاربر"),
        new("smile", "bx-smile", "#f59e0b", "لبخند"),
        new("star", "bx-star", "#d97706", "ستاره"),
        new("heart", "bx-heart", "#e11d48", "قلب"),
        new("rocket", "bx-rocket", "#7c3aed", "موشک"),
        new("leaf", "bx-leaf", "#16a34a", "برگ"),
        new("sun", "bx-sun", "#ea580c", "خورشید"),
        new("moon", "bx-moon", "#4f46e5", "ماه"),
        new("bulb", "bx-bulb", "#ca8a04", "ایده"),
        new("crown", "bx-crown", "#b45309", "تاج"),
        new("ghost", "bx-ghost", "#64748b", "روح"),
        new("planet", "bx-planet", "#0891b2", "سیاره"),
        new("bot", "bx-bot", "#0d9488", "ربات"),
        new("diamond", "bx-diamond", "#db2777", "الماس"),
        new("coffee", "bx-coffee", "#92400e", "قهوه"),
        new("music", "bx-music", "#9333ea", "موسیقی"),
    ];

    public static AvatarOption? Find(string? key) =>
        string.IsNullOrWhiteSpace(key) ? null : All.FirstOrDefault(x => x.Key == key);
}

/// <summary>Loads and saves the employee's chosen avatar and tells the open page (top bar, profile) when it changes.</summary>
public sealed class AvatarService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<AvatarService> logger)
{
    public event Action? Changed;

    public async Task<string?> GetKeyAsync(Guid? employeeId, CancellationToken ct = default)
    {
        if (employeeId is not Guid id)
            return null;
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.Employees.AsNoTracking().Where(x => x.Id == id).Select(x => x.AvatarKey).SingleOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the avatar for {EmployeeId}.", id);
            return null;
        }
    }

    /// <summary>Saves the avatar; null (or an unknown key is rejected) resets to the default.</summary>
    public async Task SaveAsync(Guid employeeId, string? key, CancellationToken ct = default)
    {
        if (key is not null && AvatarCatalog.Find(key) is null)
            throw new ArgumentException("Unknown avatar.", nameof(key));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var employee = await db.Employees.SingleAsync(x => x.Id == employeeId, ct);
        employee.SetAvatar(key);
        await db.SaveChangesAsync(ct);
        Changed?.Invoke();
    }
}

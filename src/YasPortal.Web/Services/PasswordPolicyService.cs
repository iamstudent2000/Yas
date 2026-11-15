using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Security;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

// The password rules for the whole portal (length, required character types, forbidden passwords). There is only one
// policy row. Administrators edit it on the password policy page; the sign-in and change-password screens enforce it.
/// <summary>Reads and saves the single system-wide password policy.</summary>
public sealed class PasswordPolicyService(IDbContextFactory<ApplicationDbContext> dbFactory, IPermissionChecker permissions, ILogger<PasswordPolicyService> logger)
{
    /// <summary>The saved policy, or the defaults when none is saved yet or it cannot be read.</summary>
    public async Task<PasswordPolicy> GetAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            return await db.PasswordPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == PasswordPolicy.SingletonId, ct)
                   ?? PasswordPolicy.CreateDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not read the password policy; using the defaults.");
            return PasswordPolicy.CreateDefault();
        }
    }

    // Saves the policy. Refuses unless the user may manage the password policy; the first save creates the row.
    public async Task SaveAsync(int minLength, int maxLength, bool upper, bool lower, bool digit, bool symbol,
        bool disallowUsername, bool disallowCommon, bool disallowReuse, CancellationToken ct = default)
    {
        if (!await permissions.HasPermissionAsync(Perm.AdminPasswordPolicyManage, ct))
            throw new UnauthorizedAccessException("Changing the password policy needs " + Perm.AdminPasswordPolicyManage + ".");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var policy = await db.PasswordPolicies.SingleOrDefaultAsync(x => x.Id == PasswordPolicy.SingletonId, ct);
        var isNew = policy is null;
        policy ??= PasswordPolicy.CreateDefault();
        policy.Update(minLength, maxLength, upper, lower, digit, symbol, disallowUsername, disallowCommon, disallowReuse);
        if (isNew)
            db.PasswordPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
    }
}

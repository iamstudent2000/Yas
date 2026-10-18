using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Security;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>Reads and saves the single system-wide password policy.</summary>
public sealed class PasswordPolicyService(IDbContextFactory<ApplicationDbContext> dbFactory, ILogger<PasswordPolicyService> logger)
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

    public async Task SaveAsync(int minLength, int maxLength, bool upper, bool lower, bool digit, bool symbol,
        bool disallowUsername, bool disallowCommon, bool disallowReuse, CancellationToken ct = default)
    {
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

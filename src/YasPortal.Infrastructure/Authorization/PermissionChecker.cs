using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(IDbContextFactory<ApplicationDbContext> dbContextFactory) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        // Permission checks can run concurrently in a Blazor Server circuit.
        // Never use the circuit-scoped DbContext for these reads.
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var employeeId = await db.Employees
            .Where(x => x.Username != null)
            .Select(x => new { x.Id })
            .FirstOrDefaultAsync(cancellationToken);

        return false;
    }
}

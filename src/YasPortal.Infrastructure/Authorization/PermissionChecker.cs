using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    AuthenticationStateProvider authenticationStateProvider) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return await HasPermissionAsync(user, permissionCode, cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId))
            return false;

        if (!Guid.TryParse(user.FindFirst(AuthClaimNames.ActivePositionId)?.Value, out var positionId))
            return false;

        if (permissionCode.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(user.FindFirst(AuthClaimNames.IsAdmin)?.Value, "True", StringComparison.OrdinalIgnoreCase))
            return false;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await db.UserPositionPermissions
            .AnyAsync(
                x => x.EmployeeId == employeeId &&
                     x.PositionId == positionId &&
                     x.Permission.Code == permissionCode,
                cancellationToken);
    }
}

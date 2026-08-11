using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Application.Persistence;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(IApplicationDbContext db, AuthenticationStateProvider authenticationStateProvider) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        if (user.Identity?.IsAuthenticated != true)
            return false;

        if (!Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var employeeId))
            return false;

        if (!Guid.TryParse(user.FindFirst(AuthClaimNames.ActivePositionId)?.Value, out var positionId))
            return false;

        return await db.UserPositionPermissions
            .AnyAsync(
                x => x.EmployeeId == employeeId &&
                     x.PositionId == positionId &&
                     x.Permission.Code == permissionCode,
                cancellationToken);
    }
}

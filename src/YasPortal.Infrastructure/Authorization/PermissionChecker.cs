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

        var isAdmin = string.Equals(
            user.FindFirst(AuthClaimNames.IsAdmin)?.Value,
            "True",
            StringComparison.OrdinalIgnoreCase);

        // Administrators are a separate user type. Their permissions are direct
        // Employee + Permission assignments and do not require an organizational position.
        if (isAdmin)
        {
            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            return await db.EmployeePermissions
                .AnyAsync(
                    x => x.EmployeeId == employeeId &&
                         x.Permission.Code == permissionCode,
                    cancellationToken);
        }

        if (!Guid.TryParse(user.FindFirst(AuthClaimNames.ActivePositionId)?.Value, out var positionId))
            return false;

        // Non-admin employees can only use permissions assigned to their active position.
        if (permissionCode.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase))
            return false;

        await using var employeeDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await employeeDb.UserPositionPermissions
            .AnyAsync(
                x => x.EmployeeId == employeeId &&
                     x.PositionId == positionId &&
                     x.Permission.Code == permissionCode,
                cancellationToken);
    }
}

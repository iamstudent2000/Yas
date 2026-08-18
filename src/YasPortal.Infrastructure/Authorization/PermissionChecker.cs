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

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(permissionCode))
            return false;

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId))
            return false;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Authentication cookies can outlive changes made by an administrator. The
        // database is therefore the source of truth for account status and admin state;
        // never trust those mutable values from claims for authorization decisions.
        var employee = await db.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.IsActive, x.IsAdmin })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null || !employee.IsActive)
            return false;

        if (employee.IsAdmin)
        {
            // Admin permissions = direct employee permissions UNION permissions from
            // groups assigned directly to the employee.
            return await db.EmployeePermissions.AnyAsync(
                       x => x.EmployeeId == employeeId && x.Permission.Code == permissionCode,
                       cancellationToken)
                || await db.EmployeePermissionGroups.AnyAsync(
                       x => x.EmployeeId == employeeId
                            && db.PermissionGroupPermissions.Any(gp =>
                                gp.GroupId == x.GroupId && gp.Permission.Code == permissionCode),
                       cancellationToken);
        }

        if (permissionCode.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Guid.TryParse(user.FindFirst(AuthClaimNames.ActivePositionId)?.Value, out var positionId))
            return false;

        // The active-position claim is mutable authentication state and can become stale
        // when an administrator ends an assignment while the user is still logged in.
        // A permission tied to an old position must never remain effective after that
        // position is no longer actively assigned to the employee.
        var hasActivePosition = await db.EmployeePositions.AnyAsync(
            x => x.EmployeeId == employeeId && x.PositionId == positionId && x.EndedAt == null,
            cancellationToken);

        if (!hasActivePosition)
            return false;

        // Employee+Position permissions = direct permissions UNION group permissions.
        return await db.UserPositionPermissions.AnyAsync(
                   x => x.EmployeeId == employeeId
                        && x.PositionId == positionId
                        && x.Permission.Code == permissionCode,
                   cancellationToken)
            || await db.UserPositionPermissionGroups.AnyAsync(
                   x => x.EmployeeId == employeeId
                        && x.PositionId == positionId
                        && db.PermissionGroupPermissions.Any(gp =>
                            gp.GroupId == x.GroupId && gp.Permission.Code == permissionCode),
                   cancellationToken);
    }
}

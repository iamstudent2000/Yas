using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(
    IDbContextFactory<ApplicationDbContext> dbContextFactory,
    AuthenticationStateProvider authenticationStateProvider,
    CurrentUser? currentUser = null) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return await HasPermissionAsync(user, permissionCode, cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode, CancellationToken cancellationToken = default)
    {
        // Keep the scoped current-user context synchronized with the exact principal
        // used for this authorization decision. This prevents services that consume
        // ICurrentUser from retaining an old active-position value after a position
        // switch or authentication-state change.
        currentUser?.SetPrincipal(user);

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
            return await db.EmployeePermissions.AnyAsync(
                       x => x.EmployeeId == employeeId && x.Permission.Code == permissionCode,
                       cancellationToken)
                || await db.EmployeePermissionGroups.AnyAsync(
                       x => x.EmployeeId == employeeId
                            && db.PermissionGroupPermissions.Any(gp =>
                                gp.GroupId == x.GroupId && gp.Permission.Code == permissionCode),
                       cancellationToken);
        }

        // Admin permissions are never inherited from a user's position.
        if (permissionCode.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Guid.TryParse(user.FindFirst(AuthClaimNames.ActivePositionId)?.Value, out var positionId))
            return false;

        // The claim identifies the selected position, but the database is authoritative.
        // If the assignment has ended, the old position's permissions immediately stop
        // working even though the authentication cookie has not yet expired.
        var hasActivePosition = await db.EmployeePositions.AnyAsync(
            x => x.EmployeeId == employeeId && x.PositionId == positionId && x.EndedAt == null,
            cancellationToken);

        if (!hasActivePosition)
            return false;

        // Effective permissions are ONLY User + Active Position permissions:
        // direct permissions UNION permissions supplied through position groups.
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

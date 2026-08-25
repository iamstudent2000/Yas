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

    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        currentUser?.SetPrincipal(user);

        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(permissionCode))
            return false;

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId))
            return false;

        var positionId = Guid.TryParse(
            user.FindFirst(AuthClaimNames.ActivePositionId)?.Value,
            out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Always read the current employee state from the database. In particular,
        // do not cache authorization because changing the active position must
        // immediately change the effective permissions.
        var employee = await db.Employees
            .AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.IsActive, x.IsAdmin })
            .SingleOrDefaultAsync(cancellationToken);

        if (employee is null || !employee.IsActive)
            return false;

        // Admin users do not receive permissions through a position. Their access
        // is still permission-based and comes only from their direct/group
        // employee permissions.
        if (employee.IsAdmin)
        {
            var permissions = await LoadEmployeePermissionsAsync(db, employeeId, cancellationToken);
            return permissions.Contains(permissionCode);
        }

        // Normal users receive permissions only from the currently selected
        // active position. Never fall back to permissions from another position.
        if (positionId is null || positionId == Guid.Empty)
            return false;

        var hasActivePosition = await db.EmployeePositions
            .AsNoTracking()
            .AnyAsync(x => x.EmployeeId == employeeId
                           && x.PositionId == positionId.Value
                           && x.EndedAt == null,
                cancellationToken);

        if (!hasActivePosition)
            return false;

        // Normal users must never obtain Admin.* permissions through the
        // position-permission tables.
        if (permissionCode.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase))
            return false;

        var hasDirectPermission = await db.UserPositionPermissions
            .AsNoTracking()
            .AnyAsync(x => x.EmployeeId == employeeId
                           && x.PositionId == positionId.Value
                           && x.Permission.Code == permissionCode,
                cancellationToken);

        if (hasDirectPermission)
            return true;

        return await db.UserPositionPermissionGroups
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.PositionId == positionId.Value)
            .SelectMany(x => db.PermissionGroupPermissions
                .Where(gp => gp.GroupId == x.GroupId)
                .Select(gp => gp.Permission.Code))
            .AnyAsync(code => code == permissionCode, cancellationToken);
    }

    private static async Task<HashSet<string>> LoadEmployeePermissionsAsync(
        ApplicationDbContext db,
        Guid employeeId,
        CancellationToken cancellationToken)
    {
        var direct = await db.EmployeePermissions
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .Select(x => x.Permission.Code)
            .ToListAsync(cancellationToken);

        var grouped = await db.EmployeePermissionGroups
            .AsNoTracking()
            .Where(x => x.EmployeeId == employeeId)
            .SelectMany(x => db.PermissionGroupPermissions
                .Where(gp => gp.GroupId == x.GroupId)
                .Select(gp => gp.Permission.Code))
            .ToListAsync(cancellationToken);

        direct.AddRange(grouped);
        return direct.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void Invalidate()
    {
        // Authorization is intentionally not cached. Keep this method for callers
        // that already invalidate permissions after changes.
    }
}

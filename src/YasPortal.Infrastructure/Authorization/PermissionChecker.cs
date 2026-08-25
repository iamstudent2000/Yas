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
    private AuthorizationSnapshot? _snapshot;
    private Guid _snapshotEmployeeId;
    private Guid? _snapshotPositionId;
    private bool _snapshotPrincipalIsAdmin;
    private DateTime _snapshotExpiresAtUtc;
    private readonly SemaphoreSlim _snapshotLock = new(1, 1);

    private static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(5);

    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return await HasPermissionAsync(user, permissionCode, cancellationToken);
    }

    public async Task<bool> HasPermissionAsync(ClaimsPrincipal user, string permissionCode, CancellationToken cancellationToken = default)
    {
        currentUser?.SetPrincipal(user);

        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(permissionCode))
            return false;

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var employeeId))
            return false;

        var principalPositionId = Guid.TryParse(
            user.FindFirst(AuthClaimNames.ActivePositionId)?.Value,
            out var parsedPositionId)
            ? parsedPositionId
            : (Guid?)null;

        var principalIsAdmin = bool.TryParse(
            user.FindFirst(AuthClaimNames.IsAdmin)?.Value,
            out var parsedIsAdmin) && parsedIsAdmin;

        var snapshot = await GetSnapshotAsync(
            employeeId,
            principalPositionId,
            principalIsAdmin,
            cancellationToken);

        return snapshot.IsActive && snapshot.HasPermission(permissionCode);
    }

    private async Task<AuthorizationSnapshot> GetSnapshotAsync(
        Guid employeeId,
        Guid? positionId,
        bool principalIsAdmin,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (IsCurrentSnapshot(employeeId, positionId, principalIsAdmin, now))
            return _snapshot!;

        await _snapshotLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTime.UtcNow;
            if (IsCurrentSnapshot(employeeId, positionId, principalIsAdmin, now))
                return _snapshot!;

            await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            var employee = await db.Employees
                .AsNoTracking()
                .Where(x => x.Id == employeeId)
                .Select(x => new { x.IsActive, x.IsAdmin })
                .SingleOrDefaultAsync(cancellationToken);

            if (employee is null || !employee.IsActive)
            {
                return StoreSnapshot(employeeId, positionId, principalIsAdmin,
                    new AuthorizationSnapshot(false, false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase)), now);
            }

            var isAdmin = employee.IsAdmin;

            if (isAdmin)
            {
                var permissions = await LoadEmployeePermissionsAsync(db, employeeId, cancellationToken);
                return StoreSnapshot(employeeId, positionId, principalIsAdmin,
                    new AuthorizationSnapshot(true, true, true, permissions), now);
            }

            if (positionId is null || positionId == Guid.Empty)
            {
                return StoreSnapshot(employeeId, positionId, principalIsAdmin,
                    new AuthorizationSnapshot(true, false, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase)), now);
            }

            var permissionsForPosition = await db.UserPositionPermissions
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId
                            && x.PositionId == positionId.Value
                            && db.EmployeePositions.Any(ep =>
                                ep.EmployeeId == employeeId
                                && ep.PositionId == positionId.Value
                                && ep.EndedAt == null))
                .Select(x => x.Permission.Code)
                .ToListAsync(cancellationToken);

            var groupPermissions = await db.UserPositionPermissionGroups
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId
                            && x.PositionId == positionId.Value
                            && db.EmployeePositions.Any(ep =>
                                ep.EmployeeId == employeeId
                                && ep.PositionId == positionId.Value
                                && ep.EndedAt == null))
                .SelectMany(x => db.PermissionGroupPermissions
                    .Where(gp => gp.GroupId == x.GroupId)
                    .Select(gp => gp.Permission.Code))
                .ToListAsync(cancellationToken);

            var hasActivePosition = await db.EmployeePositions
                .AsNoTracking()
                .AnyAsync(x => x.EmployeeId == employeeId
                               && x.PositionId == positionId.Value
                               && x.EndedAt == null,
                    cancellationToken);

            permissionsForPosition.AddRange(groupPermissions);

            return StoreSnapshot(employeeId, positionId, principalIsAdmin,
                new AuthorizationSnapshot(
                    true,
                    false,
                    hasActivePosition,
                    permissionsForPosition.ToHashSet(StringComparer.OrdinalIgnoreCase)),
                now);
        }
        finally
        {
            _snapshotLock.Release();
        }
    }

    private async Task<HashSet<string>> LoadEmployeePermissionsAsync(
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

    private bool IsCurrentSnapshot(
        Guid employeeId,
        Guid? positionId,
        bool principalIsAdmin,
        DateTime nowUtc) =>
        _snapshot is not null
        && nowUtc < _snapshotExpiresAtUtc
        && _snapshotEmployeeId == employeeId
        && _snapshotPositionId == positionId
        && _snapshotPrincipalIsAdmin == principalIsAdmin;

    private AuthorizationSnapshot StoreSnapshot(
        Guid employeeId,
        Guid? positionId,
        bool principalIsAdmin,
        AuthorizationSnapshot snapshot,
        DateTime nowUtc)
    {
        _snapshotEmployeeId = employeeId;
        _snapshotPositionId = positionId;
        _snapshotPrincipalIsAdmin = principalIsAdmin;
        _snapshotExpiresAtUtc = nowUtc + SnapshotLifetime;
        _snapshot = snapshot;
        return snapshot;
    }

    public void Invalidate()
    {
        _snapshot = null;
        _snapshotExpiresAtUtc = DateTime.MinValue;
    }

    private sealed record AuthorizationSnapshot(
        bool IsActive,
        bool IsAdmin,
        bool HasActivePosition,
        IReadOnlySet<string> Permissions)
    {
        public bool HasPermission(string permissionCode)
        {
            if (!IsActive)
                return false;

            if (!IsAdmin && !HasActivePosition)
                return false;

            if (!IsAdmin && permissionCode.StartsWith("Admin.", StringComparison.OrdinalIgnoreCase))
                return false;

            return Permissions.Contains(permissionCode);
        }
    }
}

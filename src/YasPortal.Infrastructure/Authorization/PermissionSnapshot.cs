using Microsoft.EntityFrameworkCore;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Authorization;

// Builds the list of permission codes that is stored in the sign-in cookie. It lives here (not in Program.cs) so it can be tested.
public static class PermissionSnapshot
{
    // Collects the permission codes a person holds, from the database. Administrators get only what is assigned to
    // them directly or through permission groups. Everyone else gets only what the currently chosen position was
    // given (directly or through groups). There is no all-powerful role that skips these checks.
    public static async Task<HashSet<string>> BuildAsync(
        ApplicationDbContext db,
        Guid employeeId,
        Guid? positionId,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        List<string> permissions;

        if (isAdmin)
        {
            // Admin is not a special full-access bypass. Admin access is still exactly
            // the direct/group permissions assigned to the employee.
            permissions = await db.EmployeePermissions
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

            permissions.AddRange(grouped);
        }
        else
        {
            if (positionId is null || positionId == Guid.Empty)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Normal users get permissions only from User + currently selected Position.
            permissions = await db.UserPositionPermissions
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.PositionId == positionId.Value)
                .Select(x => x.Permission.Code)
                .ToListAsync(cancellationToken);

            var grouped = await db.UserPositionPermissionGroups
                .AsNoTracking()
                .Where(x => x.EmployeeId == employeeId && x.PositionId == positionId.Value)
                .SelectMany(x => db.PermissionGroupPermissions
                    .Where(gp => gp.GroupId == x.GroupId)
                    .Select(gp => gp.Permission.Code))
                .ToListAsync(cancellationToken);

            permissions.AddRange(grouped);
        }

        return permissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}

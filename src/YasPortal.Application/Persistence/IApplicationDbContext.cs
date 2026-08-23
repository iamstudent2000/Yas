using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;

namespace YasPortal.Application.Persistence;

public interface IApplicationDbContext
{
    DbSet<Organization> Organizations
    {
        get;
    }
    DbSet<Employee> Employees
    {
        get;
    }
    DbSet<Position> Positions
    {
        get;
    }
    DbSet<EmployeePosition> EmployeePositions
    {
        get;
    }
    DbSet<PositionAssignmentHistory> PositionAssignmentHistories
    {
        get;
    }
    DbSet<Permission> Permissions
    {
        get;
    }
    DbSet<UserPositionPermission> UserPositionPermissions
    {
        get;
    }
    DbSet<EmployeePermission> EmployeePermissions
    {
        get;
    }
    DbSet<PermissionGroup> PermissionGroups
    {
        get;
    }
    DbSet<PermissionGroupPermission> PermissionGroupPermissions
    {
        get;
    }
    DbSet<UserPositionPermissionGroup> UserPositionPermissionGroups
    {
        get;
    }
    DbSet<EmployeePermissionGroup> EmployeePermissionGroups
    {
        get;
    }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

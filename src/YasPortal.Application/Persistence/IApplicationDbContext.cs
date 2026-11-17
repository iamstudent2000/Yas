using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;

namespace YasPortal.Application.Persistence;

// Contract for the database as the Application layer sees it. Each DbSet is one table (a list of rows of one kind).
// The real database class implements this; services depend on this interface so business logic stays separate from storage.
// The tables here cover the organization chart (organizations, employees, positions) and the permission system.
public interface IApplicationDbContext
{
    // Organizations (departments / units) of the company.
    DbSet<Organization> Organizations
    {
        get;
    }
    // People who can sign in.
    DbSet<Employee> Employees
    {
        get;
    }
    // Job seats in the organization; workflow steps are assigned to positions.
    DbSet<Position> Positions
    {
        get;
    }
    // Links between an employee and the positions that person currently or previously occupied.
    DbSet<EmployeePosition> EmployeePositions
    {
        get;
    }
    // Permanent history of who held which position and when; never deleted.
    DbSet<PositionAssignmentHistory> PositionAssignmentHistories
    {
        get;
    }
    // The catalogue of permissions (individual rights).
    DbSet<Permission> Permissions
    {
        get;
    }
    // Permissions granted directly to a person acting in a given position.
    DbSet<UserPositionPermission> UserPositionPermissions
    {
        get;
    }
    // Permissions granted directly to an employee, independent of position.
    DbSet<EmployeePermission> EmployeePermissions
    {
        get;
    }
    // Named bundles of permissions that can be granted in one step.
    DbSet<PermissionGroup> PermissionGroups
    {
        get;
    }
    // Which permissions belong to which group.
    DbSet<PermissionGroupPermission> PermissionGroupPermissions
    {
        get;
    }
    // Groups granted to a person acting in a given position.
    DbSet<UserPositionPermissionGroup> UserPositionPermissionGroups
    {
        get;
    }
    // Groups granted directly to an employee.
    DbSet<EmployeePermissionGroup> EmployeePermissionGroups
    {
        get;
    }
    // Writes all pending changes to the database in one go.
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

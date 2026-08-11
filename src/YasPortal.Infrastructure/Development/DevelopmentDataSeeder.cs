using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Infrastructure.Development;

public static class DevelopmentDataSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db, IPasswordHasher<Employee> passwordHasher, CancellationToken ct = default)
    {
        // Development only: always start from a clean database so an old/partial
        // YasPortalClean database can never leave the application without its schema.
        await db.Database.EnsureDeletedAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);

        var admin = new Employee("admin", "System Administrator", isAdmin: true);
        admin.SetPasswordHash(passwordHasher.HashPassword(admin, "Admin123!"));

        var employee = new Employee("employee", "Demo Employee");
        employee.SetPasswordHash(passwordHasher.HashPassword(employee, "Employee123!"));

        var adminPosition = new Position("Administrator");
        var employeePosition = new Position("Employee");

        var adminUsersPermission = new Permission("Admin.Users", "Manage users");
        var adminPositionsPermission = new Permission("Admin.Positions", "Manage positions");
        var adminPermissionsPermission = new Permission("Admin.Permissions", "Manage permissions");
        var dashboardPermission = new Permission("Dashboard.View", "View dashboard");

        db.Employees.AddRange(admin, employee);
        db.Positions.AddRange(adminPosition, employeePosition);
        db.Permissions.AddRange(
            adminUsersPermission,
            adminPositionsPermission,
            adminPermissionsPermission,
            dashboardPermission);

        db.EmployeePositions.AddRange(
            new EmployeePosition(admin.Id, adminPosition.Id),
            new EmployeePosition(employee.Id, employeePosition.Id));

        db.UserPositionPermissions.AddRange(
            new UserPositionPermission(admin.Id, adminPosition.Id, adminUsersPermission.Id),
            new UserPositionPermission(admin.Id, adminPosition.Id, adminPositionsPermission.Id),
            new UserPositionPermission(admin.Id, adminPosition.Id, adminPermissionsPermission.Id),
            new UserPositionPermission(employee.Id, employeePosition.Id, dashboardPermission.Id));

        await db.SaveChangesAsync(ct);
    }
}

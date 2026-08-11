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
        await db.Database.EnsureCreatedAsync(ct);

        if (await db.Employees.AnyAsync(ct))
            return;

        var admin = new Employee("admin", "System Administrator", isAdmin: true);
        admin.SetPasswordHash(passwordHasher.HashPassword(admin, "Admin123!"));

        var employee = new Employee("employee", "Demo Employee");
        employee.SetPasswordHash(passwordHasher.HashPassword(employee, "Employee123!"));

        var position = new Position("Employee");
        var dashboardPermission = new Permission("Dashboard.View", "View dashboard");

        db.Employees.AddRange(admin, employee);
        db.Positions.Add(position);
        db.Permissions.Add(dashboardPermission);
        db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
        db.UserPositionPermissions.Add(new UserPositionPermission(employee.Id, position.Id, dashboardPermission.Id));

        await db.SaveChangesAsync(ct);
    }
}

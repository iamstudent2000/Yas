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

        // Development data is idempotent: existing records are preserved.
        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "Administrator", "HR Manager", "Department Manager", "Finance", "Employee" })
        {
            var position = await db.Positions.SingleOrDefaultAsync(x => x.Name == name, ct);
            if (position is null)
            {
                position = new Position(name);
                db.Positions.Add(position);
            }
            positions[name] = position;
        }

        var permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard.View"] = new Permission("Dashboard.View", "View dashboard"),
            ["Profile.View"] = new Permission("Profile.View", "View profile"),
            ["Requests.Create"] = new Permission("Requests.Create", "Create requests"),
            ["Requests.View"] = new Permission("Requests.View", "View requests"),
            ["Requests.Approve"] = new Permission("Requests.Approve", "Approve requests"),
            ["Requests.Reject"] = new Permission("Requests.Reject", "Reject requests"),
            ["Requests.ReturnToRequester"] = new Permission("Requests.ReturnToRequester", "Return requests to requester"),
            ["Requests.ReturnToPreviousStep"] = new Permission("Requests.ReturnToPreviousStep", "Return requests to previous step"),
            ["Employees.View"] = new Permission("Employees.View", "View employees"),
            ["Employees.Manage"] = new Permission("Employees.Manage", "Manage employees"),
            ["Positions.View"] = new Permission("Positions.View", "View positions"),
            ["Positions.Manage"] = new Permission("Positions.Manage", "Manage positions"),
            ["Permissions.View"] = new Permission("Permissions.View", "View permissions"),
            ["Permissions.Manage"] = new Permission("Permissions.Manage", "Manage permissions"),
            ["Admin.Users"] = new Permission("Admin.Users", "Manage users"),
            ["Admin.Positions"] = new Permission("Admin.Positions", "Manage positions"),
            ["Admin.Permissions"] = new Permission("Admin.Permissions", "Manage permissions")
        };

        foreach (var permission in permissions.Values)
        {
            var existing = await db.Permissions.SingleOrDefaultAsync(x => x.Code == permission.Code, ct);
            if (existing is null)
                db.Permissions.Add(permission);
            else
                permissions[permission.Code] = existing;
        }

        var employeeDefinitions = new[]
        {
            (Username: "admin", FullName: "System Administrator", IsAdmin: true, Password: "Admin123!"),
            (Username: "employee", FullName: "Demo Employee", IsAdmin: false, Password: "Employee123!"),
            (Username: "hr", FullName: "Sara HR", IsAdmin: false, Password: "Hr123!"),
            (Username: "manager", FullName: "Ali Department Manager", IsAdmin: false, Password: "Manager123!"),
            (Username: "finance", FullName: "Reza Finance", IsAdmin: false, Password: "Finance123!")
        };

        var employees = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in employeeDefinitions)
        {
            var employee = await db.Employees
                .SingleOrDefaultAsync(x => x.Username == definition.Username, ct);

            if (employee is null)
            {
                employee = new Employee(definition.Username, definition.FullName, definition.IsAdmin);
                employee.SetPasswordHash(passwordHasher.HashPassword(employee, definition.Password));
                db.Employees.Add(employee);
            }
            else if (string.IsNullOrWhiteSpace(employee.PasswordHash))
            {
                employee.SetPasswordHash(passwordHasher.HashPassword(employee, definition.Password));
            }

            employees[definition.Username] = employee;
        }

        await db.SaveChangesAsync(ct);

        // User + Position assignments. Manager deliberately has two positions
        // so active-position switching can be tested.
        await EnsureEmployeePosition(db, employees["admin"], positions["Administrator"], ct);
        await EnsureEmployeePosition(db, employees["employee"], positions["Employee"], ct);
        await EnsureEmployeePosition(db, employees["hr"], positions["HR Manager"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["Department Manager"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["Employee"], ct);
        await EnsureEmployeePosition(db, employees["finance"], positions["Finance"], ct);

        await db.SaveChangesAsync(ct);

        // Administrator receives every development permission.
        foreach (var permission in permissions.Values)
            await EnsurePermission(db, employees["admin"], positions["Administrator"], permission, ct);

        await Grant(db, employees["employee"], positions["Employee"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View" }, ct);
        await Grant(db, employees["hr"], positions["HR Manager"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Employees.View" }, ct);
        await Grant(db, employees["manager"], positions["Department Manager"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Requests.ReturnToPreviousStep", "Employees.View" }, ct);
        await Grant(db, employees["manager"], positions["Employee"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View" }, ct);
        await Grant(db, employees["finance"], positions["Finance"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject" }, ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureEmployeePosition(ApplicationDbContext db, Employee employee, Position position, CancellationToken ct)
    {
        var exists = await db.EmployeePositions.AnyAsync(x =>
            x.EmployeeId == employee.Id && x.PositionId == position.Id && x.EndedAt == null, ct);
        if (!exists)
            db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
    }

    private static async Task EnsurePermission(ApplicationDbContext db, Employee employee, Position position, Permission permission, CancellationToken ct)
    {
        var exists = await db.UserPositionPermissions.AnyAsync(x =>
            x.EmployeeId == employee.Id && x.PositionId == position.Id && x.PermissionId == permission.Id, ct);
        if (!exists)
            db.UserPositionPermissions.Add(new UserPositionPermission(employee.Id, position.Id, permission.Id));
    }

    private static async Task Grant(ApplicationDbContext db, Employee employee, Position position, IReadOnlyDictionary<string, Permission> permissions, IEnumerable<string> codes, CancellationToken ct)
    {
        foreach (var code in codes)
            await EnsurePermission(db, employee, position, permissions[code], ct);
    }
}

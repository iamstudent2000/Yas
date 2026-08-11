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

        var permissions = await EnsurePermissions(db, ct);
        var positions = await EnsurePositions(db, ct);
        var employees = await EnsureEmployees(db, passwordHasher, ct);

        await EnsureEmployeePosition(db, employees["admin"], positions["مدیر سیستم"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["مدیر"], ct);
        await EnsureEmployeePosition(db, employees["finance"], positions["کارشناس مالی"], ct);
        await EnsureEmployeePosition(db, employees["employee"], positions["کارمند"], ct);
        await EnsureEmployeePosition(db, employees["admin"], positions["کارشناس مالی"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["کارمند"], ct);

        await Grant(db, employees["admin"], positions["مدیر سیستم"], permissions, new[] { "Dashboard.View", "Profile.View", "Admin.Users", "Admin.Positions", "Admin.Permissions", "Admin.Organizations", "Requests.Create", "Requests.View", "Requests.Approve", "Requests.Reject" }, ct);
        await Grant(db, employees["manager"], positions["مدیر"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View", "Requests.Approve", "Requests.Reject" }, ct);
        await Grant(db, employees["employee"], positions["کارمند"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View" }, ct);
        await Grant(db, employees["finance"], positions["کارشناس مالی"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject" }, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureEmployeePosition(ApplicationDbContext db, Employee employee, Position position, CancellationToken ct)
    {
        var existingForEmployee = await db.EmployeePositions
            .SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.PositionId == position.Id && x.EndedAt == null, ct);

        if (existingForEmployee is not null)
            return;

        // The development seed is deterministic. If an older development database
        // contains the same active position under another employee, end that stale
        // assignment and persist the update before inserting the new assignment.
        // This ordering is required because SQL Server checks the filtered unique
        // index while the INSERT is executed and EF may otherwise batch the INSERT
        // before the UPDATE of the conflicting row.
        var conflictingAssignments = await db.EmployeePositions
            .Where(x => x.PositionId == position.Id && x.EmployeeId != employee.Id && x.EndedAt == null)
            .ToListAsync(ct);

        if (conflictingAssignments.Count > 0)
        {
            foreach (var assignment in conflictingAssignments)
                assignment.End();

            await db.SaveChangesAsync(ct);
        }

        db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
    }

    private static async Task EnsurePermission(ApplicationDbContext db, Employee employee, Position position, Permission permission, CancellationToken ct)
    {
        if (!await db.UserPositionPermissions.AnyAsync(x => x.EmployeeId == employee.Id && x.PositionId == position.Id && x.PermissionId == permission.Id, ct))
            db.UserPositionPermissions.Add(new UserPositionPermission(employee.Id, position.Id, permission.Id));
    }

    private static async Task Grant(ApplicationDbContext db, Employee employee, Position position, IReadOnlyDictionary<string, Permission> permissions, IEnumerable<string> codes, CancellationToken ct)
    {
        foreach (var code in codes)
            await EnsurePermission(db, employee, position, permissions[code], ct);
    }

    private static async Task<Dictionary<string, Permission>> EnsurePermissions(ApplicationDbContext db, CancellationToken ct)
    {
        var definitions = new[]
        {
            ("Dashboard.View", "مشاهده داشبورد"),
            ("Profile.View", "مشاهده پروفایل"),
            ("Admin.Users", "مدیریت کاربران"),
            ("Admin.Positions", "مدیریت سمت‌ها"),
            ("Admin.Permissions", "مدیریت مجوزها"),
            ("Admin.Organizations", "مدیریت سازمان‌ها"),
            ("Requests.Create", "ایجاد درخواست"),
            ("Requests.View", "مشاهده درخواست‌ها"),
            ("Requests.Approve", "تأیید درخواست‌ها"),
            ("Requests.Reject", "رد درخواست‌ها")
        };

        var result = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in definitions)
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(x => x.Code == code, ct);
            if (permission is null)
            {
                permission = new Permission(code, name);
                db.Permissions.Add(permission);
            }

            result[code] = permission;
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task<Dictionary<string, Position>> EnsurePositions(ApplicationDbContext db, CancellationToken ct)
    {
        var names = new[] { "مدیر سیستم", "مدیر", "کارشناس مالی", "کارمند" };
        var result = new Dictionary<string, Position>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var position = await db.Positions.SingleOrDefaultAsync(x => x.Title == name, ct);
            if (position is null)
            {
                position = new Position(name);
                db.Positions.Add(position);
                await db.SaveChangesAsync(ct);
            }

            result[name] = position;
        }

        return result;
    }

    private static async Task<Dictionary<string, Employee>> EnsureEmployees(ApplicationDbContext db, IPasswordHasher<Employee> passwordHasher, CancellationToken ct)
    {
        var definitions = new[]
        {
            ("admin", "admin", "مدیر سیستم", true),
            ("manager", "manager", "مدیر", false),
            ("finance", "finance", "کارشناس مالی", false),
            ("employee", "employee", "کارمند", false)
        };

        var result = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, username, displayName, isAdmin) in definitions)
        {
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Username == username, ct);
            if (employee is null)
            {
                employee = new Employee(username, displayName, isAdmin);
                employee.SetPasswordHash(passwordHasher.HashPassword(employee, "P@ssw0rd"));
                db.Employees.Add(employee);
                await db.SaveChangesAsync(ct);
            }

            result[key] = employee;
        }

        return result;
    }
}

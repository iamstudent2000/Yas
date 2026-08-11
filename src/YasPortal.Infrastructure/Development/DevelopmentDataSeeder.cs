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
        if (await db.Database.CanConnectAsync(ct))
        {
            var organizationsTableExists = await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[Organizations]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct);
            if (organizationsTableExists == 0) await db.Database.EnsureDeletedAsync(ct);
        }

        await db.Database.EnsureCreatedAsync(ct);

        var organizations = new Dictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "ستاد مرکزی", "منابع انسانی", "امور مالی" })
        {
            var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Name == name, ct);
            if (organization is null) { organization = new Organization(name); db.Organizations.Add(organization); }
            organizations[name] = organization;
        }

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "مدیر سامانه", "مدیر منابع انسانی", "مدیر واحد", "کارشناس مالی", "کارمند" })
        {
            var position = await db.Positions.SingleOrDefaultAsync(x => x.Name == name, ct);
            if (position is null) { position = new Position(name); db.Positions.Add(position); }
            positions[name] = position;
        }

        var permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dashboard.View"] = new Permission("Dashboard.View", "مشاهده داشبورد"),
            ["Profile.View"] = new Permission("Profile.View", "مشاهده پروفایل"),
            ["Requests.Create"] = new Permission("Requests.Create", "ایجاد درخواست"),
            ["Requests.View"] = new Permission("Requests.View", "مشاهده درخواست‌ها"),
            ["Requests.Approve"] = new Permission("Requests.Approve", "تأیید درخواست‌ها"),
            ["Requests.Reject"] = new Permission("Requests.Reject", "رد درخواست‌ها"),
            ["Requests.ReturnToRequester"] = new Permission("Requests.ReturnToRequester", "بازگرداندن درخواست به درخواست‌کننده"),
            ["Requests.ReturnToPreviousStep"] = new Permission("Requests.ReturnToPreviousStep", "بازگرداندن درخواست به مرحله قبل"),
            ["Employees.View"] = new Permission("Employees.View", "مشاهده کارکنان"),
            ["Employees.Manage"] = new Permission("Employees.Manage", "مدیریت کارکنان"),
            ["Organizations.View"] = new Permission("Organizations.View", "مشاهده سازمان‌ها"),
            ["Organizations.Manage"] = new Permission("Organizations.Manage", "مدیریت سازمان‌ها"),
            ["Positions.View"] = new Permission("Positions.View", "مشاهده سمت‌ها"),
            ["Positions.Manage"] = new Permission("Positions.Manage", "مدیریت سمت‌ها"),
            ["Permissions.View"] = new Permission("Permissions.View", "مشاهده مجوزها"),
            ["Permissions.Manage"] = new Permission("Permissions.Manage", "مدیریت مجوزها"),
            ["Admin.Users"] = new Permission("Admin.Users", "مدیریت کاربران"),
            ["Admin.Positions"] = new Permission("Admin.Positions", "مدیریت سمت‌ها"),
            ["Admin.Permissions"] = new Permission("Admin.Permissions", "مدیریت مجوزها"),
            ["Admin.Organizations"] = new Permission("Admin.Organizations", "مدیریت سازمان‌ها")
        };

        foreach (var permission in permissions.Values.ToList())
        {
            var existing = await db.Permissions.SingleOrDefaultAsync(x => x.Code == permission.Code, ct);
            if (existing is not null) permissions[permission.Code] = existing; else db.Permissions.Add(permission);
        }
        await db.SaveChangesAsync(ct);

        var employeeDefinitions = new[]
        {
            (Username: "admin", FullName: "مدیر سامانه", Organization: "ستاد مرکزی", IsAdmin: true, Password: "Admin123!"),
            (Username: "employee", FullName: "کارمند نمونه", Organization: "ستاد مرکزی", IsAdmin: false, Password: "Employee123!"),
            (Username: "hr", FullName: "سارا احمدی", Organization: "منابع انسانی", IsAdmin: false, Password: "Hr123!"),
            (Username: "manager", FullName: "علی رضایی", Organization: "ستاد مرکزی", IsAdmin: false, Password: "Manager123!"),
            (Username: "finance", FullName: "رضا محمدی", Organization: "امور مالی", IsAdmin: false, Password: "Finance123!")
        };

        var employees = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in employeeDefinitions)
        {
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Username == definition.Username, ct);
            if (employee is null)
            {
                employee = new Employee(definition.Username, definition.FullName, organizations[definition.Organization].Id, definition.IsAdmin);
                employee.SetPasswordHash(passwordHasher.HashPassword(employee, definition.Password));
                db.Employees.Add(employee);
            }
            else
            {
                employee.ChangeOrganization(organizations[definition.Organization].Id);
                employee.SetAdmin(definition.IsAdmin);
                if (string.IsNullOrWhiteSpace(employee.PasswordHash)) employee.SetPasswordHash(passwordHasher.HashPassword(employee, definition.Password));
            }
            employees[definition.Username] = employee;
        }
        await db.SaveChangesAsync(ct);

        await EnsureEmployeePosition(db, employees["admin"], positions["مدیر سامانه"], ct);
        await EnsureEmployeePosition(db, employees["employee"], positions["کارمند"], ct);
        await EnsureEmployeePosition(db, employees["hr"], positions["مدیر منابع انسانی"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["مدیر واحد"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["کارمند"], ct);
        await EnsureEmployeePosition(db, employees["finance"], positions["کارشناس مالی"], ct);
        await db.SaveChangesAsync(ct);

        foreach (var permission in permissions.Values) await EnsurePermission(db, employees["admin"], positions["مدیر سامانه"], permission, ct);
        await Grant(db, employees["employee"], positions["کارمند"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View" }, ct);
        await Grant(db, employees["hr"], positions["مدیر منابع انسانی"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Employees.View" }, ct);
        await Grant(db, employees["manager"], positions["مدیر واحد"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Requests.ReturnToPreviousStep", "Employees.View" }, ct);
        await Grant(db, employees["manager"], positions["کارمند"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View" }, ct);
        await Grant(db, employees["finance"], positions["کارشناس مالی"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject" }, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureEmployeePosition(ApplicationDbContext db, Employee employee, Position position, CancellationToken ct)
    {
        if (!await db.EmployeePositions.AnyAsync(x => x.EmployeeId == employee.Id && x.PositionId == position.Id && x.EndedAt == null, ct)) db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
    }

    private static async Task EnsurePermission(ApplicationDbContext db, Employee employee, Position position, Permission permission, CancellationToken ct)
    {
        if (!await db.UserPositionPermissions.AnyAsync(x => x.EmployeeId == employee.Id && x.PositionId == position.Id && x.PermissionId == permission.Id, ct)) db.UserPositionPermissions.Add(new UserPositionPermission(employee.Id, position.Id, permission.Id));
    }

    private static async Task Grant(ApplicationDbContext db, Employee employee, Position position, IReadOnlyDictionary<string, Permission> permissions, IEnumerable<string> codes, CancellationToken ct)
    {
        foreach (var code in codes) await EnsurePermission(db, employee, position, permissions[code], ct);
    }
}

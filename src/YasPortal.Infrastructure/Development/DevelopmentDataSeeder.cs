using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
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
            var lastActivePositionColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[Employees]', N'LastActivePositionId') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var parentPositionColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[Positions]', N'ParentPositionId') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            if (organizationsTableExists == 0 || !lastActivePositionColumnExists || !parentPositionColumnExists)
                await db.Database.EnsureDeletedAsync(ct);
        }

        try { await db.Database.EnsureCreatedAsync(ct); }
        catch (SqlException ex) when (ex.Number == 1801) { }

        await db.Database.ExecuteSqlRawAsync("""
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeePositions') AND name = N'IX_EmployeePositions_PositionId')
                DROP INDEX [IX_EmployeePositions_PositionId] ON [dbo].[EmployeePositions];
            CREATE UNIQUE INDEX [IX_EmployeePositions_PositionId]
                ON [dbo].[EmployeePositions] ([PositionId])
                WHERE [EndedAt] IS NULL;
            """, ct);

        var organizations = new Dictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "ستاد مرکزی", "منابع انسانی", "امور مالی" })
        {
            var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Name == name, ct);
            if (organization is null) { organization = new Organization(name); db.Organizations.Add(organization); }
            organizations[name] = organization;
        }
        await db.SaveChangesAsync(ct);

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase)
        {
            ["مدیر سامانه"] = await EnsurePosition(db, "مدیر سامانه", null, ct)
        };
        positions["مدیر منابع انسانی"] = await EnsurePosition(db, "مدیر منابع انسانی", positions["مدیر سامانه"].Id, ct);
        positions["مدیر واحد"] = await EnsurePosition(db, "مدیر واحد", positions["مدیر سامانه"].Id, ct);
        positions["کارشناس مالی"] = await EnsurePosition(db, "کارشناس مالی", positions["مدیر واحد"].Id, ct);
        positions["کارمند"] = await EnsurePosition(db, "کارمند", positions["مدیر واحد"].Id, ct);

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
            ["Positions.View"] = new Permission("Positions.View", "مشاهده سمت‌ها"),
            ["Permissions.View"] = new Permission("Permissions.View", "مشاهده مجوزها"),
            ["Admin.Users"] = new Permission("Admin.Users", "مدیریت کاربران"),
            ["Admin.Positions"] = new Permission("Admin.Positions", "مدیریت سمت‌ها"),
            ["Admin.Permissions"] = new Permission("Admin.Permissions", "مدیریت مجوزها"),
            ["Admin.Organizations"] = new Permission("Admin.Organizations", "مدیریت سازمان‌ها"),
            ["Admin.Access"] = new Permission("Admin.Access", "مدیریت دسترسی‌ها"),
            ["Admin.AssignmentHistory"] = new Permission("Admin.AssignmentHistory", "مشاهده سوابق تخصیص سمت‌ها"),
            ["Admin.AuditLog"] = new Permission("Admin.AuditLog", "مشاهده گزارش رویدادها")
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

        foreach (var permission in permissions.Values)
            await EnsureDirectPermission(db, employees["admin"], permission, ct);

        foreach (var username in new[] { "employee", "hr", "manager", "finance" })
        {
            if (!employees[username].IsActive)
            {
                employees[username].Activate();
                await db.SaveChangesAsync(ct);
            }
        }

        await EnsureEmployeePosition(db, employees["employee"], positions["کارمند"], ct);
        await EnsureEmployeePosition(db, employees["hr"], positions["مدیر منابع انسانی"], ct);
        await EnsureEmployeePosition(db, employees["manager"], positions["مدیر واحد"], ct);
        await EnsureEmployeePosition(db, employees["finance"], positions["کارشناس مالی"], ct);

        await Grant(db, employees["employee"], positions["کارمند"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.Create", "Requests.View" }, ct);
        await Grant(db, employees["hr"], positions["مدیر منابع انسانی"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Employees.View" }, ct);
        await Grant(db, employees["manager"], positions["مدیر واحد"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject", "Requests.ReturnToRequester", "Requests.ReturnToPreviousStep", "Employees.View" }, ct);
        await Grant(db, employees["finance"], positions["کارشناس مالی"], permissions, new[] { "Dashboard.View", "Profile.View", "Requests.View", "Requests.Approve", "Requests.Reject" }, ct);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Position> EnsurePosition(ApplicationDbContext db, string name, Guid? parentPositionId, CancellationToken ct)
    {
        var position = await db.Positions.SingleOrDefaultAsync(x => x.Name == name, ct);
        if (position is null) { position = new Position(name, parentPositionId); db.Positions.Add(position); await db.SaveChangesAsync(ct); return position; }
        if (position.ParentPositionId != parentPositionId) { position.ChangeParent(parentPositionId); await db.SaveChangesAsync(ct); }
        return position;
    }

    private static async Task EnsureEmployeePosition(ApplicationDbContext db, Employee employee, Position position, CancellationToken ct)
    {
        var existingForEmployee = await db.EmployeePositions.SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.PositionId == position.Id, ct);
        if (existingForEmployee is not null && existingForEmployee.IsActive) return;
        var conflictingAssignments = await db.EmployeePositions.Where(x => x.PositionId == position.Id && x.EmployeeId != employee.Id && x.EndedAt == null).ToListAsync(ct);
        foreach (var assignment in conflictingAssignments) assignment.End();
        if (conflictingAssignments.Count > 0) await db.SaveChangesAsync(ct);
        if (existingForEmployee is not null) existingForEmployee.Reactivate(); else db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureDirectPermission(ApplicationDbContext db, Employee employee, Permission permission, CancellationToken ct)
    {
        if (!await db.EmployeePermissions.AnyAsync(x => x.EmployeeId == employee.Id && x.PermissionId == permission.Id, ct)) db.EmployeePermissions.Add(new EmployeePermission(employee.Id, permission.Id));
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

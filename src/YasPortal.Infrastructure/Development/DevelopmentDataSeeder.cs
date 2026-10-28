using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Domain.Workflows;
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
            // COL_LENGTH is NULL when either the table or the column is missing.
            var workflowRevisionColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowRequests]', N'Revision') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var requesterActionsTableExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[WorkflowRequesterActions]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var stepDecisionsTableExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[WorkflowStepDecisions]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var forceCloseColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowRequests]', N'ForceCloseReason') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var fieldChangesTableExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[WorkflowFieldChanges]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var reopenReasonColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowRequesterActions]', N'Reason') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var returnTargetColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowStepDecisions]', N'ReturnTargetOrder') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var delegationsTableExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[PositionDelegations]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var allowDelegationColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowStepDefinitions]', N'AllowDelegation') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var onBehalfColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowStepDecisions]', N'DelegationId') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var delegatePositionColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[PositionDelegations]', N'DelegatePositionId') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var positionLifecycleColumnsExist = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[Positions]', N'IsActive') IS NULL OR COL_LENGTH(N'[Positions]', N'OrganizationId') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var notificationsTableExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[Notifications]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var parallelStepColumnsExist = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowStepDefinitions]', N'ApprovalMode') IS NULL OR COL_LENGTH(N'[WorkflowRequestSteps]', N'RejectPolicy') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var uiPreferencesTableExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN OBJECT_ID(N'[UiPreferences]', N'U') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            var treeLevelColumnExists = organizationsTableExists == 1 && await db.Database.SqlQueryRaw<int>("SELECT CASE WHEN COL_LENGTH(N'[WorkflowStepDefinitions]', N'TreeLevel') IS NULL THEN 0 ELSE 1 END AS [Value]").SingleAsync(ct) == 1;
            if (organizationsTableExists == 0 || !uiPreferencesTableExists || !parallelStepColumnsExist || !positionLifecycleColumnsExist || !delegatePositionColumnExists || !lastActivePositionColumnExists || !parentPositionColumnExists || !workflowRevisionColumnExists || !requesterActionsTableExists || !stepDecisionsTableExists || !forceCloseColumnExists || !fieldChangesTableExists || !treeLevelColumnExists || !notificationsTableExists || !reopenReasonColumnExists || !returnTargetColumnExists || !delegationsTableExists || !allowDelegationColumnExists || !onBehalfColumnExists)
                await db.Database.EnsureDeletedAsync(ct);
        }

        try
        {
            await db.Database.EnsureCreatedAsync(ct);
        }
        catch (SqlException ex) when (ex.Number == 1801) { }

        await db.Database.ExecuteSqlRawAsync("""
            IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.EmployeePositions') AND name = N'IX_EmployeePositions_PositionId')
                DROP INDEX [IX_EmployeePositions_PositionId] ON [dbo].[EmployeePositions];
            CREATE UNIQUE INDEX [IX_EmployeePositions_PositionId]
                ON [dbo].[EmployeePositions] ([PositionId])
                WHERE [EndedAt] IS NULL;
            """, ct);

        var organizationDefinitions = new[]
        {
            (Code: "ORG-001", Name: "ستاد مرکزی"),
            (Code: "ORG-002", Name: "منابع انسانی"),
            (Code: "ORG-003", Name: "امور مالی")
        };

        var organizations = new Dictionary<string, Organization>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in organizationDefinitions)
        {
            var organization = await db.Organizations.SingleOrDefaultAsync(x => x.Name == definition.Name, ct);
            if (organization is null)
            {
                organization = new Organization(definition.Code, definition.Name);
                db.Organizations.Add(organization);
            }
            organizations[definition.Name] = organization;
        }
        await db.SaveChangesAsync(ct);

        var positions = new Dictionary<string, Position>(StringComparer.OrdinalIgnoreCase) {
            ["مدیر سامانه"] = await EnsurePosition(db, "POS-001", "مدیر سامانه", null, ct)
        };
        positions["مدیر منابع انسانی"] = await EnsurePosition(db, "POS-002", "مدیر منابع انسانی", positions["مدیر سامانه"].Id, ct);
        positions["مدیر واحد"] = await EnsurePosition(db, "POS-003", "مدیر واحد", positions["مدیر سامانه"].Id, ct);
        positions["کارشناس مالی"] = await EnsurePosition(db, "POS-004", "کارشناس مالی", positions["مدیر واحد"].Id, ct);
        positions["کارمند"] = await EnsurePosition(db, "POS-005", "کارمند", positions["مدیر واحد"].Id, ct);

        // Every permission comes from PermissionCatalog. A row still carrying an old code is renamed in place
        // (so every grant to persons, positions and groups is kept); missing rows are created; names are refreshed.
        var permissions = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PermissionCatalog.All)
        {
            var row = await db.Permissions.SingleOrDefaultAsync(x => x.Code == definition.Code, ct);
            if (row is null)
            {
                foreach (var oldCode in definition.OldCodes)
                {
                    row = await db.Permissions.SingleOrDefaultAsync(x => x.Code == oldCode, ct);
                    if (row is not null)
                    {
                        row.Rename(definition.Code, definition.Name);
                        await db.SaveChangesAsync(ct);
                        break;
                    }
                }
            }
            if (row is null)
            {
                row = new Permission(definition.Code, definition.Name);
                db.Permissions.Add(row);
                await db.SaveChangesAsync(ct);
            }
            else if (row.Name != definition.Name)
            {
                row.Rename(definition.Code, definition.Name);
                await db.SaveChangesAsync(ct);
            }
            permissions[definition.Code] = row;
        }

        // Leftover rows that still carry an old code (the new row already existed, so the rename was skipped):
        // move their grants onto the new permission, then delete them, so nothing is listed twice.
        foreach (var definition in PermissionCatalog.All)
        {
            var target = permissions[definition.Code];
            foreach (var oldCode in definition.OldCodes)
            {
                var old = await db.Permissions.SingleOrDefaultAsync(x => x.Code == oldCode, ct);
                if (old is null || old.Id == target.Id) continue;
                foreach (var g in await db.EmployeePermissions.Where(x => x.PermissionId == old.Id).ToListAsync(ct))
                    if (!await db.EmployeePermissions.AnyAsync(x => x.EmployeeId == g.EmployeeId && x.PermissionId == target.Id, ct))
                        db.EmployeePermissions.Add(new EmployeePermission(g.EmployeeId, target.Id));
                foreach (var g in await db.UserPositionPermissions.Where(x => x.PermissionId == old.Id).ToListAsync(ct))
                    if (!await db.UserPositionPermissions.AnyAsync(x => x.EmployeeId == g.EmployeeId && x.PositionId == g.PositionId && x.PermissionId == target.Id, ct))
                        db.UserPositionPermissions.Add(new UserPositionPermission(g.EmployeeId, g.PositionId, target.Id));
                foreach (var g in await db.PermissionGroupPermissions.Where(x => x.PermissionId == old.Id).ToListAsync(ct))
                    if (!await db.PermissionGroupPermissions.AnyAsync(x => x.GroupId == g.GroupId && x.PermissionId == target.Id, ct))
                        db.PermissionGroupPermissions.Add(new PermissionGroupPermission(g.GroupId, target.Id));
                await db.SaveChangesAsync(ct);
                db.Permissions.Remove(old);
                await db.SaveChangesAsync(ct);
            }
        }

        // Permissions that were once seeded but nothing ever checked; removed so they stop showing up in the access screens.
        // Deleting a permission also removes any grants of it (cascade).
        string[] retired = ["Employees.View", "Employees.Manage", "Organizations.View", "Positions.View", "Permissions.View"];
        var obsolete = await db.Permissions.Where(x => retired.Contains(x.Code)).ToListAsync(ct);
        if (obsolete.Count > 0)
            db.Permissions.RemoveRange(obsolete);
        await db.SaveChangesAsync(ct);

        var employeeDefinitions = new[]
        {
            (Code: "EMP-001", Username: "admin", FullName: "مدیر سامانه", Organization: "ستاد مرکزی", IsAdmin: true, Password: "Admin123!"),
            (Code: "EMP-002", Username: "employee", FullName: "کارمند نمونه", Organization: "ستاد مرکزی", IsAdmin: false, Password: "Employee123!"),
            (Code: "EMP-003", Username: "hr", FullName: "سارا احمدی", Organization: "منابع انسانی", IsAdmin: false, Password: "Hr123!"),
            (Code: "EMP-004", Username: "manager", FullName: "علی رضایی", Organization: "ستاد مرکزی", IsAdmin: false, Password: "Manager123!"),
            (Code: "EMP-005", Username: "finance", FullName: "رضا محمدی", Organization: "امور مالی", IsAdmin: false, Password: "Finance123!")
        };

        var employees = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in employeeDefinitions)
        {
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.Username == definition.Username, ct);
            if (employee is null)
            {
                employee = new Employee(definition.Code, definition.Username, definition.FullName, organizations[definition.Organization].Id, definition.IsAdmin);
                employee.SetPasswordHash(passwordHasher.HashPassword(employee, definition.Password));
                db.Employees.Add(employee);
            }
            else
            {
                employee.ChangeOrganization(organizations[definition.Organization].Id);
                employee.SetAdmin(definition.IsAdmin);
                if (string.IsNullOrWhiteSpace(employee.PasswordHash))
                    employee.SetPasswordHash(passwordHasher.HashPassword(employee, definition.Password));
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

        await Grant(db, employees["employee"], positions["کارمند"], permissions, new[] { Perm.DashboardPageView, Perm.ProfileOwnView, Perm.RequestsOwnCreate, Perm.RequestsOwnView, ReportPermissions.MyRequests }.Concat(FormPermissions.Codes).Append(Perm.DashboardMineView).ToArray(), ct);
        await Grant(db, employees["hr"], positions["مدیر منابع انسانی"], permissions, new[] { Perm.DashboardPageView, Perm.ProfileOwnView, Perm.RequestsOwnView, Perm.RequestsDecisionApprove, Perm.RequestsDecisionReject, Perm.RequestsDecisionReturnToRequester, Perm.RequestsDecisionReturnToSpecificStep, Perm.DelegationsOwnManage }.Concat(FormPermissions.Codes).Append(Perm.DashboardMineView).Append(Perm.NotificationsMessageSend).ToArray(), ct);
        await Grant(db, employees["manager"], positions["مدیر واحد"], permissions, new[] { Perm.DashboardPageView, Perm.ProfileOwnView, Perm.RequestsOwnView, Perm.RequestsDecisionApprove, Perm.RequestsDecisionReject, Perm.RequestsDecisionReturnToRequester, Perm.RequestsDecisionReturnToPreviousStep, Perm.RequestsDecisionReturnToSpecificStep, Perm.RequestsDecisionReopenOwn, Perm.DelegationsOwnManage, Perm.RequestsTeamView, Perm.RequestsTeamApprovalsView, ReportPermissions.TeamRequests, ReportPermissions.MyRequests }.Concat(FormPermissions.Codes).Append(Perm.DashboardMineView).Append(Perm.DashboardTeamView).Append(Perm.NotificationsMessageSend).Append(ReportPermissions.MyNotifications).ToArray(), ct);
        await Grant(db, employees["finance"], positions["کارشناس مالی"], permissions, new[] { Perm.DashboardPageView, Perm.ProfileOwnView, Perm.RequestsOwnView, Perm.RequestsDecisionApprove, Perm.RequestsDecisionReject }.Concat(FormPermissions.Codes).Append(Perm.DashboardMineView).ToArray(), ct);
        await db.SaveChangesAsync(ct);

        // Default approval paths — seeded once as a sensible starting point; an admin
        // with Admin.Workflows can add/reorder/remove steps afterward from /admin/workflows,
        // so this never re-seeds once a type already has at least one step defined.
        await EnsureWorkflowSteps(db, WorkflowTypeCode.Leave, ct,
            (Order: 1, Name: "تایید مدیر مستقیم", ManagerLevel: (int?)1, ApproverPositionId: (Guid?)null, FillsPart: (int?)null),
            (Order: 2, Name: "تایید نهایی منابع انسانی", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["مدیر منابع انسانی"].Id, FillsPart: (int?)null));
        await EnsureWorkflowSteps(db, WorkflowTypeCode.Purchase, ct,
            (Order: 1, Name: "تایید مدیر مستقیم", ManagerLevel: (int?)1, ApproverPositionId: (Guid?)null, FillsPart: (int?)null),
            (Order: 2, Name: "تایید مالی", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["کارشناس مالی"].Id, FillsPart: (int?)null));
        await EnsureWorkflowSteps(db, WorkflowTypeCode.Access, ct,
            (Order: 1, Name: "تایید مدیر مستقیم", ManagerLevel: (int?)1, ApproverPositionId: (Guid?)null, FillsPart: (int?)null),
            (Order: 2, Name: "تایید مدیر سامانه", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["مدیر سامانه"].Id, FillsPart: (int?)null));
        await EnsureWorkflowSteps(db, WorkflowTypeCode.Loan, ct,
            (Order: 1, Name: "تایید مدیر مستقیم", ManagerLevel: (int?)1, ApproverPositionId: (Guid?)null, FillsPart: (int?)null),
            (Order: 2, Name: "تایید مالی", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["کارشناس مالی"].Id, FillsPart: (int?)null),
            (Order: 3, Name: "تایید نهایی منابع انسانی", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["مدیر منابع انسانی"].Id, FillsPart: (int?)null));
        await EnsureWorkflowSteps(db, WorkflowTypeCode.Helpdesk, ct,
            (Order: 1, Name: "ارجاع به مدیر سامانه", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["مدیر سامانه"].Id, FillsPart: (int?)null));
        await EnsureWorkflowSteps(db, WorkflowTypeCode.WorkReport, ct,
            (Order: 1, Name: "مشاهده مدیر مستقیم", ManagerLevel: (int?)1, ApproverPositionId: (Guid?)null, FillsPart: (int?)null));
        // Vehicle request is multi-part: step 1 fills part 2 (تخصیص خودرو), step 2 fills part 3 (ثبت حراست). An admin can change this in /admin/workflows.
        await EnsureWorkflowSteps(db, WorkflowTypeCode.VehicleRequest, ct,
            (Order: 1, Name: "تخصیص خودرو (مدیر واحد)", ManagerLevel: (int?)1, ApproverPositionId: (Guid?)null, FillsPart: (int?)2),
            (Order: 2, Name: "ثبت حراست (مدیر منابع انسانی)", ManagerLevel: (int?)null, ApproverPositionId: (Guid?)positions["مدیر منابع انسانی"].Id, FillsPart: (int?)3));
        await db.SaveChangesAsync(ct);
    }

    private static async Task<Position> EnsurePosition(ApplicationDbContext db, string code, string name, Guid? parentPositionId, CancellationToken ct)
    {
        var position = await db.Positions.SingleOrDefaultAsync(x => x.Name == name, ct);
        if (position is null)
        {
            position = new Position(code, name, parentPositionId);
            db.Positions.Add(position);
            await db.SaveChangesAsync(ct);
            return position;
        }
        if (position.ParentPositionId != parentPositionId)
        {
            position.ChangeParent(parentPositionId);
            await db.SaveChangesAsync(ct);
        }
        return position;
    }

    private static async Task EnsureEmployeePosition(ApplicationDbContext db, Employee employee, Position position, CancellationToken ct)
    {
        var existingForEmployee = await db.EmployeePositions.SingleOrDefaultAsync(x => x.EmployeeId == employee.Id && x.PositionId == position.Id, ct);
        if (existingForEmployee is not null && existingForEmployee.IsActive)
            return;
        var conflictingAssignments = await db.EmployeePositions.Where(x => x.PositionId == position.Id && x.EmployeeId != employee.Id && x.EndedAt == null).ToListAsync(ct);
        foreach (var assignment in conflictingAssignments)
            assignment.End();
        if (conflictingAssignments.Count > 0)
            await db.SaveChangesAsync(ct);
        if (existingForEmployee is not null)
            existingForEmployee.Reactivate();
        else
            db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureDirectPermission(ApplicationDbContext db, Employee employee, Permission permission, CancellationToken ct)
    {
        if (!await db.EmployeePermissions.AnyAsync(x => x.EmployeeId == employee.Id && x.PermissionId == permission.Id, ct))
            db.EmployeePermissions.Add(new EmployeePermission(employee.Id, permission.Id));
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

    /// <summary>
    /// Seeds a workflow type's approval steps only if it has none yet, so an admin's own
    /// edits from /admin/workflows are never overwritten by a later app restart.
    /// </summary>
    private static async Task EnsureWorkflowSteps(
        ApplicationDbContext db,
        WorkflowTypeCode workflowType,
        CancellationToken ct,
        params (int Order, string Name, int? ManagerLevel, Guid? ApproverPositionId, int? FillsPart)[] steps)
    {
        if (await db.WorkflowStepDefinitions.AnyAsync(x => x.WorkflowType == workflowType, ct))
            return;
        foreach (var step in steps)
        {
            var definition = step.ApproverPositionId is Guid positionId
                ? new WorkflowStepDefinition(workflowType, step.Order, step.Name, positionId)
                : new WorkflowStepDefinition(workflowType, step.Order, step.Name, step.ManagerLevel!.Value);
            definition.SetFillsPart(step.FillsPart);
            db.WorkflowStepDefinitions.Add(definition);
        }
        await db.SaveChangesAsync(ct);
    }
}

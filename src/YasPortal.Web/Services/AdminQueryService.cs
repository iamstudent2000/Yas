using Microsoft.EntityFrameworkCore;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>Server-side lookup, paging, and administration queries.</summary>
public sealed class AdminQueryService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public sealed record LookupItem(Guid Id, string Text);
    public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
    {
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    }
    public sealed record EmployeeSummary(Guid Id, string Username, string FullName, bool IsAdmin, Guid? LastActivePositionId);
    public sealed record PermissionUsageDetails(Guid PermissionId, string PermissionName, string PermissionCode, IReadOnlyList<DirectPermissionUsage> DirectAssignments, IReadOnlyList<GroupPermissionUsage> Groups);
    public sealed record DirectPermissionUsage(Guid EmployeeId, string EmployeeName, string Username, Guid? PositionId, string? PositionName, string Source);
    public sealed record GroupAssignmentUsage(Guid EmployeeId, string EmployeeName, string Username, string? PositionName, string Source);
    public sealed record GroupPermissionUsage(Guid GroupId, string GroupName, string? Description, int PermissionCount, string AssignedEmployeeCount, string Source, IReadOnlyList<GroupAssignmentUsage> Assignments);

    public async Task<EmployeeSummary?> GetEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => x.Id == id).Select(x => new EmployeeSummary(x.Id, x.Username, x.FullName, x.IsAdmin, x.LastActivePositionId)).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchEmployeesAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => q == "" || x.FullName.Contains(q) || x.Username.Contains(q)).OrderBy(x => x.FullName).Take(30).Select(x => new LookupItem(x.Id, x.FullName + " — " + x.Username + (x.IsAdmin ? " (مدیر سامانه)" : " (کارمند)"))).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchAssignableEmployeesAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => x.IsActive && !x.IsAdmin && (q == "" || x.FullName.Contains(q) || x.Username.Contains(q))).OrderBy(x => x.FullName).Take(30).Select(x => new LookupItem(x.Id, x.FullName + " — " + x.Username)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchOrganizationsAsync(string? search, bool activeOnly = true, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().Where(x => (!activeOnly || x.IsActive) && (q == "" || x.Name.Contains(q))).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchPositionsAsync(string? search, Guid? employeeId = null, Guid? excludeId = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Positions.AsNoTracking();
        if (employeeId is Guid eid)
            query = query.Where(x => db.EmployeePositions.Any(ep => ep.EmployeeId == eid && ep.PositionId == x.Id && ep.EndedAt == null));
        if (excludeId is Guid xid)
            query = query.Where(x => x.Id != xid);
        return await query.Where(x => q == "" || x.Name.Contains(q)).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchAvailablePositionsAsync(string? search, Guid? excludePositionId = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Positions.AsNoTracking().Where(p => !db.EmployeePositions.Any(ep => ep.PositionId == p.Id && ep.EndedAt == null));
        if (excludePositionId is Guid exclude)
            query = query.Where(p => p.Id != exclude);
        return await query.Where(p => q == "" || p.Name.Contains(q)).OrderBy(p => p.Name).Take(30).Select(p => new LookupItem(p.Id, p.Name + " — آزاد")).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchPermissionsAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Permissions.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q)).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    }
    public async Task<IReadOnlyList<LookupItem>> SearchPermissionGroupsAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PermissionGroups.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || (x.Description != null && x.Description.Contains(q))).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    }

    public async Task<PermissionUsageDetails?> GetPermissionUsageAsync(Guid permissionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var permission = await db.Permissions.AsNoTracking().Where(x => x.Id == permissionId).Select(x => new { x.Id, x.Name, x.Code }).SingleOrDefaultAsync(ct);
        if (permission is null)
            return null;
        var positionAssignments = await db.UserPositionPermissions.AsNoTracking().Where(x => x.PermissionId == permissionId).OrderBy(x => x.Employee.FullName).ThenBy(x => x.Position.Name).Select(x => new { x.EmployeeId, x.Employee.FullName, x.Employee.Username, x.PositionId, x.Position.Name }).ToListAsync(ct);
        var employeeAssignments = await db.EmployeePermissions.AsNoTracking().Where(x => x.PermissionId == permissionId).OrderBy(x => x.Employee.FullName).Select(x => new { x.EmployeeId, x.Employee.FullName, x.Employee.Username }).ToListAsync(ct);
        var direct = positionAssignments.GroupBy(x => new { x.EmployeeId, x.FullName, x.Username }).Select(g => new DirectPermissionUsage(g.Key.EmployeeId, g.Key.FullName, g.Key.Username, g.Count() == 1 ? g.First().PositionId : null, string.Join("، ", g.Select(x => x.Name).Distinct()), "سمت‌ها")).ToList();
        foreach (var employee in employeeAssignments)
        {
            var existing = direct.FirstOrDefault(x => x.EmployeeId == employee.EmployeeId);
            if (existing is null)
                direct.Add(new DirectPermissionUsage(employee.EmployeeId, employee.FullName, employee.Username, null, null, "مستقیم به کارمند"));
            else
            {
                var index = direct.IndexOf(existing);
                direct[index] = existing with {
                    Source = "سمت‌ها + مستقیم به کارمند"
                };
            }
        }
        direct = direct.OrderBy(x => x.EmployeeName).ToList();
        var groupRows = await db.PermissionGroupPermissions.AsNoTracking().Where(x => x.PermissionId == permissionId).OrderBy(x => x.Group.Name).Select(x => new { x.GroupId, x.Group.Name, x.Group.Description, PermissionCount = db.PermissionGroupPermissions.Count(g => g.GroupId == x.GroupId) }).ToListAsync(ct);
        var groupIds = groupRows.Select(x => x.GroupId).ToArray();
        var positionGroupAssignments = await db.UserPositionPermissionGroups.AsNoTracking().Where(x => groupIds.Contains(x.GroupId)).OrderBy(x => x.GroupId).ThenBy(x => x.Employee.FullName).ThenBy(x => x.Position.Name).Select(x => new { x.GroupId, x.EmployeeId, x.Employee.FullName, x.Employee.Username, PositionName = x.Position.Name }).ToListAsync(ct);
        var employeeGroupAssignments = await db.EmployeePermissionGroups.AsNoTracking().Where(x => groupIds.Contains(x.GroupId)).OrderBy(x => x.GroupId).ThenBy(x => x.Employee.FullName).Select(x => new { x.GroupId, x.EmployeeId, x.Employee.FullName, x.Employee.Username }).ToListAsync(ct);
        var groups = groupRows.Select(g => { var assignments = positionGroupAssignments.Where(x => x.GroupId == g.GroupId).Select(x => new GroupAssignmentUsage(x.EmployeeId, x.FullName, x.Username, x.PositionName, "سمت")).Concat(employeeGroupAssignments.Where(x => x.GroupId == g.GroupId).Select(x => new GroupAssignmentUsage(x.EmployeeId, x.FullName, x.Username, null, "مستقیم به کارمند"))).GroupBy(x => new { x.EmployeeId, x.PositionName, x.Source }).Select(x => x.First()).OrderBy(x => x.EmployeeName).ThenBy(x => x.PositionName).ToList(); var assigneeText = assignments.Count == 0 ? "بدون تخصیص" : $"{assignments.Count} تخصیص: " + string.Join("، ", assignments.Select(x => x.PositionName is null ? $"{x.EmployeeName} (مستقیم)" : $"{x.EmployeeName} — {x.PositionName}")); return new GroupPermissionUsage(g.GroupId, g.Name, g.Description, g.PermissionCount, assigneeText, "گروه مجوز", assignments); }).ToList();
        return new PermissionUsageDetails(permission.Id, permission.Name, permission.Code, direct, groups);
    }
    public async Task<IReadOnlyList<PermissionPageRow>> GetAllPermissionsAsync(string? search = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Permissions.AsNoTracking().Where(x => q == "" || x.Name.Contains(q)).OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, DirectAssignmentCount = db.UserPositionPermissions.Count(up => up.PermissionId == x.Id) + db.EmployeePermissions.Count(ep => ep.PermissionId == x.Id), GroupMembershipCount = db.PermissionGroupPermissions.Count(gp => gp.PermissionId == x.Id) }).ToListAsync(ct);
        return rows.Select(x => new PermissionPageRow(x.Id, x.Code, x.Name, x.DirectAssignmentCount, x.GroupMembershipCount)).ToList();
    }
    public async Task<IReadOnlyList<PermissionGroupPageRow>> GetAllPermissionGroupsAsync(string? search = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.PermissionGroups.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || (x.Description != null && x.Description.Contains(q))).OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new { x.Id, x.Name, x.Description, PermissionCount = db.PermissionGroupPermissions.Count(g => g.GroupId == x.Id), AssignmentCount = db.UserPositionPermissionGroups.Count(a => a.GroupId == x.Id) + db.EmployeePermissionGroups.Count(a => a.GroupId == x.Id) }).ToListAsync(ct);
        return rows.Select(x => new PermissionGroupPageRow(x.Id, x.Name, x.Description, x.PermissionCount, x.AssignmentCount)).ToList();
    }

    public async Task<PageResult<EmployeePageRow>> GetEmployeesAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Employees.AsNoTracking().Where(x => q == "" || x.FullName.Contains(q) || x.Username.Contains(q));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.FullName).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new EmployeePageRow(x.Id, x.Username, x.FullName, db.Organizations.Where(o => o.Id == x.OrganizationId).Select(o => (string?)o.Name).FirstOrDefault(), x.IsActive, x.IsAdmin)).ToListAsync(ct);
        var employeeIds = rows.Select(x => x.Id).ToArray();
        var assignments = await db.EmployeePositions.AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) && x.EndedAt == null).OrderBy(x => x.Position.Name).Select(x => new { x.EmployeeId, x.PositionId, x.Position.Name }).ToListAsync(ct);
        var positionsByEmployee = assignments.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Select(x => new ActivePositionRow(x.PositionId, x.Name)).ToList() as IReadOnlyList<ActivePositionRow>);
        var items = rows.Select(x => x with { ActivePositions = positionsByEmployee.TryGetValue(x.Id, out var positions) ? positions : Array.Empty<ActivePositionRow>() }).ToList();
        return new(items, total, page, pageSize);
    }
    public async Task<PageResult<OrganizationPageRow>> GetOrganizationsAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Organizations.AsNoTracking().Where(x => q == "" || x.Name.Contains(q));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).Select(x => new OrganizationPageRow(x.Id, x.Name, x.IsActive, x.Employees.Count)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }
    public async Task<PageResult<PermissionPageRow>> GetPermissionsAsync(int page, int pageSize, string? search, string? usage, CancellationToken ct = default)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Permissions.AsNoTracking().Select(x => new { x.Id, x.Code, x.Name, DirectAssignmentCount = db.UserPositionPermissions.Count(up => up.PermissionId == x.Id) + db.EmployeePermissions.Count(ep => ep.PermissionId == x.Id), GroupMembershipCount = db.PermissionGroupPermissions.Count(gp => gp.PermissionId == x.Id) });
        if (q != "")
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q));
        if (usage == "used")
            query = query.Where(x => x.DirectAssignmentCount + x.GroupMembershipCount > 0);
        else if (usage == "unused")
            query = query.Where(x => x.DirectAssignmentCount + x.GroupMembershipCount == 0);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new PermissionPageRow(x.Id, x.Code, x.Name, x.DirectAssignmentCount, x.GroupMembershipCount)).ToList();
        return new(items, total, page, pageSize);
    }
    public async Task<PageResult<PermissionGroupPageRow>> GetPermissionGroupsAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.PermissionGroups.AsNoTracking().Select(x => new { x.Id, x.Name, x.Description, PermissionCount = db.PermissionGroupPermissions.Count(g => g.GroupId == x.Id), AssignmentCount = db.UserPositionPermissionGroups.Count(a => a.GroupId == x.Id) + db.EmployeePermissionGroups.Count(a => a.GroupId == x.Id) });
        if (q != "")
            query = query.Where(x => x.Name.Contains(q) || (x.Description != null && x.Description.Contains(q)));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new PermissionGroupPageRow(x.Id, x.Name, x.Description, x.PermissionCount, x.AssignmentCount)).ToList();
        return new(items, total, page, pageSize);
    }
    private static int Page(int page) => Math.Max(1, page); private static int Size(int size) => Math.Clamp(size, 5, 100); private static string Normalize(string? value) => (value ?? "").Trim();
    public sealed record ActivePositionRow(Guid Id, string Name);
    public sealed record EmployeePageRow(Guid Id, string Username, string FullName, string? OrganizationName, bool IsActive, bool IsAdmin, IReadOnlyList<ActivePositionRow>? ActivePositions = null);
    public sealed record OrganizationPageRow(Guid Id, string Name, bool IsActive, int EmployeeCount);
    public sealed record PermissionPageRow(Guid Id, string Code, string Name, int DirectAssignmentCount, int GroupMembershipCount);
    public sealed record PermissionGroupPageRow(Guid Id, string Name, string? Description, int PermissionCount, int AssignmentCount);
}
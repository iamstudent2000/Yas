using Microsoft.EntityFrameworkCore;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

// Read-only database questions that feed the admin screens: pick-lists for the search-as-you-type selectors,
// paged tables of employees, organizations and permissions, and "where is this permission used" reports.
// Service = the class that holds the logic for one area. Each method opens its own short-lived database connection.
/// <summary>Server-side lookup, paging, and administration queries.</summary>
public sealed class AdminQueryService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    // Small result shapes (records) handed back to the pages: a lookup entry is just an id plus the text shown in a list.
    public sealed record LookupItem(Guid Id, string Text);
    // One page of table rows plus the totals the pager needs. There is always at least one page, even for no rows.
    public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
    {
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    }
    // The remaining records below describe rows of the permission-usage report: who has a permission directly and via groups.
    public sealed record EmployeeSummary(Guid Id, string Username, string FullName, bool IsAdmin, Guid? LastActivePositionId);
    public sealed record PermissionUsageDetails(Guid PermissionId, string PermissionName, string PermissionCode, IReadOnlyList<DirectPermissionUsage> DirectAssignments, IReadOnlyList<GroupPermissionUsage> Groups);
    public sealed record DirectPermissionUsage(Guid EmployeeId, string EmployeeName, string Username, Guid? PositionId, string? PositionName, string Source);
    public sealed record GroupAssignmentUsage(Guid EmployeeId, string EmployeeName, string Username, string? PositionName, string Source);
    public sealed record GroupPermissionUsage(Guid GroupId, string GroupName, string? Description, int PermissionCount, int AssignedEmployeeCount, string Source, IReadOnlyList<GroupAssignmentUsage> Assignments);

    // Loads one employee by id, or nothing if that person does not exist.
    public async Task<EmployeeSummary?> GetEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => x.Id == id).Select(x => new EmployeeSummary(x.Id, x.Username, x.FullName, x.IsAdmin, x.LastActivePositionId)).SingleOrDefaultAsync(ct);
    }
    // Lookup list of employees (admins included) matching a typed name, username or code; at most 30 results.
    public async Task<IReadOnlyList<LookupItem>> SearchEmployeesAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => q == "" || x.FullName.Contains(q) || x.Username.Contains(q) || x.Code.Contains(q)).OrderBy(x => x.FullName).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.FullName + " — " + x.Username + (x.IsAdmin ? " (مدیر سامانه)" : " (کارمند)"))).ToListAsync(ct);
    }
    /// <summary>Active employees (administrators included), for choosing who to send a notification to.</summary>
    public async Task<IReadOnlyList<LookupItem>> SearchActiveEmployeesAsync(string? search, Guid? excludeId, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => x.IsActive && (excludeId == null || x.Id != excludeId) && (q == "" || x.FullName.Contains(q) || x.Username.Contains(q) || x.Code.Contains(q)))
            .OrderBy(x => x.FullName).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.FullName + " — " + x.Username + (x.IsAdmin ? " (مدیر سامانه)" : " (کارمند)"))).ToListAsync(ct);
    }
    // Same people list but paged, for tick-box pickers; can narrow by organization and leave one person out.
    /// <summary>Active employees for a tick-box list, one page at a time: optional text and organization filters.</summary>
    public async Task<(IReadOnlyList<LookupItem> Items, int Total)> BrowseActiveEmployeesAsync(string? search, Guid? organizationId, Guid? excludeId, int skip, int take, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Employees.AsNoTracking().Where(x => x.IsActive && (excludeId == null || x.Id != excludeId) && (organizationId == null || x.OrganizationId == organizationId)
            && (q == "" || x.FullName.Contains(q) || x.Username.Contains(q) || x.Code.Contains(q)));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500))
            .Select(x => new LookupItem(x.Id, x.Code + " — " + x.FullName + " — " + x.Username + (x.IsAdmin ? " (مدیر سامانه)" : " (کارمند)"))).ToListAsync(ct);
        return (items, total);
    }
    // Lookup list of active, non-admin employees only: the people who can be given a position.
    public async Task<IReadOnlyList<LookupItem>> SearchAssignableEmployeesAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking().Where(x => x.IsActive && !x.IsAdmin && (q == "" || x.FullName.Contains(q) || x.Username.Contains(q) || x.Code.Contains(q))).OrderBy(x => x.FullName).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.FullName + " — " + x.Username)).ToListAsync(ct);
    }
    // Lookup list of organizations (departments); by default only active ones are offered.
    public async Task<IReadOnlyList<LookupItem>> SearchOrganizationsAsync(string? search, bool activeOnly = true, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking().Where(x => (!activeOnly || x.IsActive) && (q == "" || x.Name.Contains(q) || x.Code.Contains(q))).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.Name)).ToListAsync(ct);
    }
    /// <summary>Distinct names of workflow steps, for the "current step" filter list.</summary>
    public async Task<IReadOnlyList<string>> GetWorkflowStepNamesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.WorkflowStepDefinitions.AsNoTracking().Select(x => x.Name).Distinct().OrderBy(x => x).ToListAsync(ct);
    }

    /// <summary>Active, non-admin employees who hold at least one active position — the people a request can be submitted for.</summary>
    public async Task<IReadOnlyList<LookupItem>> SearchRequestersAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking()
            .Where(x => x.IsActive && !x.IsAdmin && db.EmployeePositions.Any(ep => ep.EmployeeId == x.Id && ep.EndedAt == null)
                        && (q == "" || x.FullName.Contains(q) || x.Username.Contains(q) || x.Code.Contains(q)))
            .OrderBy(x => x.FullName).Take(30)
            .Select(x => new LookupItem(x.Id, x.Code + " — " + x.FullName + " — " + x.Username)).ToListAsync(ct);
    }

    /// <summary>The position to pre-select for an employee: their last chosen active position, else their first active one.</summary>
    // If the person last worked under a position that they still hold, that one is chosen; otherwise the first by name.
    public async Task<LookupItem?> GetDefaultPositionAsync(Guid employeeId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var last = await db.Employees.AsNoTracking().Where(x => x.Id == employeeId).Select(x => x.LastActivePositionId).SingleOrDefaultAsync(ct);
        var rows = await db.EmployeePositions.AsNoTracking()
            .Where(x => x.EmployeeId == employeeId && x.EndedAt == null)
            .Select(x => new { x.PositionId, x.Position.Code, x.Position.Name }).ToListAsync(ct);
        var pick = rows.FirstOrDefault(x => x.PositionId == last) ?? rows.OrderBy(x => x.Name).FirstOrDefault();
        return pick is null ? null : new LookupItem(pick.PositionId, pick.Code + " — " + pick.Name);
    }

    // Lookup list of positions. Can be limited to positions one employee currently holds, can hide one position,
    // and can offer only active positions. A position is "held" while its assignment row has no end date.
    public async Task<IReadOnlyList<LookupItem>> SearchPositionsAsync(string? search, Guid? employeeId = null, Guid? excludeId = null, CancellationToken ct = default, bool activeOnly = false)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Positions.AsNoTracking();
        if (employeeId is Guid eid)
            query = query.Where(x => db.EmployeePositions.Any(ep => ep.EmployeeId == eid && ep.PositionId == x.Id && ep.EndedAt == null));
        if (excludeId is Guid xid)
            query = query.Where(x => x.Id != xid);
        if (activeOnly)
            query = query.Where(x => x.IsActive);
        return await query.Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q)).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.Name)).ToListAsync(ct);
    }
    /// <summary>Positions labelled "code — position name — current holder" (or "خالی" when vacant); searches all three.</summary>
    public async Task<IReadOnlyList<LookupItem>> SearchPositionsWithHoldersAsync(string? search, Guid? excludeId = null, CancellationToken ct = default, bool activeOnly = false)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = db.Positions.AsNoTracking()
            .Where(x => excludeId == null || x.Id != excludeId)
            .Where(x => !activeOnly || x.IsActive)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                Holder = db.EmployeePositions.Where(ep => ep.PositionId == x.Id && ep.EndedAt == null)
                    .Join(db.Employees, ep => ep.EmployeeId, e => e.Id, (ep, e) => e.FullName).FirstOrDefault(),
            })
            .Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q) || (x.Holder != null && x.Holder.Contains(q)));
        var list = await rows.OrderBy(x => x.Name).Take(30).ToListAsync(ct);
        return list.Select(x => new LookupItem(x.Id, $"{x.Code} — {x.Name} — {(x.Holder ?? "خالی")}")).ToList();
    }
    // Lookup list of positions that are active and currently have nobody holding them (only one occupant per position).
    public async Task<IReadOnlyList<LookupItem>> SearchAvailablePositionsAsync(string? search, Guid? excludePositionId = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Positions.AsNoTracking().Where(p => p.IsActive && !db.EmployeePositions.Any(ep => ep.PositionId == p.Id && ep.EndedAt == null));
        if (excludePositionId is Guid exclude)
            query = query.Where(p => p.Id != exclude);
        return await query.Where(p => q == "" || p.Name.Contains(q) || p.Code.Contains(q)).OrderBy(p => p.Name).Take(30).Select(p => new LookupItem(p.Id, p.Code + " — " + p.Name + " — آزاد")).ToListAsync(ct);
    }
    // Lookup list of permissions (the individual things a person is allowed to do), by name or code.
    public async Task<IReadOnlyList<LookupItem>> SearchPermissionsAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Permissions.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q)).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.Name)).ToListAsync(ct);
    }
    // Lookup list of permission groups (named bundles of permissions), by name, code or description.
    public async Task<IReadOnlyList<LookupItem>> SearchPermissionGroupsAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PermissionGroups.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q) || (x.Description != null && x.Description.Contains(q))).OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Code + " — " + x.Name)).ToListAsync(ct);
    }

    // Builds the "where is this permission used" report: who holds it directly (through a position or straight on the
    // employee) and which groups contain it, with the people each group has been given to.
    public async Task<PermissionUsageDetails?> GetPermissionUsageAsync(Guid permissionId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var permission = await db.Permissions.AsNoTracking().Where(x => x.Id == permissionId).Select(x => new { x.Id, x.Name, x.Code }).SingleOrDefaultAsync(ct);
        if (permission is null)
            return null;
        var positionAssignments = await db.UserPositionPermissions.AsNoTracking().Where(x => x.PermissionId == permissionId).OrderBy(x => x.Employee.FullName).ThenBy(x => x.Position.Name).Select(x => new { x.EmployeeId, x.Employee.FullName, x.Employee.Username, x.PositionId, x.Position.Name }).ToListAsync(ct);
        var employeeAssignments = await db.EmployeePermissions.AsNoTracking().Where(x => x.PermissionId == permissionId).OrderBy(x => x.Employee.FullName).Select(x => new { x.EmployeeId, x.Employee.FullName, x.Employee.Username }).ToListAsync(ct);
        // Merge the position-based and employee-based direct grants so each person appears once, with the source labelled.
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
        // Next the groups that contain this permission, their sizes, and who was given each group.
        var groupRows = await db.PermissionGroupPermissions.AsNoTracking().Where(x => x.PermissionId == permissionId).OrderBy(x => x.Group.Name).Select(x => new { x.GroupId, x.Group.Name, x.Group.Description, PermissionCount = db.PermissionGroupPermissions.Count(g => g.GroupId == x.GroupId) }).ToListAsync(ct);
        var groupIds = groupRows.Select(x => x.GroupId).ToArray();
        var positionGroupAssignments = await db.UserPositionPermissionGroups.AsNoTracking().Where(x => groupIds.Contains(x.GroupId)).OrderBy(x => x.GroupId).ThenBy(x => x.Employee.FullName).ThenBy(x => x.Position.Name).Select(x => new { x.GroupId, x.EmployeeId, x.Employee.FullName, x.Employee.Username, PositionName = x.Position.Name }).ToListAsync(ct);
        var employeeGroupAssignments = await db.EmployeePermissionGroups.AsNoTracking().Where(x => groupIds.Contains(x.GroupId)).OrderBy(x => x.GroupId).ThenBy(x => x.Employee.FullName).Select(x => new { x.GroupId, x.EmployeeId, x.Employee.FullName, x.Employee.Username }).ToListAsync(ct);
        var groups = groupRows.Select(g => { var assignments = positionGroupAssignments.Where(x => x.GroupId == g.GroupId).Select(x => new GroupAssignmentUsage(x.EmployeeId, x.FullName, x.Username, x.PositionName, "سمت")).Concat(employeeGroupAssignments.Where(x => x.GroupId == g.GroupId).Select(x => new GroupAssignmentUsage(x.EmployeeId, x.FullName, x.Username, null, "مستقیم به کارمند"))).GroupBy(x => new { x.EmployeeId, x.PositionName, x.Source }).Select(x => x.First()).OrderBy(x => x.EmployeeName).ThenBy(x => x.PositionName).ToList(); return new GroupPermissionUsage(g.GroupId, g.Name, g.Description, g.PermissionCount, assignments.Count, "گروه مجوز", assignments); }).ToList();
        return new PermissionUsageDetails(permission.Id, permission.Name, permission.Code, direct, groups);
    }
    // Full (unpaged) permission list for the read-only Permissions page, optionally only used or only unused ones.
    // Used means given to someone directly or included in at least one group.
    public async Task<IReadOnlyList<PermissionPageRow>> GetAllPermissionsAsync(string? search = null, string? usage = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Permissions.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q)).Select(x => new { x.Id, x.Code, x.Name, DirectAssignmentCount = db.UserPositionPermissions.Count(up => up.PermissionId == x.Id) + db.EmployeePermissions.Count(ep => ep.PermissionId == x.Id), GroupMembershipCount = db.PermissionGroupPermissions.Count(gp => gp.PermissionId == x.Id) });
        if (usage == "used") query = query.Where(x => x.DirectAssignmentCount + x.GroupMembershipCount > 0);
        else if (usage == "unused") query = query.Where(x => x.DirectAssignmentCount + x.GroupMembershipCount == 0);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct);
        return rows.Select(x => new PermissionPageRow(x.Id, x.Code, x.Name, x.DirectAssignmentCount, x.GroupMembershipCount)).ToList();
    }
    // Full (unpaged) list of permission groups with how many permissions each contains and how many times it is assigned.
    public async Task<IReadOnlyList<PermissionGroupPageRow>> GetAllPermissionGroupsAsync(string? search = null, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.PermissionGroups.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q) || (x.Description != null && x.Description.Contains(q))).OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name, x.Description, PermissionCount = db.PermissionGroupPermissions.Count(g => g.GroupId == x.Id), AssignmentCount = db.UserPositionPermissionGroups.Count(a => a.GroupId == x.Id) + db.EmployeePermissionGroups.Count(a => a.GroupId == x.Id) }).ToListAsync(ct);
        return rows.Select(x => new PermissionGroupPageRow(x.Id, x.Code, x.Name, x.Description, x.PermissionCount, x.AssignmentCount)).ToList();
    }

    // Every employee with their organization name, used by assignment screens that show everyone at once.
    public async Task<IReadOnlyList<EmployeeAssignmentRow>> GetAllEmployeesForAssignmentAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking()
            .OrderBy(x => x.FullName)
            .Select(x => new EmployeeAssignmentRow(
                x.Id, x.Code, x.Username, x.FullName,
                x.OrganizationId,
                db.Organizations.Where(o => o.Id == x.OrganizationId).Select(o => (string?)o.Name).FirstOrDefault(),
                x.IsActive))
            .ToListAsync(ct);
    }

    // Paged employee list for the admin Employees page, with search, sorting and filters on status, organization and position.
    public async Task<PageResult<EmployeePageRow>> GetEmployeesAsync(int page, int pageSize, string? search, string? sortBy = null, bool sortDesc = false, CancellationToken ct = default, bool? isActive = null, Guid? organizationId = null, Guid? positionId = null)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Employees.AsNoTracking().Where(x => q == "" || x.FullName.Contains(q) || x.Username.Contains(q) || x.Code.Contains(q));
        // List filters (spec §25.2): status, department (organization) and the position currently held.
        if (isActive is bool active)
            query = query.Where(x => x.IsActive == active);
        if (organizationId is Guid orgId)
            query = query.Where(x => x.OrganizationId == orgId);
        if (positionId is Guid posId)
            query = query.Where(x => db.EmployeePositions.Any(ep => ep.EmployeeId == x.Id && ep.PositionId == posId && ep.EndedAt == null));
        var total = await query.CountAsync(ct);
        // The sort column chosen in the table header decides the order; unknown columns fall back to name order.
        query = (sortBy, sortDesc) switch
        {
            ("code", false) => query.OrderBy(x => x.Code),
            ("code", true) => query.OrderByDescending(x => x.Code),
            ("type", false) => query.OrderBy(x => x.IsAdmin).ThenBy(x => x.FullName),
            ("type", true) => query.OrderByDescending(x => x.IsAdmin).ThenBy(x => x.FullName),
            ("status", false) => query.OrderBy(x => x.IsActive).ThenBy(x => x.FullName),
            ("status", true) => query.OrderByDescending(x => x.IsActive).ThenBy(x => x.FullName),
            ("organization", false) => query.OrderBy(x => db.Organizations.Where(o => o.Id == x.OrganizationId).Select(o => o.Name).FirstOrDefault()).ThenBy(x => x.FullName),
            ("organization", true) => query.OrderByDescending(x => db.Organizations.Where(o => o.Id == x.OrganizationId).Select(o => o.Name).FirstOrDefault()).ThenBy(x => x.FullName),
            ("name", true) => query.OrderByDescending(x => x.FullName),
            _ => query.OrderBy(x => x.FullName),
        };
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new EmployeePageRow(x.Id, x.Code, x.Username, x.FullName, db.Organizations.Where(o => o.Id == x.OrganizationId).Select(o => (string?)o.Name).FirstOrDefault(), x.IsActive, x.IsAdmin)).ToListAsync(ct);
        var employeeIds = rows.Select(x => x.Id).ToArray();
        // Positions each listed employee currently holds are loaded in one extra query for the whole page.
        var assignments = await db.EmployeePositions.AsNoTracking().Where(x => employeeIds.Contains(x.EmployeeId) && x.EndedAt == null).OrderBy(x => x.Position.Name).Select(x => new { x.EmployeeId, x.PositionId, x.Position.Name }).ToListAsync(ct);
        var positionsByEmployee = assignments.GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Select(x => new ActivePositionRow(x.PositionId, x.Name)).ToList() as IReadOnlyList<ActivePositionRow>);
        var items = rows.Select(x => x with { ActivePositions = positionsByEmployee.TryGetValue(x.Id, out var positions) ? positions : Array.Empty<ActivePositionRow>() }).ToList();
        return new(items, total, page, pageSize);
    }
    // Paged organization list, with optional filters on status and whether the organization has any employees.
    public async Task<PageResult<OrganizationPageRow>> GetOrganizationsAsync(int page, int pageSize, string? search, string? sortBy = null, bool sortDesc = false, bool? active = null, string? employees = null, Guid? employeeId = null, CancellationToken ct = default)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Organizations.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || x.Code.Contains(q));
        if (active is bool isActive)
            query = query.Where(x => x.IsActive == isActive);
        // "with" = at least one employee, "without" = none.
        if (employees == "with")
            query = query.Where(x => x.Employees.Any());
        else if (employees == "without")
            query = query.Where(x => !x.Employees.Any());
        if (employeeId is Guid memberId)
            query = query.Where(x => x.Employees.Any(e => e.Id == memberId));
        var total = await query.CountAsync(ct);
        var ordered = (sortBy, sortDesc) switch
        {
            ("code", false) => query.OrderBy(x => x.Code),
            ("code", true) => query.OrderByDescending(x => x.Code),
            ("status", false) => query.OrderBy(x => x.IsActive).ThenBy(x => x.Name),
            ("status", true) => query.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name),
            ("employees", false) => query.OrderBy(x => x.Employees.Count).ThenBy(x => x.Name),
            ("employees", true) => query.OrderByDescending(x => x.Employees.Count).ThenBy(x => x.Name),
            ("name", true) => query.OrderByDescending(x => x.Name),
            _ => query.OrderBy(x => x.Name),
        };
        var items = await ordered.Skip((page - 1) * pageSize).Take(pageSize).Select(x => new OrganizationPageRow(x.Id, x.Code, x.Name, x.IsActive, x.Employees.Count)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }
    // Paged permission list with search and a used/unused filter.
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
    // Paged permission-group list with search across name, code and description.
    public async Task<PageResult<PermissionGroupPageRow>> GetPermissionGroupsAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Page(page);
        pageSize = Size(pageSize);
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.PermissionGroups.AsNoTracking().Select(x => new { x.Id, x.Code, x.Name, x.Description, PermissionCount = db.PermissionGroupPermissions.Count(g => g.GroupId == x.Id), AssignmentCount = db.UserPositionPermissionGroups.Count(a => a.GroupId == x.Id) + db.EmployeePermissionGroups.Count(a => a.GroupId == x.Id) });
        if (q != "")
            query = query.Where(x => x.Name.Contains(q) || x.Code.Contains(q) || (x.Description != null && x.Description.Contains(q)));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var items = rows.Select(x => new PermissionGroupPageRow(x.Id, x.Code, x.Name, x.Description, x.PermissionCount, x.AssignmentCount)).ToList();
        return new(items, total, page, pageSize);
    }
    // Small helpers: keep the page number at 1 or more, keep the page size between 5 and 100, and trim search text.
    private static int Page(int page) => Math.Max(1, page); private static int Size(int size) => Math.Clamp(size, 5, 100); private static string Normalize(string? value) => (value ?? "").Trim();
    public sealed record ActivePositionRow(Guid Id, string Name);
    public sealed record EmployeePageRow(Guid Id, string Code, string Username, string FullName, string? OrganizationName, bool IsActive, bool IsAdmin, IReadOnlyList<ActivePositionRow>? ActivePositions = null);
    public sealed record EmployeeAssignmentRow(Guid Id, string Code, string Username, string FullName, Guid OrganizationId, string? OrganizationName, bool IsActive);
    public sealed record OrganizationPageRow(Guid Id, string Code, string Name, bool IsActive, int EmployeeCount);
    public sealed record PermissionPageRow(Guid Id, string Code, string Name, int DirectAssignmentCount, int GroupMembershipCount);
    public sealed record PermissionGroupPageRow(Guid Id, string Code, string Name, string? Description, int PermissionCount, int AssignmentCount);
}
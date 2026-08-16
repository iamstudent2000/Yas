using Microsoft.EntityFrameworkCore;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Web.Services;

/// <summary>Server-side lookup and paging queries for administration screens.</summary>
public sealed class AdminQueryService(IDbContextFactory<ApplicationDbContext> dbFactory)
{
    public sealed record LookupItem(Guid Id, string Text);
    public sealed record PageResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
    {
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    }

    public async Task<IReadOnlyList<LookupItem>> SearchEmployeesAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Employees.AsNoTracking()
            .Where(x => q == "" || x.FullName.Contains(q) || x.Username.Contains(q))
            .OrderBy(x => x.FullName).Take(30)
            .Select(x => new LookupItem(x.Id, x.FullName + " — " + x.Username + (x.IsAdmin ? " (مدیر سامانه)" : " (کارمند)")))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LookupItem>> SearchOrganizationsAsync(string? search, bool activeOnly = true, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Organizations.AsNoTracking()
            .Where(x => (!activeOnly || x.IsActive) && (q == "" || x.Name.Contains(q)))
            .OrderBy(x => x.Name).Take(30)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
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

        var rows = await query.OrderBy(x => x.Name).Select(x => new PositionLookupRow(x.Id, x.Name, x.ParentPositionId)).ToListAsync(ct);
        if (q != "") rows = rows.Where(x => x.Name.Contains(q, StringComparison.OrdinalIgnoreCase)).Take(30).ToList();
        else rows = rows.Take(30).ToList();
        return rows.Select(x => new LookupItem(x.Id, x.Name)).ToList();
    }

    public async Task<IReadOnlyList<LookupItem>> SearchPermissionsAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Permissions.AsNoTracking().Where(x => q == "" || x.Name.Contains(q)).OrderBy(x => x.Name).Take(30)
            .Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<LookupItem>> SearchPermissionGroupsAsync(string? search, CancellationToken ct = default)
    {
        var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.PermissionGroups.AsNoTracking().Where(x => q == "" || x.Name.Contains(q) || (x.Description != null && x.Description.Contains(q)))
            .OrderBy(x => x.Name).Take(30).Select(x => new LookupItem(x.Id, x.Name)).ToListAsync(ct);
    }

    public async Task<PageResult<EmployeePageRow>> GetEmployeesAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100); var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Employees.AsNoTracking().Where(x => q == "" || x.FullName.Contains(q) || x.Username.Contains(q));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.FullName).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EmployeePageRow(x.Id, x.Username, x.FullName, x.Organization.Name, x.IsActive, x.IsAdmin,
                x.Positions.Where(p => p.EndedAt == null && p.PositionId == x.LastActivePositionId).Select(p => p.Position.Name).FirstOrDefault()))
            .ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<PageResult<OrganizationPageRow>> GetOrganizationsAsync(int page, int pageSize, string? search, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100); var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Organizations.AsNoTracking().Where(x => q == "" || x.Name.Contains(q));
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OrganizationPageRow(x.Id, x.Name, x.IsActive, x.Employees.Count)).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    public async Task<PageResult<PermissionPageRow>> GetPermissionsAsync(int page, int pageSize, string? search, string? usage, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 10, 100); var q = Normalize(search);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var query = db.Permissions.AsNoTracking().Select(x => new PermissionPageRow(x.Id, x.Code, x.Name,
            db.UserPositionPermissions.Count(up => up.PermissionId == x.Id) + db.EmployeePermissions.Count(ep => ep.PermissionId == x.Id),
            db.PermissionGroupPermissions.Count(gp => gp.PermissionId == x.Id)));
        if (q != "") query = query.Where(x => x.Name.Contains(q));
        if (usage == "used") query = query.Where(x => x.DirectAssignmentCount + x.GroupMembershipCount > 0);
        if (usage == "unused") query = query.Where(x => x.DirectAssignmentCount + x.GroupMembershipCount == 0);
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new(items, total, page, pageSize);
    }

    private static string Normalize(string? value) => (value ?? "").Trim();
    private sealed record PositionLookupRow(Guid Id, string Name, Guid? ParentPositionId);
    public sealed record EmployeePageRow(Guid Id, string Username, string FullName, string? OrganizationName, bool IsActive, bool IsAdmin, string? LastActivePositionName);
    public sealed record OrganizationPageRow(Guid Id, string Name, bool IsActive, int EmployeeCount);
    public sealed record PermissionPageRow(Guid Id, string Code, string Name, int DirectAssignmentCount, int GroupMembershipCount);
}

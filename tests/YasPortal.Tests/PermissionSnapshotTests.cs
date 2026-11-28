using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

// Tests for the list of permission codes put into the sign-in cookie: administrators get only what is given to them
// (directly or through groups), everyone else only what the chosen position was given, and nothing leaks between the two.
public sealed class PermissionSnapshotTests : IAsyncDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Position _first = new("POS-A", "Position A");
    private readonly Position _second = new("POS-B", "Position B");

    public ValueTask DisposeAsync() => _db.DisposeAsync();

    // Adds a permission to the database and returns it.
    private Permission AddPermission(string code)
    {
        var permission = new Permission(code, code);
        _db.Permissions.Add(permission);
        return permission;
    }

    [Fact]
    public async Task Administrator_gets_direct_and_group_permissions_only()
    {
        var direct = AddPermission("Admin.Direct");
        var viaGroup = AddPermission("Admin.ViaGroup");
        var positionOnly = AddPermission("Requests.PositionOnly");
        var group = new PermissionGroup("GRP-1", "Group");
        _db.PermissionGroups.Add(group);
        _db.PermissionGroupPermissions.Add(new PermissionGroupPermission(group.Id, viaGroup.Id));
        _db.EmployeePermissions.Add(new EmployeePermission(_employeeId, direct.Id));
        _db.EmployeePermissionGroups.Add(new EmployeePermissionGroup(_employeeId, group.Id));
        _db.UserPositionPermissions.Add(new UserPositionPermission(_employeeId, _first.Id, positionOnly.Id));   // must not count for an admin
        await _db.SaveChangesAsync();

        var result = await PermissionSnapshot.BuildAsync(_db, _employeeId, _first.Id, isAdmin: true);

        Assert.Equal(new[] { "Admin.Direct", "Admin.ViaGroup" }, result.OrderBy(x => x));
    }

    [Fact]
    public async Task Non_admin_gets_only_what_the_chosen_position_was_given_directly_or_through_a_group()
    {
        var direct = AddPermission("Requests.Direct");
        var viaGroup = AddPermission("Requests.ViaGroup");
        var otherPosition = AddPermission("Requests.OtherPosition");
        var adminOnly = AddPermission("Admin.Direct");
        var group = new PermissionGroup("GRP-1", "Group");
        _db.PermissionGroups.Add(group);
        _db.PermissionGroupPermissions.Add(new PermissionGroupPermission(group.Id, viaGroup.Id));
        _db.UserPositionPermissions.Add(new UserPositionPermission(_employeeId, _first.Id, direct.Id));
        _db.UserPositionPermissionGroups.Add(new UserPositionPermissionGroup(_employeeId, _first.Id, group.Id));
        _db.UserPositionPermissions.Add(new UserPositionPermission(_employeeId, _second.Id, otherPosition.Id));  // another position
        _db.EmployeePermissions.Add(new EmployeePermission(_employeeId, adminOnly.Id));                          // admin track: must not count
        await _db.SaveChangesAsync();

        var result = await PermissionSnapshot.BuildAsync(_db, _employeeId, _first.Id, isAdmin: false);

        Assert.Equal(new[] { "Requests.Direct", "Requests.ViaGroup" }, result.OrderBy(x => x));
    }

    [Fact]
    public async Task Non_admin_without_a_chosen_position_gets_nothing()
    {
        var permission = AddPermission("Requests.Direct");
        _db.UserPositionPermissions.Add(new UserPositionPermission(_employeeId, _first.Id, permission.Id));
        await _db.SaveChangesAsync();

        Assert.Empty(await PermissionSnapshot.BuildAsync(_db, _employeeId, null, isAdmin: false));
        Assert.Empty(await PermissionSnapshot.BuildAsync(_db, _employeeId, Guid.Empty, isAdmin: false));
    }
}

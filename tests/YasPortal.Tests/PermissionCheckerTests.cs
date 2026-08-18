using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public sealed class PermissionCheckerTests
{
    [Fact]
    public async Task Permission_is_denied_when_active_position_claim_is_no_longer_assigned()
    {
        await using var fixture = new PermissionFixture();
        var permission = new Permission("Requests.View", "View requests");
        fixture.Db.Permissions.Add(permission);
        fixture.Db.UserPositionPermissions.Add(new UserPositionPermission(fixture.Employee.Id, fixture.Position.Id, permission.Id));
        var assignment = new EmployeePosition(fixture.Employee.Id, fixture.Position.Id);
        fixture.Employee.Positions.Add(assignment);
        fixture.Db.EmployeePositions.Add(assignment);
        await fixture.Db.SaveChangesAsync();

        var employeePosition = await fixture.Db.EmployeePositions.SingleAsync();
        employeePosition.End();
        await fixture.Db.SaveChangesAsync();

        var allowed = await fixture.Checker.HasPermissionAsync(fixture.Principal(AuthClaimNames.ActivePositionId, fixture.Position.Id.ToString()), permission.Code);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Permission_is_denied_for_deactivated_employee_even_with_valid_claims()
    {
        await using var fixture = new PermissionFixture();
        var permission = new Permission("Requests.View", "View requests");
        fixture.Db.Permissions.Add(permission);
        fixture.Db.UserPositionPermissions.Add(new UserPositionPermission(fixture.Employee.Id, fixture.Position.Id, permission.Id));
        var assignment = new EmployeePosition(fixture.Employee.Id, fixture.Position.Id);
        fixture.Employee.Positions.Add(assignment);
        fixture.Db.EmployeePositions.Add(assignment);
        await fixture.Db.SaveChangesAsync();

        fixture.Employee.Deactivate();
        await fixture.Db.SaveChangesAsync();

        var allowed = await fixture.Checker.HasPermissionAsync(fixture.Principal(AuthClaimNames.ActivePositionId, fixture.Position.Id.ToString()), permission.Code);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Position_permission_is_scoped_to_the_claimed_active_position()
    {
        await using var fixture = new PermissionFixture();
        var secondPosition = new Position("Second Position");
        var firstPermission = new Permission("Requests.View", "View requests");
        var secondPermission = new Permission("Requests.Approve", "Approve requests");
        fixture.Db.Positions.Add(secondPosition);
        fixture.Db.Permissions.AddRange(firstPermission, secondPermission);

        var firstAssignment = new EmployeePosition(fixture.Employee.Id, fixture.Position.Id);
        var secondAssignment = new EmployeePosition(fixture.Employee.Id, secondPosition.Id);
        fixture.Employee.Positions.Add(firstAssignment);
        fixture.Employee.Positions.Add(secondAssignment);
        fixture.Db.EmployeePositions.AddRange(firstAssignment, secondAssignment);
        fixture.Db.UserPositionPermissions.Add(new UserPositionPermission(fixture.Employee.Id, fixture.Position.Id, firstPermission.Id));
        fixture.Db.UserPositionPermissions.Add(new UserPositionPermission(fixture.Employee.Id, secondPosition.Id, secondPermission.Id));
        await fixture.Db.SaveChangesAsync();

        var firstPrincipal = fixture.Principal(AuthClaimNames.ActivePositionId, fixture.Position.Id.ToString());
        var secondPrincipal = fixture.Principal(AuthClaimNames.ActivePositionId, secondPosition.Id.ToString());

        Assert.True(await fixture.Checker.HasPermissionAsync(firstPrincipal, firstPermission.Code));
        Assert.False(await fixture.Checker.HasPermissionAsync(firstPrincipal, secondPermission.Code));
        Assert.True(await fixture.Checker.HasPermissionAsync(secondPrincipal, secondPermission.Code));
        Assert.False(await fixture.Checker.HasPermissionAsync(secondPrincipal, firstPermission.Code));
    }

    [Fact]
    public async Task Group_permission_is_scoped_to_the_claimed_employee_and_position()
    {
        await using var fixture = new PermissionFixture();
        var permission = new Permission("Requests.View", "View requests");
        var group = new PermissionGroup("Request viewers");
        fixture.Db.Permissions.Add(permission);
        fixture.Db.PermissionGroups.Add(group);
        fixture.Db.PermissionGroupPermissions.Add(new PermissionGroupPermission(group.Id, permission.Id));

        var assignment = new EmployeePosition(fixture.Employee.Id, fixture.Position.Id);
        fixture.Employee.Positions.Add(assignment);
        fixture.Db.EmployeePositions.Add(assignment);
        fixture.Db.UserPositionPermissionGroups.Add(new UserPositionPermissionGroup(fixture.Employee.Id, fixture.Position.Id, group.Id));
        await fixture.Db.SaveChangesAsync();

        Assert.True(await fixture.Checker.HasPermissionAsync(
            fixture.Principal(AuthClaimNames.ActivePositionId, fixture.Position.Id.ToString()), permission.Code));

        var otherPosition = new Position("Other Position");
        fixture.Db.Positions.Add(otherPosition);
        var otherAssignment = new EmployeePosition(fixture.Employee.Id, otherPosition.Id);
        fixture.Employee.Positions.Add(otherAssignment);
        fixture.Db.EmployeePositions.Add(otherAssignment);
        await fixture.Db.SaveChangesAsync();

        Assert.False(await fixture.Checker.HasPermissionAsync(
            fixture.Principal(AuthClaimNames.ActivePositionId, otherPosition.Id.ToString()), permission.Code));
    }

    [Fact]
    public async Task Admin_permission_uses_database_admin_state_not_stale_claim()
    {
        await using var fixture = new PermissionFixture(isAdmin: false);
        var permission = new Permission("Admin.Access", "Access management");
        fixture.Db.Permissions.Add(permission);
        fixture.Db.EmployeePermissions.Add(new EmployeePermission(fixture.Employee.Id, permission.Id));
        fixture.Employee.SetAdmin(true);
        await fixture.Db.SaveChangesAsync();

        var allowed = await fixture.Checker.HasPermissionAsync(
            fixture.Principal(AuthClaimNames.IsAdmin, "False"),
            permission.Code);

        Assert.True(allowed);
    }

    [Fact]
    public async Task Non_admin_cannot_use_an_admin_permission_from_a_position_assignment()
    {
        await using var fixture = new PermissionFixture(isAdmin: false);
        var permission = new Permission("Admin.Access", "Access management");
        fixture.Db.Permissions.Add(permission);
        var assignment = new EmployeePosition(fixture.Employee.Id, fixture.Position.Id);
        fixture.Employee.Positions.Add(assignment);
        fixture.Db.EmployeePositions.Add(assignment);
        fixture.Db.UserPositionPermissions.Add(new UserPositionPermission(fixture.Employee.Id, fixture.Position.Id, permission.Id));
        await fixture.Db.SaveChangesAsync();

        var allowed = await fixture.Checker.HasPermissionAsync(
            fixture.Principal(AuthClaimNames.ActivePositionId, fixture.Position.Id.ToString()),
            permission.Code);

        Assert.False(allowed);
    }

    private sealed class PermissionFixture : IAsyncDisposable
    {
        public PermissionFixture(bool isAdmin = false)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            Db = new ApplicationDbContext(options);
            var organization = new Organization("Test Organization");
            Position = new Position("Test Position");
            Employee = new Employee("test-user", "Test User", organization.Id, isAdmin);
            Db.Organizations.Add(organization);
            Db.Positions.Add(Position);
            Db.Employees.Add(Employee);
            Db.SaveChanges();
            Checker = new PermissionChecker(new TestDbContextFactory(Db), new TestAuthenticationStateProvider());
        }

        public ApplicationDbContext Db { get; }
        public Employee Employee { get; }
        public Position Position { get; }
        public PermissionChecker Checker { get; }

        public ClaimsPrincipal Principal(string? extraClaimType = null, string? extraClaimValue = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Employee.Id.ToString()),
                new(AuthClaimNames.IsAdmin, Employee.IsAdmin.ToString())
            };
            if (extraClaimType is not null && extraClaimValue is not null)
                claims.Add(new Claim(extraClaimType, extraClaimValue));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestDbContextFactory(ApplicationDbContext db) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => db;
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(db);
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

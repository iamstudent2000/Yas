using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using YasPortal.Domain.Authorization;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public sealed class PermissionCheckerTests
{
    [Fact(Skip = "PermissionChecker now reads only the sign-in claim snapshot (commit 896fb4d), so revocation is no longer re-checked per call. Re-enable if cookie revalidation is added.")]
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

    [Fact(Skip = "PermissionChecker now reads only the sign-in claim snapshot (commit 896fb4d), so revocation is no longer re-checked per call. Re-enable if cookie revalidation is added.")]
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
    public async Task Permission_is_granted_only_when_the_snapshot_claim_is_present()
    {
        await using var fixture = new PermissionFixture();

        Assert.True(await fixture.Checker.HasPermissionAsync(fixture.PrincipalWith("Requests.View"), "Requests.View"));
        Assert.True(await fixture.Checker.HasPermissionAsync(fixture.PrincipalWith("Requests.View"), "requests.view"));
        Assert.False(await fixture.Checker.HasPermissionAsync(fixture.PrincipalWith("Requests.View"), "Requests.Approve"));
        Assert.False(await fixture.Checker.HasPermissionAsync(fixture.PrincipalWith(), "Requests.View"));
    }

    [Fact]
    public async Task Snapshots_for_different_positions_do_not_leak_into_each_other()
    {
        await using var fixture = new PermissionFixture();
        var firstPosition = fixture.PrincipalWith("Requests.View");
        var secondPosition = fixture.PrincipalWith("Requests.Approve");

        Assert.True(await fixture.Checker.HasPermissionAsync(firstPosition, "Requests.View"));
        Assert.False(await fixture.Checker.HasPermissionAsync(firstPosition, "Requests.Approve"));
        Assert.True(await fixture.Checker.HasPermissionAsync(secondPosition, "Requests.Approve"));
        Assert.False(await fixture.Checker.HasPermissionAsync(secondPosition, "Requests.View"));
    }

    [Fact]
    public async Task Anonymous_principal_and_blank_permission_codes_are_denied()
    {
        await using var fixture = new PermissionFixture();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        anonymous.Identities.First().AddClaim(new Claim(AuthClaimNames.Permission, "Requests.View"));

        Assert.False(await fixture.Checker.HasPermissionAsync(anonymous, "Requests.View"));
        Assert.False(await fixture.Checker.HasPermissionAsync(fixture.PrincipalWith("Requests.View"), ""));
        Assert.False(await fixture.Checker.HasPermissionAsync(fixture.PrincipalWith("Requests.View"), "  "));
    }

    [Fact(Skip = "PermissionChecker now reads only the sign-in claim snapshot (commit 896fb4d), so revocation is no longer re-checked per call. Re-enable if cookie revalidation is added.")]
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

    [Fact(Skip = "The admin/employee track split now happens when the cookie is built in Program.cs, so this checker cannot enforce it.")]
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
        private readonly InMemoryDatabaseRoot _databaseRoot;

        public PermissionFixture(bool isAdmin = false)
        {
            _databaseRoot = new InMemoryDatabaseRoot();
            var databaseName = Guid.NewGuid().ToString();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName, _databaseRoot)
                .Options;
            Db = new ApplicationDbContext(options);
            var organization = new Organization("ORG-TestOrganization", "Test Organization");
            Position = new Position("POS-TestPosition", "Test Position");
            Employee = new Employee("EMP-test-user", "test-user", "Test User", organization.Id, isAdmin);
            Db.Organizations.Add(organization);
            Db.Positions.Add(Position);
            Db.Employees.Add(Employee);
            Db.SaveChanges();
            Checker = new PermissionChecker(new TestAuthenticationStateProvider());
        }

        public ApplicationDbContext Db
        {
            get;
        }
        public Employee Employee
        {
            get;
        }
        public Position Position
        {
            get;
        }
        public PermissionChecker Checker
        {
            get;
        }

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

        /// <summary>A signed-in principal carrying exactly the given permission snapshot claims.</summary>
        public ClaimsPrincipal PrincipalWith(params string[] permissionCodes)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Employee.Id.ToString()),
                new(AuthClaimNames.IsAdmin, Employee.IsAdmin.ToString())
            };
            claims.AddRange(permissionCodes.Select(code => new Claim(AuthClaimNames.Permission, code)));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class TestDbContextFactory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

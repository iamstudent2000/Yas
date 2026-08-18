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
        fixture.Db.EmployeePositions.Add(new EmployeePosition(fixture.Employee.Id, fixture.Position.Id));
        await fixture.Db.SaveChangesAsync();

        var employeePosition = await fixture.Db.EmployeePositions.SingleAsync();
        employeePosition.End();
        await fixture.Db.SaveChangesAsync();

        var allowed = await fixture.Checker.HasPermissionAsync(fixture.Principal("yas_active_position_id", fixture.Position.Id.ToString()), permission.Code);

        Assert.False(allowed);
    }

    [Fact]
    public async Task Permission_is_denied_for_deactivated_employee_even_with_valid_claims()
    {
        await using var fixture = new PermissionFixture();
        var permission = new Permission("Requests.View", "View requests");
        fixture.Db.Permissions.Add(permission);
        fixture.Db.UserPositionPermissions.Add(new UserPositionPermission(fixture.Employee.Id, fixture.Position.Id, permission.Id));
        fixture.Db.EmployeePositions.Add(new EmployeePosition(fixture.Employee.Id, fixture.Position.Id));
        fixture.Employee.Deactivate();
        await fixture.Db.SaveChangesAsync();

        var allowed = await fixture.Checker.HasPermissionAsync(fixture.Principal("yas_active_position_id", fixture.Position.Id.ToString()), permission.Code);

        Assert.False(allowed);
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

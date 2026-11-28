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

// Permission checker tests. The checker only reads the permission claims stored in the sign-in cookie; these tests make sure
// anonymous and blank requests are refused and that 'manage' implies 'view'. How the claims are built is tested in PermissionSnapshotTests,
// and whether a cookie is still valid in IdentitySnapshotValidatorTests.
public sealed class PermissionCheckerTests
{
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
    public async Task Manage_implies_view_but_not_the_other_way_round()
    {
        await using var fixture = new PermissionFixture();
        var manager = fixture.PrincipalWith("Admin.Users.Manage");
        var viewer = fixture.PrincipalWith("Admin.Users.View");

        Assert.True(await fixture.Checker.HasPermissionAsync(manager, "Admin.Users.View"));
        Assert.True(await fixture.Checker.HasPermissionAsync(manager, "Admin.Users.Manage"));
        Assert.True(await fixture.Checker.HasPermissionAsync(viewer, "Admin.Users.View"));
        Assert.False(await fixture.Checker.HasPermissionAsync(viewer, "Admin.Users.Manage"));
        Assert.False(await fixture.Checker.HasPermissionAsync(manager, "Admin.Positions.View"));
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

    private sealed class TestAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}

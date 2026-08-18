using Microsoft.EntityFrameworkCore;
using Xunit;
using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Persistence;

namespace YasPortal.Tests;

public sealed class PersistenceIntegrityTests
{
    [Fact]
    public void Deactivating_employee_ends_active_assignments_and_clears_last_position()
    {
        var organizationId = Guid.NewGuid();
        var positionId = Guid.NewGuid();
        var employee = new Employee("employee", "Employee", organizationId);
        var assignment = new EmployeePosition(employee.Id, positionId);
        employee.SetLastActivePosition(positionId);
        employee.Positions.Add(assignment);

        employee.Deactivate();

        Assert.False(employee.IsActive);
        Assert.Null(employee.LastActivePositionId);
        Assert.False(assignment.IsActive);
        Assert.NotNull(assignment.EndedAt);
    }

    [Fact]
    public async Task Context_rejects_assigning_position_to_inactive_employee()
    {
        await using var db = CreateContext();
        var organization = new Organization("Org");
        var employee = new Employee("employee", "Employee", organization.Id);
        var position = new Position("Position");
        employee.Deactivate();

        db.Organizations.Add(organization);
        db.Employees.Add(employee);
        db.Positions.Add(position);
        await db.SaveChangesAsync();

        db.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("inactive employee", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Context_rejects_two_active_employees_for_one_position()
    {
        await using var db = CreateContext();
        var organization = new Organization("Org");
        var first = new Employee("first", "First", organization.Id);
        var second = new Employee("second", "Second", organization.Id);
        var position = new Position("Position");

        db.Organizations.Add(organization);
        db.Employees.AddRange(first, second);
        db.Positions.Add(position);
        db.EmployeePositions.Add(new EmployeePosition(first.Id, position.Id));
        await db.SaveChangesAsync();

        db.EmployeePositions.Add(new EmployeePosition(second.Id, position.Id));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("only one active employee", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Context_rejects_position_hierarchy_cycles()
    {
        await using var db = CreateContext();
        var first = new Position("First");
        var second = new Position("Second", first.Id);
        db.Positions.AddRange(first, second);
        await db.SaveChangesAsync();

        first.ChangeParent(second.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Assignment_history_is_closed_when_assignment_ends_even_when_history_was_not_preloaded()
    {
        await using (var setup = CreateContext())
        {
            var organization = new Organization("Org");
            var employee = new Employee("employee", "Employee", organization.Id);
            var position = new Position("Position");
            setup.Organizations.Add(organization);
            setup.Employees.Add(employee);
            setup.Positions.Add(position);
            setup.EmployeePositions.Add(new EmployeePosition(employee.Id, position.Id));
            await setup.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var assignment = await db.EmployeePositions.SingleAsync();
            assignment.End();
            await db.SaveChangesAsync();
        }

        await using (var verify = CreateContext())
        {
            var history = await verify.PositionAssignmentHistories.SingleAsync();
            Assert.NotNull(history.EndedAt);
        }
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }
}

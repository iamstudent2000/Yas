using YasPortal.Domain.Organization;
using YasPortal.Infrastructure.Authorization;
using Xunit;

namespace YasPortal.Tests;

// Tests for the check that decides whether a signed-in cookie still matches the database (active person, same admin flag, still holding the chosen position).
public sealed class IdentitySnapshotValidatorTests
{
    // A person with one position; the position is added to the person's list the way the database would load it.
    private static (Employee Employee, EmployeePosition Assignment) PersonWithPosition(bool isAdmin = false)
    {
        var employee = new Employee("EMP-1", "user1", "User One", Guid.NewGuid(), isAdmin);
        var assignment = new EmployeePosition(employee.Id, Guid.NewGuid());
        employee.Positions.Add(assignment);
        return (employee, assignment);
    }

    [Fact]
    public void Active_person_holding_the_chosen_position_is_valid()
    {
        var (employee, assignment) = PersonWithPosition();
        Assert.True(IdentitySnapshotValidator.IsStillValid(employee, false, assignment.PositionId));
    }

    [Fact]
    public void Missing_person_is_not_valid()
        => Assert.False(IdentitySnapshotValidator.IsStillValid(null, false, Guid.NewGuid()));

    [Fact]
    public void Deactivated_person_is_not_valid()
    {
        var (employee, assignment) = PersonWithPosition();
        employee.Deactivate();
        Assert.False(IdentitySnapshotValidator.IsStillValid(employee, false, assignment.PositionId));
    }

    [Fact]
    public void Changed_admin_flag_is_not_valid_in_either_direction()
    {
        var (normal, assignment) = PersonWithPosition();
        normal.SetAdmin(true);                                                         // became admin after sign-in
        Assert.False(IdentitySnapshotValidator.IsStillValid(normal, false, assignment.PositionId));

        var (admin, _) = PersonWithPosition(isAdmin: true);
        admin.SetAdmin(false);                                                         // lost admin after sign-in
        Assert.False(IdentitySnapshotValidator.IsStillValid(admin, true, null));
    }

    [Fact]
    public void Ended_or_unknown_position_is_not_valid_for_a_non_admin()
    {
        var (employee, assignment) = PersonWithPosition();
        Assert.False(IdentitySnapshotValidator.IsStillValid(employee, false, Guid.NewGuid()));   // never held
        Assert.False(IdentitySnapshotValidator.IsStillValid(employee, false, null));             // none chosen
        assignment.End();
        Assert.False(IdentitySnapshotValidator.IsStillValid(employee, false, assignment.PositionId));
    }

    [Fact]
    public void Admin_needs_no_position()
    {
        var (admin, _) = PersonWithPosition(isAdmin: true);
        Assert.True(IdentitySnapshotValidator.IsStillValid(admin, true, null));
    }
}

using YasPortal.Domain.Organization;

namespace YasPortal.Tests;

public class EmployeeTests
{
    [Fact]
    public void Employee_can_be_admin_without_a_role()
    {
        var employee = new Employee("admin", "System Administrator", isAdmin: true);

        Assert.True(employee.IsAdmin);
    }

    [Fact]
    public void Employee_defaults_to_non_admin()
    {
        var employee = new Employee("employee", "Normal Employee");

        Assert.False(employee.IsAdmin);
    }
}

using Xunit;
using YasPortal.Domain.Organization;

namespace YasPortal.Tests;

// Employee tests: whether an employee can be an administrator and the default of not being one.
public class EmployeeTests
{
    [Fact]
    public void Employee_can_be_admin_without_a_role()
    {
        var organizationId = Guid.NewGuid();
        var employee = new Employee("EMP-admin", "admin", "System Administrator", organizationId, isAdmin: true);

        Assert.True(employee.IsAdmin);
        Assert.Equal(organizationId, employee.OrganizationId);
    }

    [Fact]
    public void Employee_defaults_to_non_admin()
    {
        var organizationId = Guid.NewGuid();
        var employee = new Employee("EMP-employee", "employee", "Normal Employee", organizationId);

        Assert.False(employee.IsAdmin);
        Assert.Equal(organizationId, employee.OrganizationId);
    }
}

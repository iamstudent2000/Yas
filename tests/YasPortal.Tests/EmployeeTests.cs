using Xunit;
using YasPortal.Domain.Organization;

namespace YasPortal.Tests;

public class EmployeeTests
{
    [Fact]
    public void Employee_can_be_admin_without_a_role()
    {
        var organizationId = Guid.NewGuid();
        var employee = new Employee("admin", "System Administrator", organizationId, isAdmin: true);

        Assert.True(employee.IsAdmin);
        Assert.Equal(organizationId, employee.OrganizationId);
    }

    [Fact]
    public void Employee_defaults_to_non_admin()
    {
        var organizationId = Guid.NewGuid();
        var employee = new Employee("employee", "Normal Employee", organizationId);

        Assert.False(employee.IsAdmin);
        Assert.Equal(organizationId, employee.OrganizationId);
    }
}

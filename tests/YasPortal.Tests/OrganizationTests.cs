using Xunit;
using YasPortal.Domain.Organization;

namespace YasPortal.Tests;

public class OrganizationTests
{
    [Fact]
    public void Organization_can_be_renamed_and_toggled()
    {
        var organization = new Organization("سازمان اولیه");

        organization.Rename("سازمان جدید");
        organization.Deactivate();

        Assert.Equal("سازمان جدید", organization.Name);
        Assert.False(organization.IsActive);

        organization.Activate();
        Assert.True(organization.IsActive);
    }

    [Fact]
    public void Employee_has_one_organization_and_can_change_it()
    {
        var firstOrganizationId = Guid.NewGuid();
        var secondOrganizationId = Guid.NewGuid();
        var employee = new Employee("employee", "کارمند نمونه", firstOrganizationId);

        employee.ChangeOrganization(secondOrganizationId);

        Assert.Equal(secondOrganizationId, employee.OrganizationId);
        Assert.NotEqual(firstOrganizationId, employee.OrganizationId);
    }
}

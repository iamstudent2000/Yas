namespace YasPortal.Domain.Authorization;

// Permission = one right, such as viewing users. This file links one administrator directly to one permission.
/// <summary>
/// Direct permissions assigned to an administrator. These permissions are independent
/// of organizational positions because administrators are a different user type.
/// </summary>
public sealed class EmployeePermission
{
    private EmployeePermission()
    {
    }

    public EmployeePermission(Guid employeeId, Guid permissionId)
    {
        EmployeeId = employeeId;
        PermissionId = permissionId;
    }

    public Guid EmployeeId
    {
        get; private set;
    }
    public Guid PermissionId
    {
        get; private set;
    }

    public YasPortal.Domain.Organization.Employee Employee { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}

namespace YasPortal.Domain.Authorization;

/// <summary>
/// Assigns a reusable permission group directly to an administrator account.
/// Administrators do not require an organizational position.
/// </summary>
public sealed class EmployeePermissionGroup
{
    private EmployeePermissionGroup() { }

    public EmployeePermissionGroup(Guid employeeId, Guid groupId)
    {
        EmployeeId = employeeId;
        GroupId = groupId;
    }

    public Guid EmployeeId { get; private set; }
    public Guid GroupId { get; private set; }

    public YasPortal.Domain.Organization.Employee Employee { get; private set; } = null!;
    public PermissionGroup Group { get; private set; } = null!;
}

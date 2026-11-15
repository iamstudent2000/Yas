using YasPortal.Domain.Organization;

namespace YasPortal.Domain.Authorization;

// Grants one permission to one employee while acting in one position. Rights belong to the combination of person and
// position, so an employee who holds two positions can have different rights in each.
public sealed class UserPositionPermission
{
    private UserPositionPermission()
    {
    }

    public UserPositionPermission(Guid employeeId, Guid positionId, Guid permissionId)
    {
        EmployeeId = employeeId;
        PositionId = positionId;
        PermissionId = permissionId;
    }

    public Guid EmployeeId
    {
        get; private set;
    }
    public Guid PositionId
    {
        get; private set;
    }
    public Guid PermissionId
    {
        get; private set;
    }

    public Employee Employee { get; private set; } = null!;
    public Position Position { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}

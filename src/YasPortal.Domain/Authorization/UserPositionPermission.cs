using YasPortal.Domain.Organization;

namespace YasPortal.Domain.Authorization;

public sealed class UserPositionPermission
{
    private UserPositionPermission() { }

    public UserPositionPermission(Guid employeeId, Guid positionId, Guid permissionId)
    {
        EmployeeId = employeeId;
        PositionId = positionId;
        PermissionId = permissionId;
    }

    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid PermissionId { get; private set; }

    public Employee Employee { get; private set; } = null!;
    public Position Position { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}

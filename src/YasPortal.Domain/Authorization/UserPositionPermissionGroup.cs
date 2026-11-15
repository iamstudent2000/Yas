namespace YasPortal.Domain.Authorization;

// Like the single-permission version, but grants a whole permission group to an employee in one position.
/// <summary>
/// Assigns a reusable permission group to one Employee + Position combination.
/// </summary>
public sealed class UserPositionPermissionGroup
{
    private UserPositionPermissionGroup()
    {
    }

    public UserPositionPermissionGroup(Guid employeeId, Guid positionId, Guid groupId)
    {
        EmployeeId = employeeId;
        PositionId = positionId;
        GroupId = groupId;
    }

    public Guid EmployeeId
    {
        get; private set;
    }
    public Guid PositionId
    {
        get; private set;
    }
    public Guid GroupId
    {
        get; private set;
    }

    public YasPortal.Domain.Organization.Employee Employee { get; private set; } = null!;
    public YasPortal.Domain.Organization.Position Position { get; private set; } = null!;
    public PermissionGroup Group { get; private set; } = null!;
}

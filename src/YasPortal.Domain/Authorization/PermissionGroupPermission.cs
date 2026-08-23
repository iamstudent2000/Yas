namespace YasPortal.Domain.Authorization;

public sealed class PermissionGroupPermission
{
    private PermissionGroupPermission()
    {
    }

    public PermissionGroupPermission(Guid groupId, Guid permissionId)
    {
        GroupId = groupId;
        PermissionId = permissionId;
    }

    public Guid GroupId
    {
        get; private set;
    }
    public Guid PermissionId
    {
        get; private set;
    }

    public PermissionGroup Group { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}

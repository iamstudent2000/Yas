namespace YasPortal.Domain.Authorization;

/// <summary>
/// A reusable named bundle of permissions. Groups grant permissions only;
/// there is deliberately no deny/inheritance semantics.
/// </summary>
public sealed class PermissionGroup
{
    private PermissionGroup()
    {
    }

    public PermissionGroup(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public string? Description
    {
        get; private set;
    }

    public void Rename(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}

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

    // A group needs a code and a name; the description is optional.
    public PermissionGroup(string code, string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Group code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));
        Code = code.Trim();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description
    {
        get; private set;
    }

    // Changes the visible name and description; the code stays the same.
    public void Rename(string name, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Group name is required.", nameof(name));
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    // Changes the technical code only.
    public void ChangeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Group code is required.", nameof(code));
        Code = code.Trim();
    }
}

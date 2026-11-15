namespace YasPortal.Domain.Authorization;

// One permission = one right in the portal (for example view users). The code is the fixed technical name used by the
// program; the name is the label people see. The full list of permissions is defined in the application layer.
public sealed class Permission
{
    private Permission()
    {
    }
    // Both the code and the name are required, so an empty permission can never be created.
    public Permission(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Permission code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Permission name is required.", nameof(name));
        Code = code.Trim();
        Name = name.Trim();
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    // Used when the seed data renames or recodes an existing permission so existing grants are kept.
    public void Rename(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Permission code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Permission name is required.", nameof(name));
        Code = code.Trim();
        Name = name.Trim();
    }
}

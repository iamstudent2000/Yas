namespace YasPortal.Domain.Authorization;

public sealed class Permission
{
    private Permission()
    {
    }
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

namespace YasPortal.Domain.Organization;

public sealed class Position
{
    private Position() { }
    public Position(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Position name is required.", nameof(name));
        Name = name.Trim();
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
}

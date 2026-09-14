namespace YasPortal.Domain.Organization;

public sealed class Position
{
    private Position()
    {
    }

    public Position(string code, string name, Guid? parentPositionId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Position code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Position name is required.", nameof(name));

        if (parentPositionId == Id)
            throw new ArgumentException("A position cannot be its own parent.", nameof(parentPositionId));

        Code = code.Trim();
        Name = name.Trim();
        ParentPositionId = parentPositionId;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public Guid? ParentPositionId
    {
        get; private set;
    }
    public Position? ParentPosition
    {
        get; private set;
    }
    public ICollection<Position> Children { get; private set; } = new List<Position>();

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Position name is required.", nameof(name));

        Name = name.Trim();
    }

    public void ChangeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Position code is required.", nameof(code));

        Code = code.Trim();
    }

    public void ChangeParent(Guid? parentPositionId)
    {
        if (parentPositionId == Id)
            throw new InvalidOperationException("A position cannot be its own parent.");

        ParentPositionId = parentPositionId;
    }
}

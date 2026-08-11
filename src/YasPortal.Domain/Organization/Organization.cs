namespace YasPortal.Domain.Organization;

public sealed class Organization
{
    private Organization() { }

    public Organization(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));

        Name = name.Trim();
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public ICollection<Employee> Employees { get; private set; } = new List<Employee>();
}

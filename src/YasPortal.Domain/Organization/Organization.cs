namespace YasPortal.Domain.Organization;

public sealed class Organization
{
    private Organization()
    {
    }

    public Organization(string name)
    {
        SetName(name);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public ICollection<Employee> Employees { get; private set; } = new List<Employee>();

    public void Rename(string name) => SetName(name);

    public void Activate() => IsActive = true;

    /// <summary>
    /// Deactivating an organization also deactivates its employees. This prevents
    /// active employees from remaining attached to an inactive organization and
    /// lets Employee.Deactivate() clear their active position assignments and
    /// last-position preference as part of the same domain operation.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;

        foreach (var employee in Employees.Where(x => x.IsActive))
            employee.Deactivate();
    }

    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));

        Name = name.Trim();
    }
}

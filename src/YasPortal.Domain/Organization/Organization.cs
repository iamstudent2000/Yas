namespace YasPortal.Domain.Organization;

// An organization is a company or unit that employees belong to. It has a code and a name and can be
// switched off. Switching it off also switches off all its employees, so nobody active stays under an
// inactive organization.
public sealed class Organization
{
    private Organization()
    {
    }

    public Organization(string code, string name)
    {
        SetCode(code);
        SetName(name);
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;

    public ICollection<Employee> Employees { get; private set; } = new List<Employee>();

    public void Rename(string name) => SetName(name);

    public void ChangeCode(string code) => SetCode(code);

    public void Activate() => IsActive = true;

    // Switching an organization off cascades to its employees, as the note below explains.
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

    // Private checks used by the creation and edit methods: both the name and the code are required and are
    // trimmed.
    private void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Organization name is required.", nameof(name));

        Name = name.Trim();
    }

    private void SetCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Organization code is required.", nameof(code));

        Code = code.Trim();
    }
}

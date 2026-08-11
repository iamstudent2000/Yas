namespace YasPortal.Domain.Organization;

public sealed class Employee
{
    private Employee() { }
    public Employee(string username, string fullName, Guid organizationId, bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("Full name is required.", nameof(fullName));
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.", nameof(organizationId));
        Username = username.Trim();
        FullName = fullName.Trim();
        OrganizationId = organizationId;
        IsAdmin = isAdmin;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Username { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public Guid OrganizationId { get; private set; }
    public Organization Organization { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public bool IsAdmin { get; private set; }
    public string? PasswordHash { get; private set; }
    public ICollection<EmployeePosition> Positions { get; private set; } = new List<EmployeePosition>();

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
    public void SetFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("Full name is required.", nameof(fullName));
        FullName = fullName.Trim();
    }
    public void ChangeOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty) throw new ArgumentException("Organization is required.", nameof(organizationId));
        OrganizationId = organizationId;
    }
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

public sealed class EmployeePosition
{
    private EmployeePosition() { }
    public EmployeePosition(Guid employeeId, Guid positionId)
    {
        EmployeeId = employeeId;
        PositionId = positionId;
    }

    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public Employee Employee { get; private set; } = null!;
    public Position Position { get; private set; } = null!;
    public DateTime? EndedAt { get; private set; }
    public bool IsActive => EndedAt is null;
    public void End() => EndedAt = DateTime.UtcNow;
    public void Reactivate() => EndedAt = null;
}

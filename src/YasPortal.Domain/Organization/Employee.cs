namespace YasPortal.Domain.Organization;

public sealed class Employee
{
    private Employee() { }
    public Employee(string username, string fullName, bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(fullName)) throw new ArgumentException("Full name is required.", nameof(fullName));
        Username = username.Trim();
        FullName = fullName.Trim();
        IsAdmin = isAdmin;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Username { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public bool IsAdmin { get; private set; }
    public string? PasswordHash { get; private set; }
    public ICollection<EmployeePosition> Positions { get; private set; } = new List<EmployeePosition>();

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
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
}

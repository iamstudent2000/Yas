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

    /// <summary>
    /// The position this employee last selected as their active position.
    /// This is only a preference; the position must still have an active EmployeePosition assignment.
    /// </summary>
    public Guid? LastActivePositionId { get; private set; }

    public ICollection<EmployeePosition> Positions { get; private set; } = new List<EmployeePosition>();

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;
    public void SetLastActivePosition(Guid? positionId) => LastActivePositionId = positionId;

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

    /// <summary>
    /// Deactivating an employee immediately ends every active position assignment and
    /// clears the last-position preference. An inactive employee must never retain an
    /// active organizational assignment.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
            return;

        IsActive = false;
        LastActivePositionId = null;

        foreach (var position in Positions.Where(x => x.EndedAt is null))
            position.End();
    }
}

public sealed class EmployeePosition
{
    private EmployeePosition() { }

    public EmployeePosition(Guid employeeId, Guid positionId)
    {
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee is required.", nameof(employeeId));
        if (positionId == Guid.Empty) throw new ArgumentException("Position is required.", nameof(positionId));
        EmployeeId = employeeId;
        PositionId = positionId;
    }

    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public Employee Employee { get; private set; } = null!;
    public Position Position { get; private set; } = null!;
    public DateTime? EndedAt { get; private set; }
    public bool IsActive => EndedAt is null;

    public void End() => EndedAt ??= DateTime.UtcNow;

    public void Reactivate()
    {
        EndedAt = null;
    }
}

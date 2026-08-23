namespace YasPortal.Domain.Organization;

public sealed class Employee
{
    private Employee()
    {
    }

    public Employee(string username, string fullName, Guid organizationId, bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        Username = username.Trim();
        FullName = fullName.Trim();
        OrganizationId = organizationId;
        IsAdmin = isAdmin;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Username { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public Guid OrganizationId
    {
        get; private set;
    }
    public Organization Organization { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public bool IsAdmin
    {
        get; private set;
    }
    public string? PasswordHash
    {
        get; private set;
    }

    /// <summary>
    /// The position this employee last selected as their active position.
    /// This is a preference; when set, it must refer to an active assignment.
    /// </summary>
    public Guid? LastActivePositionId
    {
        get; private set;
    }

    public ICollection<EmployeePosition> Positions { get; private set; } = new List<EmployeePosition>();

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void SetLastActivePosition(Guid? positionId)
    {
        if (positionId is null)
        {
            LastActivePositionId = null;
            return;
        }

        if (!IsActive)
            throw new InvalidOperationException("An inactive employee cannot select an active position.");

        if (!Positions.Any(x => x.PositionId == positionId.Value && x.EndedAt is null))
            throw new InvalidOperationException("The selected position is not an active employee assignment.");

        LastActivePositionId = positionId;
    }

    public void SetFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));
        FullName = fullName.Trim();
    }

    public void ChangeOrganization(Guid organizationId)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        OrganizationId = organizationId;
    }

    public void Activate() => IsActive = true;

    /// <summary>
    /// Deactivating an employee immediately ends every active position assignment
    /// and clears the last-position preference. The operation is intentionally
    /// idempotent so it also repairs stale state on an already inactive employee.
    /// Reactivation never restores old assignments automatically.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
        LastActivePositionId = null;

        foreach (var position in Positions.Where(x => x.EndedAt is null))
            position.End();
    }
}

public sealed class EmployeePosition
{
    private EmployeePosition()
    {
    }

    public EmployeePosition(Guid employeeId, Guid positionId)
    {
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(employeeId));
        if (positionId == Guid.Empty)
            throw new ArgumentException("Position is required.", nameof(positionId));
        EmployeeId = employeeId;
        PositionId = positionId;
    }

    public Guid EmployeeId
    {
        get; private set;
    }
    public Guid PositionId
    {
        get; private set;
    }
    public Employee Employee { get; private set; } = null!;
    public Position Position { get; private set; } = null!;
    public DateTime? EndedAt
    {
        get; private set;
    }
    public bool IsActive => EndedAt is null;

    public void End() => EndedAt ??= DateTime.UtcNow;

    public void Reactivate()
    {
        EndedAt = null;
    }
}

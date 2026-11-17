namespace YasPortal.Domain.Organization;

// An employee is a person who can sign in. They belong to one organization, may be an administrator, and
// hold zero or more positions through assignments (see EmployeePosition below). Rights come from the
// position they are acting in, not from the person alone, except for administrators.
public sealed class Employee
{
    private Employee()
    {
    }

    // Code, username, full name and organization are all required, so an employee without these can never
    // be created.
    public Employee(string code, string username, string fullName, Guid organizationId, bool isAdmin = false)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Employee code is required.", nameof(code));
        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("Username is required.", nameof(username));
        if (string.IsNullOrWhiteSpace(fullName))
            throw new ArgumentException("Full name is required.", nameof(fullName));
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        Code = code.Trim();
        Username = username.Trim();
        FullName = fullName.Trim();
        OrganizationId = organizationId;
        IsAdmin = isAdmin;
    }

    // Identity details, the organization the employee belongs to, whether they are active and whether they
    // are an administrator.
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = null!;
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
    // The stored password in scrambled (hashed) form; the real password is never kept.
    public string? PasswordHash
    {
        get; private set;
    }

    // Remembers which position the employee was last acting in, so the next sign-in can start there.
    /// <summary>
    /// The position this employee last selected as their active position.
    /// This is a preference; when set, it must refer to an active assignment.
    /// </summary>
    public Guid? LastActivePositionId
    {
        get; private set;
    }

    // The avatar is a short key naming one of the pictures offered on the profile page.
    /// <summary>Key of the predefined avatar the employee picked on their profile; null means the default (initial letter).</summary>
    public string? AvatarKey
    {
        get; private set;
    }

    // The positions held by this employee, including ended ones, so the assignment history is never lost.
    public ICollection<EmployeePosition> Positions { get; private set; } = new List<EmployeePosition>();

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public const int AvatarKeyMaxLength = 40;

    public void SetAvatar(string? avatarKey)
    {
        avatarKey = string.IsNullOrWhiteSpace(avatarKey) ? null : avatarKey.Trim();
        if (avatarKey is { Length: > AvatarKeyMaxLength })
            throw new ArgumentException("Avatar key is too long.", nameof(avatarKey));
        AvatarKey = avatarKey;
    }

    // Only an active employee with a current assignment to that position can pick it as the active
    // position.
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

    // Simple edits: the code, the full name and the organization. Each one refuses an empty value.
    public void ChangeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Employee code is required.", nameof(code));
        Code = code.Trim();
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

// One assignment of an employee to a position. It has a start and, once ended, an end time; ended assignments are kept
// as history and never deleted. An assignment with no end time is the current one.
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

    // Ending an assignment stamps the end time (the first end time is kept); Reactivate makes it current again.
    public void End() => EndedAt ??= DateTime.UtcNow;

    public void Reactivate()
    {
        EndedAt = null;
    }
}

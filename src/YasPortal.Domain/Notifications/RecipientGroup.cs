namespace YasPortal.Domain.Notifications;

/// <summary>
/// A named list of employees one person keeps to send notifications to again later. Private to its owner; the members are
/// <see cref="RecipientGroupMember"/> rows. Not audited (cosmetic, per person).
/// </summary>
public sealed class RecipientGroup
{
    private RecipientGroup()
    {
    }

    public RecipientGroup(Guid ownerEmployeeId, string name)
    {
        if (ownerEmployeeId == Guid.Empty)
            throw new ArgumentException("Owner is required.", nameof(ownerEmployeeId));
        OwnerEmployeeId = ownerEmployeeId;
        Rename(name);
        CreatedAtUtc = UpdatedAtUtc = DateTime.UtcNow;
    }

    /// <summary>The longest group name (also the column size).</summary>
    public const int MaxNameLength = 100;

    /// <summary>Most groups one person can keep.</summary>
    public const int MaxGroupsPerOwner = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OwnerEmployeeId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public void Rename(string name)
    {
        var value = (name ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new ArgumentException("Group name is required.", nameof(name));
        if (value.Length > MaxNameLength)
            throw new ArgumentException($"Group name is longer than {MaxNameLength} characters.", nameof(name));
        Name = value;
    }

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}

/// <summary>One employee in a <see cref="RecipientGroup"/>.</summary>
public sealed class RecipientGroupMember
{
    private RecipientGroupMember()
    {
    }

    public RecipientGroupMember(Guid groupId, Guid employeeId)
    {
        if (groupId == Guid.Empty)
            throw new ArgumentException("Group is required.", nameof(groupId));
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(employeeId));
        GroupId = groupId;
        EmployeeId = employeeId;
    }

    public Guid GroupId { get; private set; }
    public Guid EmployeeId { get; private set; }
}

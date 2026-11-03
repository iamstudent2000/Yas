namespace YasPortal.Domain.PhoneBook;

/// <summary>A group of phone-book contacts (for example a department or a site). Groups are one level; they are not users or organizations.</summary>
public sealed class ContactGroup
{
    public const int NameMax = 100;

    private ContactGroup()
    {
    }

    public ContactGroup(string name, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(name, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public int SortOrder { get; private set; }
    /// <summary>A group that is off is hidden from the phone book together with its contacts.</summary>
    public bool IsActive { get; private set; } = true;

    public void Update(string name, int sortOrder)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > NameMax)
            throw new ArgumentException($"Group name is required and at most {NameMax} characters.", nameof(name));
        Name = name;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

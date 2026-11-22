namespace YasPortal.Domain.Guides;

// A guide group is a heading that gathers related guides on the guides page (for example «درخواست‌ها» or «فروشگاه»).
// Administrators keep the list of groups (add, rename, order, switch on or off), the same way the store keeps its item groups.
/// <summary>
/// A group of guides. Its name is unique; <see cref="SortOrder"/> decides the order of the headings on the guides page.
/// A group that is switched off is hidden from readers together with its guides, but nothing is deleted.
/// </summary>
public sealed class UserGuideGroup
{
    /// <summary>The longest name a group may have.</summary>
    public const int NameMax = 100;

    private UserGuideGroup()
    {
    }

    public UserGuideGroup(string name, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(name, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Used both when creating and when renaming. The name is cleaned (see NormalizeName) and must not be empty or too long.
    public void Update(string name, int sortOrder)
    {
        var clean = NormalizeName(name);
        if (clean is null || clean.Length > NameMax)
            throw new ArgumentException($"Name is required and at most {NameMax} characters.", nameof(name));
        Name = clean;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Cleans a typed group name so the same group is never split in two by a typing difference: spaces at the ends are removed,
    /// runs of spaces become one, and the Arabic forms of «ی» and «ک» become the Persian ones. Blank becomes null (no name).
    /// </summary>
    public static string? NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var text = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Replace('ي', 'ی').Replace('ك', 'ک');
    }
}

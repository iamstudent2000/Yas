using YasPortal.Domain.Guides;

namespace YasPortal.Domain.Gallery;

// A gallery group is a heading that gathers related pictures on the gallery page (for example «مراسم» or «محل کار»).
// Administrators keep the list of groups (add, rename, order, switch on or off), the same way as the groups of the guides.
/// <summary>
/// A group of gallery pictures. Its name is unique; <see cref="SortOrder"/> decides the order of the headings on the gallery page.
/// A group that is switched off is hidden from visitors together with its pictures, but nothing is deleted.
/// Names are cleaned and compared with the same rules as guide group names.
/// </summary>
public sealed class GalleryGroup
{
    /// <summary>The longest name a group may have.</summary>
    public const int NameMax = UserGuideGroup.NameMax;

    private GalleryGroup()
    {
    }

    public GalleryGroup(string name, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(name, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Used both when creating and when renaming. The name is cleaned and must not be empty or too long.
    public void Update(string name, int sortOrder)
    {
        var clean = UserGuideGroup.NormalizeName(name);
        if (clean is null || clean.Length > NameMax)
            throw new ArgumentException($"Name is required and at most {NameMax} characters.", nameof(name));
        Name = clean;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}

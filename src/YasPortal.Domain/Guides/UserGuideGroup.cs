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

    /// <summary>
    /// The form of a name used only to decide whether two names are the same group. It ignores everything a person cannot see or does not mean:
    /// letter case, Persian / Arabic / Latin digits (۳ ٣ 3), the half-space and other invisible marks, tatweel, and vowel marks.
    /// So «۳۳۳» and «333», or «درخواست‌ها» and «درخواستها», are the same group. The name itself is stored as it was typed (after <see cref="NormalizeName"/>).
    /// </summary>
    public static string CompareKey(string? name)
    {
        var text = NormalizeName(name) ?? "";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (ch is >= '\u06F0' and <= '\u06F9') sb.Append((char)('0' + (ch - '\u06F0')));          // Persian digits
            else if (ch is >= '\u0660' and <= '\u0669') sb.Append((char)('0' + (ch - '\u0660')));     // Arabic-Indic digits
            else if (ch is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\uFEFF' or '\u00AD' or '\u0640') continue;   // invisible marks and tatweel
            else if (ch is >= '\u064B' and <= '\u065F') continue;                                       // vowel marks
            else sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}

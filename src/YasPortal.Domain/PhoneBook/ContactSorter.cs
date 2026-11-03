using System.Globalization;
using System.Text.RegularExpressions;

namespace YasPortal.Domain.PhoneBook;

/// <summary>The columns a group's contact table can be sorted by. <see cref="Default"/> is the order the administrator set.</summary>
public enum ContactSortKey { Default, Name, JobTitle, Extension, Phone, Mobile, Email }

/// <summary>Sorts the contacts of one group for the table. A contact with nothing in the chosen column is always last, in either direction.</summary>
public static partial class ContactSorter
{
    private static readonly StringComparer Comparer = CreateComparer();

    public static IReadOnlyList<Contact> Sort(IEnumerable<Contact> contacts, ContactSortKey key, bool descending)
    {
        var list = contacts.ToList();
        if (key == ContactSortKey.Default)
            return list;

        var filled = list.Where(c => !string.IsNullOrWhiteSpace(Value(c, key))).ToList();
        var empty = list.Where(c => string.IsNullOrWhiteSpace(Value(c, key)));
        // OrderBy is stable, so contacts with the same value stay in the administrator's order
        var ordered = descending
            ? filled.OrderByDescending(c => SortText(Value(c, key)!), Comparer)
            : filled.OrderBy(c => SortText(Value(c, key)!), Comparer);
        return ordered.Concat(empty).ToList();
    }

    /// <summary>Parses a column name (as the table header sends it); anything unknown is <see cref="ContactSortKey.Default"/>.</summary>
    public static ContactSortKey Parse(string? column) =>
        Enum.TryParse<ContactSortKey>(column, ignoreCase: true, out var key) && Enum.IsDefined(key) ? key : ContactSortKey.Default;

    private static string? Value(Contact c, ContactSortKey key) => key switch
    {
        ContactSortKey.Name => c.FullName,
        ContactSortKey.JobTitle => c.JobTitle,
        ContactSortKey.Extension => c.Extension,
        ContactSortKey.Phone => c.Phone,
        ContactSortKey.Mobile => c.Mobile,
        ContactSortKey.Email => c.Email,
        _ => null,
    };

    // Numbers are compared as numbers (9 before 10), so every run of digits is padded to the same length first.
    private static string SortText(string value) =>
        DigitRun().Replace(PhoneBookText.Normalize(value).Trim(), m => m.Value.PadLeft(12, '0'));

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitRun();

    private static StringComparer CreateComparer()
    {
        try
        {
            return StringComparer.Create(CultureInfo.GetCultureInfo("fa-IR"), ignoreCase: true);
        }
        catch (CultureNotFoundException)
        {
            return StringComparer.CurrentCultureIgnoreCase;
        }
    }
}

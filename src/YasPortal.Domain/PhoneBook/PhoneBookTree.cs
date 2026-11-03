namespace YasPortal.Domain.PhoneBook;

/// <summary>One group with its contacts, as shown in the phone book.</summary>
public sealed record PhoneBookGroup(ContactGroup Group, IReadOnlyList<Contact> Contacts);

/// <summary>The phone book as a list of groups, each with its contacts; shared by the public page and the admin page.</summary>
public sealed class PhoneBookTree
{
    public IReadOnlyList<PhoneBookGroup> Groups { get; init; } = [];

    public int TotalContacts => Groups.Sum(x => x.Contacts.Count);
    public bool IsEmpty => Groups.Count == 0;

    /// <summary>
    /// Builds the tree. Without <paramref name="includeInactive"/> a group that is off, a contact that is off, and every contact
    /// of a group that is off are left out. A contact whose group does not exist is left out.
    /// </summary>
    public static PhoneBookTree Build(IEnumerable<ContactGroup> groups, IEnumerable<Contact> contacts, bool includeInactive = false)
    {
        var byGroup = contacts.Where(x => includeInactive || x.IsActive).ToLookup(x => x.GroupId);
        var list = groups.Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name, StringComparer.CurrentCulture)
            .Select(g => new PhoneBookGroup(g, byGroup[g.Id].OrderBy(x => x.SortOrder).ThenBy(x => x.FullName, StringComparer.CurrentCulture).ToList()))
            .ToList();
        return new PhoneBookTree { Groups = list };
    }

    /// <summary>
    /// The general search. A contact matches when every word of the query is found in its name, job title, numbers, email, note
    /// or its group name. A group stays when its name matches (all its contacts) or when at least one contact matches.
    /// An empty query returns everything.
    /// </summary>
    public PhoneBookTree Filter(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return this;
        var result = new List<PhoneBookGroup>();
        foreach (var entry in Groups)
        {
            if (PhoneBookText.ContainsAllWords(entry.Group.Name, query))
            {
                result.Add(entry);
                continue;
            }
            var hits = FilterContacts(entry.Contacts, query, entry.Group.Name);
            if (hits.Count > 0)
                result.Add(entry with { Contacts = hits });
        }
        return new PhoneBookTree { Groups = result };
    }

    /// <summary>The search inside one group: the contacts whose own details match every word of the query.</summary>
    public static IReadOnlyList<Contact> FilterContacts(IEnumerable<Contact> contacts, string? query, string? groupName = null)
    {
        var list = contacts.ToList();
        if (string.IsNullOrWhiteSpace(query))
            return list;
        return list.Where(c => PhoneBookText.ContainsAllWords(SearchText(c, groupName), query)).ToList();
    }

    public static string SearchText(Contact c, string? groupName = null) =>
        string.Join(' ', new[] { c.FullName, c.JobTitle, c.Extension, c.Phone, c.Mobile, c.Email, c.Note, groupName }.Where(x => !string.IsNullOrEmpty(x)));
}

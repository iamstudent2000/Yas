namespace YasPortal.Domain.PhoneBook;

// The phone book as the pages show it: groups in order, each with its contacts. This builder hides what is
// switched off and supports searching. The public page and the admin page share it.
/// <summary>One group with its contacts, as shown in the phone book.</summary>
public sealed record PhoneBookGroup(ContactGroup Group, IReadOnlyList<Contact> Contacts);

/// <summary>The phone book as a list of groups, each with its contacts; shared by the public page and the admin page.</summary>
public sealed class PhoneBookTree
{
    public IReadOnlyList<PhoneBookGroup> Groups { get; init; } = [];

    public int TotalContacts => Groups.Sum(x => x.Contacts.Count);
    public bool IsEmpty => Groups.Count == 0;

    // Switched-off groups and contacts are hidden unless the caller (the admin page) asks to include them.
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

    // Search: the group name matches, or one of its contacts matches. Groups with no match disappear.
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

    /// <summary>
    /// The admin filters. <paramref name="groupId"/> keeps one group only. <paramref name="active"/> keeps what is on (true) or off (false):
    /// a contact counts as on only when both the contact and its group are on, so everything inside a switched-off group counts as off.
    /// A group stays when it has contacts that match, or when it has no contacts at all and itself matches. Empty filters return everything.
    /// </summary>
    public PhoneBookTree Restrict(Guid? groupId, bool? active)
    {
        if (groupId is null && active is null)
            return this;
        var result = new List<PhoneBookGroup>();
        foreach (var entry in Groups)
        {
            if (groupId is Guid wanted && entry.Group.Id != wanted)
                continue;
            if (active is not bool on)
            {
                result.Add(entry);
                continue;
            }
            var hits = entry.Contacts.Where(c => (entry.Group.IsActive && c.IsActive) == on).ToList();
            if (hits.Count > 0 || (entry.Contacts.Count == 0 && entry.Group.IsActive == on))
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

    // All the searchable text of one contact joined together, including the group name.
    public static string SearchText(Contact c, string? groupName = null) =>
        string.Join(' ', new[] { c.FullName, c.JobTitle, c.Extension, c.Phone, c.Mobile, c.Email, c.Note, groupName }.Where(x => !string.IsNullOrEmpty(x)));
}

namespace YasPortal.Domain.Landing;

/// <summary>One group of the home page with its visible links and visible sub-groups.</summary>
public sealed class LinkGroupNode
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public List<PortalLink> Links { get; init; } = [];
    public List<LinkGroupNode> Children { get; init; } = [];
    /// <summary>All links in this group and below.</summary>
    public int TotalLinks => Links.Count + Children.Sum(x => x.TotalLinks);
}

/// <summary>What the home page shows: the quick-access links, the group tree, and the links that are in no group.</summary>
public sealed class LinkTree
{
    public IReadOnlyList<PortalLink> QuickAccess { get; init; } = [];
    public IReadOnlyList<LinkGroupNode> Groups { get; init; } = [];
    public IReadOnlyList<PortalLink> Ungrouped { get; init; } = [];
    /// <summary>
    /// Only what matches <paramref name="query"/> (title, address, description of a link, or the name of its group; a matching group keeps
    /// everything inside it). Quick access is left as it is. A blank query returns the tree unchanged.
    /// </summary>
    public LinkTree Filter(string? query)
    {
        query = (query ?? "").Trim();
        if (query.Length == 0)
            return this;
        bool Hit(string? text) => text?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false;
        bool LinkHit(PortalLink l) => Hit(l.Title) || Hit(l.Description) || Hit(l.Url);

        LinkGroupNode? Keep(LinkGroupNode node)
        {
            if (Hit(node.Name))
                return node;
            var children = node.Children.Select(Keep).Where(x => x is not null).Select(x => x!).ToList();
            var links = node.Links.Where(LinkHit).ToList();
            return children.Count == 0 && links.Count == 0 ? null : new LinkGroupNode { Id = node.Id, Name = node.Name, Links = links, Children = children };
        }

        return new LinkTree
        {
            QuickAccess = QuickAccess,
            Groups = Groups.Select(Keep).Where(x => x is not null).Select(x => x!).ToList(),
            Ungrouped = Ungrouped.Where(LinkHit).ToList(),
        };
    }

    /// <summary>How many links the group tree and the ungrouped list hold.</summary>
    public int TotalLinks => Groups.Sum(x => x.TotalLinks) + Ungrouped.Count;

    public bool IsEmpty => QuickAccess.Count == 0 && Groups.Count == 0 && Ungrouped.Count == 0;

    /// <summary>
    /// Builds the page from everything stored. Inactive links and inactive groups are left out; everything below an inactive group is
    /// hidden with it; a link whose group is gone or hidden is hidden; groups with nothing visible inside are left out.
    /// </summary>
    public static LinkTree Build(IEnumerable<LinkGroup> groups, IEnumerable<PortalLink> links)
    {
        var allGroups = groups.ToList();
        var byId = allGroups.ToDictionary(x => x.Id);
        var visible = new HashSet<Guid>();
        foreach (var group in allGroups)
        {
            // visible only when it and every ancestor is active (and the chain ends at a top group, not in a loop)
            var ok = true;
            Guid? current = group.Id;
            var seen = new HashSet<Guid>();
            while (current is Guid id)
            {
                if (!byId.TryGetValue(id, out var g) || !g.IsActive || !seen.Add(id)) { ok = false; break; }
                current = g.ParentId;
            }
            if (ok) visible.Add(group.Id);
        }

        var shownLinks = links.Where(x => x.IsActive && (x.GroupId is null || visible.Contains(x.GroupId.Value))).ToList();

        LinkGroupNode Node(LinkGroup group) => new()
        {
            Id = group.Id,
            Name = group.Name,
            Links = shownLinks.Where(x => x.GroupId == group.Id).OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToList(),
            Children = allGroups.Where(x => x.ParentId == group.Id && visible.Contains(x.Id)).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(Node).Where(n => n.TotalLinks > 0).ToList(),
        };

        // A top group is one without a parent (or whose parent no longer exists).
        var top = allGroups.Where(x => visible.Contains(x.Id) && (x.ParentId is null || !byId.ContainsKey(x.ParentId.Value)))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(Node).Where(n => n.TotalLinks > 0).ToList();

        return new LinkTree
        {
            QuickAccess = shownLinks.Where(x => x.IsQuickAccess).OrderBy(x => x.QuickOrder).ThenBy(x => x.Title).ToList(),
            Groups = top,
            Ungrouped = shownLinks.Where(x => x.GroupId is null).OrderBy(x => x.SortOrder).ThenBy(x => x.Title).ToList(),
        };
    }
}

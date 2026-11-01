namespace YasPortal.Domain.Landing;

/// <summary>A group of portal links, optionally inside another group (a tree up to <see cref="MaxDepth"/> levels deep).</summary>
public sealed class LinkGroup
{
    public const int NameMax = 100;
    public const int MaxDepth = 3;

    private LinkGroup()
    {
    }

    public LinkGroup(string name, Guid? parentId, int sortOrder)
    {
        Id = Guid.NewGuid();
        Update(name, parentId, sortOrder);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public Guid? ParentId { get; private set; }
    public int SortOrder { get; private set; }
    /// <summary>A group that is off is hidden from the home page together with everything inside it.</summary>
    public bool IsActive { get; private set; } = true;

    public void Update(string name, Guid? parentId, int sortOrder)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || name.Length > NameMax)
            throw new ArgumentException($"Group name is required and at most {NameMax} characters.", nameof(name));
        if (parentId == Id && Id != Guid.Empty)
            throw new ArgumentException("A group cannot be inside itself.", nameof(parentId));
        Name = name;
        ParentId = parentId;
        SortOrder = sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;

    /// <summary>Levels from the top (a top group is 1). Stops at a missing parent or a loop.</summary>
    public static int DepthOf(Guid groupId, IReadOnlyDictionary<Guid, LinkGroup> all)
    {
        var depth = 0;
        Guid? current = groupId;
        var seen = new HashSet<Guid>();
        while (current is Guid id && all.TryGetValue(id, out var group) && seen.Add(id))
        {
            depth++;
            current = group.ParentId;
        }
        return depth;
    }

    /// <summary>The group and everything below it.</summary>
    public static HashSet<Guid> SubtreeOf(Guid groupId, IEnumerable<LinkGroup> all)
    {
        var list = all.ToList();
        var result = new HashSet<Guid> { groupId };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var group in list)
                if (group.ParentId is Guid parent && result.Contains(parent) && result.Add(group.Id))
                    added = true;
        }
        return result;
    }

    /// <summary>
    /// A Persian reason <paramref name="groupId"/> cannot be put under <paramref name="parentId"/>, or null when it can:
    /// not inside itself or its own subtree, and the whole branch must fit in <see cref="MaxDepth"/> levels.
    /// </summary>
    public static string? CheckParent(Guid groupId, Guid? parentId, IReadOnlyCollection<LinkGroup> all)
    {
        if (parentId is not Guid parent)
            return null;
        var byId = all.ToDictionary(x => x.Id);
        if (!byId.ContainsKey(parent))
            return "گروه والد پیدا نشد.";
        if (SubtreeOf(groupId, all).Contains(parent))
            return "یک گروه را نمی‌توان داخل خودش یا زیرگروه‌هایش گذاشت.";
        var below = byId.ContainsKey(groupId) ? SubtreeHeight(groupId, all) : 1;
        if (DepthOf(parent, byId) + below > MaxDepth)
            return $"گروه‌ها حداکثر {MaxDepth} سطح می‌توانند تو در تو باشند.";
        return null;
    }

    // How many levels the group and its descendants take (a group with no children is 1).
    private static int SubtreeHeight(Guid groupId, IEnumerable<LinkGroup> all)
    {
        var list = all.ToList();
        int Height(Guid id, int guard) => guard > 20 ? 1 : 1 + list.Where(x => x.ParentId == id).Select(x => Height(x.Id, guard + 1)).DefaultIfEmpty(0).Max();
        return Height(groupId, 0);
    }
}

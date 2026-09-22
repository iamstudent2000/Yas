namespace YasPortal.Domain.Organization;

/// <summary>
/// Pure helper for walking the Position parent/child tree (Position.ParentPositionId
/// points at the higher-up "manager" position). Used to resolve, e.g., "the requester's
/// direct manager's position" or "their manager's manager's position" when routing a
/// workflow step, without hard-coding a specific position.
/// </summary>
public static class PositionHierarchy
{
    /// <summary>
    /// Walks up from <paramref name="positionId"/> by <paramref name="levelsUp"/> parent hops
    /// (1 = the position's own direct parent/"manager" position). Returns null if the chain
    /// reaches the root before that many hops, i.e. there is no such ancestor.
    /// </summary>
    public static Guid? GetAncestorPositionId(
        Guid positionId,
        int levelsUp,
        IReadOnlyDictionary<Guid, Guid?> parentByPosition)
    {
        if (levelsUp <= 0)
            throw new ArgumentOutOfRangeException(nameof(levelsUp), "Levels up must be at least 1.");

        var current = positionId;
        for (var i = 0; i < levelsUp; i++)
        {
            if (!parentByPosition.TryGetValue(current, out var parent) || parent is not Guid parentId)
                return null;
            current = parentId;
        }
        return current;
    }

    /// <summary>
    /// Like <see cref="GetAncestorPositionId"/>, but never fails outright on a shallow
    /// hierarchy: it walks up at most <paramref name="levelsUp"/> hops and returns whatever
    /// ancestor it reached — the requested level if the chain is long enough, otherwise the
    /// highest one actually available. Returns null only when there is no ancestor at all
    /// (i.e. <paramref name="positionId"/> is itself already at the top).
    /// </summary>
    public static Guid? GetClosestAncestorPositionId(
        Guid positionId,
        int levelsUp,
        IReadOnlyDictionary<Guid, Guid?> parentByPosition)
    {
        if (levelsUp <= 0)
            throw new ArgumentOutOfRangeException(nameof(levelsUp), "Levels up must be at least 1.");

        var current = positionId;
        Guid? lastAncestor = null;
        for (var i = 0; i < levelsUp; i++)
        {
            if (!parentByPosition.TryGetValue(current, out var parent) || parent is not Guid parentId)
                break;
            current = parentId;
            lastAncestor = parentId;
        }
        return lastAncestor;
    }
}

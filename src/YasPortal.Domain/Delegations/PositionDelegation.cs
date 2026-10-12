namespace YasPortal.Domain.Delegations;

/// <summary>Where a delegation stands on a given day.</summary>
public enum DelegationStatus
{
    Upcoming,
    Active,
    Expired,
    Revoked,
}

/// <summary>
/// A temporary hand-over of one position's approval authority to another employee (spec §15, §16).
/// Authority belongs to the <b>position</b> (§1.2): whoever holds the delegate position acts on steps assigned to that
/// position, between <see cref="StartDate"/> and <see cref="EndDate"/> (both days included), and every
/// action they take records who they acted for (§15.2).
///
/// Two kinds exist. A normal delegation is made by the position's holder
/// (<see cref="DelegatorEmployeeId"/>) and follows the step's own setting: it works only on steps
/// that allow delegation, and only while that person still holds the position. An
/// <see cref="ArrangedByAdmin"/> delegation is the SuperAdmin's temporary replacement (§16.2): it
/// works on every step of the position, including steps that forbid delegation, and does not depend
/// on who holds the position. Delegations are never deleted — they can only be revoked (§1.1).
/// A delegate cannot pass authority on: only the holder or the SuperAdmin creates delegations.
/// </summary>
public sealed class PositionDelegation
{
    private PositionDelegation()
    {
    }

    public PositionDelegation(
        Guid positionId,
        Guid? delegatorEmployeeId,
        Guid delegatePositionId,
        DateTime startDate,
        DateTime endDate,
        Guid createdByEmployeeId,
        bool arrangedByAdmin)
    {
        if (positionId == Guid.Empty)
            throw new ArgumentException("Position is required.", nameof(positionId));
        if (delegatePositionId == Guid.Empty)
            throw new ArgumentException("Delegate position is required.", nameof(delegatePositionId));
        if (createdByEmployeeId == Guid.Empty)
            throw new ArgumentException("Creator is required.", nameof(createdByEmployeeId));
        if (!arrangedByAdmin && (delegatorEmployeeId is null || delegatorEmployeeId == Guid.Empty))
            throw new ArgumentException("A delegation made by a position holder needs that holder.", nameof(delegatorEmployeeId));
        if (delegatePositionId == positionId)
            throw new ArgumentException("A position cannot be delegated to itself.", nameof(delegatePositionId));
        if (endDate.Date < startDate.Date)
            throw new ArgumentException("The end date cannot be before the start date.", nameof(endDate));

        PositionId = positionId;
        DelegatorEmployeeId = delegatorEmployeeId == Guid.Empty ? null : delegatorEmployeeId;
        DelegatePositionId = delegatePositionId;
        StartDate = startDate.Date;
        EndDate = endDate.Date;
        CreatedByEmployeeId = createdByEmployeeId;
        ArrangedByAdmin = arrangedByAdmin;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PositionId { get; private set; }

    /// <summary>The employee holding the position when it was delegated; null only for an admin arrangement on a vacant position.</summary>
    public Guid? DelegatorEmployeeId { get; private set; }
    /// <summary>The position that receives the authority: whoever holds it during the dates may act (§1.2: authority belongs to positions, not people).</summary>
    public Guid DelegatePositionId { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime EndDate { get; private set; }
    public Guid CreatedByEmployeeId { get; private set; }
    public bool ArrangedByAdmin { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? RevokedByEmployeeId { get; private set; }

    public void Revoke(Guid byEmployeeId)
    {
        if (byEmployeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(byEmployeeId));
        if (RevokedAtUtc is not null)
            throw new InvalidOperationException("This delegation was already revoked.");
        RevokedAtUtc = DateTime.UtcNow;
        RevokedByEmployeeId = byEmployeeId;
    }

    /// <summary>True when the delegation is not revoked and <paramref name="today"/> falls inside its dates.</summary>
    public bool IsInEffectOn(DateTime today) => RevokedAtUtc is null && today.Date >= StartDate && today.Date <= EndDate;

    /// <summary>Whether this delegation can be used on a step: admin arrangements always, normal ones only if the step allows delegation.</summary>
    public bool AppliesToStep(bool stepAllowsDelegation) => ArrangedByAdmin || stepAllowsDelegation;

    public DelegationStatus StatusOn(DateTime today)
    {
        if (RevokedAtUtc is not null)
            return DelegationStatus.Revoked;
        if (today.Date < StartDate)
            return DelegationStatus.Upcoming;
        return today.Date > EndDate ? DelegationStatus.Expired : DelegationStatus.Active;
    }
}

namespace YasPortal.Domain.Organization;

/// <summary>
/// Immutable record of an employee occupying a position during a period of time.
/// The current EmployeePosition row remains the fast current-state relationship;
/// this entity preserves every assignment interval, including repeated assignments.
/// </summary>
public sealed class PositionAssignmentHistory
{
    private PositionAssignmentHistory() { }

    public PositionAssignmentHistory(Guid employeeId, Guid positionId, DateTime? startedAt = null)
    {
        if (employeeId == Guid.Empty) throw new ArgumentException("Employee is required.", nameof(employeeId));
        if (positionId == Guid.Empty) throw new ArgumentException("Position is required.", nameof(positionId));

        Id = Guid.NewGuid();
        EmployeeId = employeeId;
        PositionId = positionId;
        StartedAt = startedAt ?? DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid PositionId { get; private set; }
    public DateTime StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    public Employee Employee { get; private set; } = null!;
    public Position Position { get; private set; } = null!;

    public bool IsActive => EndedAt is null;

    public void End(DateTime? endedAt = null)
    {
        if (EndedAt is not null)
            return;

        var value = endedAt ?? DateTime.UtcNow;
        if (value < StartedAt)
            throw new InvalidOperationException("Assignment end time cannot be before its start time.");

        EndedAt = value;
    }
}

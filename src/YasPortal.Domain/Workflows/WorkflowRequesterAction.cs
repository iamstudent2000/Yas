namespace YasPortal.Domain.Workflows;

/// <summary>
/// A requester-side lifecycle event on a <see cref="WorkflowRequest"/> (submit, resubmit, cancel).
/// Stored alongside approval steps so the timeline can show a complete history without inventing
/// UI-only rows.
/// </summary>
public sealed class WorkflowRequesterAction
{
    private WorkflowRequesterAction()
    {
    }

    internal WorkflowRequesterAction(Guid requestId, int round, WorkflowRequesterActionKind kind, Guid employeeId)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request is required.", nameof(requestId));
        if (round < 1)
            throw new ArgumentOutOfRangeException(nameof(round), "Round must start at 1.");
        if (employeeId == Guid.Empty)
            throw new ArgumentException("Employee is required.", nameof(employeeId));

        RequestId = requestId;
        Round = round;
        Kind = kind;
        EmployeeId = employeeId;
        AtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid RequestId { get; private set; }
    public int Round { get; private set; }
    public WorkflowRequesterActionKind Kind { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateTime AtUtc { get; private set; }
}

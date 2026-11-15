namespace YasPortal.Domain.Auditing;

// One line of the audit trail: who (employee and the position they were acting in), what happened, to which object,
// when, and for data changes the values before and after (stored as JSON text). Entries are only ever added, never edited
// or deleted; the only change allowed is stamping an archive date. Times are stored in UTC (universal time).
public sealed class AuditEntry
{
    private AuditEntry()
    {
    }

    public AuditEntry(
        Guid? employeeId,
        Guid? activePositionId,
        string action,
        string entityType,
        string? entityId,
        string? beforeJson,
        string? afterJson,
        DateTime occurredAtUtc)
    {
        Id = Guid.NewGuid();
        EmployeeId = employeeId;
        ActivePositionId = activePositionId;
        Action = action;
        EntityType = entityType;
        EntityId = entityId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        OccurredAtUtc = occurredAtUtc;
    }

    // Who did it: the employee and the position that was active at that moment. Empty when nobody was signed in.
    public Guid Id
    {
        get; private set;
    }
    public Guid? EmployeeId
    {
        get; private set;
    }
    public Guid? ActivePositionId
    {
        get; private set;
    }
    // What happened (see the list of action names) and what it happened to.
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId
    {
        get; private set;
    }
    // For data changes: the object's values before and after the change, written as JSON text.
    public string? BeforeJson
    {
        get; private set;
    }
    public string? AfterJson
    {
        get; private set;
    }
    public DateTime OccurredAtUtc
    {
        get; private set;
    }

    /// <summary>When the row was moved to the archive; null while it is a current event. Archived rows are kept and can still be searched.</summary>
    public DateTime? ArchivedAtUtc
    {
        get; private set;
    }

    /// <summary>Moves the row to the archive. Archiving twice keeps the first time.</summary>
    public void Archive(DateTime atUtc) => ArchivedAtUtc ??= atUtc;
}

namespace YasPortal.Domain.Auditing;

public sealed class AuditEntry
{
    private AuditEntry() { }

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

    public Guid Id { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public Guid? ActivePositionId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string EntityType { get; private set; } = string.Empty;
    public string? EntityId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
}

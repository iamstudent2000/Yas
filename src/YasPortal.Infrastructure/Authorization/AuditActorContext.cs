namespace YasPortal.Infrastructure.Authorization;

public sealed class AuditActorContext
{
    private static readonly AsyncLocal<AuditActor?> Current = new();

    public AuditActor? Actor => Current.Value;

    public void Set(Guid? employeeId, Guid? activePositionId, bool isAdmin)
        => Current.Value = new AuditActor(employeeId, activePositionId, isAdmin);
}

public sealed record AuditActor(Guid? EmployeeId, Guid? ActivePositionId, bool IsAdmin);

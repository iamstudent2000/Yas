using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace YasPortal.Infrastructure.Authorization;

public sealed class AuditActorContext(IHttpContextAccessor? httpContextAccessor = null)
{
    private static readonly AsyncLocal<AuditActor?> Current = new();
    private readonly IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;

    public AuditActor? Actor
    {
        get {
            var actor = Current.Value;
            if (actor is not null)
                return actor;

            var user = _httpContextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var employeeId = TryGuid(user.FindFirstValue(ClaimTypes.NameIdentifier));
            var activePositionId = TryGuid(user.FindFirstValue(AuthClaimNames.ActivePositionId));
            var isAdmin = bool.TryParse(user.FindFirstValue(AuthClaimNames.IsAdmin), out var admin) && admin;

            if (employeeId is null && !isAdmin)
                return null;

            actor = new AuditActor(employeeId, activePositionId, isAdmin);
            Current.Value = actor;
            return actor;
        }
    }

    public void Set(Guid? employeeId, Guid? activePositionId, bool isAdmin)
        => Current.Value = new AuditActor(employeeId, activePositionId, isAdmin);

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}

public sealed record AuditActor(Guid? EmployeeId, Guid? ActivePositionId, bool IsAdmin);

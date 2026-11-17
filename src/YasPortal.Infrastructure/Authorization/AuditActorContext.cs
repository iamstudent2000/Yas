using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace YasPortal.Infrastructure.Authorization;

// Remembers WHO is performing the current action, so every audit row can say which person (and which position) did it.
// The answer is kept per running action. If nothing was stored explicitly, it is read from the signed-in cookie.
public sealed class AuditActorContext(IHttpContextAccessor? httpContextAccessor = null)
{
    // One value per running operation (not shared between users), so two people working at the same time never mix up.
    private static readonly AsyncLocal<AuditActor?> Current = new();
    private readonly IHttpContextAccessor? _httpContextAccessor = httpContextAccessor;

    // The current actor: the one stored explicitly if any, otherwise built from the sign-in cookie, otherwise nobody.
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

    // Stores the actor explicitly (called once the signed-in user has been identified).
    public void Set(Guid? employeeId, Guid? activePositionId, bool isAdmin)
        => Current.Value = new AuditActor(employeeId, activePositionId, isAdmin);

    private static Guid? TryGuid(string? value) =>
        Guid.TryParse(value, out var id) ? id : null;
}

// The person, the position they act from, and whether they are an administrator (admins act without a position).
public sealed record AuditActor(Guid? EmployeeId, Guid? ActivePositionId, bool IsAdmin);

using System.Security.Claims;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

// The signed-in person as the rest of the application sees it: id, active position and admin flag.
// It is filled from the sign-in cookie and also tells the audit trail who is acting.
public sealed class CurrentUser(AuditActorContext auditActorContext) : ICurrentUser
{
    public Guid? EmployeeId
    {
        get; private set;
    }
    public Guid? ActivePositionId
    {
        get; private set;
    }
    public bool IsAdmin
    {
        get; private set;
    }

    // Reads the person's details from the cookie and shares them with the audit trail.
    public void SetPrincipal(ClaimsPrincipal principal)
    {
        EmployeeId = TryGuid(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        ActivePositionId = TryGuid(principal.FindFirstValue(AuthClaimNames.ActivePositionId));
        IsAdmin = bool.TryParse(principal.FindFirstValue(AuthClaimNames.IsAdmin), out var isAdmin) && isAdmin;
        auditActorContext.Set(EmployeeId, ActivePositionId, IsAdmin);
    }

    private static Guid? TryGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}

// Names of the values stored inside the sign-in cookie (see the Authorization model in the README).
public static class AuthClaimNames
{
    public const string ActivePositionId = "yas_active_position_id";
    public const string IsAdmin = "yas_is_admin";
    public const string Permission = "yas_permission";
}

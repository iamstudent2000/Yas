using System.Security.Claims;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

public sealed class CurrentUser : ICurrentUser
{
    public Guid? EmployeeId { get; private set; }
    public Guid? ActivePositionId { get; private set; }
    public bool IsAdmin { get; private set; }

    public void SetPrincipal(ClaimsPrincipal principal)
    {
        EmployeeId = TryGuid(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        ActivePositionId = TryGuid(principal.FindFirstValue(AuthClaimNames.ActivePositionId));
        IsAdmin = bool.TryParse(principal.FindFirstValue(AuthClaimNames.IsAdmin), out var isAdmin) && isAdmin;
    }

    private static Guid? TryGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
}

public static class AuthClaimNames
{
    public const string ActivePositionId = "yas_active_position_id";
    public const string IsAdmin = "yas_is_admin";
}

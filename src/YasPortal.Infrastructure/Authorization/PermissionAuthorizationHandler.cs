using Microsoft.AspNetCore.Authorization;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler(IPermissionChecker permissionChecker) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        if (!bool.TryParse(context.User.FindFirst(AuthClaimNames.IsAdmin)?.Value, out var isAdmin) || !isAdmin)
            return;

        if (await permissionChecker.HasPermissionAsync(requirement.Code))
            context.Succeed(requirement);
    }
}

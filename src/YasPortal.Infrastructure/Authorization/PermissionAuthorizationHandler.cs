using Microsoft.AspNetCore.Authorization;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler(PermissionChecker permissionChecker) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        if (await permissionChecker.HasPermissionAsync(context.User, requirement.Code))
            context.Succeed(requirement);
    }
}

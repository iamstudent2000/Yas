using Microsoft.AspNetCore.Authorization;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

// Connects the page-protection rules of the web site to the permission checker: a page that requires a permission
// code is opened only for a signed-in person whose cookie contains that code.
public sealed class PermissionAuthorizationHandler(PermissionChecker permissionChecker) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        // Not signed in means no access; otherwise ask the permission checker.
        if (await permissionChecker.HasPermissionAsync(context.User, requirement.Code))
            context.Succeed(requirement);
    }
}

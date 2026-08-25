using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

public sealed class PermissionChecker(
    AuthenticationStateProvider authenticationStateProvider,
    CurrentUser? currentUser = null) : IPermissionChecker
{
    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return await HasPermissionAsync(user, permissionCode, cancellationToken);
    }

    public Task<bool> HasPermissionAsync(
        ClaimsPrincipal user,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        currentUser?.SetPrincipal(user);

        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(permissionCode))
            return Task.FromResult(false);

        // Authorization uses the permission snapshot captured when the user
        // authenticated or changed the active position. The snapshot is stored
        // as repeated claims, so a normal user can never inherit permissions
        // from another position merely because they are assigned to that user.
        var hasPermission = user.FindAll(AuthClaimNames.Permission)
            .Any(claim => string.Equals(claim.Value, permissionCode, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(hasPermission);
    }

    public void Invalidate()
    {
        // Permission snapshots are replaced by re-sign-in (login or position change).
    }
}

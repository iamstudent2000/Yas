using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using YasPortal.Application.Authorization;

namespace YasPortal.Infrastructure.Authorization;

// Answers the question 'may this person do this?'. It never queries the database: it only reads the list of
// permission codes copied into the sign-in cookie when the person logged in or switched position.
public sealed class PermissionChecker(
    AuthenticationStateProvider authenticationStateProvider,
    CurrentUser? currentUser = null) : IPermissionChecker
{
    // Version for pages: finds the current user first, then uses the version below.
    public async Task<bool> HasPermissionAsync(string permissionCode, CancellationToken cancellationToken = default)
    {
        var user = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
        return await HasPermissionAsync(user, permissionCode, cancellationToken);
    }

    // Version for a known user.
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
        // "Manage" implies "View": holding Area.Resource.Manage also satisfies Area.Resource.View.
        var manageCode = Perm.ManageFor(permissionCode);
        var hasPermission = user.FindAll(AuthClaimNames.Permission)
            .Any(claim => string.Equals(claim.Value, permissionCode, StringComparison.OrdinalIgnoreCase)
                || (manageCode is not null && string.Equals(claim.Value, manageCode, StringComparison.OrdinalIgnoreCase)));

        return Task.FromResult(hasPermission);
    }

    public void Invalidate()
    {
        // Permission snapshots are replaced by re-sign-in (login or position change).
    }
}

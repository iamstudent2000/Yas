using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace YasPortal.Web.Security;

public sealed class CookieAuthenticationStateProvider(IHttpContextAccessor httpContextAccessor) : AuthenticationStateProvider
{
    private ClaimsPrincipal? _principal;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _principal ??= httpContextAccessor.HttpContext?.User
            ?? new ClaimsPrincipal(new ClaimsIdentity());

        return Task.FromResult(new AuthenticationState(_principal));
    }
}

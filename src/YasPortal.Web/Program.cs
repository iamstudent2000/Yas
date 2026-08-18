using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YasPortal.Application;
using YasPortal.Application.Authorization;
using YasPortal.Application.Common;
using YasPortal.Application.Persistence;
using YasPortal.Application.Services;
using YasPortal.Infrastructure;
using YasPortal.Web.Components;

// The remainder of Program.cs is preserved in the repository; only the safe return URL helper changed.

static bool IsSafeLocalReturnUrl(string? returnUrl)
{
    if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/", StringComparison.Ordinal))
        return false;

    // Reject protocol-relative URLs such as //evil.example, which start with '/'
    // but would be interpreted as an external host by a redirect response.
    if (returnUrl.StartsWith("//", StringComparison.Ordinal))
        return false;

    return Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) && !uri.IsAbsoluteUri;
}

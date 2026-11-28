namespace YasPortal.Infrastructure.Authorization;

/// <summary>
/// Decides whether the "return to this page afterwards" address in a link is safe to follow.
/// Only an address inside this site is accepted, so a crafted link cannot send a person to an outside website.
/// </summary>
public static class LocalReturnUrl
{
    // Safe only when it starts with exactly one slash and has no backslash. Browsers read "/\host" like "//host"
    // (an outside site), so a backslash is refused anywhere in the address.
    public static bool IsSafe(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith('/'))
            return false;
        if (returnUrl.StartsWith("//", StringComparison.Ordinal) || returnUrl.Contains('\\'))
            return false;
        return Uri.TryCreate(returnUrl, UriKind.Relative, out var uri) && !uri.IsAbsoluteUri;
    }
}

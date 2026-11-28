using YasPortal.Infrastructure.Authorization;
using Xunit;

namespace YasPortal.Tests;

// Return-address tests: only an address inside this site may be followed after sign-in or a position switch.
public sealed class LocalReturnUrlTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/requests/inbox")]
    [InlineData("/admin/users?page=2")]
    public void An_address_inside_the_site_is_safe(string url) => Assert.True(LocalReturnUrl.IsSafe(url));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]      // a browser reads this like "//evil.example"
    [InlineData("/ok\\path")]
    [InlineData("relative/path")]
    public void Anything_else_is_refused(string? url) => Assert.False(LocalReturnUrl.IsSafe(url));
}

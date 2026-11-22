using YasPortal.Domain.Landing;
using Xunit;

namespace YasPortal.Tests;

// Tests for the file uploaded for a home-page link: allowed types, replacing the file, and the download address.
public sealed class PortalLinkFileTests
{
    [Fact]
    public void Allowed_file_is_kept_with_its_type_and_a_bad_file_is_refused()
    {
        var id = Guid.NewGuid();
        var file = new PortalLinkFile(id, @"C:\docs\form.PDF", [1, 2, 3]);
        Assert.Equal("form.PDF", file.FileName);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Throws<ArgumentException>(() => new PortalLinkFile(id, "run.exe", [1]));    // type not allowed
        Assert.Throws<ArgumentException>(() => new PortalLinkFile(id, "a.pdf", []));       // empty file
    }

    [Fact]
    public void Replace_swaps_the_file_and_the_address_points_to_the_link()
    {
        var id = Guid.NewGuid();
        var file = new PortalLinkFile(id, "a.pdf", [1]);
        file.Replace("b.png", [2, 2]);
        Assert.Equal("b.png", file.FileName);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("/links/files/" + id, PortalLinkFile.UrlFor(id));
        Assert.True(PortalLinkFile.IsFileUrl(PortalLinkFile.UrlFor(id)));
        Assert.False(PortalLinkFile.IsFileUrl("/guides/files/" + id));
        Assert.NotNull(PortalLink.NormalizeUrl(PortalLinkFile.UrlFor(id)));               // the link accepts this address
    }
}

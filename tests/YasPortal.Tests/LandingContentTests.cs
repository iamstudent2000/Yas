using Microsoft.Extensions.Configuration;
using YasPortal.Web.Services;
using Xunit;

namespace YasPortal.Tests;

public sealed class LandingContentTests
{
    [Fact]
    public void Built_in_content_is_used_when_nothing_is_configured()
    {
        var content = new LandingContent(new ConfigurationBuilder().Build());

        Assert.NotEmpty(content.Slides);
        Assert.NotEmpty(content.QuickLinks);
        Assert.NotEmpty(content.Info);
        Assert.All(content.Slides, s => Assert.False(string.IsNullOrWhiteSpace(s.Title)));
    }

    [Fact]
    public void A_configured_list_replaces_the_built_in_one()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Landing:Slides:0:Title"] = "اطلاعیه",
            ["Landing:Slides:0:Text"] = "متن",
            ["Landing:QuickLinks:0:Title"] = "پرتال",
            ["Landing:QuickLinks:0:Url"] = "/login",
        }).Build();
        var content = new LandingContent(configuration);

        var slide = Assert.Single(content.Slides);
        Assert.Equal("اطلاعیه", slide.Title);
        Assert.Equal("ti-sparkles", slide.Icon);          // unset fields keep their defaults
        Assert.Equal("پرتال", Assert.Single(content.QuickLinks).Title);
        Assert.NotEmpty(content.Info);                    // a list that is not configured stays built-in
    }
}

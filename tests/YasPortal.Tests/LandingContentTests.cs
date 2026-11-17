using Microsoft.Extensions.Configuration;
using YasPortal.Infrastructure.Landing;
using Xunit;

namespace YasPortal.Tests;

// Landing content tests: the home page uses built-in text until an administrator configures its own, and a configured list fully replaces the built-in one.
public sealed class LandingContentTests
{
    [Fact]
    public void Built_in_content_is_used_when_nothing_is_configured()
    {
        var content = new LandingContent(new ConfigurationBuilder().Build());

        Assert.NotEmpty(content.Info);
        Assert.All(content.Info, i => Assert.False(string.IsNullOrWhiteSpace(i.Title)));
    }

    [Fact]
    public void A_configured_list_replaces_the_built_in_one()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Landing:Info:0:Title"] = "اطلاعیه",
            ["Landing:Info:0:Text"] = "متن",
        }).Build();
        var content = new LandingContent(configuration);

        var info = Assert.Single(content.Info);
        Assert.Equal("اطلاعیه", info.Title);
        Assert.Equal("ti-info-circle", info.Icon);        // unset fields keep their defaults
    }
}

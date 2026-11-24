using YasPortal.Domain.Landing;
using Xunit;

namespace YasPortal.Tests;

// Home page tile tests: the field rules, the limit of four tiles, and which tiles the home page shows.
public sealed class HomeTileTests
{
    private static HomeTile Make(string title = "t", int order = 1, string url = "/phone-book") =>
        new(title, null, HomeSlide.IconChoices[0].Key, url, order);

    [Fact]
    public void A_new_tile_keeps_its_details_and_is_switched_on()
    {
        var tile = new HomeTile("  دفترچه  ", "  شماره‌ها  ", HomeSlide.IconChoices[1].Key, "/phone-book", 3);
        Assert.Equal("دفترچه", tile.Title);
        Assert.Equal("شماره‌ها", tile.Text);
        Assert.Equal(3, tile.SortOrder);
        Assert.True(tile.IsActive);
    }

    [Fact]
    public void Title_icon_and_address_are_checked_and_a_blank_text_means_no_text()
    {
        Assert.Throws<ArgumentException>(() => Make(" "));
        Assert.Throws<ArgumentException>(() => Make(new string('x', HomeTile.TitleMax + 1)));
        Assert.Throws<ArgumentException>(() => new HomeTile("t", null, "ti-unknown", "/a", 1));
        Assert.Throws<ArgumentException>(() => new HomeTile("t", new string('x', HomeTile.TextMax + 1), HomeSlide.IconChoices[0].Key, "/a", 1));
        Assert.Throws<ArgumentException>(() => Make(url: "javascript:alert(1)"));
        Assert.Throws<ArgumentException>(() => Make(url: ""));
        Assert.Null(new HomeTile("t", "  ", HomeSlide.IconChoices[0].Key, "/a", 1).Text);
    }

    [Fact]
    public void The_address_follows_the_portal_link_rules()
    {
        Assert.Equal("/gallery", Make(url: "/gallery").Url);
        Assert.Equal("http://intranet/app", Make(url: "intranet/app").Url);   // a bare server name gets http://
    }

    [Fact]
    public void At_most_four_tiles_may_exist()
    {
        Assert.Null(HomeTile.ValidateCanAdd(HomeTile.MaxCount - 1));
        Assert.NotNull(HomeTile.ValidateCanAdd(HomeTile.MaxCount));
    }

    [Fact]
    public void The_home_page_shows_only_switched_on_tiles_in_order()
    {
        var off = Make("off", 0);
        off.Deactivate();
        var shown = HomeTile.ForHomePage([Make("b", 2), off, Make("a", 1), Make("c", 1)]);
        Assert.Equal(["a", "c", "b"], shown.Select(x => x.Title));
    }
}

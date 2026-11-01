using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using YasPortal.Domain.Landing;
using YasPortal.Infrastructure.Landing;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public sealed class HomeSlideTests
{
    private static readonly DateTime Today = new(2026, 11, 1);

    private static HomeSlide Slide(string title = "اطلاعیه", int order = 1, DateTime? from = null, DateTime? to = null, string icon = "ti-bell-ringing") =>
        new(title, "متن", icon, null, null, order, from, to);

    [Fact]
    public void Shown_only_inside_its_dates_and_while_active()
    {
        Assert.True(Slide().IsShownOn(Today));                                          // no dates
        Assert.True(Slide(from: Today, to: Today).IsShownOn(Today));                    // both ends are inclusive
        Assert.False(Slide(to: Today.AddDays(-1)).IsShownOn(Today));                    // expired yesterday
        Assert.False(Slide(from: Today.AddDays(1)).IsShownOn(Today));                   // not started
        Assert.True(Slide(to: Today.AddHours(23)).IsShownOn(Today.AddHours(2)));        // times of day are ignored

        var slide = Slide();
        slide.Deactivate();
        Assert.False(slide.IsShownOn(Today));
        slide.Activate();
        Assert.True(slide.IsShownOn(Today));
    }

    [Theory]
    [InlineData("/login", true)]
    [InlineData("/requests/new?x=1", true)]
    [InlineData("https://example.com/a", true)]
    [InlineData("http://intranet/a", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("//evil.example", false)]
    [InlineData("/\\evil.example", false)]
    [InlineData("data:text/html;base64,AAAA", false)]
    [InlineData("login", false)]
    [InlineData("/a b", false)]
    public void Only_local_or_http_links_are_safe(string url, bool safe) => Assert.Equal(safe, HomeSlide.IsSafeLink(url));

    [Fact]
    public void Update_validates_the_fields()
    {
        Assert.Throws<ArgumentException>(() => Slide(title: "  "));
        Assert.Throws<ArgumentException>(() => Slide(icon: "ti-nope"));
        Assert.Throws<ArgumentException>(() => Slide(from: Today, to: Today.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => new HomeSlide("t", "x", "ti-flag", "متن", "javascript:alert(1)", 1, null, null));
        Assert.Throws<ArgumentException>(() => new HomeSlide("t", "x", "ti-flag", null, "/login", 1, null, null));   // a link needs its text
        Assert.Throws<ArgumentException>(() => new HomeSlide("t", new string('x', HomeSlide.TextMax + 1), "ti-flag", null, null, 1, null, null));

        var ok = new HomeSlide(" عنوان ", " متن ", "ti-flag", " برو ", " /login ", 3, Today.AddHours(5), null);
        Assert.Equal("عنوان", ok.Title);
        Assert.Equal("/login", ok.LinkUrl);
        Assert.Equal(Today, ok.StartsOn);                                                // stored as a date
    }

    [Fact]
    public async Task Provider_returns_the_visible_slides_in_order()
    {
        var (options, provider) = Create();
        await using (var db = new ApplicationDbContext(options))
        {
            var hidden = Slide("غیرفعال", 1); hidden.Deactivate();
            db.HomeSlides.AddRange(Slide("دوم", 2), Slide("اول", 1), hidden, Slide("منقضی", 0, to: Today.AddDays(-1)), Slide("آینده", 0, from: Today.AddDays(2)));
            await db.SaveChangesAsync();
        }

        var slides = await provider.GetSlidesAsync(Today);

        Assert.Equal(new[] { "اول", "دوم" }, slides.Select(x => x.Title).ToArray());
    }

    [Fact]
    public async Task Provider_falls_back_to_the_built_in_slides_when_none_is_visible()
    {
        var (options, provider) = Create();
        await using (var db = new ApplicationDbContext(options))
        {
            db.HomeSlides.Add(Slide("منقضی", 1, to: Today.AddDays(-5)));
            await db.SaveChangesAsync();
        }

        var slides = await provider.GetSlidesAsync(Today);

        Assert.NotEmpty(slides);
        Assert.DoesNotContain(slides, x => x.Title == "منقضی");
    }

    private static (DbContextOptions<ApplicationDbContext> Options, HomeSlideProvider Provider) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        return (options, new HomeSlideProvider(new Factory(options), new LandingContent(new ConfigurationBuilder().Build()), NullLogger<HomeSlideProvider>.Instance));
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}

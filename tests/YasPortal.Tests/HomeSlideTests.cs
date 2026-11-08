using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    public async Task Provider_returns_nothing_when_no_slide_is_valid()
    {
        var (options, provider) = Create();
        await using (var db = new ApplicationDbContext(options))
        {
            db.HomeSlides.Add(Slide("منقضی", 1, to: Today.AddDays(-5)));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await provider.GetSlidesAsync(Today));   // no built-in slides: every slide belongs to the administrator
    }

    [Fact]
    public void Has_valid_tells_whether_a_slide_is_shown_today()
    {
        var expired = Slide("منقضی", 1, to: Today.AddDays(-1));
        var future = Slide("آینده", 2, from: Today.AddDays(1));
        var off = Slide("خاموش", 3); off.Deactivate();
        var live = Slide("زنده", 4);

        Assert.False(HomeSlide.HasValid([], Today));
        Assert.False(HomeSlide.HasValid([expired, future, off], Today));
        Assert.True(HomeSlide.HasValid([expired, live], Today));
        Assert.False(HomeSlide.HasValid(new[] { expired, live }.Where(x => x != live), Today));   // what the page checks before a delete
    }

    [Fact]
    public void The_starter_set_has_valid_slides()
    {
        var starter = HomeSlide.StarterSet();
        Assert.True(starter.Count >= 1);
        Assert.True(HomeSlide.HasValid(starter, Today));
        Assert.All(starter, s => Assert.Contains(HomeSlide.IconChoices, i => i.Key == s.Icon));
    }

    [Theory]
    [InlineData("notice.pdf", 1000, true)]
    [InlineData("Photo.JPG", 1000, true)]
    [InlineData("form.docx", 1000, true)]
    [InlineData("page.html", 1000, false)]
    [InlineData("logo.svg", 1000, false)]
    [InlineData("run.exe", 1000, false)]
    [InlineData("noextension", 1000, false)]
    [InlineData("empty.pdf", 0, false)]
    [InlineData("big.pdf", HomeSlideAttachment.MaxBytes + 1, false)]
    [InlineData("exactly.pdf", HomeSlideAttachment.MaxBytes, true)]
    public void Attachment_accepts_only_listed_types_and_sizes(string name, long size, bool ok) =>
        Assert.Equal(ok, HomeSlideAttachment.Validate(name, size) is null);

    [Fact]
    public void Attachment_keeps_a_clean_name_and_the_matching_content_type()
    {
        var attachment = new HomeSlideAttachment(Guid.NewGuid(), @"C:\Users\me\Desktop\اطلاعیه.PDF", [1, 2, 3]);
        Assert.Equal("اطلاعیه.PDF", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(3, attachment.SizeBytes);

        attachment.Replace("a.png", [9]);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(1, attachment.SizeBytes);
        Assert.Throws<ArgumentException>(() => attachment.Replace("a.html", [9]));
        Assert.Throws<ArgumentException>(() => new HomeSlideAttachment(Guid.NewGuid(), "a.exe", [1]));
    }

    [Fact]
    public async Task Provider_reports_the_attachment_name_and_size_but_not_its_content()
    {
        var (options, provider) = Create();
        var withFile = Slide("با فایل", 1);
        var without = Slide("بی‌فایل", 2);
        await using (var db = new ApplicationDbContext(options))
        {
            db.HomeSlides.AddRange(withFile, without);
            db.HomeSlideAttachments.Add(new HomeSlideAttachment(withFile.Id, "notice.pdf", new byte[2048]));
            await db.SaveChangesAsync();
        }

        var slides = await provider.GetSlidesAsync(Today);

        Assert.Equal(withFile.Id, slides[0].FileSlideId);
        Assert.Equal("notice.pdf", slides[0].FileName);
        Assert.Equal(2048, slides[0].FileSize);
        Assert.Null(slides[1].FileSlideId);
    }

    [Fact]
    public async Task File_contents_never_reach_the_audit_log()
    {
        var (options, _) = Create();
        var slide = Slide();
        await using (var db = new ApplicationDbContext(options))
        {
            db.HomeSlides.Add(slide);
            db.HomeSlideAttachments.Add(new HomeSlideAttachment(slide.Id, "secret.pdf", [1, 2, 3, 4]));
            await db.SaveChangesAsync();
        }

        await using var check = new ApplicationDbContext(options);
        var entry = check.AuditEntries.Single(x => x.EntityType == nameof(HomeSlideAttachment));
        Assert.Contains("secret.pdf", entry.AfterJson);
        Assert.DoesNotContain("Content\"", entry.AfterJson);
    }

    private static (DbContextOptions<ApplicationDbContext> Options, HomeSlideProvider Provider) Create()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .Options;
        return (options, new HomeSlideProvider(new Factory(options), NullLogger<HomeSlideProvider>.Instance));
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}

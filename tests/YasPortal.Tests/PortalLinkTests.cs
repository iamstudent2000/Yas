using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using YasPortal.Domain.Landing;
using YasPortal.Infrastructure.Landing;
using YasPortal.Infrastructure.Persistence;
using Xunit;

namespace YasPortal.Tests;

public sealed class PortalLinkTests
{
    [Theory]
    [InlineData("http://payroll/app", "http://payroll/app")]
    [InlineData("https://learn.example.org/a?b=1", "https://learn.example.org/a?b=1")]
    [InlineData("ftp://files.local/docs", "ftp://files.local/docs")]
    [InlineData("intranet/app", "http://intranet/app")]
    [InlineData("intranet", "http://intranet")]
    [InlineData("10.0.0.5:8080/app", "http://10.0.0.5:8080/app")]
    [InlineData("server:8080", "http://server:8080")]
    [InlineData("www.example.com", "http://www.example.com")]
    [InlineData("/login", "/login")]
    [InlineData("  http://a.b  ", "http://a.b")]
    public void Addresses_that_are_accepted(string input, string stored) => Assert.Equal(stored, PortalLink.NormalizeUrl(input));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,AAAA")]
    [InlineData("file:///c:/windows")]
    [InlineData("mailto:a@b.c")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("http://")]
    [InlineData("a b")]
    [InlineData("")]
    [InlineData("   ")]
    public void Addresses_that_are_refused(string input) => Assert.Null(PortalLink.NormalizeUrl(input));

    [Fact]
    public void A_link_is_stored_with_the_normalized_address()
    {
        var link = new PortalLink(" سامانه ", "intranet/app", "  ", null, 1, false, 0);
        Assert.Equal("سامانه", link.Title);
        Assert.Equal("http://intranet/app", link.Url);
        Assert.Null(link.Description);
        Assert.Throws<ArgumentException>(() => new PortalLink("", "http://a.b", null, null, 1, false, 0));
        Assert.Throws<ArgumentException>(() => new PortalLink("x", "javascript:alert(1)", null, null, 1, false, 0));
    }

    [Fact]
    public void Groups_nest_up_to_the_maximum_depth_and_never_loop()
    {
        var a = new LinkGroup("A", null, 1);
        var b = new LinkGroup("B", a.Id, 1);
        var c = new LinkGroup("C", b.Id, 1);
        var d = new LinkGroup("D", null, 1);
        var all = new[] { a, b, c, d };

        Assert.Null(LinkGroup.CheckParent(d.Id, null, all));
        Assert.Null(LinkGroup.CheckParent(d.Id, b.Id, all));                 // D under B: levels A > B > D = 3
        Assert.NotNull(LinkGroup.CheckParent(d.Id, c.Id, all));              // D under C would be 4 levels
        Assert.NotNull(LinkGroup.CheckParent(a.Id, c.Id, all));              // A cannot go under its own descendant
        Assert.NotNull(LinkGroup.CheckParent(a.Id, a.Id, all));              // nor under itself
        Assert.Equal(3, LinkGroup.DepthOf(c.Id, all.ToDictionary(x => x.Id)));
        Assert.Equal(new[] { a.Id, b.Id, c.Id }.OrderBy(x => x), LinkGroup.SubtreeOf(a.Id, all).OrderBy(x => x));
    }

    [Fact]
    public void Tree_hides_inactive_groups_links_and_empty_groups()
    {
        var top = new LinkGroup("اداری", null, 1);
        var sub = new LinkGroup("مالی", top.Id, 1);
        var off = new LinkGroup("خاموش", null, 2); off.Deactivate();
        var empty = new LinkGroup("خالی", null, 3);
        var inOff = new PortalLink("در گروه خاموش", "http://a.b", null, off.Id, 1, true, 1);
        var inSub = new PortalLink("حقوق", "http://payroll", null, sub.Id, 1, true, 2);
        var loose = new PortalLink("آزاد", "http://loose", null, null, 1, false, 0);
        var hidden = new PortalLink("پنهان", "http://hidden", null, top.Id, 2, false, 0); hidden.Deactivate();

        var tree = LinkTree.Build([top, sub, off, empty], [inOff, inSub, loose, hidden]);

        var group = Assert.Single(tree.Groups);
        Assert.Equal("اداری", group.Name);
        Assert.Empty(group.Links);
        Assert.Equal("مالی", Assert.Single(group.Children).Name);
        Assert.Equal(1, group.TotalLinks);
        Assert.Equal(new[] { "حقوق" }, tree.QuickAccess.Select(x => x.Title).ToArray());        // the one in the switched-off group is not quick access either
        Assert.Equal("آزاد", Assert.Single(tree.Ungrouped).Title);
        Assert.Equal(2, tree.TotalLinks);
    }

    [Fact]
    public void Quick_access_is_ordered_by_its_own_order()
    {
        var second = new PortalLink("دوم", "http://2", null, null, 1, true, 2);
        var first = new PortalLink("اول", "http://1", null, null, 9, true, 1);
        Assert.Equal(new[] { "اول", "دوم" }, LinkTree.Build([], [second, first]).QuickAccess.Select(x => x.Title).ToArray());
    }

    [Fact]
    public void Filter_keeps_matching_links_and_whole_matching_groups()
    {
        var finance = new LinkGroup("مالی", null, 1);
        var hr = new LinkGroup("منابع انسانی", null, 2);
        var tree = LinkTree.Build([finance, hr], [
            new PortalLink("حقوق", "http://payroll", null, finance.Id, 1, false, 0),
            new PortalLink("بودجه", "http://budget", null, finance.Id, 2, false, 0),
            new PortalLink("مرخصی", "http://leave", "ثبت مرخصی", hr.Id, 1, false, 0),
            new PortalLink("ویکی", "http://wiki", null, null, 1, false, 0)]);

        Assert.Same(tree, tree.Filter("  "));
        var byLink = tree.Filter("مرخصی");
        Assert.Equal("منابع انسانی", Assert.Single(byLink.Groups).Name);
        Assert.Empty(byLink.Ungrouped);
        var byGroup = tree.Filter("مالی");
        Assert.Equal(2, Assert.Single(byGroup.Groups).Links.Count);                              // the group name matched: all its links stay
        Assert.Equal("ویکی", Assert.Single(tree.Filter("WIKI").Ungrouped).Title);                 // by address, any case
        Assert.Equal(1, tree.Filter("budget").TotalLinks);                                        // by address
        Assert.Equal(0, tree.Filter("چیزی نیست").TotalLinks);
    }

    [Fact]
    public void Import_reads_groups_titles_addresses_and_reports_bad_lines()
    {
        var lines = LinkImportParser.Parse("""
            # a comment
            اداری > مالی | حقوق | payroll/app | سیستم حقوق
            | بدون گروه | https://x.example

            آموزش | یادگیری | javascript:alert(1)
            فقط یک ستون
            الف > ب > ج > د | عمیق | http://deep
            """);

        Assert.Equal(5, lines.Count);
        Assert.Equal(new[] { "اداری", "مالی" }, lines[0].GroupPath);
        Assert.Equal("http://payroll/app", lines[0].Url);
        Assert.Equal("سیستم حقوق", lines[0].Description);
        Assert.Null(lines[0].Error);
        Assert.Empty(lines[1].GroupPath);
        Assert.Null(lines[1].Error);
        Assert.NotNull(lines[2].Error);          // refused address
        Assert.NotNull(lines[3].Error);          // not enough columns
        Assert.NotNull(lines[4].Error);          // four levels
        Assert.Empty(LinkImportParser.Parse(null));
    }

    [Fact]
    public async Task Provider_builds_the_tree_from_the_database()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot()).Options;
        var group = new LinkGroup("اداری", null, 1);
        await using (var db = new ApplicationDbContext(options))
        {
            db.LinkGroups.Add(group);
            db.PortalLinks.AddRange(new PortalLink("حقوق", "http://payroll", null, group.Id, 1, true, 1), new PortalLink("خاموش", "http://off", null, group.Id, 2, false, 0));
            await db.SaveChangesAsync();
        }

        var tree = await new PortalLinkProvider(new Factory(options), NullLogger<PortalLinkProvider>.Instance).GetTreeAsync();

        Assert.Equal(2, tree.TotalLinks);
        Assert.Equal("حقوق", Assert.Single(tree.QuickAccess).Title);
    }

    [Fact]
    public void The_starter_links_are_valid_quick_access_links()
    {
        var starter = PortalLink.StarterQuickLinks();
        Assert.Equal(5, starter.Count);
        Assert.All(starter, x => { Assert.True(x.IsQuickAccess); Assert.StartsWith("/", x.Url); });
    }

    private sealed class Factory(DbContextOptions<ApplicationDbContext> options) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}

using YasPortal.Application.Authorization;
using YasPortal.Domain.Guides;
using Xunit;

namespace YasPortal.Tests;

public sealed class UserGuideTests
{
    private static UserGuide G(string title = "راهنما", string html = "<p>متن</p>", GuideVisibility visibility = GuideVisibility.SignedIn) =>
        new(title, null, html, visibility, 1);

    [Fact]
    public void Guide_needs_a_title_and_limits_title_and_summary()
    {
        Assert.Throws<ArgumentException>(() => G("  "));
        Assert.Throws<ArgumentException>(() => G(new string('x', UserGuide.TitleMax + 1)));
        var ex = Assert.Throws<ArgumentException>(() => new UserGuide("t", new string('x', UserGuide.SummaryMax + 1), "<p>a</p>", GuideVisibility.Public, 1));
        Assert.Equal("summary", ex.ParamName);
        Assert.Null(new UserGuide("t", "   ", "<p>a</p>", GuideVisibility.Public, 1).Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p><br></p>")]
    [InlineData("<p>&nbsp;</p><p> </p>")]
    public void Guide_without_visible_text_is_rejected(string html)
    {
        var ex = Assert.Throws<ArgumentException>(() => G(html: html));
        Assert.Equal("contentHtml", ex.ParamName);
    }

    [Fact]
    public void Guide_content_is_limited_in_length()
    {
        Assert.Throws<ArgumentException>(() => G(html: "<p>" + new string('x', UserGuide.ContentMax) + "</p>"));
        Assert.Equal("<p>متن</p>", G(html: "  <p>متن</p>  ").ContentHtml);
    }

    [Fact]
    public void Guide_rejects_an_unknown_visibility()
    {
        Assert.Throws<ArgumentException>(() => G(visibility: (GuideVisibility)9));
    }

    [Theory]
    [InlineData(GuideVisibility.Public, true, true, true)]
    [InlineData(GuideVisibility.Public, false, true, true)]
    [InlineData(GuideVisibility.SignedIn, true, true, true)]
    [InlineData(GuideVisibility.SignedIn, false, true, false)]
    [InlineData(GuideVisibility.Public, true, false, false)]
    [InlineData(GuideVisibility.SignedIn, true, false, false)]
    public void Readability_depends_on_visibility_sign_in_and_being_active(GuideVisibility visibility, bool signedIn, bool active, bool expected)
    {
        var guide = G(visibility: visibility);
        if (!active) guide.Deactivate();
        Assert.Equal(expected, guide.IsReadableBy(signedIn));
    }

    [Fact]
    public void Update_changes_the_guide_and_keeps_its_identity()
    {
        var guide = G();
        var id = guide.Id;
        guide.Update("عنوان جدید", "خلاصه", "<h2>الف</h2>", GuideVisibility.Public, 5);
        Assert.Equal(id, guide.Id);
        Assert.Equal("عنوان جدید", guide.Title);
        Assert.Equal(GuideVisibility.Public, guide.Visibility);
        Assert.Equal(5, guide.SortOrder);
    }

    [Fact]
    public void Guide_records_its_author_and_ignores_an_empty_id()
    {
        var me = Guid.NewGuid();
        Assert.Equal(me, new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, me).CreatedByEmployeeId);
        Assert.Null(new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, Guid.Empty).CreatedByEmployeeId);
        Assert.Null(G().CreatedByEmployeeId);
    }

    [Theory]
    // canEditAll, canEditOwn, isAuthor, expected
    [InlineData(true, false, false, true)]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, false)]
    public void Editing_needs_all_guides_permission_or_own_permission_on_an_own_guide(bool canEditAll, bool canEditOwn, bool isAuthor, bool expected)
    {
        var author = Guid.NewGuid();
        var guide = new UserGuide("t", null, "<p>a</p>", GuideVisibility.SignedIn, 1, author);
        var caller = isAuthor ? author : Guid.NewGuid();
        Assert.Equal(expected, guide.CanBeEditedBy(canEditAll, canEditOwn, caller));
    }

    [Fact]
    public void A_guide_without_a_recorded_author_is_nobodys_own()
    {
        var guide = G();   // no author
        Assert.False(guide.CanBeEditedBy(false, true, Guid.NewGuid()));
        Assert.False(guide.CanBeEditedBy(false, true, null));
        Assert.False(guide.CanBeEditedBy(false, true, Guid.Empty));
        Assert.True(guide.CanBeEditedBy(true, false, null));
        Assert.False(guide.IsOwnedBy(null));
    }

    [Fact]
    public void Guide_permissions_are_in_the_catalog_and_manage_implies_view()
    {
        Assert.Contains(Perm.AdminGuidesView, PermissionCatalog.Codes);
        Assert.Contains(Perm.AdminGuidesManage, PermissionCatalog.Codes);
        Assert.Contains(Perm.GuidesOwnManage, PermissionCatalog.Codes);
        Assert.Equal(PermissionReach.Own, PermissionCatalog.ReachOf(Perm.GuidesOwnManage));
        Assert.Equal(PermissionReach.All, PermissionCatalog.ReachOf(Perm.AdminGuidesManage));
        Assert.Equal(Perm.AdminGuidesManage, Perm.ManageFor(Perm.AdminGuidesView));
    }
}

using YasPortal.Application.Authorization;
using YasPortal.Domain.Guides;
using Xunit;

namespace YasPortal.Tests;

public sealed class UserGuideTests
{
    private static UserGuide G(string title = "راهنما", string? permission = null, GuideDirection dir = GuideDirection.Rtl) =>
        new(title, null, dir, permission, 1);

    private static GuideBlock B(string text = "متن", GuideBlockKind kind = GuideBlockKind.Paragraph, GuideDirection? dir = null) =>
        new(Guid.NewGuid(), 1, kind, text, dir, GuideAlign.Start, GuideSize.Normal, GuideTone.Default, false, false);

    [Fact]
    public void Guide_needs_a_title_and_limits_summary_and_permission_length()
    {
        Assert.Throws<ArgumentException>(() => G("  "));
        Assert.Throws<ArgumentException>(() => G(new string('x', UserGuide.TitleMax + 1)));
        Assert.Throws<ArgumentException>(() => new UserGuide("t", new string('x', UserGuide.SummaryMax + 1), GuideDirection.Rtl, null, 1));
        Assert.Throws<ArgumentException>(() => G(permission: new string('x', UserGuide.PermissionMax + 1)));
        Assert.Null(G(permission: "  ").RelatedPermissionCode);
    }

    [Fact]
    public void Guide_without_a_permission_is_readable_by_everyone_while_active()
    {
        var guide = G();
        Assert.True(guide.IsReadableBy(_ => false));
        guide.Deactivate();
        Assert.False(guide.IsReadableBy(_ => true));
        guide.Activate();
        Assert.True(guide.IsReadableBy(_ => false));
    }

    [Fact]
    public void Guide_with_a_permission_is_readable_only_by_its_holders()
    {
        var guide = G(permission: Perm.RequestsOwnCreate);
        Assert.True(guide.IsReadableBy(code => code == Perm.RequestsOwnCreate));
        Assert.False(guide.IsReadableBy(code => code == Perm.RequestsOwnView));
    }

    [Fact]
    public void Block_text_is_required_trimmed_and_limited()
    {
        Assert.Throws<ArgumentException>(() => B("   "));
        Assert.Throws<ArgumentException>(() => B(new string('x', GuideBlock.TextMax + 1)));
        Assert.Equal("a\nb", B("  a\r\nb  ").Text);
    }

    [Fact]
    public void Block_rejects_unknown_style_values()
    {
        var b = B();
        Assert.Throws<ArgumentException>(() => b.Set((GuideBlockKind)99, "x", null, GuideAlign.Start, GuideSize.Normal, GuideTone.Default, false, false));
        Assert.Throws<ArgumentException>(() => b.Set(GuideBlockKind.Paragraph, "x", (GuideDirection)9, GuideAlign.Start, GuideSize.Normal, GuideTone.Default, false, false));
        Assert.Throws<ArgumentException>(() => b.Set(GuideBlockKind.Paragraph, "x", null, (GuideAlign)9, GuideSize.Normal, GuideTone.Default, false, false));
        Assert.Throws<ArgumentException>(() => b.Set(GuideBlockKind.Paragraph, "x", null, GuideAlign.Start, (GuideSize)9, GuideTone.Default, false, false));
        Assert.Throws<ArgumentException>(() => b.Set(GuideBlockKind.Paragraph, "x", null, GuideAlign.Start, GuideSize.Normal, (GuideTone)9, false, false));
    }

    [Fact]
    public void Block_keeps_its_own_direction_or_none()
    {
        Assert.Null(B().Direction);
        Assert.Equal(GuideDirection.Ltr, B(dir: GuideDirection.Ltr).Direction);
    }

    [Fact]
    public void List_block_gives_one_item_per_non_empty_line()
    {
        var b = B("اول\n\n  دوم  \nسوم", GuideBlockKind.List);
        Assert.Equal(["اول", "دوم", "سوم"], b.Lines());
    }

    [Fact]
    public void Guide_permissions_are_in_the_catalog_and_manage_implies_view()
    {
        Assert.Contains(Perm.AdminGuidesView, PermissionCatalog.Codes);
        Assert.Contains(Perm.AdminGuidesManage, PermissionCatalog.Codes);
        Assert.Equal(Perm.AdminGuidesManage, Perm.ManageFor(Perm.AdminGuidesView));
    }
}

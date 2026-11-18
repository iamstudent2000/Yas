using YasPortal.Domain.Guides;
using YasPortal.Infrastructure.Guides;
using Xunit;

namespace YasPortal.Tests;

// Guide group and search tests: cleaning the group name, the search words rule, and arranging guides under group headings.
public sealed class GuideSearchGroupTests
{
    private static GuideListItem Item(string title, string? group, string? summary = null) =>
        new(Guid.NewGuid(), title, summary, GuideVisibility.SignedIn, group);

    [Fact]
    public void Group_name_is_cleaned_and_blank_means_no_group()
    {
        Assert.Null(new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, null, "   ").GroupName);
        Assert.Null(new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1).GroupName);
        // spaces are trimmed and collapsed, and Arabic ي / ك become the Persian ی / ک so one group is never split in two
        Assert.Equal("درخواست‌ های من", new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, null, "  درخواست‌  های من ").GroupName);
        Assert.Equal("کیف", UserGuide.NormalizeGroupName("كيف"));
    }

    [Fact]
    public void Group_name_has_a_length_limit_and_update_can_remove_the_group()
    {
        var ex = Assert.Throws<ArgumentException>(() => new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, null, new string('x', UserGuide.GroupMax + 1)));
        Assert.Equal("groupName", ex.ParamName);
        var guide = new UserGuide("t", null, "<p>a</p>", GuideVisibility.Public, 1, null, "فروشگاه");
        Assert.Equal("فروشگاه", guide.GroupName);
        guide.Update("t", null, "<p>a</p>", GuideVisibility.Public, 1);
        Assert.Null(guide.GroupName);
    }

    [Fact]
    public void Search_needs_every_word_in_title_summary_or_group_and_ignores_digit_and_letter_forms()
    {
        Assert.True(GuideSearch.Matches("ثبت درخواست", null, null, ""));
        Assert.True(GuideSearch.Matches("ثبت درخواست", "مراحل کار", "درخواست‌ها", "ثبت مراحل"));
        Assert.False(GuideSearch.Matches("ثبت درخواست", "مراحل کار", null, "ثبت فروشگاه"));
        Assert.True(GuideSearch.Matches("راهنمای ۲", null, null, "2"));
        Assert.True(GuideSearch.Matches("کیف", null, null, "كيف"));
    }

    [Fact]
    public void Search_can_also_pick_one_group_or_the_guides_without_a_group()
    {
        var list = new[] { Item("الف", "درخواست‌ها"), Item("ب", "فروشگاه"), Item("ج", null) };
        Assert.Equal(3, GuideCatalog.Search(list, "", null).Count);
        Assert.Equal(["الف"], GuideCatalog.Search(list, "", "درخواست‌ها").Select(g => g.Title));
        Assert.Equal(["ج"], GuideCatalog.Search(list, "", "").Select(g => g.Title));
        Assert.Equal(["ب"], GuideCatalog.Search(list, "فروشگاه", null).Select(g => g.Title));
    }

    [Fact]
    public void Group_orders_sections_by_first_guide_and_puts_ungrouped_last()
    {
        var list = new[] { Item("1", null), Item("2", "فروشگاه"), Item("3", "درخواست‌ها"), Item("4", "فروشگاه") };
        var sections = GuideCatalog.Group(list);
        Assert.Equal(["فروشگاه", "درخواست‌ها", null], sections.Select(s => s.GroupName));
        Assert.Equal(["2", "4"], sections[0].Guides.Select(g => g.Title));
        Assert.Equal(["1"], sections[2].Guides.Select(g => g.Title));
        Assert.Empty(GuideCatalog.Group([]));
    }
}

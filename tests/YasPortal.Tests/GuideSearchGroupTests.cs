using YasPortal.Domain.Guides;
using YasPortal.Infrastructure.Guides;
using Xunit;

namespace YasPortal.Tests;

// Guide search tests: the search words rule, picking one group, and arranging guides under group headings.
public sealed class GuideSearchGroupTests
{
    private static GuideListItem Item(string title, string? group, string? summary = null, int? groupOrder = null) =>
        new(Guid.NewGuid(), title, summary, GuideVisibility.SignedIn, group, groupOrder);

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

    [Fact]
    public void Group_orders_sections_by_the_groups_order_number_before_the_first_guide()
    {
        // the guide of "فروشگاه" comes first in the list, but its group has the higher order number, so its heading comes second
        var list = new[] { Item("1", "فروشگاه", groupOrder: 2), Item("2", "درخواست‌ها", groupOrder: 1), Item("3", "فروشگاه", groupOrder: 2) };
        var sections = GuideCatalog.Group(list);
        Assert.Equal(["درخواست‌ها", "فروشگاه"], sections.Select(s => s.GroupName));
        Assert.Equal(["1", "3"], sections[1].Guides.Select(g => g.Title));
    }
}

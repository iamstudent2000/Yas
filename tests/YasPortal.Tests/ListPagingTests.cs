using YasPortal.Domain.Paging;
using Xunit;

namespace YasPortal.Tests;

// List paging tests: page count, keeping the page number valid after rows disappear, and cutting a list into pages.
public sealed class ListPagingTests
{
    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(101, 20, 6)]
    [InlineData(5, 0, 5)]      // a page size below one is treated as one row per page
    [InlineData(-3, 10, 1)]
    public void Page_count_rounds_up_and_is_at_least_one(int total, int size, int expected) => Assert.Equal(expected, ListPaging.PageCount(total, size));

    [Theory]
    [InlineData(1, 25, 10, 1)]
    [InlineData(3, 25, 10, 3)]
    [InlineData(4, 25, 10, 3)]     // past the end: the last page
    [InlineData(0, 25, 10, 1)]
    [InlineData(-2, 25, 10, 1)]
    [InlineData(7, 0, 10, 1)]      // nothing in the list
    public void Page_number_is_kept_inside_the_pages_that_exist(int page, int total, int size, int expected) => Assert.Equal(expected, ListPaging.ClampPage(page, total, size));

    [Fact]
    public void Slice_returns_one_page_in_order()
    {
        var rows = Enumerable.Range(1, 25).ToList();
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], ListPaging.Slice(rows, 1, 10));
        Assert.Equal([11, 12, 13, 14, 15, 16, 17, 18, 19, 20], ListPaging.Slice(rows, 2, 10));
        Assert.Equal([21, 22, 23, 24, 25], ListPaging.Slice(rows, 3, 10));
        Assert.Equal([21, 22, 23, 24, 25], ListPaging.Slice(rows, 9, 10));      // a page that no longer exists shows the last one
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10], ListPaging.Slice(rows, 0, 10));
        Assert.Empty(ListPaging.Slice(new List<int>(), 1, 10));
    }

    [Fact]
    public void Offered_page_sizes_match_the_other_lists() => Assert.Equal([5, 10, 20, 50, 100], ListPaging.PageSizes);
}

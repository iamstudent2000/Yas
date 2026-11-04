using YasPortal.Application.Authorization;
using YasPortal.Domain.Store;
using YasPortal.Infrastructure.Store;

namespace YasPortal.Tests;

public class StoreTests
{
    private static readonly Guid Cat = Guid.NewGuid();

    [Fact]
    public void Item_is_trimmed_and_validated()
    {
        var item = new StoreItem("  A-1 ", " Pen ", Cat, " عدد ", "  ");
        Assert.Equal("A-1", item.Code);
        Assert.Equal("Pen", item.Name);
        Assert.Equal("عدد", item.Unit);
        Assert.Null(item.Description);
        Assert.Throws<ArgumentException>(() => new StoreItem("", "Pen", Cat, "عدد", null));
        Assert.Throws<ArgumentException>(() => new StoreItem("A", "Pen", Guid.Empty, "عدد", null));
        Assert.Throws<ArgumentException>(() => new StoreItem("A", "Pen", Cat, "عدد", null, -1));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("۱۲٫۵", 12.5)]
    [InlineData("١٢,٥", 12.5)]
    [InlineData("  ", 0)]
    public void Typed_quantities_accept_persian_digits(string text, double expected)
    {
        Assert.True(StoreNumber.TryParse(text, out var v));
        Assert.Equal((decimal)expected, v);
        Assert.False(StoreNumber.TryParse("abc", out _));
    }

    [Fact]
    public void Max_per_request_is_stored_and_validated()
    {
        var item = new StoreItem("A", "Pen", Cat, "عدد", null, 10);
        Assert.Equal(10, item.MaxPerRequest);
        Assert.Throws<ArgumentException>(() => new StoreItem("A", "Pen", Cat, "عدد", null, -1));
    }

    [Fact]
    public void Request_window_is_checked_by_day_inclusive()
    {
        var item = new StoreItem("A", "Pen", Cat, "عدد", null, 0, new DateTime(2026, 11, 10), new DateTime(2026, 11, 20), true);
        Assert.True(item.IsLottery);
        Assert.Equal(StoreRequestWindow.NotYet, item.WindowOn(new DateTime(2026, 11, 9, 23, 59, 0)));
        Assert.Equal(StoreRequestWindow.Open, item.WindowOn(new DateTime(2026, 11, 10)));
        Assert.Equal(StoreRequestWindow.Open, item.WindowOn(new DateTime(2026, 11, 20, 23, 59, 0)));
        Assert.Equal(StoreRequestWindow.Closed, item.WindowOn(new DateTime(2026, 11, 21)));
        Assert.Equal(StoreRequestWindow.Open, new StoreItem("B", "Pen", Cat, "عدد", null).WindowOn(DateTime.Today));
        Assert.Throws<ArgumentException>(() => new StoreItem("C", "Pen", Cat, "عدد", null, 0, new DateTime(2026, 11, 20), new DateTime(2026, 11, 10)));
    }

    [Fact]
    public void Category_needs_a_name()
    {
        Assert.Throws<ArgumentException>(() => new StoreCategory("  ", 0));
        Assert.Equal("لوازم", new StoreCategory(" لوازم ", 1).Name);
    }

    [Theory]
    [InlineData(StockMovementKind.Receive, 5, 5)]
    [InlineData(StockMovementKind.Return, 2, 2)]
    [InlineData(StockMovementKind.Issue, 3, -3)]
    [InlineData(StockMovementKind.Adjustment, -4, -4)]
    [InlineData(StockMovementKind.Adjustment, 4, 4)]
    public void Movement_delta_has_the_right_sign(StockMovementKind kind, double quantity, double expected)
    {
        var m = new StoreStockMovement(Guid.NewGuid(), kind, (decimal)quantity, null, null, null);
        Assert.Equal((decimal)expected, m.Delta);
    }

    [Fact]
    public void Movement_rejects_zero_negative_and_too_many_decimals()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new StoreStockMovement(id, StockMovementKind.Receive, 0, null, null, null));
        Assert.Throws<ArgumentException>(() => new StoreStockMovement(id, StockMovementKind.Issue, -1, null, null, null));
        Assert.Throws<ArgumentException>(() => new StoreStockMovement(id, StockMovementKind.Receive, 1.2345m, null, null, null));
        Assert.Throws<ArgumentException>(() => new StoreStockMovement(Guid.Empty, StockMovementKind.Receive, 1, null, null, null));
    }

    [Fact]
    public void Units_come_from_a_fixed_list()
    {
        Assert.Contains("عدد", StoreUnits.All);
        Assert.Equal(StoreUnits.All.Count, StoreUnits.All.Distinct().Count());
    }

    [Fact]
    public void Store_permissions_are_registered_with_the_right_reach()
    {
        foreach (var code in new[] { Perm.StoreShopView, Perm.StoreOrdersCreate, Perm.StoreItemsView, Perm.StoreItemsManage, Perm.StoreStockView, Perm.StoreStockManage })
            Assert.Contains(code, PermissionCatalog.Codes);
        Assert.Equal(PermissionReach.Own, PermissionCatalog.ReachOf(Perm.StoreShopView));
        Assert.Equal(PermissionReach.All, PermissionCatalog.ReachOf(Perm.StoreStockManage));
        Assert.True(PermissionCatalog.ActsOnOthers(Perm.StoreItemsManage));
        Assert.False(PermissionCatalog.ActsOnOthers(Perm.StoreShopView));
        Assert.Equal(Perm.StoreStockManage, Perm.ManageFor(Perm.StoreStockView));
    }
}

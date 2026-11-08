using YasPortal.Application.Authorization;
using YasPortal.Domain.Store;
using YasPortal.Domain.Workflows;
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

    private static StoreOrderLine Line(decimal q = 1) => new(Guid.NewGuid(), "A-1", "Pen", "عدد", q);

    [Fact]
    public void Order_belongs_to_the_person_and_needs_lines()
    {
        var employee = Guid.NewGuid();
        var order = new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), "  note ", [Line(2), Line(3)]);
        Assert.Equal(employee, order.EmployeeId);
        Assert.Equal("note", order.Note);
        Assert.Equal(2, order.Lines.Count);
        Assert.All(order.Lines, l => Assert.Equal(order.Id, l.OrderId));
        Assert.Throws<ArgumentException>(() => new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, []));
        var one = Line();
        Assert.Throws<ArgumentException>(() => new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [one, new StoreOrderLine(one.ItemId, "A", "A", "عدد", 1)]));
        Assert.Throws<ArgumentException>(() => new StoreOrderLine(Guid.NewGuid(), "A", "A", "عدد", 0));
    }

    [Fact]
    public void Returned_order_lines_can_be_replaced_but_not_after_handover()
    {
        var order = new StoreOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "old", [Line(1)]);
        order.ReplaceLines(" new ", [Line(4), new StoreOrderLine(Guid.NewGuid(), "B-1", "Cap", "عدد", 2)]);
        Assert.Equal("new", order.Note);
        Assert.Equal(2, order.Lines.Count);
        Assert.Throws<ArgumentException>(() => order.ReplaceLines(null, []));
        var one = Line();
        Assert.Throws<ArgumentException>(() => order.ReplaceLines(null, [one, new StoreOrderLine(one.ItemId, "A", "A", "عدد", 1)]));
        order.MarkIssued(Guid.NewGuid(), null);
        Assert.Throws<InvalidOperationException>(() => order.ReplaceLines(null, [Line()]));
    }

    [Fact]
    public void Lottery_draw_never_gives_out_more_than_the_stock()
    {
        var people = Enumerable.Range(0, 20).Select(_ => (Guid.NewGuid(), 3m)).ToList();
        for (var round = 0; round < 50; round++)
        {
            var winners = StoreLottery.Draw(people, 10);
            Assert.Equal(3, winners.Count); // 3 x 3 = 9 fits in 10, a fourth would need 12
            Assert.Equal(winners.Count, winners.Distinct().Count());
        }
    }

    [Fact]
    public void Lottery_draw_gives_everybody_a_win_when_stock_is_enough_and_skips_requests_that_do_not_fit()
    {
        var a = (Guid.NewGuid(), 2m); var b = (Guid.NewGuid(), 5m); var c = (Guid.NewGuid(), 1m);
        Assert.Equal(3, StoreLottery.Draw([a, b, c], 8).Count);
        // only 3 left: the big request can never win, the two small ones both can
        for (var i = 0; i < 30; i++)
        {
            var winners = StoreLottery.Draw([a, b, c], 3);
            Assert.DoesNotContain(b.Item1, winners);
            Assert.Equal(2, winners.Count);
        }
        Assert.Empty(StoreLottery.Draw([a, b, c], 0));
        Assert.Empty(StoreLottery.Draw([], 10));
    }

    [Fact]
    public void Lottery_draw_is_actually_random()
    {
        var a = (Guid.NewGuid(), 1m); var b = (Guid.NewGuid(), 1m);
        var seen = new HashSet<Guid>();
        for (var i = 0; i < 200; i++) seen.Add(StoreLottery.Draw([a, b], 1).Single());
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void Lottery_entry_moves_from_applied_to_one_final_state()
    {
        Assert.Throws<ArgumentException>(() => new StoreLotteryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));
        var e = new StoreLotteryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);
        Assert.Equal(LotteryEntryStatus.Applied, e.Status);
        e.MarkWon(Guid.NewGuid());
        Assert.Equal(LotteryEntryStatus.Won, e.Status);
        Assert.Throws<InvalidOperationException>(() => e.MarkLost());
        Assert.Throws<InvalidOperationException>(() => e.Withdraw());
        var w = new StoreLotteryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        w.Withdraw();
        Assert.Equal(LotteryEntryStatus.Withdrawn, w.Status);
        Assert.Throws<InvalidOperationException>(() => w.MarkWon(Guid.NewGuid()));
    }

    [Fact]
    public void Order_can_be_handed_over_only_once()
    {
        var order = new StoreOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, [Line()]);
        Assert.False(order.IsIssued);
        order.MarkIssued(Guid.NewGuid(), " ok ");
        Assert.True(order.IsIssued);
        Assert.Equal("ok", order.IssueNote);
        Assert.Throws<InvalidOperationException>(() => order.MarkIssued(Guid.NewGuid(), null));
    }

    [Fact]
    public void Store_order_is_a_workflow_type_but_not_creatable_from_generic_forms()
    {
        Assert.Contains(WorkflowTypeCode.StoreOrder, WorkflowFieldCatalog.AllTypes);
        Assert.DoesNotContain(WorkflowTypeCode.StoreOrder, WorkflowFieldCatalog.CreatableTypes);
        Assert.Contains(WorkflowFieldCatalog.GetFields(WorkflowTypeCode.StoreOrder), f => f.Key == "summary" && f.Required);
        Assert.NotEmpty(WorkflowFieldCatalog.GetDisplayName(WorkflowTypeCode.StoreOrder));
    }

    [Fact]
    public void Order_permissions_have_the_right_reach()
    {
        Assert.Equal(PermissionReach.Own, PermissionCatalog.ReachOf(Perm.StoreOrdersCreate));
        Assert.Equal(PermissionReach.All, PermissionCatalog.ReachOf(Perm.StoreOrdersIssue));
        Assert.True(PermissionCatalog.ActsOnOthers(Perm.StoreOrdersIssue));
        Assert.Contains(Perm.StoreOrdersIssue, PermissionCatalog.Codes);
    }

    [Fact]
    public void Item_is_listed_only_when_active_and_visible()
    {
        var item = new StoreItem("A", "Pen", Cat, "عدد", null);
        Assert.True(item.IsListed);
        item.SetVisible(false);
        Assert.False(item.IsListed);
        item.SetVisible(true);
        item.Deactivate();
        Assert.False(item.IsListed);
    }

    [Fact]
    public void Csv_quotes_cells_and_defuses_formulas()
    {
        Assert.Equal("a", StoreCsv.Cell("a"));
        Assert.Equal("\"a,b\"", StoreCsv.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", StoreCsv.Cell("say \"hi\""));
        Assert.Equal("'=SUM(A1)", StoreCsv.Cell("=SUM(A1)"));
        Assert.Equal("-3", StoreCsv.Cell("-3"));
        var bytes = StoreCsv.Build(["h1", "h2"], [["x", "y"]]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
    }

    [Fact]
    public void Reports_permission_is_registered_with_all_reach()
    {
        Assert.Contains(Perm.StoreReportsView, PermissionCatalog.Codes);
        Assert.Equal(PermissionReach.All, PermissionCatalog.ReachOf(Perm.StoreReportsView));
    }

    [Theory]
    [InlineData("عدد", 5, true)]
    [InlineData("عدد", 5.5, false)]
    [InlineData("کیلوگرم", 5.5, true)]
    [InlineData("کیلوگرم", 5.5555, false)]
    [InlineData("لیتر", 0, false)]
    [InlineData("واحد قدیمی", 1.5, true)]
    public void Counted_units_need_whole_numbers(string unit, double quantity, bool valid) =>
        Assert.Equal(valid, StoreUnits.IsValidQuantity(unit, (decimal)quantity));
}

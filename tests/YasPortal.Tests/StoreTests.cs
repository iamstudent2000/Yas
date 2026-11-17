using Xunit;
using YasPortal.Application.Authorization;
using YasPortal.Domain.Store;
using YasPortal.Domain.Workflows;
using YasPortal.Infrastructure.Store;

namespace YasPortal.Tests;

// Store tests: item and category validation, quantity entry with Persian digits, per-request limits, the request time window,
// stock movements, orders, the lottery draw, hand-over, report export and the store permissions.
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
    public void Request_window_is_checked_to_the_minute()
    {
        var item = new StoreItem("A", "Pen", Cat, "عدد", null, 0, new DateTime(2026, 11, 10), new DateTime(2026, 11, 20, 23, 59, 0), true);
        Assert.True(item.IsLottery);
        Assert.Equal(StoreRequestWindow.NotYet, item.WindowOn(new DateTime(2026, 11, 9, 23, 59, 0)));
        Assert.Equal(StoreRequestWindow.Open, item.WindowOn(new DateTime(2026, 11, 10)));
        Assert.Equal(StoreRequestWindow.Open, item.WindowOn(new DateTime(2026, 11, 20, 23, 59, 0)));
        Assert.Equal(StoreRequestWindow.Closed, item.WindowOn(new DateTime(2026, 11, 21)));
        var timed = new StoreItem("T", "Pen", Cat, "عدد", null, 0, new DateTime(2026, 11, 10, 9, 30, 0), new DateTime(2026, 11, 10, 17, 0, 0), true);
        Assert.Equal(StoreRequestWindow.NotYet, timed.WindowOn(new DateTime(2026, 11, 10, 9, 29, 0)));
        Assert.Equal(StoreRequestWindow.Open, timed.WindowOn(new DateTime(2026, 11, 10, 9, 30, 0)));
        Assert.Equal(StoreRequestWindow.Open, timed.WindowOn(new DateTime(2026, 11, 10, 17, 0, 0)));
        Assert.Equal(StoreRequestWindow.Closed, timed.WindowOn(new DateTime(2026, 11, 10, 17, 1, 0)));
        Assert.Throws<ArgumentException>(() => new StoreItem("U", "Pen", Cat, "عدد", null, 0, new DateTime(2026, 11, 10, 17, 0, 0), new DateTime(2026, 11, 10, 9, 0, 0)));
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
    public void Handover_can_be_undone_and_done_again()
    {
        var order = new StoreOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, [Line()]);
        Assert.Throws<InvalidOperationException>(() => order.UndoIssue());
        order.MarkIssued(Guid.NewGuid(), "x");
        order.UndoIssue();
        Assert.False(order.IsIssued);
        Assert.Null(order.IssueNote);
        order.MarkIssued(Guid.NewGuid(), null);
        Assert.True(order.IsIssued);
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

    // ---- prices, final price and the salary-deduction agreement ----

    private static StoreOrderLine Priced(decimal q, decimal price) => new(Guid.NewGuid(), "P-1", "Paper", "بسته", q, price);

    [Theory]
    [InlineData(3, 50000, 150000)]
    [InlineData(2.5, 10000, 25000)]
    [InlineData(0.333, 100, 33.3)]
    [InlineData(1.5, 0.01, 0.02)]   // 0.015 rounds up to 0.02, the way people round by hand
    [InlineData(4, 0, 0)]
    public void Line_total_is_unit_price_times_quantity(double quantity, double price, double expected) =>
        Assert.Equal((decimal)expected, StorePricing.LineTotal((decimal)price, (decimal)quantity));

    [Fact]
    public void Item_without_a_price_has_no_line_total_and_adds_nothing()
    {
        Assert.Null(StorePricing.LineTotal(null, 5));
        Assert.Equal(60m, StorePricing.Total([(10m, 2m), (null, 7m), (40m, 1m)]));
        Assert.False(StorePricing.NeedsDeductionConsent(0));
        Assert.True(StorePricing.NeedsDeductionConsent(0.01m));
    }

    [Fact]
    public void Order_line_keeps_the_price_it_was_sent_with()
    {
        var line = Priced(3, 50000);
        Assert.Equal(50000m, line.UnitPrice);
        Assert.Equal(150000m, line.LineTotal);
        Assert.Null(Line().LineTotal);
        Assert.Throws<ArgumentException>(() => new StoreOrderLine(Guid.NewGuid(), "A", "A", "عدد", 1, -1));
        Assert.Throws<ArgumentException>(() => new StoreOrderLine(Guid.NewGuid(), "A", "A", "عدد", 1, 1.005m));
    }

    [Fact]
    public void Order_total_is_the_sum_of_its_line_totals()
    {
        var who = Guid.NewGuid();
        var order = new StoreOrder(Guid.NewGuid(), who, Guid.NewGuid(), null, [Priced(3, 50000), Priced(2, 1250.5m), Line(4)], who);
        Assert.Equal(152501m, order.TotalPrice);
        Assert.True(order.RequiresSalaryDeduction);
    }

    [Fact]
    public void Order_that_costs_money_cannot_exist_without_the_salary_deduction_agreement()
    {
        var employee = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [Priced(1, 100)]));
        Assert.Throws<ArgumentException>(() => new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [Priced(1, 100)], Guid.Empty));

        var order = new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [Priced(2, 100)], employee);
        Assert.Equal(employee, order.SalaryDeductionAcceptedByEmployeeId);
        Assert.Equal(200m, order.SalaryDeductionAcceptedAmount);
        Assert.NotNull(order.SalaryDeductionAcceptedAtUtc);
        Assert.True(order.HasValidDeductionConsent);
    }

    [Fact]
    public void Administrator_who_places_an_order_for_someone_is_recorded_as_the_one_who_accepted()
    {
        var employee = Guid.NewGuid(); var admin = Guid.NewGuid();
        var order = new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [Priced(1, 100)], admin);
        Assert.Equal(employee, order.EmployeeId);
        Assert.Equal(admin, order.SalaryDeductionAcceptedByEmployeeId);
    }

    [Fact]
    public void Lottery_winner_keeps_the_time_they_agreed()
    {
        var agreed = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var employee = Guid.NewGuid();
        var order = new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [Priced(1, 100)], employee, agreed);
        Assert.Equal(agreed, order.SalaryDeductionAcceptedAtUtc);
    }

    [Fact]
    public void Order_without_prices_needs_and_carries_no_agreement()
    {
        var order = new StoreOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, [Line(2)]);
        Assert.Equal(0m, order.TotalPrice);
        Assert.False(order.RequiresSalaryDeduction);
        Assert.True(order.HasValidDeductionConsent);
        Assert.Null(order.SalaryDeductionAcceptedAtUtc);
        Assert.Null(order.SalaryDeductionAcceptedAmount);
        // a price of zero is also free
        Assert.False(new StoreOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, [Priced(5, 0)]).RequiresSalaryDeduction);
    }

    [Fact]
    public void Correcting_an_order_needs_the_agreement_again_for_the_new_total()
    {
        var employee = Guid.NewGuid();
        var order = new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), "old", [Priced(1, 100)], employee);
        // new total, no agreement: refused, and the order stays exactly as it was
        Assert.Throws<ArgumentException>(() => order.ReplaceLines("new", [Priced(5, 100)]));
        Assert.Equal(100m, order.TotalPrice);
        Assert.Equal("old", order.Note);
        Assert.Equal(100m, order.SalaryDeductionAcceptedAmount);
        // with the agreement: the recorded amount follows the new total
        order.ReplaceLines("new", [Priced(5, 100)], employee);
        Assert.Equal(500m, order.TotalPrice);
        Assert.Equal(500m, order.SalaryDeductionAcceptedAmount);
        Assert.True(order.HasValidDeductionConsent);
        // corrected down to something free: the old agreement is cleared
        order.ReplaceLines(null, [Line()]);
        Assert.Null(order.SalaryDeductionAcceptedAmount);
        Assert.True(order.HasValidDeductionConsent);
    }

    [Fact]
    public void Handed_over_order_cannot_be_repriced()
    {
        var employee = Guid.NewGuid();
        var order = new StoreOrder(Guid.NewGuid(), employee, Guid.NewGuid(), null, [Priced(1, 100)], employee);
        order.MarkIssued(Guid.NewGuid(), null);
        Assert.Throws<InvalidOperationException>(() => order.ReplaceLines(null, [Priced(9, 100)], employee));
        Assert.Throws<InvalidOperationException>(() => order.MarkIssued(Guid.NewGuid(), null)); // still only once
    }

    [Fact]
    public void Lottery_application_that_costs_money_needs_the_agreement()
    {
        Assert.Throws<ArgumentException>(() => new StoreLotteryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 1000));
        var e = new StoreLotteryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, 1000, true);
        Assert.Equal(1000m, e.UnitPrice);
        Assert.Equal(2000m, e.EstimatedTotal);
        Assert.NotNull(e.DeductionAcceptedAtUtc);
        // free items need no agreement and keep none
        var free = new StoreLotteryEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2);
        Assert.Null(free.EstimatedTotal);
        Assert.Null(free.DeductionAcceptedAtUtc);
        // the lottery draw itself is untouched by prices: never more than the stock
        Assert.Equal(3, StoreLottery.Draw(Enumerable.Range(0, 20).Select(_ => (Guid.NewGuid(), 3m)).ToList(), 10).Count);
    }

    [Fact]
    public void Item_price_is_a_validated_number()
    {
        Assert.Equal(50000m, new StoreItem("A", "Pen", Cat, "عدد", null, price: 50000).Price);
        Assert.Null(new StoreItem("B", "Pen", Cat, "عدد", null).Price);
        Assert.Throws<ArgumentException>(() => new StoreItem("C", "Pen", Cat, "عدد", null, price: -1));
        Assert.Throws<ArgumentException>(() => new StoreItem("D", "Pen", Cat, "عدد", null, price: 1.234m));
    }

    [Theory]
    [InlineData("50,000", 50000)]
    [InlineData("۵۰٬۰۰۰", 50000)]
    [InlineData("۵۰,۰۰۰", 50000)]
    [InlineData("12500.75", 12500.75)]
    [InlineData("۱۲۵۰۰٫۷۵", 12500.75)]
    [InlineData("1 250 000", 1250000)]
    [InlineData("  ", 0)]
    public void Typed_prices_treat_commas_as_thousands_separators(string text, double expected)
    {
        Assert.True(StoreNumber.TryParseMoney(text, out var v));
        Assert.Equal((decimal)expected, v);
        Assert.False(StoreNumber.TryParseMoney("abc", out _));
        Assert.False(StoreNumber.TryParseMoney("-5", out _));
    }
}

using Ashana.Application.Analytics;
using Ashana.Application.Common;
using Ashana.Application.Orders;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.UnitTests.Support;
using Microsoft.Extensions.Options;

namespace Ashana.UnitTests.Application;

public class AnalyticsTests
{
    private static readonly CanteenClock Clock = new(
        new ManualTimeProvider(TestData.Now),
        Options.Create(new CanteenOptions { TimeZone = "Ashana/Test", FallbackUtcOffsetHours = 5 }));

    private static readonly MenuItem Plov = TestData.Dish("Plov", 1800m, 100);
    private static readonly MenuItem Tea = TestData.Dish("Black tea", 300m, 100);

    [Fact]
    public void Revenue_counts_only_orders_that_were_picked_up()
    {
        var orders = new[]
        {
            Finished(OrderStatus.PickedUp, (Plov, 1)),            // 1800
            Finished(OrderStatus.PickedUp, (Plov, 1), (Tea, 2)),  // 2400
            Finished(OrderStatus.Cancelled, (Plov, 3)),
            Finished(OrderStatus.NoShow, (Tea, 1)),
        };

        var dashboard = AnalyticsService.Compute(orders, TestData.Today, TestData.Today);

        Assert.Equal(4200m, dashboard.Revenue);
        Assert.Equal(2100m, dashboard.AverageOrderValue);
        Assert.Equal((4, 2, 1, 1), (dashboard.Orders, dashboard.PickedUp, dashboard.Cancelled, dashboard.NoShows));
        Assert.Equal(0.25, dashboard.CancellationRate, 4);
        Assert.Equal(0.3333, dashboard.NoShowRate, 4); // no-shows among orders that were cooked
        Assert.Equal(1, dashboard.WastedPortions);
    }

    [Fact]
    public void Every_day_of_the_range_has_a_point_even_without_orders()
    {
        var dashboard = AnalyticsService.Compute([Finished(OrderStatus.PickedUp, (Plov, 1))], TestData.Today.AddDays(-6), TestData.Today);

        Assert.Equal(7, dashboard.Daily.Count);
        Assert.Equal(0, dashboard.Daily[0].Orders);
        Assert.Equal((1, 1800m), (dashboard.Daily[^1].Orders, dashboard.Daily[^1].Revenue));
    }

    [Fact]
    public void Busiest_slot_top_dishes_and_kitchen_time()
    {
        var orders = new[]
        {
            Finished(OrderStatus.PickedUp, 12, 0, (Plov, 2)),
            Finished(OrderStatus.PickedUp, 12, 0, (Tea, 1)),
            Finished(OrderStatus.PickedUp, 13, 0, (Plov, 1), (Tea, 4)),
        };

        var dashboard = AnalyticsService.Compute(orders, TestData.Today, TestData.Today);

        Assert.Equal(new TimeOnly(12, 0), dashboard.BusiestSlot!.Start);
        Assert.Equal([("Black tea", 5), ("Plov", 3)], dashboard.TopDishes.Select(d => (d.Name, d.Portions)));
        Assert.Equal(TimeSpan.FromMinutes(8), dashboard.AveragePrepTime);
    }

    [Fact]
    public void Empty_period_gives_zeros_not_errors()
    {
        var dashboard = AnalyticsService.Compute([], TestData.Today, TestData.Today);

        Assert.Equal((0m, 0m, 0.0, 0.0), (dashboard.Revenue, dashboard.AverageOrderValue, dashboard.CancellationRate, dashboard.NoShowRate));
        Assert.Null(dashboard.AveragePrepTime);
        Assert.Null(dashboard.BusiestSlot);
    }

    [Fact]
    public void Csv_export_has_a_header_and_one_row_per_order()
    {
        var order = Finished(OrderStatus.PickedUp, (Plov, 2), (Tea, 1));
        var csv = AnalyticsService.BuildCsv([order]);
        var lines = csv.TrimEnd().Split(Environment.NewLine);

        Assert.Equal("Date,Number,Slot,Customer,Status,Payment,Portions,Total,Items,PlacedAt,PickedUpAt", lines[0]);
        Assert.StartsWith("2026-10-30,101,12:00-12:15,Aruzhan Bekova,PickedUp,Card,3,3900,\"2x Plov; 1x Black tea\",", lines[1]);
        Assert.Equal(2, lines.Length);
        Assert.Equal(AnalyticsService.ToCsvRow(order), lines[1]); // the file is just the header plus mapped rows
    }

    [Theory]
    [InlineData("Plov", "Plov")]
    [InlineData("Plov, large", "\"Plov, large\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("=HYPERLINK(1)", "'=HYPERLINK(1)")]   // a formula would run in Excel: neutralised
    [InlineData("+7 701", "'+7 701")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-1", "'-1")]
    public void Csv_cells_are_escaped_and_formulas_neutralised(string value, string expected) =>
        Assert.Equal(expected, AnalyticsService.Escape(value));

    [Fact]
    public void Date_range_is_ordered_and_limited()
    {
        var (from, to) = AnalyticsService.ClampRange(TestData.Today, TestData.Today.AddDays(-3));
        var (longFrom, longTo) = AnalyticsService.ClampRange(TestData.Today.AddDays(-400), TestData.Today);

        Assert.Equal((TestData.Today.AddDays(-3), TestData.Today), (from, to));
        Assert.Equal(AnalyticsService.MaxRangeDays, longTo.DayNumber - longFrom.DayNumber + 1);
    }

    private static OrderDto Finished(OrderStatus status, params (MenuItem Item, int Quantity)[] lines) =>
        Finished(status, 12, 0, lines);

    private static OrderDto Finished(OrderStatus status, int hour, int minute, params (MenuItem Item, int Quantity)[] lines)
    {
        var placed = TestData.NewOrder(Guid.NewGuid(), TestData.Slot(hour, minute), lines);
        var start = TestData.At(hour, minute).AddMinutes(-12);
        var ready = placed.StartCooking(start).Bind(o => o.MarkReady(start.AddMinutes(8)));

        // Each status is a chain of transitions; pattern matching picks the chain.
        var order = status switch
        {
            OrderStatus.Cancelled => placed.CancelByKitchen("Test", start),
            OrderStatus.PickedUp => ready.Bind(o => o.MarkPickedUp(start.AddMinutes(12))),
            OrderStatus.NoShow => ready.Bind(o => o.MarkNoShow(start.AddMinutes(60))),
            _ => Result.Success(placed),
        };

        return OrderDto.From(order.Value, Clock);
    }
}

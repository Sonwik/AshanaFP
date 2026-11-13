using System.Globalization;
using System.Text;
using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.Orders;
using Ashana.Domain.Orders;

namespace Ashana.Application.Analytics;

public sealed record DailyPoint(DateOnly Date, int Orders, decimal Revenue);

public sealed record TimeOfDayPoint(TimeOnly Start, int Orders)
{
    public string Label => Start.ToString("HH:mm", CultureInfo.InvariantCulture);
}

public sealed record TopDish(string Name, string Emoji, int Portions, decimal Revenue);

/// <summary>Numbers for the admin dashboard.</summary>
public sealed record Dashboard(
    DateOnly From,
    DateOnly To,
    int Orders,
    int PickedUp,
    int Cancelled,
    int NoShows,
    decimal Revenue,
    decimal AverageOrderValue,
    double CancellationRate,
    double NoShowRate,
    TimeSpan? AveragePrepTime,
    int WastedPortions,
    IReadOnlyList<DailyPoint> Daily,
    IReadOnlyList<TimeOfDayPoint> PeakTimes,
    IReadOnlyList<TopDish> TopDishes)
{
    public TimeOfDayPoint? BusiestSlot => PeakTimes.OrderByDescending(p => p.Orders).FirstOrDefault();
}

/// <summary>
/// Turns orders into business insight: revenue, peak times (to plan staff), top dishes (to plan cooking)
/// and no-shows (food that was cooked but wasted).
/// </summary>
public sealed class AnalyticsService
{
    public const int MaxRangeDays = 62;

    private readonly IStateStore _store;
    private readonly CanteenClock _clock;

    public AnalyticsService(IStateStore store, CanteenClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<Dashboard> GetDashboardAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        (from, to) = ClampRange(from, to);
        return Task.FromResult(Compute(OrdersIn(from, to), from, to));
    }

    public Task<string> ExportCsvAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        (from, to) = ClampRange(from, to);
        return Task.FromResult(BuildCsv(OrdersIn(from, to).OrderBy(o => o.ServiceDate).ThenBy(o => o.Number)));
    }

    /// <summary>A lazy query over the current snapshot: nothing runs until the caller enumerates it.</summary>
    private IEnumerable<OrderDto> OrdersIn(DateOnly from, DateOnly to) =>
        _store.Current.Orders.Values
            .Where(o => o.ServiceDate >= from && o.ServiceDate <= to)
            .Select(OrderDto.In(_clock.TimeZone));

    /// <summary>Pure calculation (no storage), so it is easy to test with hand-made orders.</summary>
    public static Dashboard Compute(IEnumerable<OrderDto> source, DateOnly from, DateOnly to)
    {
        var orders = source.Where(o => o.ServiceDate >= from && o.ServiceDate <= to).ToList();
        var pickedUp = orders.Where(o => o.Status == OrderStatus.PickedUp).ToList();
        var cancelled = orders.Count(o => o.Status == OrderStatus.Cancelled);
        var noShows = orders.Where(o => o.Status == OrderStatus.NoShow).ToList();
        var notCancelled = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();

        // Payment happens at the counter, so only picked-up orders count as revenue.
        var revenue = pickedUp.Sum(o => o.Total);
        var prepTimes = orders.Select(o => o.PrepDuration).OfType<TimeSpan>().ToList();

        var daily = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(from.AddDays)
            .Select(day => new DailyPoint(
                day,
                notCancelled.Count(o => o.ServiceDate == day),
                pickedUp.Where(o => o.ServiceDate == day).Sum(o => o.Total)))
            .ToList();

        var peaks = notCancelled
            .GroupBy(o => TimeOnly.FromDateTime(o.PickupStart.DateTime))
            .Select(g => new TimeOfDayPoint(g.Key, g.Count()))
            .OrderBy(p => p.Start)
            .ToList();

        var topDishes = notCancelled
            .SelectMany(o => o.Lines)
            .GroupBy(l => l.MenuItemId)
            .Select(g => new TopDish(g.First().Name, g.First().Emoji, g.Sum(l => l.Quantity), g.Sum(l => l.LineTotal)))
            .OrderByDescending(d => d.Portions)
            .ThenBy(d => d.Name, StringComparer.CurrentCulture)
            .Take(5)
            .ToList();

        return new Dashboard(
            from,
            to,
            orders.Count,
            pickedUp.Count,
            cancelled,
            noShows.Count,
            revenue,
            pickedUp.Count == 0 ? 0 : decimal.Round(revenue / pickedUp.Count, 0),
            Ratio(cancelled, orders.Count),
            Ratio(noShows.Count, pickedUp.Count + noShows.Count),
            prepTimes.Count == 0 ? null : TimeSpan.FromSeconds(prepTimes.Average(t => t.TotalSeconds)),
            noShows.Sum(o => o.PortionCount),
            daily,
            peaks,
            topDishes);
    }

    public const string CsvHeader = "Date,Number,Slot,Customer,Status,Payment,Portions,Total,Items,PlacedAt,PickedUpAt";

    /// <summary>
    /// CSV for Excel, as a pipeline: every order is mapped to a row (a pure function), the rows are joined.
    /// Values are quoted when needed, and formulas are neutralized (CSV injection).
    /// </summary>
    public static string BuildCsv(IEnumerable<OrderDto> orders) =>
        new StringBuilder()
            .AppendLine(CsvHeader)
            .AppendJoin(string.Empty, orders.Select(ToCsvRow).Select(row => row + Environment.NewLine))
            .ToString();

    /// <summary>One order as one CSV line.</summary>
    public static string ToCsvRow(OrderDto o)
    {
        ArgumentNullException.ThrowIfNull(o);
        return string.Join(',',
            o.ServiceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            o.Number.ToString(CultureInfo.InvariantCulture),
            Escape(o.SlotLabel.Replace('–', '-')),
            Escape(o.CustomerName),
            o.Status.ToString(),
            o.Payment.ToString(),
            o.PortionCount.ToString(CultureInfo.InvariantCulture),
            o.Total.ToString("0.##", CultureInfo.InvariantCulture),
            Escape(string.Join("; ", o.Lines.Select(l => $"{l.Quantity}x {l.Name}"))),
            o.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            o.PickedUpAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty);
    }

    internal static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r', ';']) >= 0
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }

    private static double Ratio(int part, int whole) => whole == 0 ? 0 : Math.Round((double)part / whole, 4);

    internal static (DateOnly From, DateOnly To) ClampRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        return to.DayNumber - from.DayNumber >= MaxRangeDays ? (to.AddDays(-(MaxRangeDays - 1)), to) : (from, to);
    }
}

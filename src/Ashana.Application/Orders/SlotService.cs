using System.Globalization;
using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Domain.Canteen;

namespace Ashana.Application.Orders;

/// <summary>Builds the slot picker: every slot of the service day with its load and whether it can be booked.</summary>
public sealed class SlotService
{
    /// <summary>From this load a slot is shown as "busy".</summary>
    public const double BusyThreshold = 0.75;

    private readonly IStateStore _store;
    private readonly CanteenClock _clock;

    public SlotService(IStateStore store, CanteenClock clock)
    {
        _store = store;
        _clock = clock;
    }

    public Task<SlotOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var state = _store.Current;
        var rules = state.Rules;
        var now = _clock.Now;
        var day = SlotPlanner.ServiceDay(now, rules);

        var slotDay = BuildDay(day, now, _clock.Today, rules, _clock.TimeZone, state.BookedBySlot(day));
        return Task.FromResult(new SlotOverview([slotDay], rules.AcceptingOrders, rules.PauseMessage));
    }

    /// <summary>The slot picker for one day as a pure function of its inputs.</summary>
    public static SlotDay BuildDay(
        DateOnly day,
        DateTimeOffset now,
        DateOnly today,
        CanteenRules rules,
        TimeZoneInfo zone,
        IReadOnlyDictionary<DateTimeOffset, int> booked)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(booked);

        // Partial application: the rules and thresholds are fixed once, leaving a function of (slot, count).
        var classify = F.Curry<SlotThresholds, int, SlotState>(Classify)(new SlotThresholds(
            rules.MaxOrdersPerSlot,
            (int)Math.Ceiling(rules.MaxOrdersPerSlot * BusyThreshold)));
        var earliest = SlotPlanner.EarliestPickup(now, rules);

        var options = SlotPlanner.GenerateSlots(day, rules, zone)
            .Where(slot => slot.End > now) // finished slots are not interesting
            .Select(slot => (Slot: slot, Count: booked.GetValueOrDefault(slot.Start)))
            .Select(x => new SlotOption(
                x.Slot.Start,
                x.Slot.End,
                x.Count,
                rules.MaxOrdersPerSlot,
                x.Slot.Start < earliest ? SlotState.TooSoon : classify(x.Count)))
            .ToList();

        return new SlotDay(day, DayTitle(day, today), options);
    }

    /// <summary>Load thresholds of a slot.</summary>
    public sealed record SlotThresholds(int Capacity, int BusyFrom);

    /// <summary>A switch expression with guards: how full is a slot with <paramref name="count"/> orders?</summary>
    public static SlotState Classify(SlotThresholds thresholds, int count)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        return count switch
        {
            _ when count >= thresholds.Capacity => SlotState.Full,
            _ when count >= thresholds.BusyFrom => SlotState.Busy,
            _ => SlotState.Available,
        };
    }

    public static string DayTitle(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return "Today";
        }

        return day == today.AddDays(1)
            ? "Tomorrow"
            : day.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
    }
}

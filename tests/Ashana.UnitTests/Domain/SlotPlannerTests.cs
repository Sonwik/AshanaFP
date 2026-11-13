using System.Globalization;
using Ashana.Domain.Canteen;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Domain;

public class SlotPlannerTests
{
    private static readonly CanteenRules Rules = TestData.Rules; // 08:00–20:00, 15 min, 20 per slot, 10 min lead

    [Fact]
    public void Opening_hours_are_cut_into_equal_slots()
    {
        var slots = SlotPlanner.GenerateSlots(TestData.Today, Rules, TestData.Zone).ToList();

        Assert.Equal(48, slots.Count);
        Assert.Equal("08:00–08:15", slots[0].Label);
        Assert.Equal("19:45–20:00", slots[^1].Label);
        Assert.All(slots, slot => Assert.Equal(TestData.Offset, slot.Start.Offset));
        Assert.All(slots.Zip(slots.Skip(1)), pair => Assert.Equal(pair.First.End, pair.Second.Start));
    }

    [Fact]
    public void A_partial_slot_at_closing_time_is_not_offered()
    {
        var rules = Rules with { OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(9, 50) };

        var slots = SlotPlanner.GenerateSlots(TestData.Today, rules, TestData.Zone).ToList();

        Assert.Equal(7, slots.Count);
        Assert.Equal("09:30–09:45", slots[^1].Label);
    }

    [Theory]
    [InlineData("2026-10-30T07:00:00+05:00", "2026-10-30")]
    [InlineData("2026-10-30T19:59:00+05:00", "2026-10-30")]
    [InlineData("2026-10-30T20:00:00+05:00", "2026-10-31")] // after closing, students order for tomorrow
    [InlineData("2026-10-30T23:30:00+05:00", "2026-10-31")]
    public void Service_day_is_today_until_closing_then_tomorrow(string now, string expectedDay)
    {
        var localNow = DateTimeOffset.Parse(now, CultureInfo.InvariantCulture);

        Assert.Equal(DateOnly.Parse(expectedDay, CultureInfo.InvariantCulture), SlotPlanner.ServiceDay(localNow, Rules));
    }

    [Fact]
    public void A_free_slot_after_the_lead_time_is_accepted()
    {
        var result = SlotPlanner.ValidateBooking(TestData.At(10, 15), TestData.At(10, 0), Rules, TestData.Zone, _ => 19);

        Assert.True(result.IsSuccess);
        Assert.Equal(TestData.At(10, 30), result.Value.End);
    }

    [Fact]
    public void The_slot_time_can_arrive_in_any_offset()
    {
        var sameMomentInUtc = TestData.At(10, 15).ToUniversalTime();

        Assert.True(SlotPlanner.ValidateBooking(sameMomentInUtc, TestData.At(10, 0), Rules, TestData.Zone, _ => 0).IsSuccess);
    }

    [Theory]
    [InlineData(10, 0)]  // starts in 5 minutes, but the kitchen needs 10
    [InlineData(9, 0)]   // already in the past
    public void Slots_inside_the_lead_time_are_too_late(int hour, int minute)
    {
        var result = SlotPlanner.ValidateBooking(TestData.At(hour, minute), TestData.At(9, 55), Rules, TestData.Zone, _ => 0);

        Assert.Equal("slot.too_late", result.Error.Code);
    }

    [Fact]
    public void A_full_slot_is_refused() =>
        Assert.Equal(SlotPlanner.SlotFull, SlotPlanner.ValidateBooking(TestData.At(12, 0), TestData.Now, Rules, TestData.Zone, _ => 20).Error);

    [Fact]
    public void A_time_between_slots_is_refused() =>
        Assert.Equal(SlotPlanner.UnknownSlot, SlotPlanner.ValidateBooking(TestData.At(12, 7), TestData.Now, Rules, TestData.Zone, _ => 0).Error);

    [Fact]
    public void Tomorrow_cannot_be_booked_while_today_is_open() =>
        Assert.Equal(SlotPlanner.WrongDay, SlotPlanner.ValidateBooking(TestData.At(12, 0, dayOffset: 1), TestData.Now, Rules, TestData.Zone, _ => 0).Error);

    [Fact]
    public void After_closing_the_first_slots_of_tomorrow_can_be_booked() =>
        Assert.True(SlotPlanner.ValidateBooking(TestData.At(8, 0, dayOffset: 1), TestData.At(20, 30), Rules, TestData.Zone, _ => 0).IsSuccess);

    [Fact]
    public void Nothing_can_be_booked_while_orders_are_paused()
    {
        var paused = Rules with { AcceptingOrders = false };

        Assert.Equal(SlotPlanner.OrdersPaused, SlotPlanner.ValidateBooking(TestData.At(12, 0), TestData.Now, paused, TestData.Zone, _ => 0).Error);
    }

    [Fact]
    public void Slots_are_generated_lazily_so_taking_two_computes_only_two()
    {
        var produced = 0;
        var zone = TimeZoneInfo.CreateCustomTimeZone("Counting", TestData.Offset, "c", "c");

        var firstTwo = SlotPlanner.GenerateSlots(TestData.Today, Rules, zone)
            .Select(slot =>
            {
                produced++;
                return slot;
            })
            .Take(2)
            .ToList();

        Assert.Equal(2, firstTwo.Count);
        Assert.Equal(2, produced);
    }

    [Fact]
    public void A_full_slot_suggests_the_next_free_one()
    {
        var full = new HashSet<DateTimeOffset> { TestData.At(12, 0), TestData.At(12, 15) };

        var result = SlotPlanner.ValidateBooking(TestData.At(12, 0), TestData.Now, Rules, TestData.Zone, start => full.Contains(start) ? 20 : 3);

        Assert.Equal("slot.full", result.Error.Code);
        Assert.Contains("12:30–12:45", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_free_slot_is_found_by_stack_safe_recursion_even_over_many_slots()
    {
        var slots = Enumerable.Range(0, 200_000)
            .Select(i => new PickupSlot(TestData.At(0, 0).AddMinutes(i), TestData.At(0, 1).AddMinutes(i)))
            .ToList();

        var found = SlotPlanner.NextFreeSlot(slots, slots[0].Start, slot => slot == slots[^1]); // 200,000 steps deep

        Assert.Equal(Option.Some(slots[^1]), found);
        Assert.True(SlotPlanner.NextFreeSlot(slots, slots[0].Start, _ => false).IsNone);
    }

    [Fact]
    public void Earliest_pickup_adds_the_lead_time() =>
        Assert.Equal(TestData.At(10, 10), SlotPlanner.EarliestPickup(TestData.Now, Rules));
}

public class CanteenSettingsTests
{
    public static TheoryData<CanteenRules, string> InvalidRules => new()
    {
        { TestData.Rules with { OpensAt = new TimeOnly(20, 0), ClosesAt = new TimeOnly(8, 0) }, "settings.hours" },
        { TestData.Rules with { SlotMinutes = 4 }, "settings.slot_length" },
        { TestData.Rules with { SlotMinutes = 61 }, "settings.slot_length" },
        { TestData.Rules with { OpensAt = new TimeOnly(8, 0), ClosesAt = new TimeOnly(8, 10), SlotMinutes = 15 }, "settings.too_short" },
        { TestData.Rules with { MaxOrdersPerSlot = 0 }, "settings.capacity" },
        { TestData.Rules with { MinLeadMinutes = -1 }, "settings.lead" },
        { TestData.Rules with { NoShowAfterMinutes = 4 }, "settings.no_show" },
        { TestData.Rules with { MaxActiveOrdersPerUser = 11 }, "settings.active_limit" },
        { TestData.Rules with { PauseMessage = new string('m', 141) }, "settings.pause_message" },
    };

    [Fact]
    public void Default_rules_are_valid() =>
        Assert.True(CanteenSettings.Validate(TestData.Rules).IsSuccess);

    [Theory]
    [MemberData(nameof(InvalidRules))]
    public void Invalid_rules_are_rejected_with_a_clear_reason(CanteenRules rules, string expectedCode) =>
        Assert.Equal(expectedCode, CanteenSettings.Validate(rules).Error.Code);

    [Fact]
    public void Update_returns_new_settings_with_normalized_rules_and_a_new_version()
    {
        var settings = CanteenSettings.CreateDefault();

        var updated = settings.Update(TestData.Rules with { SlotMinutes = 20, AcceptingOrders = false, PauseMessage = "  Back at 12:30  " }).Value;

        Assert.Equal(20, updated.Rules.SlotMinutes);
        Assert.Equal("Back at 12:30", updated.Rules.PauseMessage);
        Assert.Equal(1, updated.Version);
        Assert.Equal((15, 0), (settings.Rules.SlotMinutes, settings.Version)); // the original is untouched
    }

    [Fact]
    public void Update_with_invalid_rules_is_an_error() =>
        Assert.Equal("settings.slot_length", CanteenSettings.CreateDefault().Update(TestData.Rules with { SlotMinutes = 1 }).Error.Code);

    [Fact]
    public void Stock_reset_is_remembered_per_day()
    {
        var settings = CanteenSettings.CreateDefault();

        var today = settings.MarkStockReset(TestData.Today);

        Assert.True(today.IsSome);
        Assert.True(today.Bind(s => s.MarkStockReset(TestData.Today)).IsNone);
        Assert.True(today.Bind(s => s.MarkStockReset(TestData.Today.AddDays(1))).IsSome);
    }
}

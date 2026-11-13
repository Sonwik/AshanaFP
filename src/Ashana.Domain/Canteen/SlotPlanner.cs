namespace Ashana.Domain.Canteen;

/// <summary>
/// Pure functions that cut the working day into pickup slots and decide whether a slot can still be booked.
/// No database, no clock: everything comes in as parameters, which makes the rules easy to unit-test.
/// </summary>
/// <remarks>
/// Students always order for the <b>service day</b>: today until closing time, then tomorrow.
/// So at 21:00 the app takes orders for tomorrow's lunch, and daily portions belong to one service day.
/// </remarks>
public static class SlotPlanner
{
    public static readonly Error OrdersPaused = Error.Failure("slot.paused", "The canteen is not accepting new orders right now.");
    public static readonly Error UnknownSlot = Error.Validation("slot.unknown", "This pickup time is not one of the canteen's slots.");
    public static readonly Error WrongDay = Error.Validation("slot.wrong_day", "Orders can only be placed for the current service day.");
    public static readonly Error SlotFull = Error.Conflict("slot.full", "This pickup time is fully booked. Please choose another one.");

    public static Error TooLate(int minLeadMinutes) =>
        Error.Conflict("slot.too_late", $"Orders must be placed at least {minLeadMinutes} minutes before pickup. Please choose a later time.");

    /// <summary>A full slot, with the next free one suggested when there is one.</summary>
    public static Error Full(Option<PickupSlot> nextFree) => nextFree.Match(
        slot => Error.Conflict("slot.full", $"This pickup time is fully booked. The next free time is {slot.Label}."),
        () => SlotFull);

    /// <summary>Canteen-local day students are ordering for right now.</summary>
    /// <param name="localNow">Current canteen-local time.</param>
    /// <param name="rules">Canteen rules.</param>
    public static DateOnly ServiceDay(DateTimeOffset localNow, CanteenRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var today = DateOnly.FromDateTime(localNow.DateTime);
        return TimeOnly.FromDateTime(localNow.DateTime) < rules.ClosesAt ? today : today.AddDays(1);
    }

    /// <summary>
    /// All slots of one canteen-local day, produced <b>lazily</b> by unfolding from the opening time:
    /// each step yields one slot and the start of the next, and stops when a slot would end after closing.
    /// </summary>
    public static IEnumerable<PickupSlot> GenerateSlots(DateOnly day, CanteenRules rules, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(timeZone);

        var close = day.ToDateTime(rules.ClosesAt);
        var length = TimeSpan.FromMinutes(rules.SlotMinutes);

        return Seq.Unfold(day.ToDateTime(rules.OpensAt), start =>
            start + length <= close
                ? Option.Some((new PickupSlot(ToLocalOffset(start, timeZone), ToLocalOffset(start + length, timeZone)), start + length))
                : Option<(PickupSlot, DateTime)>.None);
    }

    /// <summary>Earliest moment a slot may start if an order is placed at <paramref name="now"/>.</summary>
    public static DateTimeOffset EarliestPickup(DateTimeOffset now, CanteenRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return now.AddMinutes(rules.MinLeadMinutes);
    }

    /// <summary>
    /// Checks every rule for booking <paramref name="slotStart"/>: the canteen accepts orders,
    /// the slot belongs to the service day, it is not too soon, and it still has capacity.
    /// Written as one railway: each step either passes the slot on or switches to the error track.
    /// </summary>
    /// <param name="slotStart">Requested slot start.</param>
    /// <param name="now">Current time.</param>
    /// <param name="rules">Canteen rules.</param>
    /// <param name="timeZone">Canteen time zone.</param>
    /// <param name="bookedIn">How many orders are already booked in a slot (cancelled ones excluded).</param>
    /// <returns>The matching slot, or the reason it cannot be booked.</returns>
    public static Result<PickupSlot> ValidateBooking(
        DateTimeOffset slotStart,
        DateTimeOffset now,
        CanteenRules rules,
        TimeZoneInfo timeZone,
        Func<DateTimeOffset, int> bookedIn)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(bookedIn);

        var localStart = TimeZoneInfo.ConvertTime(slotStart, timeZone);
        var day = DateOnly.FromDateTime(localStart.DateTime);
        var serviceDay = ServiceDay(TimeZoneInfo.ConvertTime(now, timeZone), rules);
        var earliest = EarliestPickup(now, rules);
        bool HasRoom(PickupSlot slot) => bookedIn(slot.Start) < rules.MaxOrdersPerSlot;

        return Result.Require(rules.AcceptingOrders, OrdersPaused)
            .Bind(_ => Result.Require(day == serviceDay, slotStart < now ? TooLate(rules.MinLeadMinutes) : WrongDay))
            .Bind(_ => GenerateSlots(day, rules, timeZone).FirstOrNone(s => s.Start == localStart).ToResult(UnknownSlot))
            .Ensure(slot => slot.Start >= earliest, TooLate(rules.MinLeadMinutes))
            .Ensure(HasRoom, slot => Full(NextFreeSlot(GenerateSlots(day, rules, timeZone).ToList(), slot.End, HasRoom)));
    }

    /// <summary>
    /// The first slot starting at or after <paramref name="from"/> that satisfies <paramref name="isFree"/>.
    /// Written as tail recursion over the index and run on a <see cref="Trampoline{T}"/>, so it is stack-safe
    /// however many slots a day has.
    /// </summary>
    public static Option<PickupSlot> NextFreeSlot(IReadOnlyList<PickupSlot> slots, DateTimeOffset from, Func<PickupSlot, bool> isFree)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(isFree);

        Trampoline<Option<PickupSlot>> Search(int index) =>
            index >= slots.Count ? Trampoline.Done(Option<PickupSlot>.None)
            : slots[index].Start >= from && isFree(slots[index]) ? Trampoline.Done(Option.Some(slots[index]))
            : Trampoline.More(() => Search(index + 1));

        return Search(0).Run();
    }

    /// <summary>Converts a canteen-local wall-clock time into a <see cref="DateTimeOffset"/> with the right offset.</summary>
    public static DateTimeOffset ToLocalOffset(DateTime localWallClock, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var unspecified = DateTime.SpecifyKind(localWallClock, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }
}

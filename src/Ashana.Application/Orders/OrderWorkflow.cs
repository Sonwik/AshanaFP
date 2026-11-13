using System.Collections.Immutable;
using Ashana.Application.Menu;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;

namespace Ashana.Application.Orders;

/// <summary>The result of placing an order: the order, whether it is new (or a repeated request), and the dishes whose stock changed.</summary>
public sealed record Placement(Order Order, bool IsNew, ImmutableList<MenuItem> ReservedItems);

/// <summary>A cancelled order and the dishes whose portions went back to the menu.</summary>
public sealed record Cancellation(Order Order, ImmutableList<MenuItem> ReleasedItems);

/// <summary>Portions taken from one dish: the dish before and after, and how many.</summary>
internal sealed record Reservation(MenuItem Before, MenuItem After, int Quantity);

/// <summary>
/// The order use cases as <b>pure transitions</b> over <see cref="CanteenState"/>.
/// Nothing here reads the clock, logs, saves or publishes: time and the time zone come in as arguments,
/// and the new state comes out as a value. That is why every rule can be tested with plain inputs.
/// </summary>
public static class OrderWorkflow
{
    /// <summary>Order numbers start here every day, so a code like "#107" is easy to say aloud.</summary>
    public const int FirstOrderNumber = 101;

    /// <summary>Checks on the incoming command that need no state.</summary>
    public static readonly ImmutableArray<Rule<PlaceOrderCommand>> CommandRules =
    [
        Validation.Rule<PlaceOrderCommand>(c => c.Items.Count > 0, OrderErrors.EmptyCart),
        Validation.Rule<PlaceOrderCommand>(c => c.Items.Count <= OrderErrors.MaxLines, OrderErrors.TooManyLines),
        Validation.Rule<PlaceOrderCommand>(c => c.Items.All(i => i.Quantity is >= 1 and <= OrderErrors.MaxQuantityPerLine), OrderErrors.QuantityOutOfRange),
        Validation.Rule<PlaceOrderCommand>(c => c.Items.Select(i => i.MenuItemId).Distinct().Count() == c.Items.Count, OrderErrors.DuplicateLine),
        Validation.Rule<PlaceOrderCommand>(c => c.Comment is not { Length: > OrderErrors.CommentMaxLength }, OrderErrors.CommentTooLong),
        Validation.Rule<PlaceOrderCommand>(c => c.ClientRequestId != Guid.Empty, Error.Validation("order.request_id", "Missing request id.")),
    ];

    /// <summary>
    /// Places an order. Idempotent: the same checkout submitted twice returns the first order unchanged.
    /// </summary>
    public static Result<Change<Placement>> Place(CanteenState state, PlaceOrderCommand command, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        return command.Check(CommandRules).Bind(valid =>
            state.FindByClientRequest(valid.CustomerId, valid.ClientRequestId).Match(
                existing => Result.Success(Change.Of(state, new Placement(existing, IsNew: false, []))),
                () => PlaceNew(state, valid, now, zone)));
    }

    /// <summary>
    /// The heart of the app, written as one LINQ query over <see cref="Result{T}"/>.
    /// Each <c>from</c> is a step that can fail; the first failure skips the rest and becomes the answer.
    /// </summary>
    private static Result<Change<Placement>> PlaceNew(CanteenState state, PlaceOrderCommand command, DateTimeOffset now, TimeZoneInfo zone)
    {
        var rules = state.Rules;
        var fresh = StockDay.EnsureCurrent(state, SlotPlanner.ServiceDay(TimeZoneInfo.ConvertTime(now, zone), rules));

        return
            from customer in fresh.FindUser(command.CustomerId).ToResult(UserErrors.NotFound)
            from active in Result.Require(customer.IsActive, OrderErrors.UserInactive)
            from slot in SlotPlanner.ValidateBooking(command.SlotStart, now, rules, zone, fresh.CountBookedInSlot)
            from limit in Result.Require(
                fresh.CountActiveFor(customer.Id) < rules.MaxActiveOrdersPerUser,
                OrderErrors.TooManyActiveOrders(rules.MaxActiveOrdersPerUser))
            from reserved in command.Items.Traverse(line => Reserve(fresh, line))
            from order in Order.Create(new OrderDraft(
                NextNumber(fresh, slot.Day),
                slot.Day,
                customer.Id,
                customer.DisplayName,
                slot,
                command.Payment,
                command.Comment,
                [.. reserved.Select(r => new OrderLineDraft(r.Before, r.Quantity))],
                now,
                command.ClientRequestId))
            let reservedItems = reserved.Select(r => r.After).ToImmutableList()
            select Change.Of(
                fresh.WithAll(reservedItems).With(order),
                new Placement(order, IsNew: true, reservedItems));
    }

    /// <summary>One cart line: the dish must exist, keep its price, and have enough portions.</summary>
    private static Result<Reservation> Reserve(CanteenState state, CartItemRequest line) =>
        state.FindItem(line.MenuItemId)
            .Where(item => !item.IsArchived)
            .ToResult(MenuErrors.ItemNotFound)
            .Ensure(item => item.Price == line.ExpectedUnitPrice, item => OrderErrors.PriceChanged(item.Name))
            .Bind(item => item.Reserve(line.Quantity).Map(after => new Reservation(item, after, line.Quantity)));

    private static int NextNumber(CanteenState state, DateOnly day) => Math.Max(state.MaxNumber(day), FirstOrderNumber - 1) + 1;

    /// <summary>The student cancels before cooking starts; the portions go back to the menu.</summary>
    public static Result<Change<Cancellation>> CancelByCustomer(CanteenState state, Guid orderId, Guid customerId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.FindOrder(orderId)
            .ToResult(OrderErrors.NotFound)
            .Bind(order => order.CancelByCustomer(customerId, now).Map(cancelled => (Before: order, After: cancelled)))
            .Map(pair => ReleaseStock(state.With(pair.After), pair.Before, pair.After));
    }

    /// <summary>The kitchen cancels (for example, an ingredient ran out). The student sees the reason.</summary>
    public static Result<Change<Cancellation>> CancelByKitchen(CanteenState state, Guid orderId, string reason, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.FindOrder(orderId)
            .ToResult(OrderErrors.NotFound)
            .Bind(order => order.CancelByKitchen(reason, now).Map(cancelled => (Before: order, After: cancelled)))
            .Map(pair => ReleaseStock(state.With(pair.After), pair.Before, pair.After));
    }

    /// <summary>
    /// Moves an order one step forward (Placed → Cooking → Ready → Picked up).
    /// <paramref name="expectedStatus"/> is what the cook saw on screen: if another cook already moved the order,
    /// we refuse instead of skipping a step.
    /// </summary>
    public static Result<Change<Order>> Advance(CanteenState state, Guid orderId, OrderStatus expectedStatus, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.FindOrder(orderId)
            .ToResult(OrderErrors.NotFound)
            .Ensure(order => order.Status == expectedStatus, order => OrderErrors.InvalidTransition(expectedStatus, order.Status))
            .Bind(order => order.Advance(now))
            .Map(moved => Change.Of(state.With(moved), moved));
    }

    /// <summary>
    /// Housekeeping: Ready orders nobody picked up become "No-show", and unfinished orders from previous days
    /// are cancelled so the kitchen board stays clean. Total function: it always succeeds, possibly changing nothing.
    /// </summary>
    public static Change<ImmutableList<Order>> CloseExpired(CanteenState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var serviceDay = SlotPlanner.ServiceDay(now, state.Rules);
        var noShowAfter = state.Rules.NoShowAfterMinutes;

        Option<Order> Close(Order order) =>
            order.IsNoShowDue(now, noShowAfter) ? order.MarkNoShow(now).ToOption()
            : order.ServiceDate < serviceDay && order.Status is OrderStatus.Placed or OrderStatus.Cooking
                ? order.CancelByKitchen("Not completed by the end of the day", now).ToOption()
            : Option<Order>.None;

        var closed = state.ActiveOrders.Choose(Close).ToImmutableList();
        return Change.Of(state.WithAll(closed), closed);
    }

    /// <summary>
    /// Gives the portions of a cancelled order back, but only if nothing was cooked yet
    /// and the portions belong to the current stock day.
    /// </summary>
    internal static Change<Cancellation> ReleaseStock(CanteenState state, Order before, Order after)
    {
        if (before.Status != OrderStatus.Placed)
        {
            return Change.Of(state, new Cancellation(after, []));
        }

        var released = after.Lines
            .Choose(line => state.FindItem(line.MenuItemId)
                .Where(item => item.StockDate == after.ServiceDate)
                .Map(item => item.Release(line.Quantity)))
            .ToImmutableList();

        return Change.Of(state.WithAll(released), new Cancellation(after, released));
    }

    /// <summary>Numbers for the TV screen at the pickup counter. A pure query.</summary>
    public static BoardSnapshot Board(CanteenState state, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        var serviceDay = SlotPlanner.ServiceDay(now, state.Rules);
        static BoardEntry ToEntry(OrderDto order) => new(order.Number, order.Status, order.SlotLabel);

        var todays = state.ActiveOrders
            .Where(o => o.ServiceDate == serviceDay)
            .OrderBy(o => o.PickupStart)
            .ThenBy(o => o.Number)
            .Select(OrderDto.In(zone))
            .ToList();

        return new BoardSnapshot(
            [.. todays.Where(o => o.Status is OrderStatus.Placed or OrderStatus.Cooking).Select(ToEntry)],
            [.. todays.Where(o => o.Status == OrderStatus.Ready).Select(ToEntry)],
            now);
    }
}

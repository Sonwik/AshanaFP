using Ashana.Application.Common;
using Ashana.Domain.Canteen;
using Ashana.Domain.Orders;

namespace Ashana.Application.Orders;

public sealed record OrderLineDto(Guid MenuItemId, string Name, string Emoji, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>Read model of an order. All times are canteen-local.</summary>
public sealed record OrderDto(
    Guid Id,
    int Number,
    DateOnly ServiceDate,
    Guid CustomerId,
    string CustomerName,
    OrderStatus Status,
    PaymentMethod Payment,
    string? Comment,
    decimal Total,
    DateTimeOffset PickupStart,
    DateTimeOffset PickupEnd,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CookingStartedAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? PickedUpAt,
    DateTimeOffset? CancelledAt,
    CancelSource? CancelledBy,
    string? CancelReason,
    IReadOnlyList<OrderLineDto> Lines,
    int Version)
{
    public string Code => $"#{Number}";

    public string SlotLabel => new PickupSlot(PickupStart, PickupEnd).Label;

    public int PortionCount => Lines.Sum(l => l.Quantity);

    public bool CanCustomerCancel => Status == OrderStatus.Placed;

    public bool IsActive => Status.IsActive();

    public bool IsLate(DateTimeOffset now) =>
        Status is OrderStatus.Placed or OrderStatus.Cooking && now >= PickupStart;

    /// <summary>Kitchen time from "start cooking" to "ready", if known.</summary>
    public TimeSpan? PrepDuration => CookingStartedAt is { } start && ReadyAt is { } ready ? ready - start : null;

    /// <summary>Maps an order to its read model, converting every time to the canteen's zone. Pure.</summary>
    public static OrderDto From(Order order, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(zone);

        DateTimeOffset Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, zone);
        DateTimeOffset? LocalOrNull(DateTimeOffset? t) => t is { } value ? Local(value) : null;

        return new OrderDto(
            order.Id,
            order.Number,
            order.ServiceDate,
            order.CustomerId,
            order.CustomerName,
            order.Status,
            order.Payment,
            order.Comment,
            order.Total,
            Local(order.PickupStart),
            Local(order.PickupEnd),
            Local(order.CreatedAt),
            LocalOrNull(order.CookingStartedAt),
            LocalOrNull(order.ReadyAt),
            LocalOrNull(order.PickedUpAt),
            LocalOrNull(order.CancelledAt),
            order.CancelledBy,
            order.CancelReason,
            [.. order.Lines.Select(l => new OrderLineDto(l.MenuItemId, l.Name, l.Emoji, l.UnitPrice, l.Quantity))],
            order.Version);
    }

    public static OrderDto From(Order order, CanteenClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return From(order, clock.TimeZone);
    }

    /// <summary>
    /// Curried form: fix the time zone once and get a one-argument mapper that fits straight into
    /// <c>orders.Select(OrderDto.In(zone))</c>. This is partial application of <see cref="From(Order, TimeZoneInfo)"/>.
    /// </summary>
    public static Func<Order, OrderDto> In(TimeZoneInfo zone) =>
        F.Curry<TimeZoneInfo, Order, OrderDto>((z, order) => From(order, z))(zone);
}

/// <summary>One line of the cart sent to the server. The expected price detects price changes since the dish was added.</summary>
public sealed record CartItemRequest(Guid MenuItemId, int Quantity, decimal ExpectedUnitPrice);

public sealed record PlaceOrderCommand(
    Guid CustomerId,
    IReadOnlyList<CartItemRequest> Items,
    DateTimeOffset SlotStart,
    PaymentMethod Payment,
    string? Comment,
    Guid ClientRequestId);

/// <summary>State of one pickup slot in the slot picker.</summary>
public enum SlotState
{
    Available,

    /// <summary>Three quarters full: still bookable but shown in amber.</summary>
    Busy,
    Full,

    /// <summary>Starts too soon for the kitchen to cook in time.</summary>
    TooSoon,
}

public sealed record SlotOption(DateTimeOffset Start, DateTimeOffset End, int Booked, int Capacity, SlotState State)
{
    public string Label => new PickupSlot(Start, End).Label;

    public bool IsBookable => State is SlotState.Available or SlotState.Busy;

    public int Remaining => Math.Max(0, Capacity - Booked);

    public int LoadPercent => Capacity == 0 ? 100 : Math.Min(100, Booked * 100 / Capacity);
}

public sealed record SlotDay(DateOnly Date, string Title, IReadOnlyList<SlotOption> Slots)
{
    public bool HasBookableSlots => Slots.Any(s => s.IsBookable);
}

public sealed record SlotOverview(IReadOnlyList<SlotDay> Days, bool AcceptingOrders, string? PauseMessage)
{
    /// <summary>The first slot a student can book, used as the default choice.</summary>
    public SlotOption? FirstBookable => Days.SelectMany(d => d.Slots).FirstOrDefault(s => s.IsBookable);
}

/// <summary>Public TV board entry: only the number and status, never a name (privacy).</summary>
public sealed record BoardEntry(int Number, OrderStatus Status, string SlotLabel);

public sealed record BoardSnapshot(IReadOnlyList<BoardEntry> Preparing, IReadOnlyList<BoardEntry> Ready, DateTimeOffset GeneratedAt);

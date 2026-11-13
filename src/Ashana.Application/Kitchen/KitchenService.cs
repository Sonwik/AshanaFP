using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.Events;
using Ashana.Application.Orders;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Ashana.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace Ashana.Application.Kitchen;

/// <summary>Orders grouped by status for the kitchen's Kanban board.</summary>
public sealed record KitchenBoard(
    IReadOnlyList<OrderDto> Placed,
    IReadOnlyList<OrderDto> Cooking,
    IReadOnlyList<OrderDto> Ready,
    IReadOnlyList<OrderDto> RecentlyFinished,
    DateTimeOffset Now,
    bool AcceptingOrders,
    string? PauseMessage)
{
    public int ActiveCount => Placed.Count + Cooking.Count + Ready.Count;

    public int LateCount => Placed.Concat(Cooking).Count(o => o.IsLate(Now));
}

/// <summary>"What do we cook next?" Portions of every dish per pickup slot.</summary>
public sealed record PrepList(DateOnly Day, IReadOnlyList<PrepSlot> Slots, IReadOnlyList<PrepItem> Totals);

public sealed record PrepSlot(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<PrepItem> Items, int OrderCount)
{
    public string Label => new PickupSlot(Start, End).Label;

    public int Portions => Items.Sum(i => i.Quantity);
}

public sealed record PrepItem(Guid MenuItemId, string Name, string Emoji, int Quantity);

/// <summary>
/// Kitchen use cases: the live board, moving orders forward, cancelling with a reason, the prep list.
/// The decisions are pure functions (<see cref="OrderWorkflow"/>, <see cref="Board"/>, <c>BuildPrepList</c>);
/// this class only adds the clock, the store and the side effects.
/// </summary>
public sealed class KitchenService
{
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(45);

    private readonly IStateStore _store;
    private readonly IEventPublisher _events;
    private readonly CanteenClock _clock;
    private readonly ILogger<KitchenService> _logger;

    public KitchenService(IStateStore store, IEventPublisher events, CanteenClock clock, ILogger<KitchenService> logger)
    {
        _store = store;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    public Task<KitchenBoard> GetBoardAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Board(_store.Current, _clock.Now, _clock.TimeZone));

    /// <summary>The kitchen board as a pure function of the state and the time.</summary>
    public static KitchenBoard Board(CanteenState state, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(state);
        var toDto = OrderDto.In(zone);

        var active = state.ActiveOrders
            .OrderBy(o => o.PickupStart)
            .ThenBy(o => o.Number)
            .Select(toDto)
            .ToList();

        var since = now - RecentWindow;
        var finished = state.Orders.Values
            .Where(o => o.Status.IsFinal() && (o.PickedUpAt ?? o.CancelledAt) >= since)
            .OrderByDescending(o => o.PickedUpAt ?? o.CancelledAt)
            .Take(12)
            .Select(toDto)
            .ToList();

        List<OrderDto> InStatus(OrderStatus status) => [.. active.Where(o => o.Status == status)];

        return new KitchenBoard(
            InStatus(OrderStatus.Placed),
            InStatus(OrderStatus.Cooking),
            InStatus(OrderStatus.Ready),
            finished,
            now,
            state.Rules.AcceptingOrders,
            state.Rules.PauseMessage);
    }

    /// <summary>
    /// Moves an order one step forward (Placed → Cooking → Ready → Picked up).
    /// <paramref name="expectedStatus"/> is what the cook saw on screen: if another cook already moved the order,
    /// we refuse instead of skipping a step.
    /// </summary>
    public async Task<Result<OrderDto>> AdvanceAsync(Guid orderId, OrderStatus expectedStatus, CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var moved = await _store.UpdateAsync(state => OrderWorkflow.Advance(state, orderId, expectedStatus, now), cancellationToken);
        return moved
            .Map(OrderDto.In(_clock.TimeZone))
            .Tap(dto => _logger.LogInformation("Order {Code} moved to {Status}", dto.Code, dto.Status))
            .Tap(dto => _events.Publish(new OrderChanged(dto, OrderChangeKind.StatusChanged)));
    }

    /// <summary>The kitchen cancels (for example, an ingredient ran out). The student sees the reason.</summary>
    public async Task<Result<OrderDto>> CancelAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var cancelled = await _store.UpdateAsync(state => OrderWorkflow.CancelByKitchen(state, orderId, reason, now), cancellationToken);
        return cancelled
            .Map(c => (Dto: OrderDto.From(c.Order, _clock), c.ReleasedItems))
            .Tap(c => _logger.LogInformation("Order {Code} cancelled by the kitchen: {Reason}", c.Dto.Code, reason))
            .Tap(c => OrderService.PublishChange(_events, c.Dto, OrderChangeKind.Cancelled, c.ReleasedItems))
            .Map(c => c.Dto);
    }

    /// <summary>Portions to cook per slot for orders that are not ready yet.</summary>
    public Task<PrepList> GetPrepListAsync(CancellationToken cancellationToken = default)
    {
        var state = _store.Current;
        var day = SlotPlanner.ServiceDay(_clock.Now, state.Rules);
        var pending = state.ActiveOrders.Where(o => o.ServiceDate == day && o.Status is OrderStatus.Placed or OrderStatus.Cooking);
        return Task.FromResult(BuildPrepList(day, pending, _clock.TimeZone));
    }

    /// <summary>Pure aggregation with LINQ (GroupBy + Sum), unit-tested separately.</summary>
    public static PrepList BuildPrepList(DateOnly day, IEnumerable<Order> pendingOrders, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var orders = pendingOrders.ToList();

        static IReadOnlyList<PrepItem> Sum(IEnumerable<OrderLine> lines) => [.. lines
            .GroupBy(l => l.MenuItemId)
            .Select(g => new PrepItem(g.Key, g.First().Name, g.First().Emoji, g.Sum(l => l.Quantity)))
            .OrderByDescending(i => i.Quantity)
            .ThenBy(i => i.Name, StringComparer.CurrentCulture)];

        var slots = orders
            .GroupBy(o => o.PickupStart)
            .OrderBy(g => g.Key)
            .Select(g => new PrepSlot(
                TimeZoneInfo.ConvertTime(g.Key, zone),
                TimeZoneInfo.ConvertTime(g.First().PickupEnd, zone),
                Sum(g.SelectMany(o => o.Lines)),
                g.Count()))
            .ToList();

        return new PrepList(day, slots, Sum(orders.SelectMany(o => o.Lines)));
    }

    public static PrepList BuildPrepList(DateOnly day, IEnumerable<Order> pendingOrders, CanteenClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return BuildPrepList(day, pendingOrders, clock.TimeZone);
    }
}

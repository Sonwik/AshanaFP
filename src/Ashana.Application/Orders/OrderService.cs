using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.Events;
using Ashana.Domain.Orders;
using Microsoft.Extensions.Logging;

namespace Ashana.Application.Orders;

/// <summary>
/// Student-side use cases: place an order, cancel it, see my orders.
/// This class is the <b>imperative shell</b>: it reads the clock, runs a pure transition from
/// <see cref="OrderWorkflow"/> through the store, then does the side effects (log, publish events).
/// All decisions live in the pure core.
/// </summary>
public sealed class OrderService
{
    /// <summary>Order numbers start here every day, so a code like "#107" is easy to say aloud.</summary>
    public const int FirstOrderNumber = OrderWorkflow.FirstOrderNumber;

    private readonly IStateStore _store;
    private readonly IEventPublisher _events;
    private readonly CanteenClock _clock;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IStateStore store, IEventPublisher events, CanteenClock clock, ILogger<OrderService> logger)
    {
        _store = store;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Places an order: check the slot, check the student's limit, reserve portions, create the order.
    /// The whole thing is one pure transition, so it either fully happens or not at all.
    /// </summary>
    public async Task<Result<OrderDto>> PlaceOrderAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = _clock.Now;
        var placed = await _store.UpdateAsync(state => OrderWorkflow.Place(state, command, now, _clock.TimeZone), cancellationToken);

        return placed
            .Tap(p =>
            {
                if (p.IsNew)
                {
                    PublishPlaced(p);
                }
            })
            .Map(p => OrderDto.From(p.Order, _clock));
    }

    /// <summary>The student cancels before cooking starts; the portions go back to the menu.</summary>
    public async Task<Result<OrderDto>> CancelAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var cancelled = await _store.UpdateAsync(state => OrderWorkflow.CancelByCustomer(state, orderId, customerId, now), cancellationToken);
        return cancelled
            .Map(c => (Dto: OrderDto.From(c.Order, _clock), c.ReleasedItems))
            .Tap(c => _logger.LogInformation("Order {Code} cancelled by the student", c.Dto.Code))
            .Tap(c => PublishChange(_events, c.Dto, OrderChangeKind.Cancelled, c.ReleasedItems))
            .Map(c => c.Dto);
    }

    /// <summary>One order, visible to its owner and to kitchen staff.</summary>
    public Task<Result<OrderDto>> GetAsync(Guid orderId, Guid requesterId, bool requesterIsStaff, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            _store.Current.FindOrder(orderId)
                .ToResult(OrderErrors.NotFound)
                .Ensure(order => requesterIsStaff || order.CustomerId == requesterId, OrderErrors.NotYourOrder)
                .Map(OrderDto.In(_clock.TimeZone)));

    public Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(Guid customerId, int take = 30, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<OrderDto>>(
            [.. _store.Current.OrdersOf(customerId)
                .OrderByDescending(o => o.CreatedAt)
                .Take(take)
                .Select(OrderDto.In(_clock.TimeZone))]);

    /// <summary>Numbers for the TV screen at the pickup counter.</summary>
    public Task<BoardSnapshot> GetBoardAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OrderWorkflow.Board(_store.Current, _clock.Now, _clock.TimeZone));

    /// <summary>Background job: closes orders nobody picked up. Returns how many changed.</summary>
    public async Task<int> CloseExpiredOrdersAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var closed = await _store.UpdateAsync(state => Result.Success(OrderWorkflow.CloseExpired(state, now)), cancellationToken);
        var orders = closed.GetValueOrDefault([]);

        foreach (var dto in orders.Select(OrderDto.In(_clock.TimeZone)))
        {
            _logger.LogInformation("Order {Code} closed automatically as {Status}", dto.Code, dto.Status);
            _events.Publish(new OrderChanged(dto, dto.Status == OrderStatus.Cancelled ? OrderChangeKind.Cancelled : OrderChangeKind.StatusChanged));
        }

        return orders.Count;
    }

    /// <summary>Publishes an order change, plus a stock change when portions moved.</summary>
    internal static void PublishChange(IEventPublisher events, OrderDto dto, OrderChangeKind kind, IReadOnlyCollection<Domain.Menu.MenuItem> stockItems)
    {
        events.Publish(new OrderChanged(dto, kind));
        if (stockItems.Count > 0)
        {
            events.Publish(new StockChanged([.. stockItems.Select(i => new StockLevel(i.Id, i.RemainingStock, i.IsOrderable))]));
        }
    }

    private void PublishPlaced(Placement placement)
    {
        var dto = OrderDto.From(placement.Order, _clock);
        _logger.LogInformation(
            "Order {Code} placed by {CustomerId} for {Slot}: {Portions} portion(s), {Total}",
            dto.Code, dto.CustomerId, dto.SlotLabel, dto.PortionCount, dto.Total);
        PublishChange(_events, dto, OrderChangeKind.Placed, placement.ReservedItems);
    }
}

using System.Threading.Channels;
using Ashana.Application.Abstractions;
using Ashana.Application.Events;
using Ashana.Application.Orders;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;

namespace Ashana.Web.Realtime;

/// <summary>
/// Bridges application events to SignalR clients.
/// Order changes are forwarded immediately to the kitchen and to the order's owner.
/// The TV board snapshot is rebuilt at most a few times per second: during the lunch rush
/// twenty status changes in one second produce one board update, not twenty (debouncing).
/// It also evicts cached API responses when the menu or stock changes.
/// </summary>
public sealed class HubBroadcaster : BackgroundService
{
    private static readonly TimeSpan BoardDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IEventSubscriber _events;
    private readonly IHubContext<OrderHub, IOrderHubClient> _hub;
    private readonly OrderService _orders;
    private readonly IOutputCacheStore _cache;
    private readonly ILogger<HubBroadcaster> _logger;
    private readonly Channel<bool> _boardDirty = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public HubBroadcaster(
        IEventSubscriber events,
        IHubContext<OrderHub, IOrderHubClient> hub,
        OrderService orders,
        IOutputCacheStore cache,
        ILogger<HubBroadcaster> logger)
    {
        _events = events;
        _hub = hub;
        _orders = orders;
        _cache = cache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var orderSubscription = _events.Subscribe<OrderChanged>(async change =>
        {
            var order = change.Order;
            await _hub.Clients.Groups(OrderHub.KitchenGroup, OrderHub.UserGroup(order.CustomerId)).OrderUpdated(order);
            _boardDirty.Writer.TryWrite(true);
            await _cache.EvictByTagAsync(ApiCacheTags.Slots, default);
        });

        using var stockSubscription = _events.Subscribe<StockChanged>(_ => _cache.EvictByTagAsync(ApiCacheTags.Menu, default).AsTask());
        using var menuSubscription = _events.Subscribe<MenuChanged>(_ => _cache.EvictByTagAsync(ApiCacheTags.Menu, default).AsTask());
        using var settingsSubscription = _events.Subscribe<SettingsChanged>(_ => _cache.EvictByTagAsync(ApiCacheTags.Slots, default).AsTask());

        try
        {
            await foreach (var _ in _boardDirty.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await Task.Delay(BoardDebounce, stoppingToken);
                    var snapshot = await _orders.GetBoardAsync(stoppingToken);
                    await _hub.Clients.Group(OrderHub.BoardGroup).BoardUpdated(snapshot);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable CA1031 // Keep broadcasting after a failed update.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _logger.LogWarning(ex, "Could not broadcast the board");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }
}

/// <summary>Output-cache tags for API responses.</summary>
public static class ApiCacheTags
{
    public const string Menu = "api-menu";
    public const string Slots = "api-slots";
}

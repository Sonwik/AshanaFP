using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ashana.Infrastructure.Background;

public sealed class WorkerOptions
{
    public const string SectionName = "Worker";

    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker runs, in seconds.</summary>
    public int IntervalSeconds { get; set; } = 30;
}

/// <summary>
/// Housekeeping that must happen even when nobody clicks anything:
/// resets daily portions when a new service day starts and closes orders that were never picked up.
/// Uses <see cref="PeriodicTimer"/> with <see cref="TimeProvider"/>, so tests can drive it with fake time.
/// </summary>
public sealed class OrderLifecycleWorker : BackgroundService
{
    private readonly MenuService _menu;
    private readonly OrderService _orders;
    private readonly TimeProvider _time;
    private readonly IOptions<WorkerOptions> _options;
    private readonly ILogger<OrderLifecycleWorker> _logger;

    public OrderLifecycleWorker(
        MenuService menu,
        OrderService orders,
        TimeProvider time,
        IOptions<WorkerOptions> options,
        ILogger<OrderLifecycleWorker> logger)
    {
        _menu = menu;
        _orders = orders;
        _time = time;
        _options = options;
        _logger = logger;
    }

    /// <summary>One housekeeping pass. Public so tests can run it directly.</summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await _menu.ResetDailyStockIfNeededAsync(cancellationToken);
        var closed = await _orders.CloseExpiredOrdersAsync(cancellationToken);
        if (closed > 0)
        {
            _logger.LogInformation("Closed {Count} expired order(s)", closed);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(5, _options.Value.IntervalSeconds));
        using var timer = new PeriodicTimer(interval, _time);

        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The worker must survive a failed run (for example, a locked file) and retry later.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                _logger.LogError(ex, "Housekeeping run failed; will retry in {Interval}", interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}

using Ashana.Application.Abstractions;
using Ashana.Application.Events;
using Ashana.Application.Menu;

namespace Ashana.Web.State;

/// <summary>
/// The menu as this browser tab sees it, kept up to date in real time.
/// One subscription per tab (not per component): when a student orders the last plov,
/// every open menu shows "Sold out" within a fraction of a second.
/// </summary>
public sealed class LiveMenu : IDisposable
{
    private readonly MenuService _menuService;
    private readonly IEventSubscriber _events;
    private readonly ILogger<LiveMenu> _logger;
    private readonly List<IDisposable> _subscriptions = [];

    public LiveMenu(MenuService menuService, IEventSubscriber events, ILogger<LiveMenu> logger)
    {
        _menuService = menuService;
        _events = events;
        _logger = logger;
    }

    public event Action? Changed;

    public MenuDto Menu { get; private set; } = MenuDto.Empty;

    public bool IsLoaded { get; private set; }

    /// <param name="live">Subscribe to updates (only in interactive rendering, not during prerendering).</param>
    public async Task EnsureLoadedAsync(bool live)
    {
        if (!IsLoaded)
        {
            Menu = await _menuService.GetMenuAsync();
            IsLoaded = true;
        }

        if (live && _subscriptions.Count == 0)
        {
            _subscriptions.Add(_events.Subscribe<StockChanged>(OnStockChangedAsync));
            _subscriptions.Add(_events.Subscribe<MenuChanged>(OnMenuChangedAsync));
        }
    }

    /// <summary>Reloads now (for example after the server rejected an order because the menu changed).</summary>
    public async Task RefreshAsync()
    {
        Menu = await _menuService.GetMenuAsync();
        IsLoaded = true;
        Changed?.Invoke();
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private Task OnStockChangedAsync(StockChanged change)
    {
        Menu = Menu.WithStock(change.Levels);
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    private async Task OnMenuChangedAsync(MenuChanged change)
    {
        try
        {
            Menu = await _menuService.GetMenuAsync();
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not refresh the menu");
        }
    }
}

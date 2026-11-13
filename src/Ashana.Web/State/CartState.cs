using System.Security.Cryptography;
using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.Application.Shopping;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.JSInterop;

namespace Ashana.Web.State;

/// <summary>
/// State container for the cart, one per browser tab (scoped service).
/// <list type="bullet">
/// <item>The cart itself is an immutable <see cref="Cart"/> value. This container holds the <i>current</i> value
/// and replaces it with the new one returned by each operation (the only mutation is this one reference).</item>
/// <item>Components read it and subscribe to <see cref="Changed"/>; nobody passes the cart through parameters.</item>
/// <item>The cart is saved in the browser with <see cref="ProtectedLocalStorage"/> (encrypted and signed by the server),
/// so it survives a page reload and cannot be tampered with.</item>
/// </list>
/// </summary>
public sealed class CartState
{
    private const string StorageKey = "ashana.cart.v1";

    private readonly ProtectedLocalStorage _storage;
    private readonly ILogger<CartState> _logger;
    private Cart _cart = Cart.Empty;

    public CartState(ProtectedLocalStorage storage, ILogger<CartState> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>Raised after every change. Handlers must marshal to the UI thread with InvokeAsync.</summary>
    public event Action? Changed;

    /// <summary>False until the cart was read from the browser (not possible during prerendering).</summary>
    public bool IsLoaded { get; private set; }

    public IReadOnlyList<CartLine> Lines => _cart.Lines;

    public int Count => _cart.Count;

    public decimal Total => _cart.Total;

    public bool IsEmpty => _cart.IsEmpty;

    public int QuantityOf(Guid menuItemId) => _cart.QuantityOf(menuItemId);

    public async Task LoadAsync()
    {
        if (IsLoaded)
        {
            return;
        }

        try
        {
            var saved = await _storage.GetAsync<List<CartLine>>(StorageKey);
            if (saved.Success && saved.Value is not null)
            {
                _cart = Cart.From(saved.Value);
            }
        }
        catch (CryptographicException)
        {
            // The server's data-protection keys changed (for example a new machine): start with an empty cart.
            await TryAsync(() => _storage.DeleteAsync(StorageKey).AsTask());
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Cart could not be read from the browser");
        }

        IsLoaded = true;
        Changed?.Invoke();
    }

    public Task<Result<Unit>> AddAsync(MenuItemDto item, int quantity = 1) => ApplyAsync(_cart.Add(item, quantity));

    public Task<Result<Unit>> SetQuantityAsync(Guid menuItemId, int quantity, int? remainingStock = null) =>
        ApplyAsync(_cart.SetQuantity(menuItemId, quantity, remainingStock));

    public Task RemoveAsync(Guid menuItemId) => ApplyAsync(Result.Success(_cart.Remove(menuItemId)));

    public Task ClearAsync() => ApplyAsync(Result.Success(Cart.Empty));

    /// <summary>Updates prices and removes sold-out dishes; returns what changed so the UI can explain it.</summary>
    public async Task<IReadOnlyList<CartAdjustment>> ReconcileAsync(MenuDto menu)
    {
        var reconciled = _cart.Reconcile(menu);
        if (reconciled.Changes.Count > 0)
        {
            await ApplyAsync(Result.Success(reconciled.Cart));
        }

        return reconciled.Changes;
    }

    /// <summary>On success the new cart replaces the old one and is saved; on failure nothing changes.</summary>
    private async Task<Result<Unit>> ApplyAsync(Result<Cart> next)
    {
        if (next.IsSuccess)
        {
            _cart = next.Value;
            await SaveAsync();
        }

        return next.ToUnit();
    }

    public IReadOnlyList<CartItemRequest> ToRequests() => _cart.ToRequests();

    private async Task SaveAsync()
    {
        Changed?.Invoke();
        await TryAsync(() => _storage.SetAsync(StorageKey, _cart.Lines.ToList()).AsTask());
    }

    private async Task TryAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException)
        {
            _logger.LogDebug(ex, "Browser storage is not available");
        }
    }
}

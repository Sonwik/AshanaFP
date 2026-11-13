using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.Events;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Microsoft.Extensions.Logging;

namespace Ashana.Application.Menu;

/// <summary>
/// Reading the menu and managing it (admin, kitchen stop list).
/// </summary>
/// <remarks>
/// There is no cache to invalidate by hand: the menu is a pure function of the state snapshot,
/// memoized with <see cref="F.MemoizeLast{T,TOut}"/>. While the state object is the same, every student gets
/// the same ready-made <see cref="MenuDto"/>; the first read after any change builds a new one automatically.
/// </remarks>
public sealed class MenuService
{
    private readonly IStateStore _store;
    private readonly IEventPublisher _events;
    private readonly CanteenClock _clock;
    private readonly ILogger<MenuService> _logger;
    private readonly Func<CanteenState, MenuDto> _menuOf;

    public MenuService(IStateStore store, IEventPublisher events, CanteenClock clock, ILogger<MenuService> logger)
    {
        _store = store;
        _events = events;
        _clock = clock;
        _logger = logger;
        _menuOf = F.MemoizeLast<CanteenState, MenuDto>(state => BuildMenu(state, _clock.Now));
    }

    /// <summary>The current menu (shared, immutable, rebuilt only when the state changes).</summary>
    public Task<MenuDto> GetMenuAsync(CancellationToken cancellationToken = default) => Task.FromResult(_menuOf(_store.Current));

    /// <summary>Pure projection of the state into the menu read model.</summary>
    public static MenuDto BuildMenu(CanteenState state, DateTimeOffset builtAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new MenuDto(
            [.. state.Categories.Values.OrderBy(c => c.SortOrder).Select(CategoryDto.From)],
            [.. state.MenuItems.Values.Where(i => !i.IsArchived).OrderBy(i => i.Name, StringComparer.Ordinal).Select(MenuItemDto.From)],
            builtAt);
    }

    /// <summary>Kept for API compatibility: with a memoized pure projection there is nothing to clear.</summary>
    public void InvalidateCache()
    {
    }

    /// <summary>All dishes including archived ones (admin table).</summary>
    public Task<IReadOnlyList<MenuItemDto>> GetAllForAdminAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MenuItemDto>>([.. _store.Current.MenuItems.Values.Select(MenuItemDto.From)]);

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CategoryDto>>([.. _store.Current.Categories.Values.OrderBy(c => c.SortOrder).Select(CategoryDto.From)]);

    public Task<Result<MenuItemDto>> GetItemAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Current.FindItem(id).ToResult(MenuErrors.ItemNotFound).Map(MenuItemDto.From));

    public async Task<Result<MenuItemDto>> CreateAsync(MenuItemDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var now = _clock.Now;
        var created = await _store.UpdateAsync(state => MenuWorkflow.Create(state, details, now), cancellationToken);
        return created
            .Tap(item => _logger.LogInformation("Dish {Name} ({Id}) created", item.Name, item.Id))
            .Tap(item => AfterMenuChange(item.Id))
            .Map(MenuItemDto.From);
    }

    /// <summary>
    /// Updates a dish. <paramref name="expectedVersion"/> is the version the admin opened in the form;
    /// if someone saved in between, the update is rejected instead of silently overwriting their change.
    /// </summary>
    public async Task<Result<MenuItemDto>> UpdateAsync(Guid id, int expectedVersion, MenuItemDetails details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        var updated = await _store.UpdateAsync(state => MenuWorkflow.Update(state, id, expectedVersion, details), cancellationToken);
        return updated.Tap(item => AfterMenuChange(item.Id)).Map(MenuItemDto.From);
    }

    /// <summary>Kitchen "stop list": hide a dish immediately (or bring it back).</summary>
    public Task<Result<Unit>> SetAvailabilityAsync(Guid id, bool isAvailable, CancellationToken cancellationToken = default) =>
        ChangeItemAsync(id, item => item.SetAvailability(isAvailable), $"availability set to {isAvailable}", cancellationToken);

    /// <summary>The kitchen cooked extra portions.</summary>
    public Task<Result<Unit>> RestockAsync(Guid id, int extraPortions, CancellationToken cancellationToken = default) =>
        ChangeItemAsync(id, item => item.Restock(extraPortions), $"restocked with {extraPortions}", cancellationToken);

    /// <summary>Soft delete: the dish disappears from the menu but old orders keep their history.</summary>
    public Task<Result<Unit>> ArchiveAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeItemAsync(id, item => item.Archive(), "archived", cancellationToken);

    public async Task<Result<CategoryDto>> CreateCategoryAsync(string name, string icon, CancellationToken cancellationToken = default)
    {
        var created = await _store.UpdateAsync(state => MenuWorkflow.CreateCategory(state, name, icon), cancellationToken);
        return created.Tap(_ => AfterMenuChange(null)).Map(CategoryDto.From);
    }

    /// <summary>Called by the background worker. When a new service day starts, every dish gets its planned portions back.</summary>
    /// <returns><c>true</c> if the stock was reset.</returns>
    public async Task<bool> ResetDailyStockIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.Now;
        var reset = await _store.UpdateAsync(
            state =>
            {
                var serviceDay = SlotPlanner.ServiceDay(now, state.Rules);
                var fresh = StockDay.EnsureCurrent(state, serviceDay);
                return Result.Success(Change.Of(fresh, ReferenceEquals(fresh, state) ? Option<DateOnly>.None : serviceDay));
            },
            cancellationToken);

        return reset.GetValueOrDefault(Option<DateOnly>.None).Match(
            serviceDay =>
            {
                _logger.LogInformation("Daily stock reset for service day {Date}", serviceDay);
                AfterMenuChange(null);
                return true;
            },
            () => false);
    }

    private async Task<Result<Unit>> ChangeItemAsync(Guid id, Func<MenuItem, Result<MenuItem>> change, string description, CancellationToken cancellationToken)
    {
        var changed = await _store.UpdateAsync(state => MenuWorkflow.ChangeItem(state, id, change), cancellationToken);
        return changed
            .Tap(item => _logger.LogInformation("Dish {Name} {Change}", item.Name, description))
            .Tap(item => AfterMenuChange(item.Id))
            .ToUnit();
    }

    private Task<Result<Unit>> ChangeItemAsync(Guid id, Func<MenuItem, MenuItem> change, string description, CancellationToken cancellationToken) =>
        ChangeItemAsync(id, item => Result.Success(change(item)), description, cancellationToken);

    private void AfterMenuChange(Guid? itemId) => _events.Publish(new MenuChanged(itemId));
}

/// <summary>Menu use cases as pure transitions over <see cref="CanteenState"/>.</summary>
public static class MenuWorkflow
{
    public static readonly Error CategoryExists = Error.Conflict("menu.category_exists", "A category with this name already exists.");

    public static Result<Change<MenuItem>> Create(CanteenState state, MenuItemDetails details, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        return
            from category in state.FindCategory(details.CategoryId).ToResult(MenuErrors.CategoryNotFound)
            from item in MenuItem.Create(details, SlotPlanner.ServiceDay(now, state.Rules))
            select Change.Of(state.With(item), item);
    }

    /// <summary>Optimistic concurrency is just one more <c>Ensure</c> in the pipeline.</summary>
    public static Result<Change<MenuItem>> Update(CanteenState state, Guid id, int expectedVersion, MenuItemDetails details)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.FindItem(id)
            .ToResult(MenuErrors.ItemNotFound)
            .Ensure(item => item.Version == expectedVersion, MenuErrors.EditConflict)
            .Ensure(_ => state.FindCategory(details.CategoryId).IsSome, MenuErrors.CategoryNotFound)
            .Bind(item => item.Update(details))
            .Map(updated => Change.Of(state.With(updated), updated));
    }

    /// <summary>A higher-order transition: the actual change is passed in as a function.</summary>
    public static Result<Change<MenuItem>> ChangeItem(CanteenState state, Guid id, Func<MenuItem, Result<MenuItem>> change)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(change);
        return state.FindItem(id)
            .ToResult(MenuErrors.ItemNotFound)
            .Bind(change)
            .Map(changed => Change.Of(state.With(changed), changed));
    }

    public static Result<Change<Category>> CreateCategory(CanteenState state, string name, string icon)
    {
        ArgumentNullException.ThrowIfNull(state);
        var existing = state.Categories.Values.ToList();
        var nextOrder = existing.Count == 0 ? 1 : existing.Max(c => c.SortOrder) + 1;
        return Result.Require(!existing.Any(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)), CategoryExists)
            .Bind(_ => Category.Create(name ?? string.Empty, icon, nextOrder))
            .Map(category => Change.Of(state.With(category), category));
    }
}

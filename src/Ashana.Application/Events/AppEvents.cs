using Ashana.Application.Orders;
using Ashana.Domain.Canteen;

namespace Ashana.Application.Events;

public enum OrderChangeKind
{
    Placed,
    StatusChanged,
    Cancelled,
}

/// <summary>
/// Everything that can happen in the canteen and that open screens react to, as one closed
/// discriminated union of immutable records. The internal constructor means no other assembly can
/// add a case, so a <c>switch</c> over <see cref="AppEvent"/> in this solution covers every event.
/// </summary>
public abstract record AppEvent
{
    internal AppEvent()
    {
    }
}

/// <summary>An order was placed or changed status. Kitchen boards, the student's tracking page and the TV board react to it.</summary>
public sealed record OrderChanged(OrderDto Order, OrderChangeKind Kind) : AppEvent;

/// <summary>Remaining portions of some dishes changed, so "sold out" badges update for everyone at once.</summary>
public sealed record StockChanged(IReadOnlyList<StockLevel> Levels) : AppEvent;

public sealed record StockLevel(Guid MenuItemId, int Remaining, bool IsOrderable);

/// <summary>An admin edited the menu (price, name, new dish...). Clients reload the menu.</summary>
public sealed record MenuChanged(Guid? MenuItemId) : AppEvent;

/// <summary>Canteen rules changed (hours, capacity, pause). Slot pickers and banners refresh.</summary>
public sealed record SettingsChanged(CanteenRules Rules) : AppEvent;

public static class AppEventDescriptions
{
    /// <summary>Exhaustive pattern matching over the union (used for logging).</summary>
    public static string Describe(this AppEvent appEvent) => appEvent switch
    {
        OrderChanged e => $"Order {e.Order.Code} {e.Kind}",
        StockChanged e => $"Stock changed for {e.Levels.Count} dish(es)",
        MenuChanged e => e.MenuItemId is { } id ? $"Dish {id} changed" : "Menu changed",
        SettingsChanged e => $"Settings changed (accepting orders: {e.Rules.AcceptingOrders})",
        _ => throw new ArgumentOutOfRangeException(nameof(appEvent), appEvent, "Unknown event."),
    };
}

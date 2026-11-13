using System.Collections.Immutable;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;

namespace Ashana.Application.State;

/// <summary>
/// The whole state of the canteen as <b>one immutable value</b>: immutable dictionaries of immutable records.
/// <para>
/// Nobody ever modifies a state. A use case is a pure function <c>CanteenState → Result&lt;Change&lt;T&gt;&gt;</c>
/// that returns a <i>new</i> state; the store then swaps the reference in one step. Because a snapshot never
/// changes, any number of readers can use it at the same time without locks, and a failed operation simply
/// throws its new state away: there is nothing to roll back.
/// </para>
/// <para>Immutable collections share structure, so "a copy with one more order" costs O(log n), not a full copy.</para>
/// </summary>
public sealed record CanteenState(
    ImmutableDictionary<Guid, User> Users,
    ImmutableDictionary<Guid, Category> Categories,
    ImmutableDictionary<Guid, MenuItem> MenuItems,
    ImmutableDictionary<Guid, Order> Orders,
    CanteenSettings Settings)
{
    public static CanteenState Empty(string timeZoneId = "Asia/Almaty") => new(
        ImmutableDictionary<Guid, User>.Empty,
        ImmutableDictionary<Guid, Category>.Empty,
        ImmutableDictionary<Guid, MenuItem>.Empty,
        ImmutableDictionary<Guid, Order>.Empty,
        CanteenSettings.CreateDefault(timeZoneId));

    public CanteenRules Rules => Settings.Rules;

    // ---- Queries (pure; "find" returns Option instead of null) -----------------------------------

    public Option<User> FindUser(Guid id) => Users.Find(id);

    public Option<User> FindUserByEmail(string normalizedEmail) =>
        Users.Values.FirstOrNone(u => u.Email == normalizedEmail);

    public Option<Order> FindOrder(Guid id) => Orders.Find(id);

    public Option<MenuItem> FindItem(Guid id) => MenuItems.Find(id);

    public Option<Category> FindCategory(Guid id) => Categories.Find(id);

    public Option<Order> FindByClientRequest(Guid customerId, Guid clientRequestId) =>
        Orders.Values.FirstOrNone(o => o.CustomerId == customerId && o.ClientRequestId == clientRequestId);

    /// <summary>Orders that still need work: Placed, Cooking or Ready.</summary>
    public IEnumerable<Order> ActiveOrders => Orders.Values.Where(o => o.Status.IsActive());

    public IEnumerable<Order> OrdersOf(Guid customerId) => Orders.Values.Where(o => o.CustomerId == customerId);

    /// <summary>Non-cancelled orders in the slot that starts at <paramref name="slotStart"/> (slot capacity).</summary>
    public int CountBookedInSlot(DateTimeOffset slotStart) =>
        Orders.Values.Count(o => o.PickupStart == slotStart && o.Status != OrderStatus.Cancelled);

    /// <summary>Non-cancelled orders per slot start for one day.</summary>
    public IReadOnlyDictionary<DateTimeOffset, int> BookedBySlot(DateOnly serviceDate) =>
        Orders.Values
            .Where(o => o.ServiceDate == serviceDate && o.Status != OrderStatus.Cancelled)
            .GroupBy(o => o.PickupStart)
            .ToDictionary(g => g.Key, g => g.Count());

    public int CountActiveFor(Guid customerId) => ActiveOrders.Count(o => o.CustomerId == customerId);

    /// <summary>Highest order number used on that day, or 0.</summary>
    public int MaxNumber(DateOnly serviceDate) =>
        Orders.Values.Where(o => o.ServiceDate == serviceDate).Select(o => o.Number).DefaultIfEmpty(0).Max();

    public int CountActiveAdmins() => Users.Values.Count(u => u.Role == UserRole.Admin && u.IsActive);

    // ---- Updates (pure: each returns a new state that shares everything else) --------------------

    public CanteenState With(User user) => this with { Users = Users.SetItem(user.Id, user) };

    public CanteenState With(Category category) => this with { Categories = Categories.SetItem(category.Id, category) };

    public CanteenState With(MenuItem item) => this with { MenuItems = MenuItems.SetItem(item.Id, item) };

    public CanteenState With(Order order) => this with { Orders = Orders.SetItem(order.Id, order) };

    public CanteenState With(CanteenSettings settings) => this with { Settings = settings };

    public CanteenState WithAll(IEnumerable<MenuItem> items) => this with { MenuItems = MenuItems.SetItems(items.Select(i => KeyValuePair.Create(i.Id, i))) };

    public CanteenState WithAll(IEnumerable<Order> orders) => this with { Orders = Orders.SetItems(orders.Select(o => KeyValuePair.Create(o.Id, o))) };
}

/// <summary>What a transition produced: the new state plus a value for the caller (the created order, for example).</summary>
public sealed record Change<T>(CanteenState State, T Value);

public static class Change
{
    public static Change<T> Of<T>(CanteenState state, T value) => new(state, value);

    /// <summary>A transition that only reads: the same state comes back.</summary>
    public static Result<Change<T>> ReadOnly<T>(CanteenState state, Result<T> value) => value.Map(v => Of(state, v));
}

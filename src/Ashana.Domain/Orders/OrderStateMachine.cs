using System.Collections.Frozen;

namespace Ashana.Domain.Orders;

/// <summary>
/// The only place that knows which status changes are legal. The transition table is immutable data
/// (a frozen dictionary), and the functions below only read it.
/// <code>
/// Placed ──► Cooking ──► Ready ──► PickedUp
///   │           │          └────► NoShow
///   └───────────┴─────────────► Cancelled
/// </code>
/// </summary>
public static class OrderStateMachine
{
    private static readonly FrozenDictionary<OrderStatus, OrderStatus[]> Transitions =
        new Dictionary<OrderStatus, OrderStatus[]>
        {
            [OrderStatus.Placed] = [OrderStatus.Cooking, OrderStatus.Cancelled],
            [OrderStatus.Cooking] = [OrderStatus.Ready, OrderStatus.Cancelled],
            [OrderStatus.Ready] = [OrderStatus.PickedUp, OrderStatus.NoShow],
            [OrderStatus.PickedUp] = [],
            [OrderStatus.Cancelled] = [],
            [OrderStatus.NoShow] = [],
        }.ToFrozenDictionary();

    public static bool CanMove(OrderStatus from, OrderStatus to) =>
        Transitions.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<OrderStatus> AllowedTargets(OrderStatus from) =>
        Transitions.TryGetValue(from, out var targets) ? targets : [];

    /// <summary>The "happy path" next step the kitchen button performs, or <c>None</c> at the end.</summary>
    public static Option<OrderStatus> NextKitchenStep(OrderStatus from) => from switch
    {
        OrderStatus.Placed => OrderStatus.Cooking,
        OrderStatus.Cooking => OrderStatus.Ready,
        OrderStatus.Ready => OrderStatus.PickedUp,
        _ => Option<OrderStatus>.None,
    };
}

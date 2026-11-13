namespace Ashana.Domain.Orders;

/// <summary>Life cycle of an order. See <see cref="OrderStateMachine"/> for the allowed moves.</summary>
public enum OrderStatus
{
    /// <summary>Student placed the order; the kitchen has not started yet. The student can still cancel.</summary>
    Placed = 0,

    /// <summary>The kitchen is preparing the order.</summary>
    Cooking = 1,

    /// <summary>Packed and waiting at the pickup counter.</summary>
    Ready = 2,

    /// <summary>Handed to the student. Final state.</summary>
    PickedUp = 3,

    /// <summary>Cancelled by the student or the kitchen. Final state.</summary>
    Cancelled = 4,

    /// <summary>Ready but never picked up. Set automatically by a background job. Final state.</summary>
    NoShow = 5,
}

/// <summary>How the student will pay at the pickup counter.</summary>
public enum PaymentMethod
{
    Card = 0,
    Cash = 1,
    Qr = 2,
}

/// <summary>Who cancelled an order.</summary>
public enum CancelSource
{
    Customer = 0,
    Kitchen = 1,
}

public static class OrderStatusExtensions
{
    /// <summary>Active orders still need attention from the kitchen or the student.</summary>
    public static bool IsActive(this OrderStatus status) =>
        status is OrderStatus.Placed or OrderStatus.Cooking or OrderStatus.Ready;

    public static bool IsFinal(this OrderStatus status) => !status.IsActive();

    /// <summary>Short label for badges.</summary>
    public static string Label(this OrderStatus status) => status switch
    {
        OrderStatus.Placed => "Placed",
        OrderStatus.Cooking => "Cooking",
        OrderStatus.Ready => "Ready",
        OrderStatus.PickedUp => "Picked up",
        OrderStatus.Cancelled => "Cancelled",
        OrderStatus.NoShow => "Not picked up",
        _ => status.ToString(),
    };
}

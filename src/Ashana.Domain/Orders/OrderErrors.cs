namespace Ashana.Domain.Orders;

public static class OrderErrors
{
    public const int MaxLines = 15;
    public const int MaxQuantityPerLine = 10;
    public const int CommentMaxLength = 200;

    public static readonly Error EmptyCart = Error.Validation("order.empty", "Your cart is empty.");
    public static readonly Error TooManyLines = Error.Validation("order.too_many_lines", $"An order can contain at most {MaxLines} different dishes.");
    public static readonly Error QuantityOutOfRange = Error.Validation("order.quantity_range", $"Quantity must be between 1 and {MaxQuantityPerLine}.");
    public static readonly Error DuplicateLine = Error.Validation("order.duplicate_line", "The same dish appears twice in the order.");
    public static readonly Error CommentTooLong = Error.Validation("order.comment_too_long", $"Comment must be at most {CommentMaxLength} characters.");
    public static readonly Error NotFound = Error.NotFound("order.not_found", "Order not found.");
    public static readonly Error NotYourOrder = Error.Forbidden("order.forbidden", "This is not your order.");
    public static readonly Error CannotCancelNow = Error.Conflict("order.cannot_cancel", "The kitchen has already started this order, so it can no longer be cancelled here. Please talk to the counter.");
    public static readonly Error ReasonRequired = Error.Validation("order.reason_required", "Please give a short reason for the cancellation.");
    public static readonly Error UserInactive = Error.Forbidden("order.user_inactive", "Your account is blocked. Please contact the canteen administrator.");

    public static Error TooManyActiveOrders(int limit) =>
        Error.Conflict("order.active_limit", $"You already have {limit} active order(s). Pick them up or cancel one first.");

    public static Error PriceChanged(string dishName) =>
        Error.Conflict("order.price_changed", $"The price of {dishName} has changed. Please review your cart.");

    public static Error InvalidTransition(OrderStatus from, OrderStatus to) =>
        Error.Conflict("order.invalid_transition", $"An order that is \"{from.Label()}\" cannot become \"{to.Label()}\". It may have been updated by someone else.");
}

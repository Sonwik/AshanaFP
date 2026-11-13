namespace Ashana.Domain.Menu;

/// <summary>All menu-related errors in one place, so messages stay consistent.</summary>
public static class MenuErrors
{
    public const int MaxPrice = 100_000;
    public const int MaxDailyStock = 1_000;

    public static readonly Error NameRequired = Error.Validation("menu.name_required", "Dish name is required.");
    public static readonly Error NameTooLong = Error.Validation("menu.name_too_long", "Dish name must be at most 60 characters.");
    public static readonly Error DescriptionTooLong = Error.Validation("menu.description_too_long", "Description must be at most 300 characters.");
    public static readonly Error PriceOutOfRange = Error.Validation("menu.price_range", $"Price must be between 1 and {MaxPrice:N0} ₸.");
    public static readonly Error StockOutOfRange = Error.Validation("menu.stock_range", $"Daily portions must be between 0 and {MaxDailyStock}.");
    public static readonly Error PrepTimeOutOfRange = Error.Validation("menu.prep_range", "Preparation time must be between 1 and 120 minutes.");
    public static readonly Error QuantityMustBePositive = Error.Validation("menu.quantity_positive", "Quantity must be positive.");
    public static readonly Error ItemNotFound = Error.NotFound("menu.not_found", "This dish no longer exists.");
    public static readonly Error CategoryNotFound = Error.NotFound("menu.category_not_found", "Category not found.");
    public static readonly Error EditConflict = Error.Conflict("menu.edit_conflict", "Someone else changed this dish while you were editing. Reload and try again.");

    public static Error SoldOut(string dishName) =>
        Error.Conflict("menu.sold_out", $"Sorry, {dishName} has just sold out.");

    public static Error NotEnoughStock(string dishName, int left) =>
        Error.Conflict("menu.not_enough_stock", $"Only {left} portion(s) of {dishName} left.");

    public static Error Unavailable(string dishName) =>
        Error.Conflict("menu.unavailable", $"{dishName} is not available right now.");
}

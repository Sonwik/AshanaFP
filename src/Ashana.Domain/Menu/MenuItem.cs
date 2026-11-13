using System.Collections.Immutable;
using Ashana.Domain.Common;

namespace Ashana.Domain.Menu;

/// <summary>Editable fields of a dish. Used both for creating and for updating.</summary>
public sealed record MenuItemDetails(
    Guid CategoryId,
    string Name,
    string Description,
    decimal Price,
    string Emoji,
    string? ImageUrl,
    int? Calories,
    MenuTags Tags,
    int PrepMinutes,
    int DailyStock);

/// <summary>
/// A dish on the menu (an immutable record). Besides price and description it tracks <b>daily stock</b>:
/// how many portions the kitchen plans to cook today and how many are still free.
/// Reserving portions returns a new <see cref="MenuItem"/> with fewer portions left, or an error.
/// </summary>
public sealed record MenuItem : IEntity
{
    public const int NameMaxLength = 60;
    public const int DescriptionMaxLength = 300;

    /// <summary>Field rules, as data. Shared by <see cref="Create"/> and <see cref="Update"/>.</summary>
    public static readonly ImmutableArray<Rule<MenuItemDetails>> Rules =
    [
        Validation.Rule<MenuItemDetails>(d => !string.IsNullOrWhiteSpace(d.Name), MenuErrors.NameRequired),
        Validation.Rule<MenuItemDetails>(d => d.Name.Trim().Length <= NameMaxLength, MenuErrors.NameTooLong),
        Validation.Rule<MenuItemDetails>(d => (d.Description ?? string.Empty).Length <= DescriptionMaxLength, MenuErrors.DescriptionTooLong),
        Validation.Rule<MenuItemDetails>(d => d.Price is > 0 and <= MenuErrors.MaxPrice, MenuErrors.PriceOutOfRange),
        Validation.Rule<MenuItemDetails>(d => d.DailyStock is >= 0 and <= MenuErrors.MaxDailyStock, MenuErrors.StockOutOfRange),
        Validation.Rule<MenuItemDetails>(d => d.PrepMinutes is >= 1 and <= 120, MenuErrors.PrepTimeOutOfRange),
    ];

    private MenuItem()
    {
    }

    public Guid Id { get; private init; }

    public int Version { get; private init; }

    public Guid CategoryId { get; private init; }

    public string Name { get; private init; } = string.Empty;

    public string Description { get; private init; } = string.Empty;

    /// <summary>Price in tenge (₸).</summary>
    public decimal Price { get; private init; }

    /// <summary>Emoji shown when there is no photo.</summary>
    public string Emoji { get; private init; } = string.Empty;

    public string? ImageUrl { get; private init; }

    public int? Calories { get; private init; }

    public MenuTags Tags { get; private init; }

    /// <summary>Typical cooking time in minutes.</summary>
    public int PrepMinutes { get; private init; }

    /// <summary>Portions planned per day.</summary>
    public int DailyStock { get; private init; }

    /// <summary>Portions still available on <see cref="StockDate"/>.</summary>
    public int RemainingStock { get; private init; }

    /// <summary>Service day for which <see cref="RemainingStock"/> is valid.</summary>
    public DateOnly StockDate { get; private init; }

    /// <summary>Temporarily hidden by the kitchen ("stop list").</summary>
    public bool IsAvailable { get; private init; }

    /// <summary>Soft-deleted. Old orders still reference archived dishes, so we never hard-delete them.</summary>
    public bool IsArchived { get; private init; }

    /// <summary>True when a student can add this dish to the cart right now.</summary>
    public bool IsOrderable => IsAvailable && !IsArchived && RemainingStock > 0;

    public static Result<MenuItem> Create(MenuItemDetails details, DateOnly serviceDay, Guid? id = null)
    {
        ArgumentNullException.ThrowIfNull(details);
        return details.Check(Rules).Map(d => new MenuItem
        {
            Id = id ?? Guid.NewGuid(),
            IsAvailable = true,
            StockDate = serviceDay,
            RemainingStock = d.DailyStock,
        }.WithDetails(d));
    }

    /// <summary>Updates the dish. Changing the daily plan also shifts today's remaining portions.</summary>
    public Result<MenuItem> Update(MenuItemDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return details.Check(Rules).Map(d => WithDetails(d) with
        {
            RemainingStock = Math.Max(0, RemainingStock + (d.DailyStock - DailyStock)),
            Version = Version + 1,
        });
    }

    /// <summary>Takes <paramref name="quantity"/> portions for an order. The checks run in order, like a railway.</summary>
    public Result<MenuItem> Reserve(int quantity) =>
        Result.Success(this)
            .Ensure(_ => quantity > 0, MenuErrors.QuantityMustBePositive)
            .Ensure(item => item.IsAvailable && !item.IsArchived, item => MenuErrors.Unavailable(item.Name))
            .Ensure(item => item.RemainingStock > 0, item => MenuErrors.SoldOut(item.Name))
            .Ensure(item => item.RemainingStock >= quantity, item => MenuErrors.NotEnoughStock(item.Name, item.RemainingStock))
            .Map(item => item with { RemainingStock = item.RemainingStock - quantity });

    /// <summary>Returns portions after a cancellation. Never exceeds the daily plan.</summary>
    public MenuItem Release(int quantity) =>
        quantity <= 0 ? this : this with { RemainingStock = Math.Min(DailyStock, RemainingStock + quantity) };

    /// <summary>The kitchen cooked extra portions during the day.</summary>
    public Result<MenuItem> Restock(int extraPortions) =>
        Result.Success(this)
            .Ensure(_ => extraPortions > 0, MenuErrors.QuantityMustBePositive)
            .Ensure(item => item.DailyStock + extraPortions <= MenuErrors.MaxDailyStock, MenuErrors.StockOutOfRange)
            .Map(item => item with
            {
                DailyStock = item.DailyStock + extraPortions,
                RemainingStock = item.RemainingStock + extraPortions,
                Version = item.Version + 1,
            });

    /// <summary>Starts a new service day: all planned portions become available again. Same item if already current.</summary>
    public MenuItem ResetDailyStock(DateOnly serviceDay) =>
        StockDate >= serviceDay ? this : this with { StockDate = serviceDay, RemainingStock = DailyStock };

    /// <summary>Puts the dish on or off the stop list.</summary>
    public MenuItem SetAvailability(bool isAvailable) => this with { IsAvailable = isAvailable, Version = Version + 1 };

    public MenuItem Archive() => this with { IsArchived = true, IsAvailable = false, Version = Version + 1 };

    private MenuItem WithDetails(MenuItemDetails details) => this with
    {
        CategoryId = details.CategoryId,
        Name = details.Name.Trim(),
        Description = (details.Description ?? string.Empty).Trim(),
        Price = decimal.Round(details.Price, 2),
        Emoji = string.IsNullOrWhiteSpace(details.Emoji) ? "🍽️" : details.Emoji.Trim(),
        ImageUrl = string.IsNullOrWhiteSpace(details.ImageUrl) ? null : details.ImageUrl.Trim(),
        Calories = details.Calories,
        Tags = details.Tags,
        PrepMinutes = details.PrepMinutes,
        DailyStock = details.DailyStock,
    };
}

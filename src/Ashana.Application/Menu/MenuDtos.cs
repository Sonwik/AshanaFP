using Ashana.Domain.Menu;

namespace Ashana.Application.Menu;

public sealed record CategoryDto(Guid Id, string Name, string Icon, int SortOrder)
{
    public static CategoryDto From(Category category) =>
        new(category.Id, category.Name, category.Icon, category.SortOrder);
}

/// <summary>What the UI needs to show a dish. Records are immutable, so a cached copy can be shared safely.</summary>
public sealed record MenuItemDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string Description,
    decimal Price,
    string Emoji,
    string? ImageUrl,
    int? Calories,
    MenuTags Tags,
    int PrepMinutes,
    int DailyStock,
    int RemainingStock,
    bool IsAvailable,
    bool IsArchived,
    int Version)
{
    public bool IsOrderable => IsAvailable && !IsArchived && RemainingStock > 0;

    /// <summary>Few portions left: show a "hurry up" hint.</summary>
    public bool IsLowStock => IsOrderable && RemainingStock <= Math.Max(3, DailyStock / 10);

    public bool Has(MenuTags tag) => (Tags & tag) == tag;

    public static MenuItemDto From(MenuItem item) => new(
        item.Id, item.CategoryId, item.Name, item.Description, item.Price, item.Emoji, item.ImageUrl,
        item.Calories, item.Tags, item.PrepMinutes, item.DailyStock, item.RemainingStock,
        item.IsAvailable, item.IsArchived, item.Version);

    public MenuItemDetails ToDetails() =>
        new(CategoryId, Name, Description, Price, Emoji, ImageUrl, Calories, Tags, PrepMinutes, DailyStock);
}

/// <summary>The whole menu as one snapshot.</summary>
public sealed record MenuDto(IReadOnlyList<CategoryDto> Categories, IReadOnlyList<MenuItemDto> Items, DateTimeOffset LoadedAt)
{
    public static readonly MenuDto Empty = new([], [], DateTimeOffset.MinValue);

    public MenuItemDto? Find(Guid id) => Items.FirstOrDefault(i => i.Id == id);

    /// <summary>The dish with this id, or <c>None</c>.</summary>
    public Option<MenuItemDto> FindItem(Guid id) => Items.FirstOrNone(i => i.Id == id);

    /// <summary>Applies live stock changes without reloading the whole menu.</summary>
    public MenuDto WithStock(IEnumerable<Events.StockLevel> levels)
    {
        var map = levels.ToDictionary(l => l.MenuItemId);
        return map.Count == 0
            ? this
            : this with { Items = [.. Items.Select(i => map.Find(i.Id).Match(level => i with { RemainingStock = level.Remaining }, () => i))] };
    }
}

/// <summary>Sort options of the menu page.</summary>
public enum MenuSort
{
    Recommended,
    PriceLowToHigh,
    PriceHighToLow,
    Name,
}

public sealed record MenuFilter(
    Guid? CategoryId = null,
    string? Search = null,
    MenuTags RequiredTags = MenuTags.None,
    bool OnlyAvailable = false,
    MenuSort Sort = MenuSort.Recommended);

public static class MenuFiltering
{
    /// <summary>
    /// Pure filtering shared by the UI and the API. The filter is turned into a list of small steps
    /// (each a function from dishes to dishes) and the steps are composed into one pipeline.
    /// </summary>
    public static IReadOnlyList<MenuItemDto> Apply(MenuDto menu, MenuFilter filter)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(filter);

        var pipeline = F.Compose(Steps(menu, filter));
        return [.. pipeline(menu.Items.Where(i => !i.IsArchived))];
    }

    /// <summary>Only the steps the filter asks for; lazily produced with <c>yield</c>.</summary>
    internal static IEnumerable<Func<IEnumerable<MenuItemDto>, IEnumerable<MenuItemDto>>> Steps(MenuDto menu, MenuFilter filter)
    {
        if (filter.CategoryId is { } categoryId)
        {
            yield return items => items.Where(i => i.CategoryId == categoryId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            yield return items => items.Where(i =>
                i.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                i.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.RequiredTags != MenuTags.None)
        {
            yield return items => items.Where(i => i.Has(filter.RequiredTags));
        }

        if (filter.OnlyAvailable)
        {
            yield return items => items.Where(i => i.IsOrderable);
        }

        yield return Sort(filter.Sort, menu.Categories.ToDictionary(c => c.Id, c => c.SortOrder));
    }

    private static Func<IEnumerable<MenuItemDto>, IEnumerable<MenuItemDto>> Sort(MenuSort sort, IReadOnlyDictionary<Guid, int> categoryOrder) => sort switch
    {
        MenuSort.PriceLowToHigh => items => items.OrderBy(i => i.Price).ThenBy(i => i.Name, StringComparer.CurrentCulture),
        MenuSort.PriceHighToLow => items => items.OrderByDescending(i => i.Price).ThenBy(i => i.Name, StringComparer.CurrentCulture),
        MenuSort.Name => items => items.OrderBy(i => i.Name, StringComparer.CurrentCulture),
        _ => items => items
            .OrderByDescending(i => i.IsOrderable)
            .ThenBy(i => categoryOrder.GetValueOrDefault(i.CategoryId, int.MaxValue))
            .ThenBy(i => i.Name, StringComparer.CurrentCulture),
    };
}

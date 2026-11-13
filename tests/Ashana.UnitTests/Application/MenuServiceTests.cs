using Ashana.Application.Events;
using Ashana.Application.Menu;
using Ashana.Domain.Menu;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

public sealed class MenuServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();
    private Seeded _data = null!;

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Menu_is_cached_until_something_changes()
    {
        var first = await _app.Menu.GetMenuAsync();
        var second = await _app.Menu.GetMenuAsync();

        await _app.Menu.SetAvailabilityAsync(_data.TeaId, isAvailable: false);
        var afterChange = await _app.Menu.GetMenuAsync();

        Assert.Same(first, second);
        Assert.NotSame(first, afterChange);
        Assert.False(afterChange.Find(_data.TeaId)!.IsOrderable);
        Assert.Contains(_app.Events.OfType<MenuChanged>(), e => e.MenuItemId == _data.TeaId);
    }

    [Fact]
    public async Task Menu_lists_categories_in_their_order_and_hides_archived_dishes()
    {
        await _app.Menu.ArchiveAsync(_data.TeaId);

        var menu = await _app.Menu.GetMenuAsync();
        var admin = await _app.Menu.GetAllForAdminAsync();

        Assert.Equal(["Mains", "Drinks"], menu.Categories.Select(c => c.Name));
        Assert.Null(menu.Find(_data.TeaId));
        Assert.True(Assert.Single(admin, i => i.Id == _data.TeaId).IsArchived);
    }

    [Fact]
    public async Task Two_admins_editing_the_same_dish_cannot_overwrite_each_other()
    {
        var opened = (await _app.Menu.GetItemAsync(_data.PlovId)).Value; // both admins open the form at version 0

        var firstSave = await _app.Menu.UpdateAsync(_data.PlovId, opened.Version, opened.ToDetails() with { Price = 1900m });
        var secondSave = await _app.Menu.UpdateAsync(_data.PlovId, opened.Version, opened.ToDetails() with { Description = "New text" });

        Assert.True(firstSave.IsSuccess);
        Assert.Equal(MenuErrors.EditConflict, secondSave.Error);
        var saved = await _app.DishAsync(_data.PlovId);
        Assert.Equal((1900m, "Rice slow-cooked with beef and carrots"), (saved.Price, saved.Description));
    }

    [Fact]
    public async Task Update_validates_the_details_and_the_category()
    {
        var dish = (await _app.Menu.GetItemAsync(_data.PlovId)).Value;

        var badPrice = await _app.Menu.UpdateAsync(dish.Id, dish.Version, dish.ToDetails() with { Price = 0m });
        var badCategory = await _app.Menu.UpdateAsync(dish.Id, dish.Version, dish.ToDetails() with { CategoryId = Guid.NewGuid() });

        Assert.Equal(MenuErrors.PriceOutOfRange, badPrice.Error);
        Assert.Equal(MenuErrors.CategoryNotFound, badCategory.Error);
    }

    [Fact]
    public async Task New_dish_is_on_sale_right_away()
    {
        var created = await _app.Menu.CreateAsync(new MenuItemDetails(_data.DrinksId, "Ayran", "Cold and salty", 400m, "🥛", null, 60, MenuTags.Vegetarian, 1, 30));

        Assert.True(created.Value.IsOrderable);
        Assert.Equal(30, created.Value.RemainingStock);
        Assert.NotNull((await _app.Menu.GetMenuAsync()).Find(created.Value.Id));
    }

    [Fact]
    public async Task New_dish_needs_an_existing_category()
    {
        var created = await _app.Menu.CreateAsync(TestData.Details("Ayran") with { CategoryId = Guid.NewGuid() });

        Assert.Equal(MenuErrors.CategoryNotFound, created.Error);
    }

    [Fact]
    public async Task Restock_puts_a_sold_out_dish_back_on_the_menu()
    {
        Assert.True((await _app.Menu.RestockAsync(_data.SoupId, 12)).IsSuccess);

        var soup = (await _app.Menu.GetMenuAsync()).Find(_data.SoupId)!;
        Assert.Equal((12, true), (soup.RemainingStock, soup.IsOrderable));
    }

    [Fact]
    public async Task Changing_an_unknown_dish_reports_not_found()
    {
        var result = await _app.Menu.ArchiveAsync(Guid.NewGuid());

        Assert.Equal(MenuErrors.ItemNotFound, result.Error);
    }

    [Fact]
    public async Task Category_names_are_unique_ignoring_case()
    {
        var duplicate = await _app.Menu.CreateCategoryAsync("mains", "🍛");
        var created = await _app.Menu.CreateCategoryAsync("Desserts", "🍰");

        Assert.Equal("menu.category_exists", duplicate.Error.Code);
        Assert.Equal(3, created.Value.SortOrder); // added at the end
    }

    [Fact]
    public async Task Daily_stock_refills_once_on_the_next_service_day()
    {
        await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.LagmanId, 3, 1700m));
        Assert.False(await _app.Menu.ResetDailyStockIfNeededAsync()); // still the same day

        _app.Time.Set(TestApp.At(20, 1)); // after closing: tomorrow's menu
        Assert.True(await _app.Menu.ResetDailyStockIfNeededAsync());
        Assert.False(await _app.Menu.ResetDailyStockIfNeededAsync());

        Assert.Equal(3, (await _app.DishAsync(_data.LagmanId)).RemainingStock);
    }
}

public class MenuFilteringTests
{
    private static readonly Guid Mains = Guid.NewGuid();
    private static readonly Guid Drinks = Guid.NewGuid();

    private static readonly MenuDto Menu = new(
        [new CategoryDto(Mains, "Mains", "🍛", 1), new CategoryDto(Drinks, "Drinks", "🍵", 2)],
        [
            Item("Plov", Mains, 1800m, stock: 10, MenuTags.Halal, "Rice with beef"),
            Item("Lagman", Mains, 1700m, stock: 0, MenuTags.Spicy, "Noodles"),
            Item("Vegetable stew", Mains, 1200m, stock: 5, MenuTags.Vegetarian, "Seasonal vegetables"),
            Item("Black tea", Drinks, 300m, stock: 50, MenuTags.Vegetarian, "With milk"),
            Item("Americano", Drinks, 700m, stock: 20, MenuTags.Vegetarian, "Strong coffee"),
        ],
        TestData.Now);

    [Fact]
    public void Recommended_order_puts_available_dishes_first_then_by_category()
    {
        var names = MenuFiltering.Apply(Menu, new MenuFilter()).Select(i => i.Name);

        Assert.Equal(["Plov", "Vegetable stew", "Americano", "Black tea", "Lagman"], names);
    }

    [Theory]
    [InlineData("NOODLE", "Lagman")]       // case-insensitive
    [InlineData("milk", "Black tea")]      // searches descriptions too
    [InlineData("  plov ", "Plov")]        // ignores surrounding spaces
    public void Search_finds_dishes_by_name_or_description(string search, string expected) =>
        Assert.Equal(expected, Assert.Single(MenuFiltering.Apply(Menu, new MenuFilter(Search: search))).Name);

    [Fact]
    public void Filters_combine()
    {
        var result = MenuFiltering.Apply(Menu, new MenuFilter(CategoryId: Mains, RequiredTags: MenuTags.Vegetarian, OnlyAvailable: true));

        Assert.Equal("Vegetable stew", Assert.Single(result).Name);
    }

    [Theory]
    [InlineData(MenuSort.PriceLowToHigh, "Black tea")]
    [InlineData(MenuSort.PriceHighToLow, "Plov")]
    [InlineData(MenuSort.Name, "Americano")]
    public void Sorting_options(MenuSort sort, string expectedFirst) =>
        Assert.Equal(expectedFirst, MenuFiltering.Apply(Menu, new MenuFilter(Sort: sort))[0].Name);

    [Fact]
    public void Live_stock_updates_patch_the_cached_menu()
    {
        var plov = Menu.Items[0];

        var updated = Menu.WithStock([new StockLevel(plov.Id, 0, false)]);

        Assert.False(updated.Find(plov.Id)!.IsOrderable);
        Assert.True(Menu.Find(plov.Id)!.IsOrderable); // the original snapshot is immutable
    }

    private static MenuItemDto Item(string name, Guid category, decimal price, int stock, MenuTags tags, string description) =>
        new(Guid.NewGuid(), category, name, description, price, "🍽️", null, null, tags, 10, Math.Max(stock, 10), stock, true, false, 0);
}

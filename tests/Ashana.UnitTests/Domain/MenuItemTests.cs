using Ashana.Domain.Menu;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Domain;

public class MenuItemTests
{
    public static TheoryData<MenuItemDetails, string> InvalidDetails => new()
    {
        { TestData.Details(name: "  "), "menu.name_required" },
        { TestData.Details(name: new string('a', MenuItem.NameMaxLength + 1)), "menu.name_too_long" },
        { TestData.Details(description: new string('d', MenuItem.DescriptionMaxLength + 1)), "menu.description_too_long" },
        { TestData.Details(price: 0m), "menu.price_range" },
        { TestData.Details(price: MenuErrors.MaxPrice + 1), "menu.price_range" },
        { TestData.Details(stock: -1), "menu.stock_range" },
        { TestData.Details(stock: MenuErrors.MaxDailyStock + 1), "menu.stock_range" },
        { TestData.Details() with { PrepMinutes = 0 }, "menu.prep_range" },
    };

    [Fact]
    public void New_dish_starts_the_day_with_its_full_stock()
    {
        var dish = MenuItem.Create(TestData.Details(name: "  Plov  ", stock: 25), TestData.Today).Value;

        Assert.Equal("Plov", dish.Name);
        Assert.Equal(25, dish.RemainingStock);
        Assert.Equal(TestData.Today, dish.StockDate);
        Assert.True(dish.IsOrderable);
    }

    [Theory]
    [MemberData(nameof(InvalidDetails))]
    public void Invalid_details_are_rejected(MenuItemDetails details, string expectedCode)
    {
        var result = MenuItem.Create(details, TestData.Today);

        Assert.True(result.IsFailure);
        Assert.Equal(expectedCode, result.Error.Code);
    }

    [Fact]
    public void Blank_emoji_gets_a_default_plate()
    {
        var dish = MenuItem.Create(TestData.Details() with { Emoji = " " }, TestData.Today).Value;

        Assert.Equal("🍽️", dish.Emoji);
    }

    [Fact]
    public void Reserve_returns_a_new_dish_with_fewer_portions_and_keeps_the_original()
    {
        var dish = TestData.Dish(stock: 10);

        var reserved = dish.Reserve(3).Value;

        Assert.Equal(7, reserved.RemainingStock);
        Assert.Equal(10, reserved.DailyStock);
        Assert.Equal(10, dish.RemainingStock); // immutability: the old value did not change
    }

    [Fact]
    public void Reserve_more_than_is_left_says_how_many_are_left()
    {
        var dish = TestData.Dish("Lagman", stock: 2);

        var result = dish.Reserve(3);

        Assert.Equal("menu.not_enough_stock", result.Error.Code);
        Assert.Contains("Only 2", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reserve_on_a_sold_out_dish_says_it_is_sold_out()
    {
        var soldOut = TestData.Dish(stock: 1).Reserve(1).Value;

        Assert.Equal("menu.sold_out", soldOut.Reserve(1).Error.Code);
        Assert.False(soldOut.IsOrderable);
    }

    [Fact]
    public void Reserve_on_a_hidden_or_archived_dish_is_refused()
    {
        var hidden = TestData.Dish().SetAvailability(false);
        var archived = TestData.Dish().Archive();

        Assert.Equal("menu.unavailable", hidden.Reserve(1).Error.Code);
        Assert.Equal("menu.unavailable", archived.Reserve(1).Error.Code);
        Assert.False(archived.IsAvailable);
    }

    [Fact]
    public void Reserve_needs_a_positive_quantity() =>
        Assert.Equal(MenuErrors.QuantityMustBePositive, TestData.Dish().Reserve(0).Error);

    [Fact]
    public void Release_returns_portions_but_never_above_the_daily_stock()
    {
        var dish = TestData.Dish(stock: 10).Reserve(4).Value;

        Assert.Equal(9, dish.Release(3).RemainingStock);
        Assert.Equal(10, dish.Release(3).Release(5).RemainingStock);
        Assert.Same(dish, dish.Release(0)); // nothing to release: the very same value comes back
    }

    [Fact]
    public void Restock_adds_extra_portions_for_today()
    {
        var restocked = TestData.Dish(stock: 10).Reserve(10).Bind(d => d.Restock(5)).Value;

        Assert.Equal(15, restocked.DailyStock);
        Assert.Equal(5, restocked.RemainingStock);
        Assert.True(restocked.IsOrderable);
        Assert.Equal(1, restocked.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(MenuErrors.MaxDailyStock)]
    public void Restock_rejects_zero_and_unrealistic_amounts(int extra) =>
        Assert.True(TestData.Dish(stock: 10).Restock(extra).IsFailure);

    [Fact]
    public void Daily_stock_is_refilled_once_per_service_day()
    {
        var dish = TestData.Dish(stock: 10).Reserve(8).Value;

        Assert.Same(dish, dish.ResetDailyStock(TestData.Today));        // same day: keep what was sold
        var tomorrow = dish.ResetDailyStock(TestData.Today.AddDays(1)); // next day: full again
        Assert.Equal(10, tomorrow.RemainingStock);
        Assert.Same(tomorrow, tomorrow.ResetDailyStock(TestData.Today.AddDays(1)));
    }

    [Theory]
    [InlineData(15, 11)] // more portions planned: the 4 already sold stay sold
    [InlineData(5, 1)]
    [InlineData(2, 0)]   // fewer than already sold: nothing left, never negative
    public void Changing_the_daily_stock_keeps_already_sold_portions(int newDailyStock, int expectedRemaining)
    {
        var dish = TestData.Dish(stock: 10).Reserve(4).Value;

        Assert.Equal(expectedRemaining, dish.Update(TestData.Details(stock: newDailyStock)).Value.RemainingStock);
    }

    [Fact]
    public void Editing_a_dish_bumps_its_version_but_selling_does_not()
    {
        var sold = TestData.Dish().Reserve(1).Value;
        Assert.Equal(0, sold.Version);

        var edited = sold.Update(TestData.Details(price: 1900m)).Value;
        Assert.Equal((1, 1900m), (edited.Version, edited.Price));
    }

    [Fact]
    public void Price_is_rounded_to_tiyn()
    {
        var dish = MenuItem.Create(TestData.Details(price: 1799.999m), TestData.Today).Value;

        Assert.Equal(1800.00m, dish.Price);
    }

    [Fact]
    public void All_broken_field_rules_can_be_listed_at_once()
    {
        var details = TestData.Details(name: " ", price: 0m, stock: -1);

        Assert.Equal(["menu.name_required", "menu.price_range", "menu.stock_range"], details.AllErrors(MenuItem.Rules).Select(e => e.Code));
    }
}

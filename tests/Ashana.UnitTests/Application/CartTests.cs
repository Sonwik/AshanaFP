using Ashana.Application.Menu;
using Ashana.Application.Shopping;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

public class CartTests
{
    private static readonly MenuItemDto Plov = Dish("Plov", 1800m, remaining: 10);
    private static readonly MenuItemDto Tea = Dish("Black tea", 300m, remaining: 50);

    [Fact]
    public void Adding_returns_a_new_cart_and_the_same_dish_twice_increases_the_quantity()
    {
        var empty = Cart.Empty;

        var cart = empty.Add(Plov).Bind(c => c.Add(Plov, 2)).Bind(c => c.Add(Tea)).Value;

        Assert.Equal(2, cart.Lines.Count);
        Assert.Equal(3, cart.QuantityOf(Plov.Id));
        Assert.Equal(4, cart.Count);
        Assert.Equal(5700m, cart.Total);
        Assert.True(empty.IsEmpty); // the empty cart is still empty: carts are values
    }

    [Fact]
    public void Cart_never_holds_more_than_is_left_or_more_than_ten_of_a_dish()
    {
        var lastTwo = Dish("Lagman", 1700m, remaining: 2);

        Assert.Equal("menu.not_enough_stock", Cart.Empty.Add(lastTwo, 3).Error.Code);
        Assert.Equal(OrderErrors.QuantityOutOfRange, Cart.Empty.Add(Tea, OrderErrors.MaxQuantityPerLine + 1).Error);
        Assert.Equal(OrderErrors.QuantityOutOfRange, Cart.Empty.Add(Tea, 0).Error);
    }

    [Fact]
    public void Sold_out_and_hidden_dishes_cannot_be_added()
    {
        Assert.Equal("menu.sold_out", Cart.Empty.Add(Dish("Soup", 900m, remaining: 0)).Error.Code);
        Assert.Equal("menu.unavailable", Cart.Empty.Add(Plov with { IsAvailable = false }).Error.Code);
    }

    [Fact]
    public void Cart_holds_at_most_fifteen_different_dishes()
    {
        var full = Enumerable.Range(0, OrderErrors.MaxLines)
            .FoldResult(Cart.Empty, (cart, i) => cart.Add(Dish($"Dish {i}", 100m, 5)))
            .Value;

        Assert.Equal(OrderErrors.MaxLines, full.Lines.Count);
        Assert.Equal(OrderErrors.TooManyLines, full.Add(Dish("One more", 100m, 5)).Error);
    }

    [Fact]
    public void Quantity_changes_respect_the_limits_and_zero_removes_the_line()
    {
        var cart = Cart.Empty.Add(Plov, 2).Value;

        var five = cart.SetQuantity(Plov.Id, 5, remainingStock: 10).Value;
        Assert.Equal(5, five.QuantityOf(Plov.Id));
        Assert.Equal(OrderErrors.QuantityOutOfRange, five.SetQuantity(Plov.Id, 11, remainingStock: 20).Error);
        Assert.Equal("menu.not_enough_stock", five.SetQuantity(Plov.Id, 8, remainingStock: 6).Error.Code);
        Assert.True(five.SetQuantity(Plov.Id, 0).Value.IsEmpty);
        Assert.Same(five, five.SetQuantity(Guid.NewGuid(), 3).Value); // unknown dish: nothing to change
    }

    [Fact]
    public void Checkout_fixes_the_cart_when_the_menu_changed()
    {
        var lagman = Dish("Lagman", 1700m, remaining: 5);
        var cart = Cart.Empty.Add(Plov, 2).Bind(c => c.Add(Tea, 1)).Bind(c => c.Add(lagman, 4)).Value;

        var menu = new MenuDto(
            [],
            [Plov with { Price = 1900m }, lagman with { RemainingStock = 1 }], // tea was removed from the menu
            TestData.Now);

        var (fixedCart, changes) = cart.Reconcile(menu);

        Assert.Equal(3, changes.Count);
        Assert.Contains(changes, c => c.Message.Contains("Price of Plov changed", StringComparison.Ordinal));
        Assert.Contains(changes, c => c.Message.Contains("Black tea is no longer available", StringComparison.Ordinal));
        Assert.Contains(changes, c => c.Message.Contains("Only 1 portion(s) of Lagman", StringComparison.Ordinal));
        Assert.Equal([(Plov.Id, 2, 1900m), (lagman.Id, 1, 1700m)], fixedCart.Lines.Select(l => (l.MenuItemId, l.Quantity, l.UnitPrice)));
        Assert.Equal(3, cart.Lines.Count); // the original cart is unchanged
    }

    [Fact]
    public void Checkout_sends_the_prices_the_student_agreed_to()
    {
        var request = Assert.Single(Cart.Empty.Add(Plov, 2).Value.ToRequests());

        Assert.Equal((Plov.Id, 2, 1800m), (request.MenuItemId, request.Quantity, request.ExpectedUnitPrice));
    }

    [Fact]
    public void A_cart_restored_from_the_browser_is_cleaned_up()
    {
        var id = Guid.NewGuid();
        CartLine[] saved =
        [
            new(id, "Plov", "🍛", 1800m, 25),       // more than the per-dish limit
            new(id, "Plov again", "🍛", 1800m, 1),  // duplicate
            new(Guid.NewGuid(), "Zero", "🍵", 300m, 0),
        ];

        var line = Assert.Single(Cart.From(saved).Lines);

        Assert.Equal(OrderErrors.MaxQuantityPerLine, line.Quantity);
    }

    private static MenuItemDto Dish(string name, decimal price, int remaining) =>
        new(Guid.NewGuid(), TestData.MainsId, name, string.Empty, price, "🍽️", null, null, MenuTags.None, 10, Math.Max(remaining, 10), remaining, true, false, 0);
}

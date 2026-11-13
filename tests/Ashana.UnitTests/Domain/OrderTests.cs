using Ashana.Domain.Orders;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Domain;

public class OrderTests
{
    private static readonly Guid Customer = Guid.NewGuid();

    private static Order Placed() => TestData.NewOrder(Customer, TestData.Slot(10, 15), (TestData.Dish(), 1));

    [Fact]
    public void Create_copies_names_and_prices_and_computes_the_total()
    {
        var plov = TestData.Dish("Plov", 1800m);
        var tea = TestData.Dish("Black tea", 300m);

        var order = TestData.NewOrder(Customer, TestData.Slot(10, 15), (plov, 2), (tea, 1));

        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(3900m, order.Total);
        Assert.Equal(3, order.PortionCount);
        Assert.Equal("#101", order.Code);
        Assert.Equal(TestData.Today, order.ServiceDate);
        Assert.Collection(
            order.Lines,
            line => Assert.Equal(("Plov", 1800m, 2), (line.Name, line.UnitPrice, line.Quantity)),
            line => Assert.Equal(("Black tea", 300m, 1), (line.Name, line.UnitPrice, line.Quantity)));
    }

    [Fact]
    public void Lines_keep_the_price_the_student_saw_when_the_menu_changes_later()
    {
        var plov = TestData.Dish("Plov", 1800m);
        var order = TestData.NewOrder((plov, 1));

        var repriced = plov.Update(TestData.Details("Plov", price: 2100m)).Value;

        Assert.Equal(2100m, repriced.Price);
        Assert.Equal(1800m, Assert.Single(order.Lines).UnitPrice);
        Assert.Equal(1800m, order.Total);
    }

    [Fact]
    public void Create_rejects_an_empty_cart() =>
        Assert.Equal(OrderErrors.EmptyCart, Order.Create(TestData.Draft(Customer, TestData.Slot(10, 15))).Error);

    [Fact]
    public void Create_rejects_more_than_fifteen_different_dishes()
    {
        var lines = Enumerable.Range(1, OrderErrors.MaxLines + 1).Select(i => (TestData.Dish($"Dish {i}"), 1)).ToArray();

        var result = Order.Create(TestData.Draft(Customer, TestData.Slot(10, 15), lines));

        Assert.Equal(OrderErrors.TooManyLines, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(OrderErrors.MaxQuantityPerLine + 1)]
    public void Create_rejects_quantities_outside_one_to_ten(int quantity)
    {
        var result = Order.Create(TestData.Draft(Customer, TestData.Slot(10, 15), (TestData.Dish(), quantity)));

        Assert.True(result.IsFailure);
        Assert.Equal(OrderErrors.QuantityOutOfRange, result.Error);
    }

    [Fact]
    public void Create_rejects_the_same_dish_twice()
    {
        var plov = TestData.Dish();

        var result = Order.Create(TestData.Draft(Customer, TestData.Slot(10, 15), (plov, 1), (plov, 2)));

        Assert.Equal(OrderErrors.DuplicateLine, result.Error);
    }

    [Theory]
    [InlineData("  no onion, please  ", "no onion, please")]
    [InlineData("   ", null)]
    public void Comment_is_trimmed_and_blank_comments_are_dropped(string comment, string? expected)
    {
        var draft = TestData.Draft(Customer, TestData.Slot(10, 15), (TestData.Dish(), 1)) with { Comment = comment };

        Assert.Equal(expected, Order.Create(draft).Value.Comment);
    }

    [Fact]
    public void Create_rejects_a_comment_longer_than_200_characters()
    {
        var draft = TestData.Draft(Customer, TestData.Slot(10, 15), (TestData.Dish(), 1)) with { Comment = new string('x', 201) };

        Assert.Equal(OrderErrors.CommentTooLong, Order.Create(draft).Error);
    }

    [Fact]
    public void The_draft_rules_are_data_and_the_first_broken_rule_wins()
    {
        var draft = TestData.Draft(Customer, TestData.Slot(10, 15)) with { Comment = new string('x', 201) }; // empty AND too long

        var broken = draft.AllErrors(Order.DraftRules);

        Assert.Equal([OrderErrors.EmptyCart, OrderErrors.CommentTooLong], broken);
        Assert.Equal(OrderErrors.EmptyCart, Order.Create(draft).Error);
    }

    [Fact]
    public void Kitchen_steps_return_new_orders_and_record_when_each_step_happened()
    {
        var placed = Placed();
        var start = TestData.At(10, 3);

        var pickedUp = placed.StartCooking(start)
            .Bind(o => o.MarkReady(start.AddMinutes(9)))
            .Bind(o => o.MarkPickedUp(start.AddMinutes(14)))
            .Value;

        Assert.Equal(OrderStatus.PickedUp, pickedUp.Status);
        Assert.Equal(start, pickedUp.CookingStartedAt);
        Assert.Equal(start.AddMinutes(9), pickedUp.ReadyAt);
        Assert.Equal(start.AddMinutes(14), pickedUp.PickedUpAt);
        Assert.Equal(3, pickedUp.Version); // every change bumps the optimistic-concurrency version

        // Immutability: the original value is exactly what it was.
        Assert.Equal((OrderStatus.Placed, 0, (DateTimeOffset?)null), (placed.Status, placed.Version, placed.CookingStartedAt));
        Assert.Equal(placed.Id, pickedUp.Id);
    }

    [Fact]
    public void Advance_walks_the_happy_path_and_then_stops()
    {
        var steps = Seq.Iterate(Result.Success(Placed()), r => r.Bind(o => o.Advance(TestData.Now))).Take(5).ToList();

        Assert.Equal(
            [OrderStatus.Placed, OrderStatus.Cooking, OrderStatus.Ready, OrderStatus.PickedUp],
            steps.Take(4).Select(r => r.Value.Status));
        Assert.Equal("order.invalid_transition", steps[4].Error.Code);
    }

    [Fact]
    public void A_refused_step_returns_an_error_and_the_order_stays_as_it_was()
    {
        var order = Placed();

        var result = order.MarkReady(TestData.Now);

        Assert.Equal(ErrorKind.Conflict, result.Error.Kind);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Null(order.ReadyAt);
        Assert.Equal(0, order.Version);
    }

    [Fact]
    public void Student_can_cancel_while_the_order_is_waiting()
    {
        var cancelled = Placed().CancelByCustomer(Customer, TestData.At(10, 2)).Value;

        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(CancelSource.Customer, cancelled.CancelledBy);
        Assert.Equal(TestData.At(10, 2), cancelled.CancelledAt);
    }

    [Fact]
    public void Student_cannot_cancel_after_the_kitchen_started()
    {
        var cooking = Placed().StartCooking(TestData.Now).Value;

        var result = cooking.CancelByCustomer(Customer, TestData.Now);

        Assert.Equal(OrderErrors.CannotCancelNow, result.Error);
        Assert.Equal(OrderStatus.Cooking, cooking.Status);
    }

    [Fact]
    public void Student_cannot_cancel_someone_elses_order() =>
        Assert.Equal(ErrorKind.Forbidden, Placed().CancelByCustomer(Guid.NewGuid(), TestData.Now).Error.Kind);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Kitchen_must_give_a_reason_to_cancel(string reason) =>
        Assert.Equal(OrderErrors.ReasonRequired, Placed().CancelByKitchen(reason, TestData.Now).Error);

    [Fact]
    public void Kitchen_can_cancel_while_cooking_but_not_when_the_food_is_ready()
    {
        var cooking = Placed().StartCooking(TestData.Now).Value;
        var ready = cooking.MarkReady(TestData.Now).Value;

        var cancelled = cooking.CancelByKitchen(" Out of rice ", TestData.Now).Value;

        Assert.Equal("Out of rice", cancelled.CancelReason);
        Assert.Equal(CancelSource.Kitchen, cancelled.CancelledBy);
        Assert.True(ready.CancelByKitchen("Too late", TestData.Now).IsFailure);
    }

    [Fact]
    public void Order_is_late_when_its_slot_started_and_it_is_not_ready()
    {
        var order = Placed();
        var ready = order.StartCooking(TestData.At(10, 5)).Bind(o => o.MarkReady(TestData.At(10, 12))).Value;

        Assert.False(order.IsLate(TestData.At(10, 14)));
        Assert.True(order.IsLate(TestData.At(10, 15)));
        Assert.False(ready.IsLate(TestData.At(10, 20)));
    }

    [Fact]
    public void No_show_is_due_thirty_minutes_after_the_slot_ends()
    {
        var ready = Placed().StartCooking(TestData.At(10, 5)).Bind(o => o.MarkReady(TestData.At(10, 14))).Value; // slot 10:15–10:30

        Assert.False(ready.IsNoShowDue(TestData.At(10, 59), noShowAfterMinutes: 30));
        Assert.True(ready.IsNoShowDue(TestData.At(11, 0), noShowAfterMinutes: 30));
        Assert.Equal(OrderStatus.NoShow, ready.MarkNoShow(TestData.At(11, 0)).Value.Status);
    }

    [Fact]
    public void Two_orders_built_from_the_same_data_are_equal_values_except_for_identity()
    {
        var first = Placed();
        var second = first.StartCooking(TestData.Now).Value;

        Assert.NotEqual(first, second);
        Assert.Equal(first.Lines, second.Lines); // the lines were shared, not copied
    }
}

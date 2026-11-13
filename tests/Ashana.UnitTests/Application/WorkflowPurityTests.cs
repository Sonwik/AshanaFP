using Ashana.Application.Events;
using Ashana.Application.Kitchen;
using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

/// <summary>
/// The use cases are pure functions of (state, input, time). These tests call them directly:
/// no services, no store, no clock, no mocks. Just values in and values out.
/// </summary>
public class WorkflowPurityTests
{
    private static readonly User Student = User.Create("s@uni.kz", "Aruzhan", "hash", UserRole.Student, TestData.Now).Value;
    private static readonly MenuItem Plov = TestData.Dish("Plov", 1800m, stock: 2);
    private static readonly MenuItem Tea = TestData.Dish("Black tea", 300m, stock: 50);

    private static readonly CanteenState Start = CanteenState.Empty()
        .With(CanteenState.Empty().Settings.MarkStockReset(TestData.Today).GetOrElse(CanteenState.Empty().Settings))
        .With(Student)
        .WithAll([Plov, Tea]);

    private static PlaceOrderCommand Command(int plov, int tea = 1, Guid? requestId = null) => new(
        Student.Id,
        [new CartItemRequest(Plov.Id, plov, 1800m), new CartItemRequest(Tea.Id, tea, 300m)],
        TestData.At(12, 0),
        PaymentMethod.Card,
        null,
        requestId ?? Guid.NewGuid());

    private static Result<Change<Placement>> Place(CanteenState state, PlaceOrderCommand command) =>
        OrderWorkflow.Place(state, command, TestData.Now, TestData.Zone);

    [Fact]
    public void Placing_returns_a_new_state_and_leaves_the_input_state_untouched()
    {
        var placed = Place(Start, Command(plov: 2)).Value;

        Assert.Single(placed.State.Orders);
        Assert.Equal(0, placed.State.FindItem(Plov.Id).Map(i => i.RemainingStock).GetOrElse(-1));
        Assert.Equal(2, Start.FindItem(Plov.Id).Map(i => i.RemainingStock).GetOrElse(-1));
        Assert.Empty(Start.Orders);
        Assert.Equal([0, 49], placed.Value.ReservedItems.Select(i => i.RemainingStock));
    }

    [Fact]
    public void Same_inputs_give_the_same_decision()
    {
        var command = Command(plov: 1);

        var first = Place(Start, command).Value;
        var second = Place(Start, command).Value;

        Assert.Equal(
            (first.Value.Order.Number, first.Value.Order.Total, first.Value.Order.Status),
            (second.Value.Order.Number, second.Value.Order.Total, second.Value.Order.Status));
        Assert.Equal(first.Value.ReservedItems, second.Value.ReservedItems);
    }

    [Fact]
    public void A_failure_anywhere_in_the_pipeline_is_just_an_error_value()
    {
        var notEnough = Place(Start, Command(plov: 3));

        Assert.Equal("menu.not_enough_stock", notEnough.Error.Code);
        Assert.Equal(2, Start.FindItem(Plov.Id).Map(i => i.RemainingStock).GetOrElse(-1)); // tea was not half-reserved either
        Assert.Equal(50, Start.FindItem(Tea.Id).Map(i => i.RemainingStock).GetOrElse(-1));
    }

    [Fact]
    public void Repeating_a_request_is_idempotent()
    {
        var requestId = Guid.NewGuid();
        var once = Place(Start, Command(plov: 1, requestId: requestId)).Value;

        var twice = Place(once.State, Command(plov: 1, requestId: requestId)).Value;

        Assert.Same(once.State, twice.State);   // nothing changed the second time
        Assert.False(twice.Value.IsNew);
        Assert.Equal(once.Value.Order.Id, twice.Value.Order.Id);
    }

    [Fact]
    public void A_whole_order_life_is_a_fold_of_transitions_over_the_state()
    {
        var placed = Place(Start, Command(plov: 1)).Value;
        var id = placed.Value.Order.Id;

        var finalState = new[] { OrderStatus.Placed, OrderStatus.Cooking, OrderStatus.Ready }
            .FoldResult(placed.State, (state, expected) => OrderWorkflow.Advance(state, id, expected, TestData.At(11, 50)).Map(c => c.State))
            .Value;

        Assert.Equal(OrderStatus.PickedUp, finalState.FindOrder(id).Map(o => o.Status).GetOrElse(OrderStatus.Placed));
    }

    [Fact]
    public void A_cook_with_an_old_screen_cannot_skip_a_step()
    {
        var placed = Place(Start, Command(plov: 1)).Value;
        var id = placed.Value.Order.Id;
        var cooking = OrderWorkflow.Advance(placed.State, id, OrderStatus.Placed, TestData.Now).Value.State;

        var stale = OrderWorkflow.Advance(cooking, id, OrderStatus.Placed, TestData.Now); // second cook saw "Placed"

        Assert.Equal("order.invalid_transition", stale.Error.Code);
    }

    [Fact]
    public void Cancelling_a_placed_order_gives_the_portions_back()
    {
        var placed = Place(Start, Command(plov: 2)).Value;

        var cancelled = OrderWorkflow.CancelByCustomer(placed.State, placed.Value.Order.Id, Student.Id, TestData.At(10, 5)).Value;

        Assert.Equal(OrderStatus.Cancelled, cancelled.Value.Order.Status);
        Assert.Equal(2, cancelled.State.FindItem(Plov.Id).Map(i => i.RemainingStock).GetOrElse(-1));
        Assert.Equal(2, cancelled.Value.ReleasedItems.Count);
    }

    [Fact]
    public void Housekeeping_is_a_total_function_that_may_change_nothing()
    {
        var unchanged = OrderWorkflow.CloseExpired(Start, TestData.Now);

        Assert.Same(Start.Orders, unchanged.State.Orders);
        Assert.Empty(unchanged.Value);
    }

    [Fact]
    public void Stock_day_reset_returns_the_same_state_when_nothing_is_due()
    {
        Assert.Same(Start, StockDay.EnsureCurrent(Start, TestData.Today));

        var tomorrow = StockDay.EnsureCurrent(Place(Start, Command(plov: 2)).Value.State, TestData.Today.AddDays(1));
        Assert.Equal(2, tomorrow.FindItem(Plov.Id).Map(i => i.RemainingStock).GetOrElse(-1));
    }

    [Fact]
    public void Read_models_are_projections_of_the_state()
    {
        var placed = Place(Start, Command(plov: 1)).Value.State;

        var board = KitchenService.Board(placed, TestData.Now, TestData.Zone);
        var tv = OrderWorkflow.Board(placed, TestData.Now, TestData.Zone);
        var menu = MenuService.BuildMenu(placed, TestData.Now);

        Assert.Single(board.Placed);
        Assert.Equal(101, Assert.Single(tv.Preparing).Number);
        Assert.Equal(1, menu.Items.Single(i => i.Id == Plov.Id).RemainingStock);
    }

    [Fact]
    public void Menu_filter_steps_are_composed_into_one_pipeline()
    {
        var menu = MenuService.BuildMenu(Start, TestData.Now);

        var steps = MenuFiltering.Steps(menu, new MenuFilter(Search: "tea", Sort: MenuSort.Name)).ToList();
        var filtered = MenuFiltering.Apply(menu, new MenuFilter(Search: "tea", Sort: MenuSort.Name));

        Assert.Equal(2, steps.Count); // search + sort; no category or tag step was needed
        Assert.Equal("Black tea", Assert.Single(filtered).Name);
    }

    [Fact]
    public void Every_event_case_has_a_description()
    {
        AppEvent[] events =
        [
            new MenuChanged(null),
            new MenuChanged(Plov.Id),
            new StockChanged([new StockLevel(Plov.Id, 1, true)]),
            new SettingsChanged(TestData.Rules),
        ];

        Assert.All(events, e => Assert.False(string.IsNullOrWhiteSpace(e.Describe())));
        Assert.Equal("Menu changed", events[0].Describe());
    }
}

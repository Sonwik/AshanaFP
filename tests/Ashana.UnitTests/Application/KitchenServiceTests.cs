using Ashana.Application.Events;
using Ashana.Application.Kitchen;
using Ashana.Domain.Orders;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

public sealed class KitchenServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();
    private Seeded _data = null!;

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Cook_moves_an_order_through_the_kitchen()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));

        var cooking = await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Placed);
        var ready = await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Cooking);
        var handedOver = await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Ready);

        Assert.Equal(
            [OrderStatus.Cooking, OrderStatus.Ready, OrderStatus.PickedUp],
            new[] { cooking, ready, handedOver }.Select(r => r.Value.Status));
        Assert.Equal(3, _app.Events.OfType<OrderChanged>().Count(e => e.Kind == OrderChangeKind.StatusChanged));
    }

    [Fact]
    public async Task Second_cook_pressing_the_same_button_does_not_skip_a_step()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));

        // Both screens still show "New". The first press starts cooking; the second one must not mark it ready.
        var first = await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Placed);
        var second = await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Placed);

        Assert.True(first.IsSuccess);
        Assert.Equal("order.invalid_transition", second.Error.Code);
        Assert.Equal(OrderStatus.Cooking, (await _app.OrderAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Kitchen_cancel_of_a_waiting_order_returns_the_portions()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 4, 1800m));

        var result = await _app.Kitchen.CancelAsync(order.Id, "Out of rice");

        Assert.Equal((OrderStatus.Cancelled, CancelSource.Kitchen, "Out of rice"), (result.Value.Status, result.Value.CancelledBy, result.Value.CancelReason));
        Assert.Equal(10, (await _app.DishAsync(_data.PlovId)).RemainingStock);
    }

    [Fact]
    public async Task Kitchen_cancel_while_cooking_keeps_the_portions_used()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 4, 1800m));
        await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Placed);

        Assert.True((await _app.Kitchen.CancelAsync(order.Id, "Burnt")).IsSuccess);

        Assert.Equal(6, (await _app.DishAsync(_data.PlovId)).RemainingStock); // the food was already being cooked
    }

    [Fact]
    public async Task Kitchen_cancel_needs_a_reason()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));

        Assert.Equal(OrderErrors.ReasonRequired, (await _app.Kitchen.CancelAsync(order.Id, " ")).Error);
    }

    [Fact]
    public async Task Board_groups_orders_and_counts_the_late_ones()
    {
        var a = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));
        await _app.PlaceAsync(_data.Student2Id, TestApp.At(10, 45), (_data.TeaId, 1, 300m));
        await _app.Kitchen.AdvanceAsync(a.Id, OrderStatus.Placed);

        _app.Time.Set(TestApp.At(10, 20)); // order a (10:15) is still cooking: late
        var board = await _app.Kitchen.GetBoardAsync();

        Assert.Equal((1, 1, 0), (board.Placed.Count, board.Cooking.Count, board.Ready.Count));
        Assert.Equal(2, board.ActiveCount);
        Assert.Equal(1, board.LateCount);
        Assert.True(board.AcceptingOrders);
    }

    [Fact]
    public async Task Prep_list_adds_up_portions_per_slot_and_for_the_day()
    {
        await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 2, 1800m), (_data.TeaId, 1, 300m));
        await _app.PlaceAsync(_data.Student2Id, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));
        var later = await _app.PlaceAsync(_data.Student2Id, TestApp.At(10, 30), (_data.LagmanId, 1, 1700m));
        var done = await _app.PlaceAsync(_data.StudentId, TestApp.At(11, 0), (_data.TeaId, 5, 300m));
        await _app.Kitchen.AdvanceAsync(done.Id, OrderStatus.Placed);
        await _app.Kitchen.AdvanceAsync(done.Id, OrderStatus.Cooking); // ready: nothing left to cook

        var prep = await _app.Kitchen.GetPrepListAsync();

        Assert.Equal(["10:15–10:30", "10:30–10:45"], prep.Slots.Select(s => s.Label));
        var first = prep.Slots[0];
        Assert.Equal((2, 4), (first.OrderCount, first.Portions));
        Assert.Equal([("Plov", 3), ("Black tea", 1)], first.Items.Select(i => (i.Name, i.Quantity)));
        Assert.Equal([("Plov", 3), ("Black tea", 1), ("Lagman", 1)], prep.Totals.Select(i => (i.Name, i.Quantity)));
        Assert.Equal(later.PickupStart, prep.Slots[1].Start);
    }
}

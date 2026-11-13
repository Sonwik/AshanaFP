using Ashana.Application.Events;
using Ashana.Application.Orders;
using Ashana.Domain.Canteen;
using Ashana.Domain.Orders;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

public sealed class OrderServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create(); // Friday 10:00 in the canteen
    private Seeded _data = null!;

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Placing_an_order_reserves_portions_and_gives_it_a_number()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 2, 1800m), (_data.TeaId, 1, 300m));

        Assert.Equal(OrderService.FirstOrderNumber, order.Number);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(3900m, order.Total);
        Assert.Equal("10:15–10:30", order.SlotLabel);
        Assert.Equal("Aruzhan Bekova", order.CustomerName);
        Assert.Equal(8, (await _app.DishAsync(_data.PlovId)).RemainingStock);
        Assert.Equal(49, (await _app.DishAsync(_data.TeaId)).RemainingStock);
    }

    [Fact]
    public async Task Orders_of_the_day_are_numbered_one_after_another()
    {
        var first = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));
        var second = await _app.PlaceAsync(_data.Student2Id, TestApp.At(11, 0), (_data.TeaId, 1, 300m));

        Assert.Equal((101, 102), (first.Number, second.Number));
    }

    [Fact]
    public async Task Sending_the_same_checkout_twice_creates_one_order()
    {
        var command = TestApp.Command(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 2, 1800m));

        var first = await _app.Orders.PlaceOrderAsync(command);
        var retry = await _app.Orders.PlaceOrderAsync(command); // double click, or a retry after a lost response

        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(8, (await _app.DishAsync(_data.PlovId)).RemainingStock);
        Assert.Single(await _app.Orders.GetMyOrdersAsync(_data.StudentId));
    }

    [Fact]
    public async Task If_one_dish_is_short_nothing_is_reserved()
    {
        var command = TestApp.Command(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 2, 1800m), (_data.LagmanId, 5, 1700m));

        var result = await _app.Orders.PlaceOrderAsync(command);

        Assert.Equal("menu.not_enough_stock", result.Error.Code);
        Assert.Equal(10, (await _app.DishAsync(_data.PlovId)).RemainingStock); // all or nothing
        Assert.Equal(3, (await _app.DishAsync(_data.LagmanId)).RemainingStock);
        Assert.Empty(_app.Events.OfType<OrderChanged>());
    }

    [Fact]
    public async Task Sold_out_dish_cannot_be_ordered()
    {
        var result = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(10, 15), (_data.SoupId, 1, 900m)));

        Assert.Equal("menu.sold_out", result.Error.Code);
    }

    [Fact]
    public async Task Order_is_refused_when_the_price_changed_after_the_student_saw_it()
    {
        var result = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1500m)));

        Assert.Equal("order.price_changed", result.Error.Code);
        Assert.Contains("Plov", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Archived_or_unknown_dishes_cannot_be_ordered()
    {
        await _app.Menu.ArchiveAsync(_data.TeaId);

        var archived = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(10, 15), (_data.TeaId, 1, 300m)));
        var unknown = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(10, 15), (Guid.NewGuid(), 1, 300m)));

        Assert.Equal("menu.not_found", archived.Error.Code);
        Assert.Equal("menu.not_found", unknown.Error.Code);
    }

    [Fact]
    public async Task A_full_slot_sends_students_to_another_time()
    {
        await _app.SetRulesAsync(rules => rules with { MaxOrdersPerSlot = 1 });
        await _app.PlaceAsync(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m));

        var second = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.Student2Id, TestApp.At(12, 0), (_data.TeaId, 1, 300m)));
        var otherSlot = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.Student2Id, TestApp.At(12, 15), (_data.TeaId, 1, 300m)));

        Assert.Equal(SlotPlanner.SlotFull.Code, second.Error.Code);
        Assert.Contains("The next free time is 12:15–12:30", second.Error.Message, StringComparison.Ordinal);
        Assert.True(otherSlot.IsSuccess);
    }

    [Fact]
    public async Task Cancelled_orders_free_their_place_in_the_slot()
    {
        await _app.SetRulesAsync(rules => rules with { MaxOrdersPerSlot = 1 });
        var first = await _app.PlaceAsync(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m));
        await _app.Orders.CancelAsync(first.Id, _data.StudentId);

        var second = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.Student2Id, TestApp.At(12, 0), (_data.TeaId, 1, 300m)));

        Assert.True(second.IsSuccess);
    }

    [Fact]
    public async Task Nothing_can_be_ordered_while_the_kitchen_paused_orders()
    {
        await _app.Settings.SetAcceptingOrdersAsync(false, "Back at 12:30");

        var result = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m)));

        Assert.Equal(SlotPlanner.OrdersPaused, result.Error);
    }

    [Fact]
    public async Task The_kitchen_needs_ten_minutes()
    {
        var result = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(10, 0), (_data.TeaId, 1, 300m)));

        Assert.Equal("slot.too_late", result.Error.Code);
    }

    [Fact]
    public async Task A_student_can_have_at_most_three_active_orders()
    {
        for (var i = 0; i < 3; i++)
        {
            await _app.PlaceAsync(_data.StudentId, TestApp.At(11, 15 * i), (_data.TeaId, 1, 300m));
        }

        var fourth = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m)));

        Assert.Equal("order.active_limit", fourth.Error.Code);
    }

    [Fact]
    public async Task Blocked_students_cannot_order()
    {
        await _app.UserAdmin.SetActiveAsync(_data.AdminId, _data.StudentId, isActive: false);

        var result = await _app.Orders.PlaceOrderAsync(TestApp.Command(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m)));

        Assert.Equal(OrderErrors.UserInactive, result.Error);
    }

    [Fact]
    public async Task Malformed_checkouts_are_rejected_before_touching_stock()
    {
        var empty = TestApp.Command(_data.StudentId, TestApp.At(12, 0));
        var noRequestId = TestApp.Command(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m)) with { ClientRequestId = Guid.Empty };
        var duplicate = TestApp.Command(_data.StudentId, TestApp.At(12, 0), (_data.TeaId, 1, 300m), (_data.TeaId, 2, 300m));

        Assert.Equal(OrderErrors.EmptyCart, (await _app.Orders.PlaceOrderAsync(empty)).Error);
        Assert.Equal(ErrorKind.Validation, (await _app.Orders.PlaceOrderAsync(noRequestId)).Error.Kind);
        Assert.Equal(OrderErrors.DuplicateLine, (await _app.Orders.PlaceOrderAsync(duplicate)).Error);
    }

    [Fact]
    public async Task Placing_an_order_tells_the_kitchen_and_the_menus()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 2, 1800m));

        var changed = Assert.Single(_app.Events.OfType<OrderChanged>());
        Assert.Equal((order.Id, OrderChangeKind.Placed), (changed.Order.Id, changed.Kind));
        var stock = Assert.Single(Assert.Single(_app.Events.OfType<StockChanged>()).Levels);
        Assert.Equal((_data.PlovId, 8, true), (stock.MenuItemId, stock.Remaining, stock.IsOrderable));
    }

    [Fact]
    public async Task Cancelling_a_waiting_order_puts_the_portions_back_on_sale()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.LagmanId, 3, 1700m));
        Assert.Equal(0, (await _app.DishAsync(_data.LagmanId)).RemainingStock);

        var result = await _app.Orders.CancelAsync(order.Id, _data.StudentId);

        Assert.Equal(OrderStatus.Cancelled, result.Value.Status);
        Assert.Equal(CancelSource.Customer, result.Value.CancelledBy);
        Assert.Equal(3, (await _app.DishAsync(_data.LagmanId)).RemainingStock);
        Assert.Contains(_app.Events.OfType<OrderChanged>(), e => e.Kind == OrderChangeKind.Cancelled);
    }

    [Fact]
    public async Task Students_cannot_cancel_once_cooking_started_or_cancel_other_peoples_orders()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));

        var byOther = await _app.Orders.CancelAsync(order.Id, _data.Student2Id);
        await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Placed);
        var tooLate = await _app.Orders.CancelAsync(order.Id, _data.StudentId);

        Assert.Equal(ErrorKind.Forbidden, byOther.Error.Kind);
        Assert.Equal(OrderErrors.CannotCancelNow, tooLate.Error);
    }

    [Fact]
    public async Task Students_only_see_their_own_orders_but_staff_see_all()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));

        Assert.True((await _app.Orders.GetAsync(order.Id, _data.StudentId, requesterIsStaff: false)).IsSuccess);
        Assert.Equal(OrderErrors.NotYourOrder, (await _app.Orders.GetAsync(order.Id, _data.Student2Id, requesterIsStaff: false)).Error);
        Assert.True((await _app.Orders.GetAsync(order.Id, _data.CookId, requesterIsStaff: true)).IsSuccess);
        Assert.Equal(OrderErrors.NotFound, (await _app.Orders.GetAsync(Guid.NewGuid(), _data.CookId, requesterIsStaff: true)).Error);
    }

    [Fact]
    public async Task Parallel_checkouts_never_sell_more_portions_than_exist()
    {
        // Lagman has 3 portions. Twelve students press "Place order" at the same moment.
        var students = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            students.Add(await _app.AddUserAsync($"rush{i}@test.kz"));
        }

        var results = await Task.WhenAll(students.Select(id =>
            Task.Run(() => _app.Orders.PlaceOrderAsync(TestApp.Command(id, TestApp.At(12, 0), (_data.LagmanId, 1, 1700m))))));

        Assert.Equal(3, results.Count(r => r.IsSuccess));
        Assert.All(results.Where(r => r.IsFailure), r => Assert.Equal("menu.sold_out", r.Error.Code));
        Assert.Equal(0, (await _app.DishAsync(_data.LagmanId)).RemainingStock);
        Assert.Equal([101, 102, 103], results.Where(r => r.IsSuccess).Select(r => r.Value.Number).Order());
    }

    [Fact]
    public async Task Pickup_board_lists_numbers_by_status()
    {
        var cooking = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m));
        var ready = await _app.PlaceAsync(_data.Student2Id, TestApp.At(10, 15), (_data.TeaId, 1, 300m));
        var waiting = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 30), (_data.TeaId, 1, 300m));
        await _app.Kitchen.AdvanceAsync(cooking.Id, OrderStatus.Placed);
        await _app.Kitchen.AdvanceAsync(ready.Id, OrderStatus.Placed);
        await _app.Kitchen.AdvanceAsync(ready.Id, OrderStatus.Cooking);

        var board = await _app.Orders.GetBoardAsync();

        Assert.Equal([cooking.Number, waiting.Number], board.Preparing.Select(e => e.Number));
        Assert.Equal(ready.Number, Assert.Single(board.Ready).Number);
    }

    [Fact]
    public async Task Uncollected_orders_become_no_shows_thirty_minutes_after_their_slot()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.PlovId, 1, 1800m)); // 10:15–10:30
        await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Placed);
        await _app.Kitchen.AdvanceAsync(order.Id, OrderStatus.Cooking);

        _app.Time.Set(TestApp.At(10, 59));
        Assert.Equal(0, await _app.Orders.CloseExpiredOrdersAsync());

        _app.Time.Set(TestApp.At(11, 0));
        Assert.Equal(1, await _app.Orders.CloseExpiredOrdersAsync());
        Assert.Equal(OrderStatus.NoShow, (await _app.OrderAsync(order.Id)).Status);
    }

    [Fact]
    public async Task Orders_left_unfinished_at_the_end_of_the_day_are_cancelled()
    {
        var order = await _app.PlaceAsync(_data.StudentId, TestApp.At(19, 45), (_data.PlovId, 1, 1800m));

        _app.Time.Set(TestApp.At(20, 5)); // after closing the service day is tomorrow
        var closed = await _app.Orders.CloseExpiredOrdersAsync();

        var saved = await _app.OrderAsync(order.Id);
        Assert.Equal(1, closed);
        Assert.Equal((OrderStatus.Cancelled, CancelSource.Kitchen), (saved.Status, saved.CancelledBy));
    }
}

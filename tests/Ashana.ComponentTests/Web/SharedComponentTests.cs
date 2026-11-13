using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.ComponentTests.Support;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.UI;
using Ashana.Web.Components.Shared;

namespace Ashana.ComponentTests.Web;

public sealed class SlotPickerTests : IAsyncDisposable
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(5);
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Every_slot_says_how_full_it_is_and_full_ones_cannot_be_picked()
    {
        var picker = await RenderPicker(selected: null, onSelect: _ => { });

        var tooSoon = picker.Find("button", b => b.Text.StartsWith("12:00", StringComparison.Ordinal));
        var free = picker.Find("button", b => b.Text.StartsWith("12:15", StringComparison.Ordinal));
        var busy = picker.Find("button", b => b.Text.StartsWith("12:30", StringComparison.Ordinal));
        var full = picker.Find("button", b => b.Text.StartsWith("12:45", StringComparison.Ordinal));

        Assert.True(tooSoon.Has("disabled"));
        Assert.Equal("12:15–12:30, 18 of 20 places free", free["aria-label"]);
        Assert.Contains("3 left", busy.Text);
        Assert.Equal("12:45–13:00, fully booked", full["aria-label"]);
        Assert.True(full.Has("disabled"));
    }

    [Fact]
    public async Task Picking_a_slot_reports_its_start_time()
    {
        DateTimeOffset? picked = null;
        var picker = await RenderPicker(selected: At(12, 30), onSelect: value => picked = value);

        await picker.ClickAsync(picker.Find("button", b => b.Text.StartsWith("12:15", StringComparison.Ordinal)));
        await picker.ClickAsync(picker.Find("button", b => b.Text.StartsWith("12:45", StringComparison.Ordinal))); // full: ignored

        Assert.Equal(At(12, 15), picked);
        Assert.Equal("true", picker.Find("button", b => b.Text.StartsWith("12:30", StringComparison.Ordinal))["aria-checked"]);
    }

    [Fact]
    public async Task Paused_kitchen_explains_why_nothing_can_be_picked()
    {
        var picker = await _ctx.RenderAsync<SlotPicker>(new Dictionary<string, object?>
        {
            [nameof(SlotPicker.Day)] = Day(),
            [nameof(SlotPicker.AcceptingOrders)] = false,
            [nameof(SlotPicker.PauseMessage)] = "Back at 12:30.",
        });

        Assert.Contains("New orders are paused. Back at 12:30.", picker.Text);
        Assert.Empty(picker.FindAll("button"));
    }

    private Task<Rendered<SlotPicker>> RenderPicker(DateTimeOffset? selected, Action<DateTimeOffset?> onSelect) =>
        _ctx.RenderAsync<SlotPicker>(new Dictionary<string, object?>
        {
            [nameof(SlotPicker.Day)] = Day(),
            [nameof(SlotPicker.Selected)] = selected,
            [nameof(SlotPicker.SelectedChanged)] = Fragments.Callback(onSelect),
        });

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 30, hour, minute, 0, Offset);

    private static SlotDay Day() => new(new DateOnly(2026, 10, 30), "Today",
    [
        new SlotOption(At(12, 0), At(12, 15), 0, 20, SlotState.TooSoon),
        new SlotOption(At(12, 15), At(12, 30), 2, 20, SlotState.Available),
        new SlotOption(At(12, 30), At(12, 45), 17, 20, SlotState.Busy),
        new SlotOption(At(12, 45), At(13, 0), 20, 20, SlotState.Full),
    ]);
}

public sealed class MenuItemRowTests : IAsyncDisposable
{
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Dish_shows_price_tags_and_calories()
    {
        var row = await RenderRow(Dish(remaining: 30, tags: MenuTags.Spicy | MenuTags.Halal));

        Assert.Contains("Lagman", row.Find("h3").Text);
        Assert.Equal("1 700 ₸", row.Find("span", s => s.HasClass("dish-price")).Text);
        Assert.Equal(["Spicy", "Halal"], row.FindAll("span", s => s.HasClass("tag")).Select(s => s.Text));
        Assert.Contains("650 kcal", row.Text);
    }

    [Fact]
    public async Task Add_button_sends_the_dish_to_the_cart()
    {
        MenuItemDto? added = null;
        var dish = Dish(remaining: 30);
        var row = await RenderRow(dish, onAdd: item => added = item);

        await row.ClickAsync(row.Find("button", b => b["aria-label"] == "Add Lagman"));

        Assert.Equal(dish, added);
    }

    [Fact]
    public async Task Sold_out_dish_cannot_be_added()
    {
        var row = await RenderRow(Dish(remaining: 0));

        Assert.Contains("Sold out", row.Text);
        Assert.True(row.Find("button").Has("disabled"));
        Assert.True(row.Find("article").HasClass("is-unavailable"));
    }

    [Fact]
    public async Task Last_portions_are_highlighted()
    {
        var row = await RenderRow(Dish(remaining: 2));

        Assert.Contains("2 left", row.Text);
    }

    [Fact]
    public async Task Dish_already_in_the_cart_shows_a_quantity_stepper()
    {
        var row = await RenderRow(Dish(remaining: 30), quantity: 2);

        Assert.Equal("2", row.Find("output").Text);
        Assert.Empty(row.FindAll("button", b => b["aria-label"] == "Add Lagman"));
    }

    private Task<Rendered<MenuItemRow>> RenderRow(MenuItemDto item, int quantity = 0, Action<MenuItemDto>? onAdd = null) =>
        _ctx.RenderAsync<MenuItemRow>(new Dictionary<string, object?>
        {
            [nameof(MenuItemRow.Item)] = item,
            [nameof(MenuItemRow.Quantity)] = quantity,
            [nameof(MenuItemRow.OnAdd)] = Fragments.Callback(onAdd ?? (_ => { })),
        });

    private static MenuItemDto Dish(int remaining, MenuTags tags = MenuTags.None) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Lagman", "Hand-pulled noodles", 1700m, "🍜", null, 650, tags, 12, 45, remaining, true, false, 0);
}

public sealed class KitchenDocketTests : IAsyncDisposable
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(5);
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Theory]
    [InlineData(OrderStatus.Placed, "Start cooking")]
    [InlineData(OrderStatus.Cooking, "Mark ready")]
    [InlineData(OrderStatus.Ready, "Hand over")]
    public async Task The_big_button_is_always_the_next_step(OrderStatus status, string expected)
    {
        var docket = await RenderDocket(Order(status), now: At(10, 0));

        Assert.Equal(expected, docket.FindAll("button")[0].Text);
    }

    [Fact]
    public async Task Pressing_the_button_hands_the_order_to_the_page()
    {
        OrderDto? advanced = null;
        var order = Order(OrderStatus.Placed);
        var docket = await RenderDocket(order, now: At(10, 0), onAdvance: o => advanced = o);

        await docket.ClickAsync(docket.Find("button", b => b.Text == "Start cooking"));

        Assert.Equal(order, advanced);
    }

    [Fact]
    public async Task Late_orders_stand_out()
    {
        var docket = await RenderDocket(Order(OrderStatus.Cooking), now: At(10, 20)); // slot started at 10:15

        Assert.True(docket.Find("article").HasClass("is-late"));
        Assert.Contains("5 min late", docket.Text);
    }

    [Fact]
    public async Task Ready_orders_cannot_be_cancelled_from_the_docket()
    {
        var docket = await RenderDocket(Order(OrderStatus.Ready), now: At(10, 0));

        Assert.Single(docket.FindAll("button"));
        Assert.Equal("117", docket.Find("article")["data-order"]);
    }

    [Fact]
    public async Task Student_comment_is_shown_to_the_cook()
    {
        var docket = await RenderDocket(Order(OrderStatus.Placed) with { Comment = "No onion, please" }, now: At(10, 0));

        Assert.Contains("No onion, please", docket.Text);
        Assert.Contains("2× Plov", docket.Text);
    }

    private Task<Rendered<KitchenDocket>> RenderDocket(OrderDto order, DateTimeOffset now, Action<OrderDto>? onAdvance = null) =>
        _ctx.RenderAsync<KitchenDocket>(new Dictionary<string, object?>
        {
            [nameof(KitchenDocket.Order)] = order,
            [nameof(KitchenDocket.Now)] = now,
            [nameof(KitchenDocket.OnAdvance)] = Fragments.Callback(onAdvance ?? (_ => { })),
        });

    private static DateTimeOffset At(int hour, int minute) => new(2026, 10, 30, hour, minute, 0, Offset);

    private static OrderDto Order(OrderStatus status) => new(
        Guid.NewGuid(), 117, new DateOnly(2026, 10, 30), Guid.NewGuid(), "Aruzhan Bekova", status, PaymentMethod.Card, null, 3600m,
        At(10, 15), At(10, 30), At(9, 50), null, null, null, null, null, null,
        [new OrderLineDto(Guid.NewGuid(), "Plov", "🍛", 1800m, 2)], 0);
}

public sealed class OrderStatusTextTests : IAsyncDisposable
{
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Theory]
    [InlineData(OrderStatus.Placed, "badge-info", true)]
    [InlineData(OrderStatus.Cooking, "badge-warning", true)]
    [InlineData(OrderStatus.Ready, "badge-success", true)]
    [InlineData(OrderStatus.PickedUp, "badge-neutral", false)]
    [InlineData(OrderStatus.NoShow, "badge-danger", false)]
    public async Task Status_badge_uses_words_color_and_a_live_dot(OrderStatus status, string toneClass, bool live)
    {
        var html = await _ctx.HtmlAsync<OrderStatusBadge>(new Dictionary<string, object?> { [nameof(OrderStatusBadge.Status)] = status });

        Assert.Contains(toneClass, html);
        Assert.Contains(status.Label(), html);
        Assert.Equal(live, html.Contains("badge-dot", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(15, "in 15 min")]
    [InlineData(0, "now")]
    [InlineData(-5, "5 min late")]
    [InlineData(120, "at 12:00")]
    public void Times_are_relative_to_now(int minutesFromNow, string expected)
    {
        var now = new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.FromHours(5));

        Assert.Equal(expected, UiText.Relative(now.AddMinutes(minutesFromNow), now));
    }

    [Fact]
    public void Order_summary_lists_two_dishes_and_counts_the_rest()
    {
        var lines = new List<OrderLineDto>
        {
            new(Guid.NewGuid(), "Lagman", "🍜", 1700m, 2),
            new(Guid.NewGuid(), "Sorpa", "🍲", 900m, 1),
            new(Guid.NewGuid(), "Tea", "🍵", 300m, 1),
        };

        Assert.Equal("Lagman ×2, Sorpa and 1 more", UiText.Summary(lines));
    }

    [Fact]
    public void Kitchen_cancellation_tells_the_student_why()
    {
        var order = new OrderDto(
            Guid.NewGuid(), 120, new DateOnly(2026, 10, 30), Guid.NewGuid(), "Timur", OrderStatus.Cancelled, PaymentMethod.Cash, null, 900m,
            DateTimeOffset.Now, DateTimeOffset.Now, DateTimeOffset.Now, null, null, null, DateTimeOffset.Now, CancelSource.Kitchen, "Out of rice", [], 1);

        Assert.Equal("The kitchen cancelled this order: Out of rice.", UiText.StudentMessage(order));
        Assert.Equal(StepState.Failed, UiText.Steps(order)[^1].State);
    }
}

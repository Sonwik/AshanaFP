using Ashana.Domain.Canteen;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;

namespace Ashana.UnitTests.Support;

/// <summary>Small builders for domain objects. Every test runs on Friday 30 October 2026 in a UTC+5 canteen.</summary>
public static class TestData
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(5);

    /// <summary>A fixed-offset zone, so results never depend on the time zone database of the machine.</summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("Ashana/Test", Offset, "Canteen time", "Canteen time");

    public static readonly DateOnly Today = new(2026, 10, 30);

    public static readonly Guid MainsId = new("00000000-0000-0000-0000-00000000c001");

    /// <summary>10:00 in the canteen.</summary>
    public static DateTimeOffset Now => At(10, 0);

    /// <summary>Default rules: 08:00–20:00, 15-minute slots, 20 orders per slot, order 10 minutes ahead.</summary>
    public static CanteenRules Rules => CanteenSettings.CreateDefault().Rules;

    public static DateTimeOffset At(int hour, int minute, int dayOffset = 0) =>
        new(Today.AddDays(dayOffset).ToDateTime(new TimeOnly(hour, minute)), Offset);

    public static PickupSlot Slot(int hour, int minute, int lengthMinutes = 15) =>
        new(At(hour, minute), At(hour, minute).AddMinutes(lengthMinutes));

    public static MenuItemDetails Details(
        string name = "Plov",
        decimal price = 1800m,
        int stock = 10,
        MenuTags tags = MenuTags.None,
        string description = "Rice slow-cooked with beef") =>
        new(MainsId, name, description, price, "🍛", null, 650, tags, 10, stock);

    public static MenuItem Dish(string name = "Plov", decimal price = 1800m, int stock = 10) =>
        MenuItem.Create(Details(name, price, stock), Today).Value;

    public static Order NewOrder(params (MenuItem Item, int Quantity)[] lines) =>
        NewOrder(Guid.NewGuid(), Slot(10, 15), lines);

    public static Order NewOrder(Guid customerId, PickupSlot slot, params (MenuItem Item, int Quantity)[] lines) =>
        Order.Create(Draft(customerId, slot, lines)).Value;

    public static OrderDraft Draft(Guid customerId, PickupSlot slot, params (MenuItem Item, int Quantity)[] lines) =>
        new(
            101,
            slot.Day,
            customerId,
            "Aruzhan Bekova",
            slot,
            PaymentMethod.Card,
            null,
            lines.Select(l => new OrderLineDraft(l.Item, l.Quantity)).ToList(),
            Now);
}

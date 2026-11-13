using System.Globalization;
using Ashana.Application.Orders;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.UI;

namespace Ashana.Web.Components.Shared;

/// <summary>Wording and colors shared by every screen, so an order status reads the same everywhere.</summary>
public static class UiText
{
    public static string Title(string page) => $"{page} | Ashana";

    public static Tone ToneOf(OrderStatus status) => status switch
    {
        OrderStatus.Placed => Tone.Info,
        OrderStatus.Cooking => Tone.Warning,
        OrderStatus.Ready => Tone.Success,
        OrderStatus.Cancelled => Tone.Danger,
        OrderStatus.NoShow => Tone.Danger,
        _ => Tone.Neutral,
    };

    /// <summary>One sentence telling the student what is happening and what to do.</summary>
    public static string StudentMessage(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return order.Status switch
        {
            OrderStatus.Placed => "The kitchen has your order. You can still cancel it until cooking starts.",
            OrderStatus.Cooking => "Your food is being cooked now.",
            OrderStatus.Ready => $"Ready! Come to the counter and say number {order.Number}.",
            OrderStatus.PickedUp => "Picked up. Enjoy your meal!",
            OrderStatus.Cancelled => order.CancelledBy == CancelSource.Kitchen
                ? $"The kitchen cancelled this order: {order.CancelReason}."
                : "You cancelled this order.",
            OrderStatus.NoShow => "This order was ready but nobody picked it up.",
            _ => order.Status.Label(),
        };
    }

    public static string Payment(PaymentMethod method) => method switch
    {
        PaymentMethod.Card => "Card",
        PaymentMethod.Cash => "Cash",
        PaymentMethod.Qr => "QR code",
        _ => method.ToString(),
    };

    public static string PaymentIcon(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "banknote",
        PaymentMethod.Qr => "qr-code",
        _ => "credit-card",
    };

    /// <summary>"in 12 min", "now", "8 min late".</summary>
    public static string Relative(DateTimeOffset target, DateTimeOffset now)
    {
        var minutes = (int)Math.Round((target - now).TotalMinutes);
        return minutes switch
        {
            > 90 => $"at {target.ToString("HH:mm", CultureInfo.InvariantCulture)}",
            > 0 => $"in {minutes} min",
            0 => "now",
            _ => $"{-minutes} min late",
        };
    }

    public static string Portions(int count) => count == 1 ? "1 dish" : $"{count} dishes";

    /// <summary>"Plov, Black tea and 2 more".</summary>
    public static string Summary(IReadOnlyList<OrderLineDto> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var names = lines.Take(2).Select(l => l.Quantity > 1 ? $"{l.Name} ×{l.Quantity}" : l.Name);
        var rest = lines.Count - 2;
        return string.Join(", ", names) + (rest > 0 ? $" and {rest} more" : string.Empty);
    }

    public static IEnumerable<(MenuTags Tag, string Text)> TagLabels(MenuTags tags)
    {
        if (tags.HasFlag(MenuTags.New))
        {
            yield return (MenuTags.New, "New");
        }

        if (tags.HasFlag(MenuTags.Vegetarian))
        {
            yield return (MenuTags.Vegetarian, "Vegetarian");
        }

        if (tags.HasFlag(MenuTags.Spicy))
        {
            yield return (MenuTags.Spicy, "Spicy");
        }

        if (tags.HasFlag(MenuTags.GlutenFree))
        {
            yield return (MenuTags.GlutenFree, "Gluten-free");
        }

        if (tags.HasFlag(MenuTags.Halal))
        {
            yield return (MenuTags.Halal, "Halal");
        }
    }

    public static IReadOnlyList<StepItem> Steps(OrderDto order)
    {
        ArgumentNullException.ThrowIfNull(order);
        static string? At(DateTimeOffset? time) => time?.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (order.Status is OrderStatus.Cancelled or OrderStatus.NoShow)
        {
            var reached = new List<StepItem> { new("Placed", At(order.CreatedAt), StepState.Done) };
            if (order.CookingStartedAt is not null)
            {
                reached.Add(new StepItem("Cooking", At(order.CookingStartedAt), StepState.Done));
            }

            if (order.ReadyAt is not null)
            {
                reached.Add(new StepItem("Ready", At(order.ReadyAt), StepState.Done));
            }

            reached.Add(new StepItem(order.Status.Label(), At(order.CancelledAt), StepState.Failed));
            return reached;
        }

        StepState State(OrderStatus step) =>
            order.Status > step ? StepState.Done
            : order.Status == step ? (step == OrderStatus.PickedUp ? StepState.Done : StepState.Current)
            : StepState.Upcoming;

        return
        [
            new("Placed", At(order.CreatedAt), StepState.Done),
            new("Cooking", At(order.CookingStartedAt), State(OrderStatus.Cooking)),
            new("Ready", At(order.ReadyAt), State(OrderStatus.Ready)),
            new("Picked up", At(order.PickedUpAt), State(OrderStatus.PickedUp)),
        ];
    }
}

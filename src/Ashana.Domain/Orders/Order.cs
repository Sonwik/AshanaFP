using System.Collections.Immutable;
using Ashana.Domain.Canteen;
using Ashana.Domain.Common;
using Ashana.Domain.Menu;

namespace Ashana.Domain.Orders;

/// <summary>One requested dish with its quantity, used to build an <see cref="Order"/>.</summary>
public sealed record OrderLineDraft(MenuItem Item, int Quantity);

/// <summary>Everything needed to create an order. The application layer fills it after checking slot and stock.</summary>
public sealed record OrderDraft(
    int Number,
    DateOnly ServiceDate,
    Guid CustomerId,
    string CustomerName,
    PickupSlot Slot,
    PaymentMethod Payment,
    string? Comment,
    IReadOnlyList<OrderLineDraft> Lines,
    DateTimeOffset Now,
    Guid? ClientRequestId = null);

/// <summary>One dish in an order, with the name and price copied at the moment of ordering.</summary>
public sealed record OrderLine(Guid MenuItemId, string Name, string Emoji, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// A student's pre-order: an <b>immutable</b> record. Status changes do not modify the order;
/// they return a new order (or an <see cref="Error"/> when the change is not allowed).
/// All setters are <c>private init</c>, so code outside this type can neither mutate an order
/// nor build an invalid one with a <c>with</c> expression: <see cref="Create"/> is the only way in.
/// </summary>
public sealed record Order : IEntity
{
    /// <summary>The rules a draft must satisfy, as data. Checked in this order; the first broken one is reported.</summary>
    public static readonly ImmutableArray<Rule<OrderDraft>> DraftRules =
    [
        Validation.Rule<OrderDraft>(d => d.Lines.Count > 0, OrderErrors.EmptyCart),
        Validation.Rule<OrderDraft>(d => d.Lines.Count <= OrderErrors.MaxLines, OrderErrors.TooManyLines),
        Validation.Rule<OrderDraft>(d => d.Lines.All(l => l.Quantity is >= 1 and <= OrderErrors.MaxQuantityPerLine), OrderErrors.QuantityOutOfRange),
        Validation.Rule<OrderDraft>(d => d.Lines.Select(l => l.Item.Id).Distinct().Count() == d.Lines.Count, OrderErrors.DuplicateLine),
        Validation.Rule<OrderDraft>(d => d.Comment is not { Length: > OrderErrors.CommentMaxLength }, OrderErrors.CommentTooLong),
    ];

    private Order()
    {
    }

    public Guid Id { get; private init; }

    public int Version { get; private init; }

    /// <summary>Short number the student says at the counter. Starts at 101 every service day.</summary>
    public int Number { get; private init; }

    /// <summary>Canteen-local day the order is for.</summary>
    public DateOnly ServiceDate { get; private init; }

    public Guid CustomerId { get; private init; }

    /// <summary>Name snapshot shown to the kitchen.</summary>
    public string CustomerName { get; private init; } = string.Empty;

    public ImmutableArray<OrderLine> Lines { get; private init; } = [];

    public DateTimeOffset PickupStart { get; private init; }

    public DateTimeOffset PickupEnd { get; private init; }

    public OrderStatus Status { get; private init; }

    public PaymentMethod Payment { get; private init; }

    public string? Comment { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? CookingStartedAt { get; private init; }

    public DateTimeOffset? ReadyAt { get; private init; }

    public DateTimeOffset? PickedUpAt { get; private init; }

    public DateTimeOffset? CancelledAt { get; private init; }

    public CancelSource? CancelledBy { get; private init; }

    public string? CancelReason { get; private init; }

    /// <summary>Idempotency key from the checkout page: pressing "Place order" twice creates one order.</summary>
    public Guid? ClientRequestId { get; private init; }

    // ---- Derived values: computed from the data above, never stored ---------------------------

    public decimal Total => Lines.Sum(l => l.LineTotal);

    public int PortionCount => Lines.Sum(l => l.Quantity);

    public PickupSlot Slot => new(PickupStart, PickupEnd);

    /// <summary>The student can cancel only before cooking starts.</summary>
    public bool CanCustomerCancel => Status == OrderStatus.Placed;

    /// <summary>Formatted pickup code, for example "#107".</summary>
    public string Code => $"#{Number}";

    // ---- Construction --------------------------------------------------------------------------

    /// <summary>Validates the draft against <see cref="DraftRules"/> and builds the order.</summary>
    public static Result<Order> Create(OrderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return draft.Check(DraftRules).Map(d => new Order
        {
            Id = Guid.NewGuid(),
            Number = d.Number,
            ServiceDate = d.ServiceDate,
            CustomerId = d.CustomerId,
            CustomerName = d.CustomerName,
            PickupStart = d.Slot.Start,
            PickupEnd = d.Slot.End,
            Status = OrderStatus.Placed,
            Payment = d.Payment,
            Comment = string.IsNullOrWhiteSpace(d.Comment) ? null : d.Comment.Trim(),
            CreatedAt = d.Now,
            ClientRequestId = d.ClientRequestId,
            Lines = [.. d.Lines.Select(l => new OrderLine(l.Item.Id, l.Item.Name, l.Item.Emoji, l.Item.Price, l.Quantity))],
        });
    }

    // ---- Transitions: each returns a NEW order or an error -------------------------------------

    public Result<Order> StartCooking(DateTimeOffset now) => MoveTo(OrderStatus.Cooking, now);

    public Result<Order> MarkReady(DateTimeOffset now) => MoveTo(OrderStatus.Ready, now);

    public Result<Order> MarkPickedUp(DateTimeOffset now) => MoveTo(OrderStatus.PickedUp, now);

    /// <summary>Background job: the order was ready but nobody came.</summary>
    public Result<Order> MarkNoShow(DateTimeOffset now) => MoveTo(OrderStatus.NoShow, now);

    /// <summary>Moves to the next "happy path" status (what the big kitchen button does).</summary>
    public Result<Order> Advance(DateTimeOffset now) =>
        OrderStateMachine.NextKitchenStep(Status)
            .ToResult(OrderErrors.InvalidTransition(Status, Status))
            .Bind(next => MoveTo(next, now));

    /// <summary>Railway style: each check either passes the order on or switches to the error track.</summary>
    public Result<Order> CancelByCustomer(Guid customerId, DateTimeOffset now) =>
        Result.Success(this)
            .Ensure(order => order.CustomerId == customerId, OrderErrors.NotYourOrder)
            .Ensure(order => order.CanCustomerCancel, OrderErrors.CannotCancelNow)
            .Bind(order => order.MoveTo(OrderStatus.Cancelled, now))
            .Map(order => order with { CancelledBy = CancelSource.Customer, CancelReason = "Cancelled by student" });

    public Result<Order> CancelByKitchen(string reason, DateTimeOffset now) =>
        Result.Success(reason)
            .Ensure(r => !string.IsNullOrWhiteSpace(r), OrderErrors.ReasonRequired)
            .Bind(r => MoveTo(OrderStatus.Cancelled, now).Map(order => order with { CancelledBy = CancelSource.Kitchen, CancelReason = r.Trim() }));

    // ---- Questions (pure predicates) -----------------------------------------------------------

    /// <summary>The slot has started but the kitchen has not finished: highlight it on the board.</summary>
    public bool IsLate(DateTimeOffset now) =>
        Status is OrderStatus.Placed or OrderStatus.Cooking && now >= PickupStart;

    /// <summary>Ready orders become "No-show" this long after the slot ends.</summary>
    public bool IsNoShowDue(DateTimeOffset now, int noShowAfterMinutes) =>
        Status == OrderStatus.Ready && now >= PickupEnd.AddMinutes(noShowAfterMinutes);

    private Result<Order> MoveTo(OrderStatus target, DateTimeOffset now) =>
        OrderStateMachine.CanMove(Status, target)
            ? Stamp(target, now) with { Status = target, Version = Version + 1 }
            : OrderErrors.InvalidTransition(Status, target);

    /// <summary>Pattern matching decides which timestamp a status change records.</summary>
    private Order Stamp(OrderStatus target, DateTimeOffset now) => target switch
    {
        OrderStatus.Cooking => this with { CookingStartedAt = now },
        OrderStatus.Ready => this with { ReadyAt = now },
        OrderStatus.PickedUp => this with { PickedUpAt = now },
        OrderStatus.Cancelled or OrderStatus.NoShow => this with { CancelledAt = now },
        _ => this,
    };
}

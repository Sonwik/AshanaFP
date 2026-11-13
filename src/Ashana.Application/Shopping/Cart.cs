using System.Collections.Immutable;
using Ashana.Application.Common;
using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;

namespace Ashana.Application.Shopping;

/// <summary>One line in the cart. A plain record, so it serializes to browser storage as JSON.</summary>
public sealed record CartLine(Guid MenuItemId, string Name, string Emoji, decimal UnitPrice, int Quantity)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>What changed when the cart was compared with the latest menu.</summary>
public sealed record CartAdjustment(string DishName, string Message);

/// <summary>A reconciled cart together with the explanations for the user.</summary>
public sealed record Reconciled(Cart Cart, ImmutableList<CartAdjustment> Changes);

/// <summary>
/// Shopping cart as an <b>immutable value</b>. "Adding" a dish returns a new cart (or an error),
/// so the old cart is never half-changed, and undo is as simple as keeping the previous value.
/// The Blazor <c>CartState</c> holds the current cart, adds change notifications and saves it in the browser.
/// </summary>
public sealed record Cart
{
    public static readonly Cart Empty = new(ImmutableList<CartLine>.Empty);

    private Cart(ImmutableList<CartLine> lines) => Lines = lines;

    public ImmutableList<CartLine> Lines { get; }

    public bool IsEmpty => Lines.IsEmpty;

    /// <summary>Total number of portions (badge on the cart icon).</summary>
    public int Count => Lines.Sum(l => l.Quantity);

    public decimal Total => Lines.Sum(l => l.LineTotal);

    /// <summary>Restores a saved cart, dropping anything that breaks the limits (data from the browser is untrusted).</summary>
    public static Cart From(IEnumerable<CartLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return new Cart(lines
            .Where(l => l.Quantity > 0)
            .DistinctBy(l => l.MenuItemId)
            .Take(OrderErrors.MaxLines)
            .Select(l => l with { Quantity = Math.Min(l.Quantity, OrderErrors.MaxQuantityPerLine) })
            .ToImmutableList());
    }

    public int QuantityOf(Guid menuItemId) => Find(menuItemId).Map(l => l.Quantity).GetOrElse(0);

    public Option<CartLine> Find(Guid menuItemId) => Lines.FirstOrNone(l => l.MenuItemId == menuItemId);

    /// <summary>Adds portions of a dish, respecting stock and per-order limits. Returns the new cart.</summary>
    public Result<Cart> Add(MenuItemDto item, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(item);
        var wanted = QuantityOf(item.Id) + quantity;

        return Result.Success(item)
            .Ensure(_ => quantity > 0, OrderErrors.QuantityOutOfRange)
            .Ensure(i => i.IsOrderable, i => i.IsAvailable && !i.IsArchived && i.RemainingStock == 0 ? MenuErrors.SoldOut(i.Name) : MenuErrors.Unavailable(i.Name))
            .Ensure(_ => wanted <= OrderErrors.MaxQuantityPerLine, OrderErrors.QuantityOutOfRange)
            .Ensure(i => wanted <= i.RemainingStock, i => MenuErrors.NotEnoughStock(i.Name, i.RemainingStock))
            .Bind(i => Find(i.Id).Match(
                line => Result.Success(Replace(line, line with { Quantity = wanted })),
                () => Lines.Count < OrderErrors.MaxLines
                    ? Result.Success(new Cart(Lines.Add(new CartLine(i.Id, i.Name, i.Emoji, i.Price, quantity))))
                    : Result.Failure<Cart>(OrderErrors.TooManyLines)));
    }

    /// <summary>Sets an exact quantity; 0 removes the line. Returns the new cart.</summary>
    public Result<Cart> SetQuantity(Guid menuItemId, int quantity, int? remainingStock = null) =>
        Find(menuItemId).Match(
            line => quantity <= 0 ? Result.Success(Remove(menuItemId))
                : quantity > OrderErrors.MaxQuantityPerLine ? Result.Failure<Cart>(OrderErrors.QuantityOutOfRange)
                : remainingStock is { } left && quantity > left ? Result.Failure<Cart>(MenuErrors.NotEnoughStock(line.Name, left))
                : Result.Success(Replace(line, line with { Quantity = quantity })),
            () => Result.Success(this));

    public Cart Remove(Guid menuItemId) => new(Lines.RemoveAll(l => l.MenuItemId == menuItemId));

    public Cart Clear() => Empty;

    /// <summary>
    /// Brings the cart up to date with the latest menu: new prices and names, sold-out dishes removed,
    /// quantities capped by remaining stock. Each line is checked by a pure function that returns the updated
    /// line (or none) plus its explanations; the results are then folded into a new cart.
    /// </summary>
    public Reconciled Reconcile(MenuDto menu)
    {
        ArgumentNullException.ThrowIfNull(menu);
        var checkedLines = Lines.Select(line => ReconcileLine(line, menu.FindItem(line.MenuItemId))).ToList();
        return new Reconciled(
            new Cart(checkedLines.Choose(r => r.Line).ToImmutableList()),
            checkedLines.SelectMany(r => r.Changes).ToImmutableList());
    }

    /// <summary>Converts the cart into the request sent when placing an order.</summary>
    public IReadOnlyList<CartItemRequest> ToRequests() =>
        [.. Lines.Select(l => new CartItemRequest(l.MenuItemId, l.Quantity, l.UnitPrice))];

    private static (Option<CartLine> Line, IEnumerable<CartAdjustment> Changes) ReconcileLine(CartLine line, Option<MenuItemDto> found) =>
        found.Where(item => item.IsOrderable).Match(
            item =>
            {
                var changes = new List<CartAdjustment>();
                if (item.Price != line.UnitPrice)
                {
                    changes.Add(new CartAdjustment(item.Name, $"Price of {item.Name} changed from {Money.Tenge(line.UnitPrice)} to {Money.Tenge(item.Price)}."));
                }

                if (line.Quantity > item.RemainingStock)
                {
                    changes.Add(new CartAdjustment(item.Name, $"Only {item.RemainingStock} portion(s) of {item.Name} left, quantity reduced."));
                }

                var updated = line with
                {
                    UnitPrice = item.Price,
                    Quantity = Math.Min(line.Quantity, item.RemainingStock),
                    Name = item.Name,
                    Emoji = item.Emoji,
                };
                return (Option.Some(updated), (IEnumerable<CartAdjustment>)changes);
            },
            () => (Option<CartLine>.None, [new CartAdjustment(line.Name, $"{line.Name} is no longer available and was removed.")]));

    private Cart Replace(CartLine old, CartLine updated) => new(Lines.Replace(old, updated));
}

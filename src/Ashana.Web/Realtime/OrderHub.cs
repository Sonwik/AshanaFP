using Ashana.Application.Orders;
using Ashana.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Ashana.Web.Realtime;

/// <summary>Messages the server sends to SignalR clients (strongly typed: a typo is a compile error, not a silent bug).</summary>
public interface IOrderHubClient
{
    /// <summary>New numbers for the TV board at the pickup counter.</summary>
    Task BoardUpdated(BoardSnapshot snapshot);

    /// <summary>An order changed (sent to the kitchen group and to the order's owner).</summary>
    Task OrderUpdated(OrderDto order);
}

/// <summary>
/// Public real-time endpoint at <c>/hubs/orders</c>.
/// Blazor pages already run over their own SignalR circuit and get updates through the in-process event bus;
/// this hub is for clients outside Blazor: the TV board (plain JavaScript) and future mobile apps.
/// </summary>
public sealed class OrderHub : Hub<IOrderHubClient>
{
    public const string Path = "/hubs/orders";
    public const string BoardGroup = "board";
    public const string KitchenGroup = "kitchen";

    private readonly OrderService _orders;

    public OrderHub(OrderService orders) => _orders = orders;

    public static string UserGroup(Guid userId) => $"user:{userId}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user?.GetUserId() is { } id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(id));
        }

        if (user?.IsStaff() == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, KitchenGroup);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Called by the TV board. Joins the board group and returns the current numbers.</summary>
    [AllowAnonymous]
    public async Task<BoardSnapshot> JoinBoard()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, BoardGroup);
        return await _orders.GetBoardAsync(Context.ConnectionAborted);
    }
}

using System.Text.Json;
using Ashana.Application.Kitchen;
using Ashana.Domain.Orders;
using Ashana.IntegrationTests.Support;

namespace Ashana.IntegrationTests;

/// <summary>The public SignalR hub that keeps the TV at the pickup counter up to date.</summary>
public sealed class RealtimeTests(AshanaServer server) : IClassFixture<AshanaServer>
{
    [Fact]
    public async Task Hub_offers_websockets_with_fallbacks()
    {
        using var browser = server.CreateBrowser();

        using var response = await browser.Http.PostAsync(new Uri("/hubs/orders/negotiate?negotiateVersion=1", UriKind.Relative), null);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var transports = json.RootElement.GetProperty("availableTransports").EnumerateArray().Select(t => t.GetProperty("transport").GetString()).ToList();
        Assert.Equal(["WebSockets", "ServerSentEvents", "LongPolling"], transports);
    }

    [Fact]
    public async Task Tv_board_gets_the_current_numbers_and_then_live_updates()
    {
        await using var tv = await HubClient.ConnectAsync(server.BaseAddress);
        using var snapshot = await tv.InvokeAsync("JoinBoard");
        var readyBefore = snapshot.RootElement.GetProperty("ready").GetArrayLength();

        // A cook marks an order as ready...
        var kitchen = server.Get<KitchenService>();
        var cooking = (await kitchen.GetBoardAsync()).Cooking[0];
        Assert.True((await kitchen.AdvanceAsync(cooking.Id, OrderStatus.Cooking)).IsSuccess);

        // ...and the TV hears about it without asking.
        using var update = await tv.WaitForCallAsync("BoardUpdated");
        var ready = update.RootElement.GetProperty("arguments")[0].GetProperty("ready").EnumerateArray()
            .Select(entry => entry.GetProperty("number").GetInt32())
            .ToList();
        Assert.Contains(cooking.Number, ready);
        Assert.Equal(readyBefore + 1, ready.Count);
    }

    [Fact]
    public async Task Board_snapshot_has_numbers_and_slots_but_no_personal_data()
    {
        await using var tv = await HubClient.ConnectAsync(server.BaseAddress);

        using var snapshot = await tv.InvokeAsync("JoinBoard");

        var entry = snapshot.RootElement.GetProperty("preparing")[0];
        Assert.Equal(["number", "status", "slotLabel"], entry.EnumerateObject().Select(p => p.Name));
    }
}

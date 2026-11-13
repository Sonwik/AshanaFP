using System.Diagnostics;
using Ashana.Application.Abstractions;
using Ashana.Domain.Orders;
using Ashana.Domain.Users;
using Ashana.Infrastructure.Background;
using Ashana.Infrastructure.Messaging;
using Ashana.Infrastructure.Persistence;
using Ashana.Infrastructure.Seeding;
using Ashana.UnitTests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ashana.UnitTests.Infrastructure;

public sealed class InProcessEventBusTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private readonly InProcessEventBus _bus = new(NullLogger<InProcessEventBus>.Instance);

    private sealed record Ping(int Number);

    private sealed record Pong(int Number);

    public void Dispose() => _bus.Dispose();

    [Fact]
    public async Task Subscribers_receive_events_of_their_type_in_order()
    {
        var received = new List<int>();
        var done = new TaskCompletionSource();
        using var subscription = _bus.Subscribe<Ping>(ping =>
        {
            received.Add(ping.Number);
            if (received.Count == 3)
            {
                done.SetResult();
            }

            return Task.CompletedTask;
        });

        _bus.Publish(new Pong(0));
        _bus.Publish(new Ping(1));
        _bus.Publish(new Ping(2));
        _bus.Publish(new Ping(3));

        await done.Task.WaitAsync(Wait);
        Assert.Equal([1, 2, 3], received);
    }

    [Fact]
    public void Disposing_a_subscription_unsubscribes()
    {
        var subscription = _bus.Subscribe<Ping>(_ => Task.CompletedTask);
        Assert.Equal(1, _bus.SubscriberCount);

        subscription.Dispose();
        subscription.Dispose(); // twice is harmless

        Assert.Equal(0, _bus.SubscriberCount);
    }

    [Fact]
    public async Task A_failing_subscriber_does_not_stop_the_others()
    {
        var healthy = new TaskCompletionSource<int>();
        using var broken = _bus.Subscribe<Ping>(_ => throw new InvalidOperationException("boom"));
        using var good = _bus.Subscribe<Ping>(ping =>
        {
            healthy.TrySetResult(ping.Number);
            return Task.CompletedTask;
        });

        _bus.Publish(new Ping(7));

        Assert.Equal(7, await healthy.Task.WaitAsync(Wait));
    }

    [Fact]
    public void A_stuck_subscriber_never_blocks_the_publisher()
    {
        var never = new TaskCompletionSource();
        using var stuck = _bus.Subscribe<Ping>(_ => never.Task); // for example a browser tab that stopped responding

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 10_000; i++)
        {
            _bus.Publish(new Ping(i));
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"Publishing took {watch.Elapsed}");
        never.SetResult();
    }
}

public sealed class OrderLifecycleWorkerTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();
    private Seeded _data = null!;

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task One_run_closes_expired_orders_and_refills_stock_for_a_new_day()
    {
        var ready = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.LagmanId, 3, 1700m));
        await _app.Kitchen.AdvanceAsync(ready.Id, OrderStatus.Placed);
        await _app.Kitchen.AdvanceAsync(ready.Id, OrderStatus.Cooking);
        var worker = _app.Get<OrderLifecycleWorker>();

        _app.Time.Set(TestApp.At(8, 0, dayOffset: 1));
        await worker.RunOnceAsync(CancellationToken.None);

        Assert.Equal(OrderStatus.NoShow, (await _app.OrderAsync(ready.Id)).Status);
        Assert.Equal(3, (await _app.DishAsync(_data.LagmanId)).RemainingStock);
    }
}

public sealed class DemoDataSeederTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create(config: new Dictionary<string, string?> { ["Seed:DemoData"] = "true" });

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Demo_data_is_created_once_and_is_consistent()
    {
        var seeder = _app.Get<DemoDataSeeder>();

        Assert.True(await seeder.SeedAsync());
        Assert.False(await seeder.SeedAsync()); // never touches a store that already has data

        var state = _app.State;
        var users = state.Users.Values.ToList();
        var dishes = state.MenuItems.Values.ToList();
        var active = state.ActiveOrders.ToList();

        Assert.Equal(12, users.Count);
        Assert.Equal(UserRole.Admin, users.Single(u => u.Email == DemoAccounts.AdminEmail).Role);
        Assert.Equal(UserRole.Cook, users.Single(u => u.Email == DemoAccounts.CookEmail).Role);
        Assert.Equal(6, state.Categories.Count);
        Assert.Equal(29, dishes.Count);
        Assert.NotEmpty(active);
        Assert.All(active, order => Assert.Equal(TestData.Today, order.ServiceDate));
        Assert.All(dishes, dish => Assert.InRange(dish.RemainingStock, 0, dish.DailyStock));
    }

    [Fact]
    public void Building_the_demo_state_is_deterministic_for_a_seed()
    {
        var zone = _app.Clock.TimeZone;
        var first = DemoDataSeeder.Build(_app.State, "hash", TestData.Now, zone, 3, new Random(1));
        var second = DemoDataSeeder.Build(_app.State, "hash", TestData.Now, zone, 3, new Random(1));

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(
            first.State.Orders.Values.Select(o => (o.ServiceDate, o.Number, o.Status, o.Total)).Order(),
            second.State.Orders.Values.Select(o => (o.ServiceDate, o.Number, o.Status, o.Total)).Order());
    }

    [Fact]
    public async Task Every_demo_account_signs_in_with_the_shared_password()
    {
        await _app.Get<DemoDataSeeder>().SeedAsync();

        foreach (var email in new[] { DemoAccounts.AdminEmail, DemoAccounts.CookEmail, DemoAccounts.StudentEmail })
        {
            var result = await _app.Accounts.AuthenticateAsync(email, DemoAccounts.Password);
            Assert.True(result.IsSuccess, $"{email}: {(result.IsFailure ? result.Error.Message : string.Empty)}");
        }
    }

    [Fact]
    public async Task Seeding_can_be_switched_off()
    {
        await using var app = TestApp.Create(); // Seed:DemoData = false

        Assert.False(await app.Get<DemoDataSeeder>().SeedAsync());
        Assert.Empty(app.State.Orders);
    }
}

public sealed class FileSnapshotStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ashana-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Saved_data_is_loaded_back_after_a_restart()
    {
        var path = Path.Combine(_folder, "data.json");
        Guid orderId;
        await using (var before = TestApp.Create())
        {
            var data = await before.SeedAsync();
            orderId = (await before.PlaceAsync(data.StudentId, TestApp.At(10, 15), (data.PlovId, 2, 1800m))).Id;
            await new FileSnapshotStore(before.Get<InMemoryStateStore>(), path, NullLogger<FileSnapshotStore>.Instance).SaveAsync();
        }

        await using var after = TestApp.Create();
        await new FileSnapshotStore(after.Get<InMemoryStateStore>(), path, NullLogger<FileSnapshotStore>.Instance).LoadAsync();

        var order = await after.OrderAsync(orderId);
        Assert.Equal((OrderStatus.Placed, 3600m), (order.Status, order.Total));
        Assert.Equal(4, (await after.UserAdmin.ListAsync()).Count);
        Assert.False(File.Exists(path + ".tmp")); // written to a temporary file first, then renamed
    }

    [Fact]
    public async Task A_missing_file_means_an_empty_store()
    {
        using var store = new InMemoryStateStore(Ashana.Application.State.CanteenState.Empty());

        await new FileSnapshotStore(store, Path.Combine(_folder, "missing.json"), NullLogger<FileSnapshotStore>.Instance).LoadAsync();

        Assert.Empty(store.Current.Orders);
    }
}

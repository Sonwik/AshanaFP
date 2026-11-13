using Ashana.Application.State;
using Ashana.Domain.Menu;
using Ashana.Domain.Orders;
using Ashana.Infrastructure.Persistence;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Infrastructure;

public sealed class InMemoryStateStoreTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();
    private Seeded _data = null!;

    private InMemoryStateStore Store => _app.Get<InMemoryStateStore>();

    public async Task InitializeAsync() => _data = await _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private static Func<CanteenState, Result<Change<MenuItem>>> Reserve(Guid id, int quantity) =>
        state => state.FindItem(id).ToResult(MenuErrors.ItemNotFound)
            .Bind(item => item.Reserve(quantity))
            .Map(item => Change.Of(state.With(item), item));

    [Fact]
    public async Task A_snapshot_never_changes_after_it_was_read()
    {
        var before = Store.Current;

        await Store.UpdateAsync(Reserve(_data.PlovId, 4));

        Assert.Equal(10, before.FindItem(_data.PlovId).Map(i => i.RemainingStock).GetOrElse(-1)); // old snapshot is untouched
        Assert.Equal(6, Store.Current.FindItem(_data.PlovId).Map(i => i.RemainingStock).GetOrElse(-1));
        Assert.NotSame(before, Store.Current);
    }

    [Fact]
    public async Task A_failed_transition_keeps_the_old_state_exactly()
    {
        var before = Store.Current;

        var result = await Store.UpdateAsync(Reserve(_data.LagmanId, 4)); // only 3 left

        Assert.Equal("menu.not_enough_stock", result.Error.Code);
        Assert.Same(before, Store.Current);
    }

    [Fact]
    public async Task An_exception_inside_a_transition_also_keeps_the_old_state()
    {
        var before = Store.Current;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Store.UpdateAsync<Unit>(_ => throw new InvalidOperationException("bug")));

        Assert.Same(before, Store.Current);
        Assert.True((await Store.UpdateAsync(Reserve(_data.TeaId, 1))).IsSuccess); // and the store still works
    }

    [Fact]
    public async Task Transitions_run_one_at_a_time_so_no_update_is_lost()
    {
        var updates = Enumerable.Range(0, 40).Select(_ => Task.Run(() => Store.UpdateAsync(Reserve(_data.TeaId, 1))));

        await Task.WhenAll(updates);

        Assert.Equal(10, Store.Current.FindItem(_data.TeaId).Map(i => i.RemainingStock).GetOrElse(-1));
    }

    [Fact]
    public async Task Commit_is_announced_only_when_the_state_changed()
    {
        var commits = 0;
        Store.Committed += () => commits++;
        var revision = Store.Revision;

        await Store.UpdateAsync(state => Change.ReadOnly(state, Result.Success(state.MenuItems.Count)));
        await Store.UpdateAsync(Reserve(_data.LagmanId, 99));
        await Store.UpdateAsync(Reserve(_data.TeaId, 1));

        Assert.Equal(1, commits);
        Assert.Equal(revision + 1, Store.Revision);
    }

    [Fact]
    public async Task State_queries_count_only_matching_orders()
    {
        var mine = await _app.PlaceAsync(_data.StudentId, TestApp.At(10, 15), (_data.TeaId, 1, 300m));
        await _app.PlaceAsync(_data.Student2Id, TestApp.At(10, 15), (_data.TeaId, 1, 300m));
        await _app.Orders.CancelAsync(mine.Id, _data.StudentId);
        var state = Store.Current;

        Assert.Equal(1, state.CountBookedInSlot(TestApp.At(10, 15))); // cancelled orders free the slot
        Assert.Equal(0, state.CountActiveFor(_data.StudentId));
        Assert.Single(state.ActiveOrders);
        Assert.Equal(102, state.MaxNumber(TestData.Today));
        Assert.Equal(mine.Id, state.OrdersOf(_data.StudentId).Single().Id);
        Assert.True(state.FindOrder(Guid.NewGuid()).IsNone);
    }
}

public class StateSerializerTests
{
    private static CanteenState RoundTrip(CanteenState state) => StateSerializer.FromJson(StateSerializer.ToJson(state));

    [Fact]
    public void Orders_survive_a_round_trip_with_private_state_and_lines()
    {
        var order = TestData.NewOrder((TestData.Dish("Plov", 1800m), 2), (TestData.Dish("Black tea", 300m), 1))
            .StartCooking(TestData.At(10, 5)).Value;

        var copy = RoundTrip(CanteenState.Empty().With(order)).FindOrder(order.Id).GetOrElse(() => throw new InvalidOperationException());

        Assert.Equal((order.Id, order.Number, order.Status, order.Total, order.Version), (copy.Id, copy.Number, copy.Status, copy.Total, copy.Version));
        Assert.Equal(order.CookingStartedAt, copy.CookingStartedAt);
        Assert.Equal(order.Lines.ToArray(), copy.Lines.ToArray()); // ImmutableArray.Equals compares by reference, so compare the elements
    }

    [Fact]
    public void Dishes_keep_their_stock_and_flags()
    {
        var dish = TestData.Dish(stock: 10).Reserve(3).Value.SetAvailability(false);

        var copy = RoundTrip(CanteenState.Empty().With(dish)).FindItem(dish.Id).GetOrElse(() => throw new InvalidOperationException());

        Assert.Equal(dish, copy); // records compare by value: every field survived
    }

    [Fact]
    public void Calculated_values_are_not_stored()
    {
        var json = StateSerializer.ToJson(CanteenState.Empty().With(TestData.NewOrder((TestData.Dish(), 1))));

        Assert.DoesNotContain("\"code\"", json);
        Assert.DoesNotContain("\"portionCount\"", json);
        Assert.Contains("\"lines\"", json);
        Assert.Contains($"\"formatVersion\":{StateSerializer.FormatVersion}", json);
    }

    [Fact]
    public void Settings_keep_their_rules_and_version()
    {
        var settings = CanteenState.Empty().Settings.Update(CanteenState.Empty().Rules with { MaxOrdersPerSlot = 7 }).Value;

        var copy = RoundTrip(CanteenState.Empty().With(settings)).Settings;

        Assert.Equal(settings, copy);
    }
}

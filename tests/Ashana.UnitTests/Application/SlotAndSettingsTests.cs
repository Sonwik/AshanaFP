using Ashana.Application.Events;
using Ashana.Application.Orders;
using Ashana.Application.Settings;
using Ashana.UnitTests.Support;

namespace Ashana.UnitTests.Application;

public sealed class SlotServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();

    public Task InitializeAsync() => _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Finished_slots_are_hidden_and_the_next_one_is_too_soon()
    {
        var overview = await _app.Slots.GetOverviewAsync(); // 10:00, ordering needs 10 minutes

        var day = Assert.Single(overview.Days);
        Assert.Equal("Today", day.Title);
        Assert.Equal("10:00–10:15", day.Slots[0].Label);
        Assert.Equal(SlotState.TooSoon, day.Slots[0].State);
        Assert.Equal("10:15–10:30", overview.FirstBookable!.Label);
        Assert.Equal("19:45–20:00", day.Slots[^1].Label);
    }

    [Fact]
    public void Slots_show_how_busy_they_are()
    {
        var rules = TestData.Rules with { MaxOrdersPerSlot = 4 }; // "busy" from 3 orders (75 %)
        var booked = new Dictionary<DateTimeOffset, int>
        {
            [TestData.At(10, 15)] = 2,
            [TestData.At(10, 30)] = 3,
            [TestData.At(10, 45)] = 4,
        };

        var slots = SlotService.BuildDay(TestData.Today, TestData.Now, TestData.Today, rules, TestData.Zone, booked).Slots;

        Assert.Equal(
            [SlotState.TooSoon, SlotState.Available, SlotState.Busy, SlotState.Full, SlotState.Available],
            slots.Take(5).Select(s => s.State));
        Assert.Equal((50, 2), (slots[1].LoadPercent, slots[1].Remaining));
        Assert.False(slots[3].IsBookable);
        Assert.True(slots[2].IsBookable);
    }

    [Theory]
    [InlineData(0, SlotState.Available)]
    [InlineData(14, SlotState.Available)]
    [InlineData(15, SlotState.Busy)]
    [InlineData(20, SlotState.Full)]
    [InlineData(25, SlotState.Full)]
    public void Classify_is_a_pure_function_of_the_thresholds(int count, SlotState expected)
    {
        var classify = F.Curry<SlotService.SlotThresholds, int, SlotState>(SlotService.Classify)(new SlotService.SlotThresholds(20, 15));

        Assert.Equal(expected, classify(count));
    }

    [Fact]
    public async Task After_closing_students_see_tomorrows_slots()
    {
        _app.Time.Set(TestApp.At(21, 0));

        var day = Assert.Single((await _app.Slots.GetOverviewAsync()).Days);

        Assert.Equal("Tomorrow", day.Title);
        Assert.Equal("08:00–08:15", day.Slots[0].Label);
        Assert.True(day.Slots[0].IsBookable);
    }

    [Theory]
    [InlineData(0, "Today")]
    [InlineData(1, "Tomorrow")]
    [InlineData(2, "Sunday, 1 November")]
    public void Day_titles_are_friendly(int daysAhead, string expected) =>
        Assert.Equal(expected, SlotService.DayTitle(TestData.Today.AddDays(daysAhead), TestData.Today));
}

public sealed class SettingsServiceTests : IAsyncLifetime
{
    private readonly TestApp _app = TestApp.Create();

    public Task InitializeAsync() => _app.SeedAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Invalid_rules_are_not_saved()
    {
        var result = await _app.Settings.UpdateAsync(TestData.Rules with { SlotMinutes = 2 });

        Assert.Equal("settings.slot_length", result.Error.Code);
        Assert.Equal(15, (await _app.Settings.GetRulesAsync()).SlotMinutes);
    }

    [Fact]
    public async Task Saved_rules_are_visible_at_once_and_announced()
    {
        await _app.Settings.GetRulesAsync(); // warm the cache

        await _app.Settings.UpdateAsync(TestData.Rules with { MaxOrdersPerSlot = 35 });

        Assert.Equal(35, (await _app.Settings.GetRulesAsync()).MaxOrdersPerSlot);
        Assert.Equal(35, Assert.Single(_app.Events.OfType<SettingsChanged>()).Rules.MaxOrdersPerSlot);
    }

    [Fact]
    public async Task Admin_form_cannot_silently_undo_a_pause_made_by_the_kitchen()
    {
        var form = await _app.Settings.GetForEditAsync();                                    // admin opens settings
        await _app.Settings.SetAcceptingOrdersAsync(false, null);                            // cook pauses orders
        var save = await _app.Settings.UpdateAsync(form.Rules with { MaxOrdersPerSlot = 30 }, form.Version);

        Assert.Equal(SettingsService.Conflict, save.Error);
        Assert.False((await _app.Settings.GetRulesAsync()).AcceptingOrders);
    }

    [Fact]
    public async Task Pause_uses_a_default_message_and_resume_clears_it()
    {
        await _app.Settings.SetAcceptingOrdersAsync(false, "  ");
        var paused = await _app.Settings.GetRulesAsync();

        await _app.Settings.SetAcceptingOrdersAsync(true, "ignored");
        var resumed = await _app.Settings.GetRulesAsync();

        Assert.False(paused.AcceptingOrders);
        Assert.False(string.IsNullOrWhiteSpace(paused.PauseMessage));
        Assert.True(resumed.AcceptingOrders);
        Assert.Null(resumed.PauseMessage);
    }
}

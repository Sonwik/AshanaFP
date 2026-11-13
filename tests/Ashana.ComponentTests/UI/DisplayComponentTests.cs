using Ashana.ComponentTests.Support;
using Ashana.UI;
using Ashana.UI.Components;

namespace Ashana.ComponentTests.UI;

public sealed class DisplayComponentTests : IAsyncDisposable
{
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Decorative_icons_are_hidden_from_screen_readers()
    {
        var html = await _ctx.HtmlAsync<Icon>(new Dictionary<string, object?> { [nameof(Icon.Name)] = "chef-hat" });

        Assert.Contains("aria-hidden=\"true\"", html);
        Assert.DoesNotContain("role=\"img\"", html);
    }

    [Fact]
    public async Task Meaningful_icons_get_a_name_and_unknown_names_fall_back()
    {
        var labelled = await _ctx.HtmlAsync<Icon>(new Dictionary<string, object?> { [nameof(Icon.Name)] = "bell-ring", [nameof(Icon.Label)] = "Ready" });
        var unknown = await _ctx.HtmlAsync<Icon>(new Dictionary<string, object?> { [nameof(Icon.Name)] = "no-such-icon" });
        var help = await _ctx.HtmlAsync<Icon>(new Dictionary<string, object?> { [nameof(Icon.Name)] = "circle-help" });

        Assert.Contains("role=\"img\"", labelled);
        Assert.Contains("aria-label=\"Ready\"", labelled);
        Assert.Equal(help, unknown);
    }

    [Theory]
    [InlineData("Aruzhan Bekova", "AB")]
    [InlineData("madina", "M")]
    [InlineData("Saule Nurlanovna Zhakupova", "SZ")]
    [InlineData("   ", "?")]
    public async Task Avatar_shows_initials(string name, string expected)
    {
        var avatar = await _ctx.RenderAsync<Avatar>(new Dictionary<string, object?> { [nameof(Avatar.Name)] = name });

        Assert.Equal(expected, avatar.Text);
    }

    [Fact]
    public async Task Avatar_color_is_the_same_for_the_same_name()
    {
        var first = await _ctx.HtmlAsync<Avatar>(new Dictionary<string, object?> { [nameof(Avatar.Name)] = "Timur Akhmetov" });
        var second = await _ctx.HtmlAsync<Avatar>(new Dictionary<string, object?> { [nameof(Avatar.Name)] = "Timur Akhmetov" });

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(10, "ok", "10")]
    [InlineData(75, "busy", "75")]
    [InlineData(100, "full", "100")]
    [InlineData(140, "full", "100")]
    [InlineData(-5, "ok", "0")]
    public async Task Load_bar_changes_color_with_load_and_reports_it(int percent, string level, string announced)
    {
        var bar = await _ctx.RenderAsync<LoadBar>(new Dictionary<string, object?> { [nameof(LoadBar.Percent)] = percent, [nameof(LoadBar.Label)] = "12:00 load" });

        var meter = bar.Find("span", e => e["role"] == "meter");
        Assert.True(meter.HasClass($"load-{level}"));
        Assert.Equal(announced, meter["aria-valuenow"]);
        Assert.Equal("12:00 load", meter["aria-label"]);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(7.0, 10.0)]
    [InlineData(12.0, 20.0)]
    [InlineData(48.0, 50.0)]
    [InlineData(51.0, 100.0)]
    [InlineData(721450.0, 1000000.0)]
    public void Chart_axis_ends_at_a_round_number(double max, double expected) =>
        Assert.Equal(expected, BarChart.NiceCeiling(max));

    [Fact]
    public async Task Chart_has_a_text_alternative_and_highlights_the_highest_bar()
    {
        var html = await _ctx.HtmlAsync<BarChart>(new Dictionary<string, object?>
        {
            [nameof(BarChart.Title)] = "Orders per day",
            [nameof(BarChart.Points)] = new List<ChartPoint> { new("Mon", 30), new("Tue", 72), new("Wed", 15) },
        });

        Assert.Contains("aria-label=\"Orders per day: 3 values, highest Tue with 72.\"", html);
        Assert.Matches("<th scope=\"row\"[^>]*>Tue</th><td[^>]*>72</td>", html); // hidden table for screen readers
        Assert.Single(html.Split("is-max")[1..]);
    }

    [Fact]
    public async Task Chart_without_data_says_so()
    {
        var html = await _ctx.HtmlAsync<BarChart>(new Dictionary<string, object?> { [nameof(BarChart.Points)] = new List<ChartPoint>() });

        Assert.Contains("No data for this period.", html);
    }

    [Fact]
    public async Task Status_steps_mark_the_current_step()
    {
        var steps = await _ctx.RenderAsync<StatusSteps>(new Dictionary<string, object?>
        {
            [nameof(StatusSteps.Label)] = "Order progress",
            [nameof(StatusSteps.Steps)] = new List<StepItem>
            {
                new("Placed", "10:02", StepState.Done),
                new("Cooking", null, StepState.Current),
                new("Ready", null, StepState.Upcoming),
            },
        });

        var items = steps.FindAll("li");
        Assert.Equal(3, items.Count);
        Assert.Equal("step", items[1]["aria-current"]);
        Assert.Null(items[0]["aria-current"]);
        Assert.True(items[0].HasClass("step-done"));
        Assert.Equal("Order progress", steps.Find("ol")["aria-label"]);
    }

    [Fact]
    public async Task Empty_state_tells_what_to_do_next()
    {
        var html = await _ctx.HtmlAsync<EmptyState>(new Dictionary<string, object?>
        {
            [nameof(EmptyState.Title)] = "No orders yet",
            [nameof(EmptyState.Icon)] = "receipt",
            [nameof(EmptyState.ChildContent)] = Fragments.Text("Pick something from the menu."),
            [nameof(EmptyState.Actions)] = Fragments.Text("Open the menu"),
        });

        Assert.Matches("<h3 class=\"empty-title\"[^>]*>No orders yet</h3>", html);
        Assert.Contains("Pick something from the menu.", html);
        Assert.Matches("<div class=\"empty-actions\"[^>]*>Open the menu</div>", html);
    }

    [Fact]
    public async Task Empty_state_can_be_the_main_heading_of_a_page()
    {
        var html = await _ctx.HtmlAsync<EmptyState>(new Dictionary<string, object?>
        {
            [nameof(EmptyState.Title)] = "Page not found",
            [nameof(EmptyState.HeadingLevel)] = 1,
        });

        Assert.Matches("<h1 class=\"empty-title\"[^>]*>Page not found</h1>", html);
    }

    [Fact]
    public async Task Ticket_shows_the_pickup_number_and_celebrates_when_ready()
    {
        var html = await _ctx.HtmlAsync<TicketStub>(new Dictionary<string, object?>
        {
            [nameof(TicketStub.Number)] = "117",
            [nameof(TicketStub.Tone)] = Tone.Success,
            [nameof(TicketStub.Celebrate)] = true,
            [nameof(TicketStub.AriaLabel)] = "Order 117, ready",
        });

        Assert.Matches("<p class=\"ticket-number\"[^>]*>117</p>", html);
        Assert.Contains("ticket-success ticket-celebrate", html);
        Assert.Contains("aria-label=\"Order 117, ready\"", html);
    }

    [Fact]
    public async Task Badge_says_the_status_in_words_not_only_in_color()
    {
        var html = await _ctx.HtmlAsync<Badge>(new Dictionary<string, object?>
        {
            [nameof(Badge.Tone)] = Tone.Danger,
            [nameof(Badge.Dot)] = true,
            [nameof(Badge.ChildContent)] = Fragments.Text("Sold out"),
        });

        Assert.Contains("badge badge-danger", html);
        Assert.Matches("<span class=\"badge-dot\" aria-hidden=\"true\"[^>]*></span>Sold out", html);
    }
}

public class ToastServiceTests
{
    [Fact]
    public void Toasts_are_shown_newest_last_and_old_ones_drop_off()
    {
        var toasts = new ToastService();
        var changes = 0;
        toasts.Changed += () => changes++;

        for (var i = 1; i <= 5; i++)
        {
            toasts.Info($"Message {i}");
        }

        Assert.Equal(["Message 2", "Message 3", "Message 4", "Message 5"], toasts.Toasts.Select(t => t.Message));
        Assert.Equal(5, changes);
    }

    [Fact]
    public void Errors_stay_longer_than_other_messages()
    {
        var toasts = new ToastService();

        Assert.True(toasts.Error("Could not save").Duration > toasts.Success("Saved").Duration);
    }

    [Fact]
    public void Dismiss_removes_one_toast()
    {
        var toasts = new ToastService();
        var keep = toasts.Info("Keep");
        var drop = toasts.Warning("Drop");

        toasts.Dismiss(drop.Id);
        toasts.Dismiss(Guid.NewGuid()); // unknown ids are ignored

        Assert.Equal(keep, Assert.Single(toasts.Toasts));
    }
}

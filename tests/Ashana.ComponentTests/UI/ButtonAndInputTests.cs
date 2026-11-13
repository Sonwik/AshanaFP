using Ashana.ComponentTests.Support;
using Ashana.UI;
using Ashana.UI.Components;
using Microsoft.AspNetCore.Components;

namespace Ashana.ComponentTests.UI;

public sealed class ButtonTests : IAsyncDisposable
{
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Renders_variant_size_type_and_label()
    {
        var html = await _ctx.HtmlAsync<Button>(new Dictionary<string, object?>
        {
            [nameof(Button.Variant)] = ButtonVariant.Primary,
            [nameof(Button.Size)] = ButtonSize.Large,
            [nameof(Button.Type)] = "submit",
            [nameof(Button.Icon)] = "save",
            [nameof(Button.ChildContent)] = Fragments.Text("Save settings"),
        });

        Assert.Contains("class=\"btn btn-primary btn-lg", html);
        Assert.Contains("type=\"submit\"", html);
        Assert.Matches("<span class=\"btn-label\"[^>]*>Save settings</span>", html); // [^>]* skips the CSS isolation attribute
        Assert.Contains("<svg class=\"icon", html);
    }

    [Fact]
    public async Task Busy_button_is_disabled_and_says_so_to_screen_readers()
    {
        var html = await _ctx.HtmlAsync<Button>(new Dictionary<string, object?>
        {
            [nameof(Button.Busy)] = true,
            [nameof(Button.Icon)] = "save",
            [nameof(Button.ChildContent)] = Fragments.Text("Place order"),
        });

        Assert.Contains("disabled", html);
        Assert.Contains("aria-busy=\"true\"", html);
        Assert.Contains("btn-spinner", html);
        Assert.DoesNotContain("<svg", html); // the spinner replaces the icon
    }

    [Fact]
    public async Task Icon_without_text_gets_the_icon_only_style_and_keeps_extra_attributes()
    {
        var html = await _ctx.HtmlAsync<Button>(new Dictionary<string, object?>
        {
            [nameof(Button.Icon)] = "plus",
            ["aria-label"] = "Add Plov",
            ["data-testid"] = "add-plov",
        });

        Assert.Contains("btn-icon-only", html);
        Assert.Contains("aria-label=\"Add Plov\"", html);
        Assert.Contains("data-testid=\"add-plov\"", html);
    }

    [Fact]
    public async Task Click_runs_the_callback_unless_the_button_is_disabled()
    {
        var clicks = 0;
        var parameters = new Dictionary<string, object?>
        {
            [nameof(Button.OnClick)] = Fragments.Click(() => clicks++),
            [nameof(Button.ChildContent)] = Fragments.Text("Go"),
        };
        var button = await _ctx.RenderAsync<Button>(parameters);

        await button.ClickAsync(button.Find("button"));
        parameters[nameof(Button.Disabled)] = true;
        await button.SetParametersAsync(parameters);
        await button.ClickAsync(button.Find("button"));

        Assert.Equal(1, clicks);
        Assert.True(button.Find("button").Has("disabled"));
    }
}

public sealed class QuantityStepperTests : IAsyncDisposable
{
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Plus_and_minus_ask_the_parent_for_a_new_value()
    {
        var requested = new List<int>();
        var stepper = await _ctx.RenderAsync<QuantityStepper>(new Dictionary<string, object?>
        {
            [nameof(QuantityStepper.Value)] = 3,
            [nameof(QuantityStepper.Label)] = "Plov",
            [nameof(QuantityStepper.ValueChanged)] = Fragments.Callback<int>(requested.Add),
        });

        await stepper.ClickAsync(stepper.Find("button", b => b["aria-label"] == "One more Plov"));
        await stepper.ClickAsync(stepper.Find("button", b => b["aria-label"] == "One less Plov"));

        Assert.Equal([4, 2], requested);
        Assert.Equal("3", stepper.Find("output").Text); // controlled: the value changes only when the parent says so
    }

    [Fact]
    public async Task Buttons_are_disabled_at_the_limits()
    {
        var stepper = await _ctx.RenderAsync<QuantityStepper>(new Dictionary<string, object?>
        {
            [nameof(QuantityStepper.Value)] = 10,
            [nameof(QuantityStepper.Min)] = 10,
            [nameof(QuantityStepper.Max)] = 10,
            [nameof(QuantityStepper.Label)] = "Tea",
        });

        Assert.All(stepper.FindAll("button"), button => Assert.True(button.Has("disabled")));
    }

    [Fact]
    public async Task Going_below_one_offers_to_remove_the_dish()
    {
        var stepper = await _ctx.RenderAsync<QuantityStepper>(new Dictionary<string, object?>
        {
            [nameof(QuantityStepper.Value)] = 1,
            [nameof(QuantityStepper.Label)] = "Lagman",
        });

        var minus = stepper.FindAll("button")[0];
        Assert.Equal("Remove Lagman", minus["aria-label"]);
        Assert.Equal("Quantity of Lagman", stepper.Find("div")["aria-label"]);
    }
}

public sealed class ChipGroupTests : IAsyncDisposable
{
    private readonly ComponentTestContext _ctx = new();

    public ValueTask DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Selected_chip_is_pressed_and_other_chips_change_the_value()
    {
        var chosen = new List<string?>();
        var group = await _ctx.RenderAsync<ChipGroup<string>>(new Dictionary<string, object?>
        {
            [nameof(ChipGroup<string>.Value)] = "soups",
            [nameof(ChipGroup<string>.AriaLabel)] = "Categories",
            [nameof(ChipGroup<string>.ValueChanged)] = Fragments.Callback<string?>(chosen.Add),
            [nameof(ChipGroup<string>.ChildContent)] = Chips(("mains", "Mains", 7), ("soups", "Soups", 4)),
        });

        var mains = group.Find("button", b => b.Text.StartsWith("Mains", StringComparison.Ordinal));
        var soups = group.Find("button", b => b.Text.StartsWith("Soups", StringComparison.Ordinal));
        await group.ClickAsync(soups); // already selected: nothing to report
        await group.ClickAsync(mains);

        Assert.Equal("true", soups["aria-pressed"]);
        Assert.Equal("false", mains["aria-pressed"]);
        Assert.Equal(["mains"], chosen);
        Assert.EndsWith("7", mains.Text); // the dish count is shown on the chip
        Assert.Equal("Categories", group.Find("div")["aria-label"]);
    }

    private static RenderFragment Chips(params (string Value, string Text, int Count)[] chips) => builder =>
    {
        foreach (var (value, text, count) in chips)
        {
            builder.OpenComponent<Chip<string>>(0);
            builder.AddAttribute(1, nameof(Chip<string>.Value), value);
            builder.AddAttribute(2, nameof(Chip<string>.Count), count);
            builder.AddAttribute(3, nameof(Chip<string>.ChildContent), Fragments.Text(text));
            builder.CloseComponent();
        }
    };
}

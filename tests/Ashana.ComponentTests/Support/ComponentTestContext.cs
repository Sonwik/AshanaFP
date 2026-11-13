#pragma warning disable BL0006 // TestRenderer derives from the RenderTree Renderer on purpose.

using Ashana.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace Ashana.ComponentTests.Support;

/// <summary>
/// Services and renderers for one test. Components get the real UI services (ToastService, AshanaJs) and a fake
/// JavaScript runtime that records calls instead of running a browser.
/// </summary>
public sealed class ComponentTestContext : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly HtmlRenderer _html;

    public ComponentTestContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IJSRuntime>(Js);
        services.AddAshanaUi();
        _services = services.BuildServiceProvider();
        _html = new HtmlRenderer(_services, _services.GetRequiredService<ILoggerFactory>());
        Renderer = new TestRenderer(_services);
    }

    public FakeJsRuntime Js { get; } = new();

    public TestRenderer Renderer { get; }

    public T Get<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Renders a component to HTML, exactly as static server-side rendering would.</summary>
    public Task<string> HtmlAsync<TComponent>(IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : IComponent =>
        _html.Dispatcher.InvokeAsync(async () =>
        {
            var output = await _html.RenderComponentAsync<TComponent>(TestRenderer.ToParameterView(parameters));
            return output.ToHtmlString();
        });

    /// <summary>Renders a component so a test can click it and inspect the result.</summary>
    public Task<Rendered<TComponent>> RenderAsync<TComponent>(IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : IComponent => Renderer.RenderAsync<TComponent>(parameters);

    public async ValueTask DisposeAsync()
    {
        await _html.DisposeAsync();
        Renderer.Dispose();
        await _services.DisposeAsync();
    }
}

/// <summary>Records JavaScript interop calls; returns default values.</summary>
public sealed class FakeJsRuntime : IJSRuntime
{
    public List<string> Calls { get; } = [];

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add(identifier);
        return ValueTask.FromResult(default(TValue)!);
    }
}

/// <summary>Helpers to write render fragments and callbacks in C# tests.</summary>
public static class Fragments
{
    public static RenderFragment Text(string text) => builder => builder.AddContent(0, text);

    public static EventCallback<T> Callback<T>(Action<T> onInvoke) => EventCallback.Factory.Create(new object(), onInvoke);

    public static EventCallback<MouseEventArgs> Click(Action onClick) => EventCallback.Factory.Create<MouseEventArgs>(new object(), onClick);
}

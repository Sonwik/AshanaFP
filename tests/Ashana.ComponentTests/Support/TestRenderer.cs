#pragma warning disable BL0006 // Reading RenderTree frames is exactly what a test renderer is for.

using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ashana.ComponentTests.Support;

/// <summary>
/// A minimal Blazor renderer for tests. It keeps every component's current render tree, so a test can find an
/// element, read its attributes and text, and dispatch a click to its event handler, like a browser would.
/// </summary>
public sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
{
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

    /// <summary>Exceptions thrown by components during rendering or event handling.</summary>
    public List<Exception> Errors { get; } = [];

    public Task<Rendered<TComponent>> RenderAsync<TComponent>(IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : IComponent =>
        Dispatcher.InvokeAsync(async () =>
        {
            var component = (TComponent)InstantiateComponent(typeof(TComponent));
            var id = AssignRootComponentId(component);
            await RenderRootComponentAsync(id, ToParameterView(parameters));
            return new Rendered<TComponent>(this, id, component);
        });

    internal ArrayRange<RenderTreeFrame> FramesOf(int componentId) => GetCurrentRenderTreeFrames(componentId);

    internal Task DispatchAsync(ulong eventHandlerId, EventArgs args) =>
        Dispatcher.InvokeAsync(() => DispatchEventAsync(eventHandlerId, null, args));

    internal static ParameterView ToParameterView(IReadOnlyDictionary<string, object?>? parameters) =>
        ParameterView.FromDictionary(new Dictionary<string, object?>(parameters ?? new Dictionary<string, object?>()));

    protected override void HandleException(Exception exception) => Errors.Add(exception);

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
}

/// <summary>One HTML element of the rendered output.</summary>
public sealed record RenderedElement(
    string Name,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyDictionary<string, ulong> EventHandlers,
    string Text)
{
    /// <summary>Attribute value as text, or null when the attribute is not rendered.</summary>
    public string? this[string attribute] => Attributes.TryGetValue(attribute, out var value) ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) : null;

    public bool Has(string attribute) => Attributes.ContainsKey(attribute);

    public bool HasClass(string className) =>
        this["class"]?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className, StringComparer.Ordinal) == true;
}

/// <summary>A component rendered by <see cref="TestRenderer"/>.</summary>
public sealed partial class Rendered<TComponent>(TestRenderer renderer, int componentId, TComponent instance)
    where TComponent : IComponent
{
    public TComponent Instance => instance;

    /// <summary>Visible text of the whole component, including child components.</summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder();
            CollectText(componentId, 0, int.MaxValue, text);
            return Spaces().Replace(text.ToString(), " ").Trim();
        }
    }

    public IReadOnlyList<RenderedElement> FindAll(string tag, Func<RenderedElement, bool>? where = null) =>
        Elements(componentId).Where(e => e.Name == tag && (where?.Invoke(e) ?? true)).ToList();

    /// <summary>The single matching element; fails if there is none or more than one.</summary>
    public RenderedElement Find(string tag, Func<RenderedElement, bool>? where = null)
    {
        var matches = FindAll(tag, where);
        return matches.Count == 1
            ? matches[0]
            : throw new InvalidOperationException($"Expected one <{tag}> but found {matches.Count}. Text: {Text}");
    }

    public Task ClickAsync(RenderedElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.Has("disabled"))
        {
            return Task.CompletedTask; // browsers do not send clicks to disabled buttons
        }

        return element.EventHandlers.TryGetValue("onclick", out var handler)
            ? renderer.DispatchAsync(handler, new MouseEventArgs { Detail = 1 })
            : throw new InvalidOperationException($"<{element.Name}> has no click handler.");
    }

    public Task SetParametersAsync(IReadOnlyDictionary<string, object?> parameters) =>
        renderer.Dispatcher.InvokeAsync(() => instance.SetParametersAsync(TestRenderer.ToParameterView(parameters)));

    private IEnumerable<RenderedElement> Elements(int id)
    {
        var frames = renderer.FramesOf(id);
        var result = new List<RenderedElement>();
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Element)
            {
                var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
                var handlers = new Dictionary<string, ulong>(StringComparer.Ordinal);
                var end = i + frame.ElementSubtreeLength;
                for (var j = i + 1; j < end && frames.Array[j].FrameType == RenderTreeFrameType.Attribute; j++)
                {
                    var attribute = frames.Array[j];
                    if (attribute.AttributeEventHandlerId != 0)
                    {
                        handlers[attribute.AttributeName] = attribute.AttributeEventHandlerId;
                    }
                    else
                    {
                        attributes[attribute.AttributeName] = attribute.AttributeValue;
                    }
                }

                var text = new StringBuilder();
                CollectText(id, i + 1, end, text);
                result.Add(new RenderedElement(frame.ElementName, attributes, handlers, Spaces().Replace(text.ToString(), " ").Trim()));
            }
            else if (frame.FrameType == RenderTreeFrameType.Component)
            {
                result.AddRange(Elements(frame.ComponentId));
            }
        }

        return result;
    }

    private void CollectText(int id, int start, int end, StringBuilder text)
    {
        var frames = renderer.FramesOf(id);
        for (var i = start; i < Math.Min(end, frames.Count); i++)
        {
            var frame = frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Text:
                    text.Append(frame.TextContent);
                    break;
                case RenderTreeFrameType.Markup:
                    text.Append(Tags().Replace(frame.MarkupContent, " "));
                    break;
                case RenderTreeFrameType.Component:
                    CollectText(frame.ComponentId, 0, int.MaxValue, text);
                    break;
            }
        }
    }

    // Only ordinary whitespace: no-break spaces inside prices ("1 800 ₸") are meaningful and must stay.
    [GeneratedRegex("[ \t\r\n\f]+")]
    private static partial Regex Spaces();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex Tags();
}

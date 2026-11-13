using System.Collections.Concurrent;
using Ashana.Application.Abstractions;

namespace Ashana.UnitTests.Support;

/// <summary>Captures published events synchronously, so tests can assert on them without waiting.</summary>
public sealed class RecordingPublisher : IEventPublisher
{
    private readonly ConcurrentQueue<object> _events = new();

    public IReadOnlyList<object> Events => [.. _events];

    public void Publish<TEvent>(TEvent @event)
        where TEvent : class => _events.Enqueue(@event);

    public IReadOnlyList<TEvent> OfType<TEvent>() => [.. _events.OfType<TEvent>()];

    public void Clear() => _events.Clear();
}

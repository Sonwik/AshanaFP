using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Threading.Channels;
using Ashana.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Ashana.Infrastructure.Messaging;

/// <summary>
/// In-memory publish/subscribe used for real-time updates inside one server.
/// <para>
/// Every subscriber gets its own small mailbox (a bounded <see cref="Channel{T}"/>) and its own reader task.
/// So a slow or crashing subscriber (for example, a browser tab on a bad connection) never delays the others,
/// and each subscriber still receives events in order.
/// </para>
/// For several servers this class would be replaced by a message broker or a SignalR backplane.
/// </summary>
public sealed class InProcessEventBus : IEventPublisher, IEventSubscriber, IDisposable
{
    private const int MailboxCapacity = 256;

    private readonly ConcurrentDictionary<Type, ImmutableList<Subscription>> _subscriptions = new();
    private readonly ILogger<InProcessEventBus> _logger;

    public InProcessEventBus(ILogger<InProcessEventBus> logger) => _logger = logger;

    /// <summary>Number of live subscriptions (useful in tests and diagnostics).</summary>
    public int SubscriberCount => _subscriptions.Values.Sum(list => list.Count);

    public void Publish<TEvent>(TEvent @event)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var subscribers))
        {
            return;
        }

        foreach (var subscription in subscribers)
        {
            subscription.Post(@event);
        }
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        var subscription = new Subscription(
            typeof(TEvent),
            evt => handler((TEvent)evt),
            this);

        _subscriptions.AddOrUpdate(
            typeof(TEvent),
            _ => [subscription],
            (_, list) => list.Add(subscription));

        return subscription;
    }

    public void Dispose()
    {
        foreach (var list in _subscriptions.Values)
        {
            foreach (var subscription in list)
            {
                subscription.Dispose();
            }
        }

        _subscriptions.Clear();
    }

    private void Remove(Subscription subscription) =>
        _subscriptions.AddOrUpdate(
            subscription.EventType,
            _ => [],
            (_, list) => list.Remove(subscription));

    private sealed class Subscription : IDisposable
    {
        private readonly Channel<object> _mailbox = Channel.CreateBounded<object>(new BoundedChannelOptions(MailboxCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest, // the UI only needs the latest state
        });

        private readonly Func<object, Task> _handler;
        private readonly InProcessEventBus _owner;
        private int _disposed;

        public Subscription(Type eventType, Func<object, Task> handler, InProcessEventBus owner)
        {
            EventType = eventType;
            _handler = handler;
            _owner = owner;
            _ = Task.Run(PumpAsync);
        }

        public Type EventType { get; }

        public void Post(object @event) => _mailbox.Writer.TryWrite(@event);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _mailbox.Writer.TryComplete();
            _owner.Remove(this);
        }

        private async Task PumpAsync()
        {
            await foreach (var @event in _mailbox.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await _handler(@event).ConfigureAwait(false);
                }
#pragma warning disable CA1031 // A subscriber must never crash the bus.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _owner._logger.LogWarning(ex, "Subscriber for {EventType} failed", EventType.Name);
                }
            }
        }
    }
}

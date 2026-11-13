namespace Ashana.Application.Abstractions;

/// <summary>
/// Publishes application events (an order changed, stock changed...).
/// Services only need to <i>publish</i>, so they depend on this small interface
/// (Interface Segregation Principle) and know nothing about Blazor or SignalR.
/// </summary>
public interface IEventPublisher
{
    /// <summary>Queues the event for delivery. Never blocks the caller and never throws because of a subscriber.</summary>
    void Publish<TEvent>(TEvent @event)
        where TEvent : class;
}

/// <summary>Subscribes to application events. Used by UI components and the SignalR broadcaster.</summary>
public interface IEventSubscriber
{
    /// <summary>Registers a handler. Dispose the returned object to unsubscribe (do it in the component's Dispose).</summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
        where TEvent : class;
}

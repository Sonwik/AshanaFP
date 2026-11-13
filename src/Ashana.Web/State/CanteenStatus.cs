using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.Events;
using Ashana.Application.Settings;
using Ashana.Domain.Canteen;

namespace Ashana.Web.State;

/// <summary>Live canteen rules for this tab: shows the "orders paused" banner the moment the kitchen presses pause.</summary>
public sealed class CanteenStatus : IDisposable
{
    private readonly SettingsService _settings;
    private readonly IEventSubscriber _events;
    private readonly CanteenClock _clock;
    private IDisposable? _subscription;

    public CanteenStatus(SettingsService settings, IEventSubscriber events, CanteenClock clock)
    {
        _settings = settings;
        _events = events;
        _clock = clock;
    }

    public event Action? Changed;

    public CanteenRules? Rules { get; private set; }

    public bool AcceptingOrders => Rules?.AcceptingOrders ?? true;

    public string? PauseMessage => Rules?.PauseMessage;

    /// <summary>The day students are ordering for right now.</summary>
    public DateOnly? ServiceDay => Rules is null ? null : SlotPlanner.ServiceDay(_clock.Now, Rules);

    public async Task EnsureLoadedAsync(bool live)
    {
        Rules ??= await _settings.GetRulesAsync();
        if (live && _subscription is null)
        {
            _subscription = _events.Subscribe<SettingsChanged>(change =>
            {
                Rules = change.Rules;
                Changed?.Invoke();
                return Task.CompletedTask;
            });
        }
    }

    public void Dispose() => _subscription?.Dispose();
}

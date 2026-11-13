using Ashana.Application.Abstractions;
using Ashana.Application.Events;
using Ashana.Application.State;
using Ashana.Domain.Canteen;
using Microsoft.Extensions.Logging;

namespace Ashana.Application.Settings;

/// <summary>The rules together with the version they were read at (for optimistic concurrency on the settings page).</summary>
public sealed record SettingsSnapshot(CanteenRules Rules, int Version);

/// <summary>Reads and changes canteen rules. Changes are pushed to every open page.</summary>
public sealed class SettingsService
{
    /// <summary>Returned when the settings changed after the admin opened the form (for example, the kitchen paused orders).</summary>
    public static readonly Error Conflict = Error.Conflict(
        "settings.conflict", "Settings were changed by someone else in the meantime (for example, the kitchen paused orders). Reload and try again.");

    public const string DefaultPauseMessage = "The kitchen is very busy. New orders are paused for a few minutes.";

    private readonly IStateStore _store;
    private readonly IEventPublisher _events;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(IStateStore store, IEventPublisher events, ILogger<SettingsService> logger)
    {
        _store = store;
        _events = events;
        _logger = logger;
    }

    /// <summary>Reading the current rules is just reading the snapshot: no cache needed.</summary>
    public Task<CanteenRules> GetRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(_store.Current.Rules);

    /// <summary>Fresh rules and their version for an edit form.</summary>
    public Task<SettingsSnapshot> GetForEditAsync(CancellationToken cancellationToken = default)
    {
        var settings = _store.Current.Settings;
        return Task.FromResult(new SettingsSnapshot(settings.Rules, settings.Version));
    }

    /// <summary>
    /// Validates and saves new rules. With <paramref name="expectedVersion"/> the save is refused if the settings
    /// changed after the form was opened, so an admin cannot silently undo the kitchen's "pause orders".
    /// </summary>
    public async Task<Result<Unit>> UpdateAsync(CanteenRules rules, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        var updated = await _store.UpdateAsync(state => Update(state, rules, expectedVersion), cancellationToken);
        return updated
            .Tap(r => _logger.LogInformation("Canteen settings updated: {Rules}", r))
            .Tap(r => _events.Publish(new SettingsChanged(r)))
            .ToUnit();
    }

    /// <summary>"Pause new orders" button on the kitchen board.</summary>
    public Task<Result<Unit>> SetAcceptingOrdersAsync(bool accepting, string? message, CancellationToken cancellationToken = default)
    {
        var current = _store.Current.Rules;
        return UpdateAsync(WithPause(current, accepting, message), expectedVersion: null, cancellationToken);
    }

    /// <summary>Pure: the rules with orders paused or resumed.</summary>
    public static CanteenRules WithPause(CanteenRules rules, bool accepting, string? message)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules with
        {
            AcceptingOrders = accepting,
            PauseMessage = accepting ? null : (string.IsNullOrWhiteSpace(message) ? DefaultPauseMessage : message),
        };
    }

    /// <summary>The pure transition behind <see cref="UpdateAsync"/>.</summary>
    public static Result<Change<CanteenRules>> Update(CanteenState state, CanteenRules rules, int? expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Result.Success(state.Settings)
            .Ensure(settings => expectedVersion is not { } expected || settings.Version == expected, Conflict)
            .Bind(settings => settings.Update(rules))
            .Map(settings => Change.Of(state.With(settings), settings.Rules));
    }
}

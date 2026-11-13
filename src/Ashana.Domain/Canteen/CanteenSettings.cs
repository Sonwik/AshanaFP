using System.Collections.Immutable;
using Ashana.Domain.Common;

namespace Ashana.Domain.Canteen;

/// <summary>Editable canteen rules. Admins change them on the Settings page without restarting the app.</summary>
public sealed record CanteenRules(
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    int SlotMinutes,
    int MaxOrdersPerSlot,
    int MinLeadMinutes,
    int NoShowAfterMinutes,
    int MaxActiveOrdersPerUser,
    bool AcceptingOrders,
    string? PauseMessage);

/// <summary>
/// Working hours and capacity of the canteen (a single stored record, immutable).
/// The pickup-slot capacity is what flattens the lunch rush: when a slot is full,
/// students pick the next one instead of all arriving at 12:00.
/// </summary>
public sealed record CanteenSettings : IEntity
{
    public static readonly Guid SingletonId = new("5e771265-0000-4000-8000-000000000001");

    /// <summary>Every rule for valid settings, as data.</summary>
    public static readonly ImmutableArray<Rule<CanteenRules>> RuleChecks =
    [
        Validation.Rule<CanteenRules>(r => r.ClosesAt > r.OpensAt, Error.Validation("settings.hours", "Closing time must be later than opening time.")),
        Validation.Rule<CanteenRules>(r => r.SlotMinutes is >= 5 and <= 60, Error.Validation("settings.slot_length", "Slot length must be between 5 and 60 minutes.")),
        Validation.Rule<CanteenRules>(r => (r.ClosesAt - r.OpensAt).TotalMinutes >= r.SlotMinutes, Error.Validation("settings.too_short", "Working hours must fit at least one slot.")),
        Validation.Rule<CanteenRules>(r => r.MaxOrdersPerSlot is >= 1 and <= 500, Error.Validation("settings.capacity", "Orders per slot must be between 1 and 500.")),
        Validation.Rule<CanteenRules>(r => r.MinLeadMinutes is >= 0 and <= 180, Error.Validation("settings.lead", "Preparation lead time must be between 0 and 180 minutes.")),
        Validation.Rule<CanteenRules>(r => r.NoShowAfterMinutes is >= 5 and <= 240, Error.Validation("settings.no_show", "No-show timeout must be between 5 and 240 minutes.")),
        Validation.Rule<CanteenRules>(r => r.MaxActiveOrdersPerUser is >= 1 and <= 10, Error.Validation("settings.active_limit", "Active orders per student must be between 1 and 10.")),
        Validation.Rule<CanteenRules>(r => r.PauseMessage is not { Length: > 140 }, Error.Validation("settings.pause_message", "Pause message must be at most 140 characters.")),
    ];

    private CanteenSettings()
    {
    }

    public Guid Id { get; private init; } = SingletonId;

    public int Version { get; private init; }

    /// <summary>The current rules (one immutable value instead of many fields).</summary>
    public CanteenRules Rules { get; private init; } = Default;

    /// <summary>IANA time zone of the canteen, for example <c>Asia/Almaty</c>.</summary>
    public string TimeZoneId { get; private init; } = "Asia/Almaty";

    /// <summary>Service day for which daily stock was last reset.</summary>
    public DateOnly LastStockReset { get; private init; }

    public bool AcceptingOrders => Rules.AcceptingOrders;

    public int NoShowAfterMinutes => Rules.NoShowAfterMinutes;

    /// <summary>Default rules for a typical university canteen.</summary>
    public static CanteenRules Default { get; } = new(
        OpensAt: new TimeOnly(8, 0),
        ClosesAt: new TimeOnly(20, 0),
        SlotMinutes: 15,
        MaxOrdersPerSlot: 20,
        MinLeadMinutes: 10,
        NoShowAfterMinutes: 30,
        MaxActiveOrdersPerUser: 3,
        AcceptingOrders: true,
        PauseMessage: null);

    public static CanteenSettings CreateDefault(string timeZoneId = "Asia/Almaty") => new() { TimeZoneId = timeZoneId };

    public static Result<CanteenRules> Validate(CanteenRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Check(RuleChecks);
    }

    /// <summary>Validated, normalized new rules in a new settings record.</summary>
    public Result<CanteenSettings> Update(CanteenRules rules) =>
        Validate(rules)
            .Map(Normalize)
            .Map(valid => this with { Rules = valid, Version = Version + 1 });

    /// <summary>Remembers that the daily stock reset ran for <paramref name="serviceDay"/>; <c>None</c> if it already did.</summary>
    public Option<CanteenSettings> MarkStockReset(DateOnly serviceDay) =>
        LastStockReset >= serviceDay ? Option<CanteenSettings>.None : this with { LastStockReset = serviceDay };

    private static CanteenRules Normalize(CanteenRules rules) =>
        rules with { PauseMessage = string.IsNullOrWhiteSpace(rules.PauseMessage) ? null : rules.PauseMessage.Trim() };
}

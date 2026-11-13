using Microsoft.Extensions.Options;

namespace Ashana.Application.Common;

/// <summary>Configuration section <c>Canteen</c> in appsettings.json.</summary>
public sealed class CanteenOptions
{
    public const string SectionName = "Canteen";

    /// <summary>IANA time zone, for example <c>Asia/Almaty</c>.</summary>
    public string TimeZone { get; set; } = "Asia/Almaty";

    /// <summary>Used if the operating system does not know <see cref="TimeZone"/>.</summary>
    public int FallbackUtcOffsetHours { get; set; } = 5;

    public string Name { get; set; } = "Ashana Canteen";
}

/// <summary>
/// "What time is it in the canteen?" Wraps <see cref="TimeProvider"/> (testable time, .NET 8+)
/// and the canteen time zone, so the server can run in any zone (for example UTC in the cloud).
/// </summary>
public sealed class CanteenClock
{
    private readonly TimeProvider _time;

    public CanteenClock(TimeProvider time, IOptions<CanteenOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _time = time;
        TimeZone = ResolveTimeZone(options.Value);
    }

    public TimeZoneInfo TimeZone { get; }

    /// <summary>Current canteen-local time.</summary>
    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(_time.GetUtcNow(), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, TimeZone);

    public DateOnly LocalDate(DateTimeOffset moment) => DateOnly.FromDateTime(ToLocal(moment).DateTime);

    /// <summary>The canteen-local service day students are ordering for right now.</summary>
    public DateOnly ServiceDay(Ashana.Domain.Canteen.CanteenRules rules) => Ashana.Domain.Canteen.SlotPlanner.ServiceDay(Now, rules);

    private static TimeZoneInfo ResolveTimeZone(CanteenOptions options)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZone, out var zone))
        {
            return zone;
        }

        // The operating system has no time zone data for this id (for example a slim container image):
        // use a fixed offset instead of failing at startup.
        var offset = TimeSpan.FromHours(options.FallbackUtcOffsetHours);
        var id = $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
        return TimeZoneInfo.CreateCustomTimeZone(id, offset, $"{options.TimeZone} ({id})", id);
    }
}

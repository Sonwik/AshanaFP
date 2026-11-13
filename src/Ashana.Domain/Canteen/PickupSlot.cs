namespace Ashana.Domain.Canteen;

/// <summary>
/// A time window when a student picks up the order, for example 12:15–12:30.
/// Times carry the canteen's UTC offset, so <c>Start.ToString("HH:mm")</c> is local time.
/// </summary>
public readonly record struct PickupSlot(DateTimeOffset Start, DateTimeOffset End)
{
    public DateOnly Day => DateOnly.FromDateTime(Start.DateTime);

    /// <summary>Text like "12:15–12:30".</summary>
    public string Label => $"{Start:HH\\:mm}–{End:HH\\:mm}";

    public bool Contains(DateTimeOffset moment) => moment >= Start && moment < End;
}

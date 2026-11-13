namespace Ashana.UnitTests.Support;

/// <summary>
/// A clock the test controls. Services read time only through <see cref="TimeProvider"/>,
/// so a test can jump to "after closing time" or "tomorrow" without waiting.
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _now.ToUniversalTime();
        }
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate)
        {
            _now += by;
        }
    }

    public void Set(DateTimeOffset moment)
    {
        lock (_gate)
        {
            _now = moment;
        }
    }
}

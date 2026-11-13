namespace Ashana.Web.State;

/// <summary>
/// One shared timer per browser tab that ticks every 20 seconds, so "late by 3 min" labels stay correct
/// without every card running its own timer. Starts with the first subscriber.
/// </summary>
public sealed class Ticker : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private readonly TimeProvider _time;
    private ITimer? _timer;
    private Action? _tick;

    public Ticker(TimeProvider time) => _time = time;

    public event Action? Tick
    {
        add
        {
            _tick += value;
            _timer ??= _time.CreateTimer(_ => _tick?.Invoke(), null, Interval, Interval);
        }

        remove => _tick -= value;
    }

    public void Dispose() => _timer?.Dispose();
}

namespace Ashana.Web.State;

/// <summary>
/// Runs an action once after a quiet period. Ten "order changed" events within 300 ms
/// cause one reload instead of ten (saves queries and re-renders).
/// </summary>
public sealed class Debouncer : IDisposable
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _pending;

    public Debouncer(TimeSpan delay) => _delay = delay;

    public void Run(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pending, next);
        previous?.Cancel();
        previous?.Dispose();
        _ = RunAsync(action, next.Token);
    }

    public void Dispose()
    {
        var pending = Interlocked.Exchange(ref _pending, null);
        pending?.Cancel();
        pending?.Dispose();
    }

    private async Task RunAsync(Func<Task> action, CancellationToken token)
    {
        try
        {
            await Task.Delay(_delay, token);
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
            // the component was removed while waiting
        }
    }
}

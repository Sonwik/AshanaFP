using Ashana.Application.Abstractions;
using Ashana.Application.State;

namespace Ashana.Infrastructure.Persistence;

/// <summary>
/// Holds the current <see cref="CanteenState"/> in one reference. This is the only mutable thing in the
/// whole data path, and it is isolated here:
/// <list type="bullet">
/// <item>Readers take <see cref="Current"/>, an immutable snapshot, without any lock.</item>
/// <item>Writers run their pure transition one at a time (a single-writer queue built on a semaphore),
/// then the new state is published with one atomic reference write.</item>
/// </list>
/// So "check stock, then reserve it" can never interleave with another order, and a reader can never
/// observe half of a change.
/// </summary>
public sealed class InMemoryStateStore : IStateStore, IDisposable
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    private CanteenState _state;
    private long _revision;

    public InMemoryStateStore(CanteenState initial)
    {
        ArgumentNullException.ThrowIfNull(initial);
        _state = initial;
    }

    /// <summary>Raised after every committed change (outside the writer lock). The file store listens to it.</summary>
    public event Action? Committed;

    public CanteenState Current => Volatile.Read(ref _state);

    /// <summary>Incremented on every committed change; the file store uses it to know when to write.</summary>
    public long Revision => Interlocked.Read(ref _revision);

    public async Task<Result<T>> UpdateAsync<T>(Func<CanteenState, Result<Change<T>>> transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        Result<Change<T>> outcome;
        var changed = false;

        await _writer.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var before = _state;
            outcome = transition(before);

            // A failure (or an exception thrown above) leaves the old state in place: all-or-nothing for free.
            if (outcome.IsSuccess && !ReferenceEquals(outcome.Value.State, before))
            {
                Volatile.Write(ref _state, outcome.Value.State);
                Interlocked.Increment(ref _revision);
                changed = true;
            }
        }
        finally
        {
            _writer.Release();
        }

        if (changed)
        {
            Committed?.Invoke();
        }

        return outcome.Map(change => change.Value);
    }

    /// <summary>Replaces the whole state (loading a file, resetting demo data).</summary>
    public void Replace(CanteenState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _writer.Wait();
        try
        {
            Volatile.Write(ref _state, state);
            Interlocked.Increment(ref _revision);
        }
        finally
        {
            _writer.Release();
        }

        Committed?.Invoke();
    }

    public void Dispose() => _writer.Dispose();
}

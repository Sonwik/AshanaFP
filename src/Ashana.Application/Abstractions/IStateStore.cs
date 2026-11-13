using Ashana.Application.State;

namespace Ashana.Application.Abstractions;

/// <summary>
/// The imperative shell around the immutable <see cref="CanteenState"/>: the one place where something changes.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><see cref="Current"/> is a snapshot: readers never wait and never see half of a change.</item>
/// <item><see cref="UpdateAsync{T}"/> applies a <b>pure</b> transition. Transitions run one at a time,
/// so "check stock, then reserve it" can never interleave with another order (no lost updates).</item>
/// <item>If the transition returns an error (or throws), the old state stays: every update is all-or-nothing.</item>
/// </list>
/// The in-memory implementation can be replaced (for example, by one that stores events in PostgreSQL)
/// without touching a single use case.
/// </remarks>
public interface IStateStore
{
    /// <summary>The latest committed state.</summary>
    CanteenState Current { get; }

    /// <summary>Applies <paramref name="transition"/> to the latest state and commits its new state on success.</summary>
    Task<Result<T>> UpdateAsync<T>(Func<CanteenState, Result<Change<T>>> transition, CancellationToken cancellationToken = default);
}

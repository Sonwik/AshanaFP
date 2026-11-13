namespace Ashana.Functional;

/// <summary>
/// Lazy sequence builders. Nothing is computed until someone enumerates the sequence,
/// so a sequence can even be infinite as long as the caller takes only what it needs.
/// </summary>
public static class Seq
{
    /// <summary>
    /// The opposite of a fold: grows a sequence from a seed. <paramref name="step"/> returns the next element
    /// and the next state, or <c>None</c> to stop. For example, pickup slots are unfolded from the opening time.
    /// </summary>
    public static IEnumerable<T> Unfold<TState, T>(TState seed, Func<TState, Option<(T Value, TState Next)>> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return Iterator();

        IEnumerable<T> Iterator()
        {
            var state = seed;
            while (true)
            {
                var next = step(state);
                if (next.IsNone)
                {
                    yield break;
                }

                var (value, nextState) = next.GetOrElse(default((T, TState)));
                yield return value;
                state = nextState;
            }
        }
    }

    /// <summary>The infinite lazy sequence <c>seed, f(seed), f(f(seed)), ...</c>.</summary>
    public static IEnumerable<T> Iterate<T>(T seed, Func<T, T> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return Iterator();

        IEnumerable<T> Iterator()
        {
            var current = seed;
            while (true)
            {
                yield return current;
                current = next(current);
            }
        }
    }

    /// <summary>A lazy running fold: every intermediate accumulator (for example, a running total).</summary>
    public static IEnumerable<TAcc> Scan<T, TAcc>(this IEnumerable<T> source, TAcc seed, Func<TAcc, T, TAcc> step)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(step);
        return Iterator();

        IEnumerable<TAcc> Iterator()
        {
            var acc = seed;
            foreach (var item in source)
            {
                acc = step(acc, item);
                yield return acc;
            }
        }
    }
}

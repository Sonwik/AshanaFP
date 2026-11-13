using System.Collections.Concurrent;

namespace Ashana.Functional;

/// <summary>
/// Higher-order helpers that treat functions as values: piping, composition, currying,
/// partial application and memoization.
/// </summary>
public static class F
{
    /// <summary>The function that returns its argument. Useful as a default "do nothing" mapping.</summary>
    public static T Identity<T>(T value) => value;

    /// <summary>
    /// Pipes a value into a function, so code reads left to right like F#'s <c>|&gt;</c>:
    /// <c>price.Pipe(ApplyDiscount).Pipe(Round)</c> instead of <c>Round(ApplyDiscount(price))</c>.
    /// </summary>
    public static TOut Pipe<T, TOut>(this T value, Func<T, TOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return f(value);
    }

    /// <summary>Function composition: <c>f.Then(g)</c> is a new function <c>x =&gt; g(f(x))</c>.</summary>
    public static Func<T, TOut> Then<T, TMiddle, TOut>(this Func<T, TMiddle> f, Func<TMiddle, TOut> g)
    {
        ArgumentNullException.ThrowIfNull(f);
        ArgumentNullException.ThrowIfNull(g);
        return x => g(f(x));
    }

    /// <summary>Composes a list of same-typed steps into one function (a pipeline built as data).</summary>
    public static Func<T, T> Compose<T>(IEnumerable<Func<T, T>> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.Aggregate((Func<T, T>)Identity, (pipeline, step) => pipeline.Then(step));
    }

    /// <summary>Currying: a two-argument function becomes a chain of one-argument functions.</summary>
    public static Func<T1, Func<T2, TOut>> Curry<T1, T2, TOut>(this Func<T1, T2, TOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return a => b => f(a, b);
    }

    public static Func<T1, Func<T2, Func<T3, TOut>>> Curry<T1, T2, T3, TOut>(this Func<T1, T2, T3, TOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return a => b => c => f(a, b, c);
    }

    public static Func<T1, T2, TOut> Uncurry<T1, T2, TOut>(this Func<T1, Func<T2, TOut>> f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return (a, b) => f(a)(b);
    }

    /// <summary>Partial application: fixes the first argument and returns a function of the rest.</summary>
    public static Func<T2, TOut> Apply<T1, T2, TOut>(this Func<T1, T2, TOut> f, T1 first)
    {
        ArgumentNullException.ThrowIfNull(f);
        return second => f(first, second);
    }

    public static Func<T2, T3, TOut> Apply<T1, T2, T3, TOut>(this Func<T1, T2, T3, TOut> f, T1 first)
    {
        ArgumentNullException.ThrowIfNull(f);
        return (second, third) => f(first, second, third);
    }

    /// <summary>
    /// Memoization: remembers the result for every argument. Only correct for pure functions,
    /// because the function is called at most once per argument.
    /// </summary>
    public static Func<T, TOut> Memoize<T, TOut>(this Func<T, TOut> f)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(f);
        var cache = new ConcurrentDictionary<T, TOut>();
        return x => cache.GetOrAdd(x, f);
    }

    /// <summary>
    /// Remembers only the last argument (compared by reference) and its result.
    /// Perfect for immutable snapshots: as long as the snapshot object is the same, the answer is the same,
    /// and a new snapshot automatically invalidates the cache.
    /// </summary>
    public static Func<T, TOut> MemoizeLast<T, TOut>(this Func<T, TOut> f)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(f);
        Tuple<T, TOut>? last = null;
        return x =>
        {
            var current = Volatile.Read(ref last);
            if (current is not null && ReferenceEquals(current.Item1, x))
            {
                return current.Item2;
            }

            var computed = Tuple.Create(x, f(x));
            Volatile.Write(ref last, computed);
            return computed.Item2;
        };
    }
}

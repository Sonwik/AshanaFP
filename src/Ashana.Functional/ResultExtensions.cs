using System.Collections.Immutable;

namespace Ashana.Functional;

/// <summary>
/// Combinators for <see cref="Result{T}"/>. Each one is a small pure higher-order function;
/// together they let us write a business workflow as one expression.
/// </summary>
public static class ResultExtensions
{
    /// <summary>Functor: transforms the value of a success, leaves a failure untouched.</summary>
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return result.Match(value => Result.Success(map(value)), Result.Failure<TOut>);
    }

    /// <summary>
    /// Monad: chains a step that can itself fail. Unlike <see cref="Map{T,TOut}"/>, the function returns a result,
    /// and Bind flattens it, so we never get a <c>Result&lt;Result&lt;T&gt;&gt;</c>.
    /// </summary>
    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.Match(bind, Result.Failure<TOut>);
    }

    /// <summary>Keeps the value but fails with <paramref name="error"/> if the predicate does not hold.</summary>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Error error) =>
        result.Ensure(predicate, _ => error);

    /// <summary>Like <see cref="Ensure{T}(Result{T}, Func{T, bool}, Error)"/>, but the error can mention the value.</summary>
    public static Result<T> Ensure<T>(this Result<T> result, Func<T, bool> predicate, Func<T, Error> error)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(error);
        return result.Bind(value => predicate(value) ? Result.Success(value) : Result.Failure<T>(error(value)));
    }

    /// <summary>Transforms the error of a failure (for example, to translate a low-level error into a friendly one).</summary>
    public static Result<T> MapError<T>(this Result<T> result, Func<Error, Error> map)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(map);
        return result.Match(Result.Success, error => Result.Failure<T>(map(error)));
    }

    /// <summary>Recovers from a failure with another computation.</summary>
    public static Result<T> OrElse<T>(this Result<T> result, Func<Error, Result<T>> recover)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(recover);
        return result.Match(Result.Success, recover);
    }

    /// <summary>Runs a side effect on success (logging, events) and passes the result on unchanged.</summary>
    public static Result<T> Tap<T>(this Result<T> result, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsSuccess)
        {
            action(result.Value);
        }

        return result;
    }

    /// <summary>Runs a side effect on failure and passes the result on unchanged.</summary>
    public static Result<T> TapError<T>(this Result<T> result, Action<Error> action)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(action);
        if (result.IsFailure)
        {
            action(result.Error);
        }

        return result;
    }

    public static T GetValueOrDefault<T>(this Result<T> result, T fallback)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(value => value, _ => fallback);
    }

    /// <summary>Forgets the value but keeps the success or failure.</summary>
    public static Result<Unit> ToUnit<T>(this Result<T> result) => result.Map(_ => Unit.Value);

    /// <summary>A success becomes <c>Some</c>, a failure becomes <c>None</c>.</summary>
    public static Option<T> ToOption<T>(this Result<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(Option.Some, _ => Option<T>.None);
    }

    // ---- LINQ query syntax: "from x in r1 from y in r2 select ..." --------------------------------

    public static Result<TOut> Select<T, TOut>(this Result<T> result, Func<T, TOut> map) => result.Map(map);

    public static Result<TOut> SelectMany<T, TOut>(this Result<T> result, Func<T, Result<TOut>> bind) => result.Bind(bind);

    public static Result<TOut> SelectMany<T, TMiddle, TOut>(
        this Result<T> result,
        Func<T, Result<TMiddle>> bind,
        Func<T, TMiddle, TOut> project)
    {
        ArgumentNullException.ThrowIfNull(bind);
        ArgumentNullException.ThrowIfNull(project);
        return result.Bind(value => bind(value).Map(middle => project(value, middle)));
    }

    // ---- Collections -------------------------------------------------------------------------------

    /// <summary>
    /// Applies a function that can fail to every element and collects the values.
    /// The first failure wins and the remaining elements are not processed (fail fast).
    /// Written as a fold: the accumulator is itself a result.
    /// </summary>
    public static Result<ImmutableList<TOut>> Traverse<T, TOut>(this IEnumerable<T> source, Func<T, Result<TOut>> f)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(f);
        return source.Aggregate(
            Result.Success(ImmutableList<TOut>.Empty),
            (acc, item) => acc.Bind(list => f(item).Map(list.Add)));
    }

    /// <summary>Turns a list of results into a result of a list.</summary>
    public static Result<ImmutableList<T>> Sequence<T>(this IEnumerable<Result<T>> results) => results.Traverse(r => r);

    /// <summary>
    /// Threads a state through a list of steps that can fail, like <c>Aggregate</c> but stopping at the first failure.
    /// Used, for example, to reserve portions of several dishes one after another.
    /// </summary>
    public static Result<TState> FoldResult<T, TState>(this IEnumerable<T> source, TState seed, Func<TState, T, Result<TState>> step)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(step);
        return source.Aggregate(Result.Success(seed), (acc, item) => acc.Bind(state => step(state, item)));
    }

    // ---- Async -------------------------------------------------------------------------------------

    public static async Task<Result<TOut>> MapAsync<T, TOut>(this Task<Result<T>> task, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(task);
        return (await task.ConfigureAwait(false)).Map(map);
    }

    public static async Task<Result<TOut>> BindAsync<T, TOut>(this Result<T> result, Func<T, Task<Result<TOut>>> bind)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(bind);
        return result.IsSuccess ? await bind(result.Value).ConfigureAwait(false) : Result.Failure<TOut>(result.Error);
    }

    public static async Task<Result<T>> TapAsync<T>(this Task<Result<T>> task, Action<T> action)
    {
        ArgumentNullException.ThrowIfNull(task);
        return (await task.ConfigureAwait(false)).Tap(action);
    }
}

using System.Diagnostics.CodeAnalysis;

namespace Ashana.Functional;

/// <summary>
/// A value that may be missing: <c>Some(value)</c> or <c>None</c> (the Maybe type of Haskell, Option of F#).
/// Used instead of <c>null</c> for "find" operations, so the compiler forces the caller to handle "not found".
/// </summary>
/// <remarks>It is a struct, so <c>default(Option&lt;T&gt;)</c> is <c>None</c> and an option itself can never be null.</remarks>
/// <typeparam name="T">Type of the value.</typeparam>
public readonly struct Option<T> : IEquatable<Option<T>>
{
    private readonly T? _value;

    private Option(T value)
    {
        _value = value;
        IsSome = true;
    }

    public static Option<T> None => default;

    [MemberNotNullWhen(true, nameof(_value))]
    public bool IsSome { get; }

    public bool IsNone => !IsSome;

    internal static Option<T> Some(T value) =>
        value is null ? throw new ArgumentNullException(nameof(value), "Some cannot hold null; use None.") : new(value);

    /// <summary>A plain value becomes <c>Some</c>; <c>null</c> becomes <c>None</c>.</summary>
#pragma warning disable CA2225 // Option.Some is the named alternative.
    public static implicit operator Option<T>(T? value) => value is null ? None : new Option<T>(value);
#pragma warning restore CA2225

    public static bool operator ==(Option<T> left, Option<T> right) => left.Equals(right);

    public static bool operator !=(Option<T> left, Option<T> right) => !left.Equals(right);

    /// <summary>Handles both cases; the only way to get the value out.</summary>
    public TOut Match<TOut>(Func<T, TOut> some, Func<TOut> none)
    {
        ArgumentNullException.ThrowIfNull(some);
        ArgumentNullException.ThrowIfNull(none);
        return IsSome ? some(_value) : none();
    }

    public bool Equals(Option<T> other) =>
        IsSome == other.IsSome && (!IsSome || EqualityComparer<T>.Default.Equals(_value, other._value));

    public override bool Equals(object? obj) => obj is Option<T> other && Equals(other);

    public override int GetHashCode() => IsSome ? HashCode.Combine(true, _value) : 0;

    public override string ToString() => IsSome ? $"Some({_value})" : "None";
}

/// <summary>Factory functions and combinators for <see cref="Option{T}"/>.</summary>
public static class Option
{
    public static Option<T> Some<T>(T value) => Option<T>.Some(value);

    public static Option<T> None<T>() => Option<T>.None;

    /// <summary>Functor: transforms the value if there is one.</summary>
    public static Option<TOut> Map<T, TOut>(this Option<T> option, Func<T, TOut> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return option.Match(value => Some(map(value)), () => Option<TOut>.None);
    }

    /// <summary>Monad: chains a lookup that may itself find nothing.</summary>
    public static Option<TOut> Bind<T, TOut>(this Option<T> option, Func<T, Option<TOut>> bind)
    {
        ArgumentNullException.ThrowIfNull(bind);
        return option.Match(bind, () => Option<TOut>.None);
    }

    /// <summary>Keeps the value only if it satisfies the predicate.</summary>
    public static Option<T> Where<T>(this Option<T> option, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return option.Bind(value => predicate(value) ? Some(value) : Option<T>.None);
    }

    public static T GetOrElse<T>(this Option<T> option, T fallback) => option.Match(value => value, () => fallback);

    public static T GetOrElse<T>(this Option<T> option, Func<T> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        return option.Match(value => value, fallback);
    }

    /// <summary>Bridges to <see cref="Result{T}"/>: "not found" becomes a specific error.</summary>
    public static Result<T> ToResult<T>(this Option<T> option, Error error) =>
        option.Match(Result.Success, () => Result.Failure<T>(error));

    /// <summary>For nullable reference types coming from outside code.</summary>
    public static Option<T> ToOption<T>(this T? value)
        where T : class => value is null ? Option<T>.None : Some(value);

    /// <summary>For nullable value types coming from outside code.</summary>
    public static Option<T> ToOption<T>(this T? value)
        where T : struct => value is { } v ? Some(v) : Option<T>.None;

    /// <summary>The first element matching the predicate, or <c>None</c> (instead of <c>FirstOrDefault</c> returning null).</summary>
    public static Option<T> FirstOrNone<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(predicate);
        foreach (var item in source)
        {
            if (predicate(item))
            {
                return Some(item);
            }
        }

        return Option<T>.None;
    }

    public static Option<T> FirstOrNone<T>(this IEnumerable<T> source) => source.FirstOrNone(_ => true);

    /// <summary>Dictionary lookup that returns an option instead of an out parameter.</summary>
    public static Option<TValue> Find<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        return dictionary.TryGetValue(key, out var value) ? Some(value) : Option<TValue>.None;
    }

    /// <summary>Map and filter in one step: keeps only the <c>Some</c> results (F#'s List.choose).</summary>
    public static IEnumerable<TOut> Choose<T, TOut>(this IEnumerable<T> source, Func<T, Option<TOut>> choose)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(choose);
        foreach (var item in source)
        {
            var chosen = choose(item);
            if (chosen.IsSome)
            {
                yield return chosen.GetOrElse(default(TOut)!);
            }
        }
    }

    // ---- LINQ query syntax ------------------------------------------------------------------------

    public static Option<TOut> Select<T, TOut>(this Option<T> option, Func<T, TOut> map) => option.Map(map);

    public static Option<TOut> SelectMany<T, TMiddle, TOut>(
        this Option<T> option,
        Func<T, Option<TMiddle>> bind,
        Func<T, TMiddle, TOut> project)
    {
        ArgumentNullException.ThrowIfNull(bind);
        ArgumentNullException.ThrowIfNull(project);
        return option.Bind(value => bind(value).Map(middle => project(value, middle)));
    }
}

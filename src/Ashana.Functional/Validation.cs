namespace Ashana.Functional;

/// <summary>
/// One business rule as data: a predicate and the error to report when it does not hold.
/// Rules are plain values, so a list of them can be stored, combined, reused and tested on its own.
/// </summary>
/// <typeparam name="T">The kind of value the rule checks.</typeparam>
public sealed record Rule<T>(Func<T, bool> Holds, Error Error);

/// <summary>Validation built from lists of <see cref="Rule{T}"/>.</summary>
public static class Validation
{
    public static Rule<T> Rule<T>(Func<T, bool> holds, Error error) => new(holds, error);

    /// <summary>Checks the rules in order and returns the value, or the first broken rule's error.</summary>
    public static Result<T> Check<T>(this T value, IEnumerable<Rule<T>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules
            .FirstOrNone(rule => !rule.Holds(value))
            .Match(broken => Result.Failure<T>(broken.Error), () => Result.Success(value));
    }

    /// <summary>All broken rules at once (for forms that show every problem together).</summary>
    public static IReadOnlyList<Error> AllErrors<T>(this T value, IEnumerable<Rule<T>> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        return rules.Where(rule => !rule.Holds(value)).Select(rule => rule.Error).ToList();
    }
}

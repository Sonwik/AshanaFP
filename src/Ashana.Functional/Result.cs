namespace Ashana.Functional;

/// <summary>
/// The outcome of an operation that can fail in an expected way: either a value (success)
/// or an <see cref="Error"/> (failure). It is the <c>Either&lt;Error, T&gt;</c> of functional languages.
/// </summary>
/// <remarks>
/// <para>
/// Results are immutable values. They are combined with <see cref="ResultExtensions.Map{T,TOut}"/> (functor),
/// <see cref="ResultExtensions.Bind{T,TOut}"/> (monad) and LINQ query syntax, so a whole business workflow
/// reads as a pipeline where the first failure short-circuits the rest ("railway-oriented programming").
/// </para>
/// <para>Business failures never throw. Exceptions are reserved for bugs and for things outside our control.</para>
/// </remarks>
/// <typeparam name="T">Type of the value on success.</typeparam>
public sealed record Result<T>
{
    private readonly T? _value;
    private readonly Error? _error;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        _error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>The value of a successful result. Reading it from a failure is a programming error.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result ({_error!.Code}).");

    /// <summary>The error of a failed result. Reading it from a success is a programming error.</summary>
    public Error Error => _error ?? throw new InvalidOperationException("A successful result has no error.");

    internal static Result<T> Ok(T value) => new(value);

    internal static Result<T> Fail(Error error) => new(error);

    /// <summary>Lets a method return a plain value where a <c>Result&lt;T&gt;</c> is expected.</summary>
    public static implicit operator Result<T>(T value) => new(value);

    /// <summary>Lets a method return an <see cref="Error"/> where a <c>Result&lt;T&gt;</c> is expected.</summary>
    public static implicit operator Result<T>(Error error) => new(error);

    /// <summary>The only way to look inside: one function per case, so neither case can be forgotten.</summary>
    public TOut Match<TOut>(Func<T, TOut> success, Func<Error, TOut> failure)
    {
        ArgumentNullException.ThrowIfNull(success);
        ArgumentNullException.ThrowIfNull(failure);
        return IsSuccess ? success(_value!) : failure(_error!);
    }

    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({_error})";
}

/// <summary>Factory functions for <see cref="Result{T}"/>.</summary>
public static class Result
{
    /// <summary>A successful result that carries no value.</summary>
    public static readonly Result<Unit> Unit = Result<Unit>.Ok(Functional.Unit.Value);

    public static Result<T> Success<T>(T value) => Result<T>.Ok(value);

    public static Result<Unit> Success() => Unit;

    public static Result<T> Failure<T>(Error error) => Result<T>.Fail(error);

    /// <summary>
    /// Runs code that may throw (a library call, file access) and turns the exception into a value,
    /// so the rest of the pipeline stays exception-free.
    /// </summary>
    public static Result<T> Try<T>(Func<T> action, Func<Exception, Error> onException)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onException);
        try
        {
            return action();
        }
#pragma warning disable CA1031 // Converting every exception into an Error value is the purpose of this function.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return onException(ex);
        }
    }

    /// <summary>Success when the condition holds, otherwise the given error.</summary>
    public static Result<Unit> Require(bool condition, Error error) => condition ? Unit : error;

    /// <summary>The first failure, or success when every check passed.</summary>
    public static Result<Unit> Combine(params Result<Unit>[] checks)
    {
        ArgumentNullException.ThrowIfNull(checks);
        return checks.FirstOrDefault(c => c.IsFailure) ?? Unit;
    }
}

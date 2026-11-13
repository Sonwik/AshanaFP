namespace Ashana.Functional;

/// <summary>What kind of problem an <see cref="Error"/> describes. The web layer maps it to an HTTP status code.</summary>
public enum ErrorKind
{
    /// <summary>A business rule was violated (HTTP 422).</summary>
    Failure,

    /// <summary>The input is invalid (HTTP 400).</summary>
    Validation,

    /// <summary>The requested thing does not exist (HTTP 404).</summary>
    NotFound,

    /// <summary>The data changed in the meantime: sold out, slot full, edited by someone else (HTTP 409).</summary>
    Conflict,

    /// <summary>The user is not allowed to do this (HTTP 403).</summary>
    Forbidden,
}

/// <summary>
/// An expected, user-facing failure: the "left" side of <see cref="Result{T}"/>.
/// It is an immutable value (a record), so two errors with the same code and message are equal.
/// </summary>
/// <param name="Code">Stable machine-readable code, for example <c>order.sold_out</c>.</param>
/// <param name="Message">Human-readable message that can be shown in the UI.</param>
/// <param name="Kind">Category of the error.</param>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Failure(string code, string message) => new(code, message, ErrorKind.Failure);

    public override string ToString() => $"{Code}: {Message}";
}

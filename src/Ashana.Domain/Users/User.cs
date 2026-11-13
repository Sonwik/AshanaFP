using System.Collections.Immutable;
using System.Net.Mail;
using Ashana.Domain.Common;

namespace Ashana.Domain.Users;

/// <summary>Roles drive role-based access control (RBAC) in the web layer.</summary>
public enum UserRole
{
    /// <summary>Orders food.</summary>
    Student = 0,

    /// <summary>Works in the kitchen: sees the order board and changes statuses.</summary>
    Cook = 1,

    /// <summary>Manages menu, settings and users; can also use the kitchen board.</summary>
    Admin = 2,
}

/// <summary>Answer of the password hasher. The hashing itself is a side effect, done outside the domain.</summary>
public enum PasswordCheck
{
    Failed,
    Success,

    /// <summary>Correct password, but the hash uses old parameters and should be upgraded.</summary>
    SuccessRehashNeeded,
}

/// <summary>
/// The result of a sign-in attempt as a closed discriminated union: exactly one of these cases, nothing else.
/// The web layer handles it with a <c>switch</c> expression, and the compiler sees every case.
/// </summary>
public abstract record LoginOutcome
{
    private LoginOutcome()
    {
    }

    /// <summary>Correct password. <paramref name="User"/> already has the login recorded.</summary>
    public sealed record SignedIn(User User, bool RehashNeeded) : LoginOutcome;

    /// <summary>Wrong password. <paramref name="User"/> has the failed attempt counted (and may be locked now).</summary>
    public sealed record WrongPassword(User User, bool NowLocked) : LoginOutcome;

    /// <summary>Too many failed attempts recently; the password was not even checked.</summary>
    public sealed record LockedOut : LoginOutcome;

    /// <summary>Correct password, but an administrator blocked the account.</summary>
    public sealed record Blocked : LoginOutcome;
}

/// <summary>An account (immutable record). The password is stored only as a salted PBKDF2 hash.</summary>
public sealed record User : IEntity
{
    public const int NameMaxLength = 50;
    public const int MinPasswordLength = 8;

    /// <summary>Password policy as data: at least 8 characters with a letter and a digit, at most 128.</summary>
    public static readonly ImmutableArray<Rule<string>> PasswordRules =
    [
        Validation.Rule<string>(p => p.Length >= MinPasswordLength, UserErrors.PasswordTooWeak),
        Validation.Rule<string>(p => p.Any(char.IsLetter) && p.Any(char.IsDigit), UserErrors.PasswordTooWeak),
        Validation.Rule<string>(p => p.Length <= 128, UserErrors.PasswordTooLong),
    ];

    private User()
    {
    }

    public Guid Id { get; private init; }

    public int Version { get; private init; }

    public string Email { get; private init; } = string.Empty;

    public string DisplayName { get; private init; } = string.Empty;

    public string PasswordHash { get; private init; } = string.Empty;

    public UserRole Role { get; private init; }

    public bool IsActive { get; private init; }

    public DateTimeOffset CreatedAt { get; private init; }

    public DateTimeOffset? LastLoginAt { get; private init; }

    /// <summary>Failed logins in a row; used to lock the account for a while.</summary>
    public int FailedLoginCount { get; private init; }

    public DateTimeOffset? LockedUntil { get; private init; }

    /// <summary>Creates an account. The caller hashes the password before calling (hashing is a side effect).</summary>
    public static Result<User> Create(string email, string displayName, string passwordHash, UserRole role, DateTimeOffset now) =>
        from validEmail in ValidateEmail(email)
        from validName in ValidateName(displayName)
        from hash in Result.Success(passwordHash).Ensure(h => !string.IsNullOrWhiteSpace(h), UserErrors.PasswordTooWeak)
        select new User
        {
            Id = Guid.NewGuid(),
            Email = validEmail,
            DisplayName = validName,
            PasswordHash = hash,
            Role = role,
            IsActive = true,
            CreatedAt = now,
        };

    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    public static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email)
        && email.Length <= 100
        && MailAddress.TryCreate(email, out var parsed)
        && parsed.Address == email
        && email.Contains('.', StringComparison.Ordinal);

    public static Result<string> ValidatePassword(string? password) => (password ?? string.Empty).Check(PasswordRules);

    public bool IsLockedOut(DateTimeOffset now) => LockedUntil is { } until && until > now;

    /// <summary>
    /// The whole sign-in decision as one pure function: given the password check (done outside),
    /// it returns which case happened and the updated user. No clock, no hasher, no storage inside.
    /// </summary>
    public LoginOutcome Login(PasswordCheck check, DateTimeOffset now, int maxAttempts, TimeSpan lockout)
    {
        if (IsLockedOut(now))
        {
            return new LoginOutcome.LockedOut();
        }

        if (check == PasswordCheck.Failed)
        {
            var failed = FailedLoginCount + 1;
            var locked = failed >= maxAttempts;
            var updated = this with
            {
                FailedLoginCount = locked ? 0 : failed,
                LockedUntil = locked ? now + lockout : LockedUntil,
            };
            return new LoginOutcome.WrongPassword(updated, locked);
        }

        return IsActive
            ? new LoginOutcome.SignedIn(this with { FailedLoginCount = 0, LockedUntil = null, LastLoginAt = now }, check == PasswordCheck.SuccessRehashNeeded)
            : new LoginOutcome.Blocked();
    }

    /// <summary>Used when the hashing algorithm gets stronger and the stored hash is upgraded.</summary>
    public User WithPasswordHash(string newHash) => this with { PasswordHash = newHash, Version = Version + 1 };

    public User ChangeRole(UserRole role) => this with { Role = role, Version = Version + 1 };

    public User SetActive(bool isActive) => this with { IsActive = isActive, Version = Version + 1 };

    public Result<User> Rename(string displayName) =>
        ValidateName(displayName).Map(name => this with { DisplayName = name, Version = Version + 1 });

    private static Result<string> ValidateEmail(string? email) =>
        Result.Success(NormalizeEmail(email)).Ensure(IsValidEmail, UserErrors.InvalidEmail);

    private static Result<string> ValidateName(string? name) =>
        Result.Success((name ?? string.Empty).Trim())
            .Ensure(n => n.Length > 0, UserErrors.NameRequired)
            .Ensure(n => n.Length <= NameMaxLength, UserErrors.NameTooLong);
}

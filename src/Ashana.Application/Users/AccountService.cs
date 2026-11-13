using Ashana.Application.Abstractions;
using Ashana.Application.Common;
using Ashana.Application.State;
using Ashana.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Ashana.Application.Users;

public sealed record UserDto(Guid Id, string Email, string DisplayName, UserRole Role, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt)
{
    public static UserDto From(User user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new(user.Id, user.Email, user.DisplayName, user.Role, user.IsActive, user.CreatedAt, user.LastLoginAt);
    }
}

/// <summary>Registration and login checks. Cookies are issued by the web layer; this class only decides "who is it?".</summary>
public sealed class AccountService
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly IStateStore _store;
    private readonly IPasswordHasher _hasher;
    private readonly CanteenClock _clock;
    private readonly ILogger<AccountService> _logger;

    /// <summary>Lazy evaluation: the dummy hash is computed on the first failed lookup, not at startup.</summary>
    private readonly Lazy<string> _dummyHash;

    public AccountService(IStateStore store, IPasswordHasher hasher, CanteenClock clock, ILogger<AccountService> logger)
    {
        _store = store;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
        _dummyHash = new Lazy<string>(() => hasher.Hash("dummy-password-for-timing-1"));
    }

    /// <summary>Self-registration always creates a Student. Staff roles are given by an admin.</summary>
    public async Task<Result<UserDto>> RegisterAsync(string email, string displayName, string password, CancellationToken cancellationToken = default)
    {
        // Hashing is slow and impure, so it happens before the transition, never inside it.
        var hash = User.ValidatePassword(password).Map(_hasher.Hash);
        var now = _clock.Now;

        var registered = await hash.BindAsync(h => _store.UpdateAsync(state => Register(state, email, displayName, h, now), cancellationToken));
        return registered
            .Tap(user => _logger.LogInformation("New student account {UserId}", user.Id))
            .Map(UserDto.From);
    }

    /// <summary>Pure transition: the email must be free; the new user is added to the state.</summary>
    public static Result<Change<User>> Register(CanteenState state, string email, string displayName, string passwordHash, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var normalized = User.NormalizeEmail(email);
        return Result.Require(state.FindUserByEmail(normalized).IsNone, UserErrors.EmailTaken)
            .Bind(_ => User.Create(normalized, displayName, passwordHash, UserRole.Student, now))
            .Map(user => Change.Of(state.With(user), user));
    }

    /// <summary>
    /// Checks email and password. After <see cref="MaxFailedAttempts"/> wrong passwords the account is locked
    /// for <see cref="LockoutDuration"/>, which stops password guessing.
    /// </summary>
    public Task<Result<UserDto>> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default) =>
        _store.Current.FindUserByEmail(User.NormalizeEmail(email)).Match(
            user => SignInAsync(user, password ?? string.Empty, cancellationToken),
            () =>
            {
                // Verify against a dummy hash anyway, so the response time does not reveal whether the email exists.
                _ = _hasher.Verify(_dummyHash.Value, password ?? string.Empty);
                return Task.FromResult<Result<UserDto>>(UserErrors.InvalidCredentials);
            });

    private async Task<Result<UserDto>> SignInAsync(User user, string password, CancellationToken cancellationToken)
    {
        var now = _clock.Now;
        var check = user.IsLockedOut(now) ? PasswordCheck.Failed : _hasher.Verify(user.PasswordHash, password);

        // Exhaustive pattern matching over the LoginOutcome union decides what to save and what to answer.
        switch (user.Login(check, now, MaxFailedAttempts, LockoutDuration))
        {
            case LoginOutcome.SignedIn signedIn:
                var saved = signedIn.RehashNeeded ? signedIn.User.WithPasswordHash(_hasher.Hash(password)) : signedIn.User;
                await SaveQuietlyAsync(user, saved, cancellationToken);
                return UserDto.From(saved);

            case LoginOutcome.WrongPassword wrong:
                await SaveQuietlyAsync(user, wrong.User, cancellationToken);
                _logger.LogWarning("Failed login for user {UserId}", user.Id);
                return wrong.NowLocked ? UserErrors.LockedOut : UserErrors.InvalidCredentials;

            case LoginOutcome.LockedOut:
                return UserErrors.LockedOut;

            case LoginOutcome.Blocked:
                return UserErrors.Inactive;

            default:
                throw new InvalidOperationException("Unknown login outcome.");
        }
    }

    /// <summary>Used to re-check a signed-in user periodically (blocked accounts lose access).</summary>
    public Task<UserDto?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Current.FindUser(userId).Map(UserDto.From).Match<UserDto?>(dto => dto, () => null));

    /// <summary>
    /// Login counters are bookkeeping: the updated record replaces the stored one only if nobody changed it
    /// since we read it (compare-and-swap on the immutable value); otherwise the update is skipped.
    /// Losing one counter update to a parallel login is harmless.
    /// </summary>
    private async Task SaveQuietlyAsync(User original, User updated, CancellationToken cancellationToken)
    {
        var result = await _store.UpdateAsync(
            state => state.FindUser(original.Id)
                .Where(current => current == original)
                .ToResult(UserAdminService.EditConflict)
                .Map(_ => Change.Of(state.With(updated), Unit.Value)),
            cancellationToken);

        result.TapError(error => _logger.LogDebug("Login bookkeeping skipped: {Error}", error));
    }
}

using Ashana.Application.Abstractions;
using Ashana.Application.State;
using Ashana.Domain.Users;
using Microsoft.Extensions.Logging;

namespace Ashana.Application.Users;

/// <summary>Admin screen: list users, change roles, block or unblock accounts.</summary>
public sealed class UserAdminService
{
    /// <summary>Two admins changed the same account at the same moment.</summary>
    public static readonly Error EditConflict = Error.Conflict("user.conflict", "This account was changed by someone else a moment ago. Reload and try again.");

    private readonly IStateStore _store;
    private readonly ILogger<UserAdminService> _logger;

    public UserAdminService(IStateStore store, ILogger<UserAdminService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task<IReadOnlyList<UserDto>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<UserDto>>([.. _store.Current.Users.Values
            .OrderByDescending(u => u.Role)
            .ThenBy(u => u.DisplayName, StringComparer.CurrentCulture)
            .Select(UserDto.From)]);

    public async Task<Result<UserDto>> ChangeRoleAsync(Guid adminId, Guid userId, UserRole role, CancellationToken cancellationToken = default)
    {
        var changed = await _store.UpdateAsync(state => ChangeRole(state, adminId, userId, role), cancellationToken);
        return changed
            .Tap(_ => _logger.LogInformation("Admin {AdminId} changed role of {UserId} to {Role}", adminId, userId, role))
            .Map(UserDto.From);
    }

    public async Task<Result<UserDto>> SetActiveAsync(Guid adminId, Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        var changed = await _store.UpdateAsync(state => SetActive(state, adminId, userId, isActive), cancellationToken);
        return changed
            .Tap(_ => _logger.LogInformation("Admin {AdminId} set {UserId} active={Active}", adminId, userId, isActive))
            .Map(UserDto.From);
    }

    /// <summary>Pure transition. Guards: not yourself, the user exists, and at least one active admin remains.</summary>
    public static Result<Change<User>> ChangeRole(CanteenState state, Guid adminId, Guid userId, UserRole role)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Result.Require(adminId != userId, UserErrors.CannotChangeSelf)
            .Bind(_ => state.FindUser(userId).ToResult(UserErrors.NotFound))
            .Ensure(
                user => !(user.Role == UserRole.Admin && role != UserRole.Admin && user.IsActive && state.CountActiveAdmins() <= 1),
                UserErrors.LastAdmin)
            .Map(user => user.ChangeRole(role))
            .Map(user => Change.Of(state.With(user), user));
    }

    public static Result<Change<User>> SetActive(CanteenState state, Guid adminId, Guid userId, bool isActive)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Result.Require(adminId != userId, UserErrors.CannotChangeSelf)
            .Bind(_ => state.FindUser(userId).ToResult(UserErrors.NotFound))
            .Ensure(user => isActive || user.Role != UserRole.Admin || state.CountActiveAdmins() > 1, UserErrors.LastAdmin)
            .Map(user => user.SetActive(isActive))
            .Map(user => Change.Of(state.With(user), user));
    }
}

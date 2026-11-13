using System.Security.Claims;
using Ashana.Application.Users;
using Ashana.Domain.Users;

namespace Ashana.Web.Security;

/// <summary>Authorization policy names (role-based access control).</summary>
public static class Policies
{
    /// <summary>Cooks and admins: kitchen board, prep list, stop list.</summary>
    public const string Kitchen = "Kitchen";

    /// <summary>Admins only: menu management, settings, users, analytics.</summary>
    public const string Admin = "Admin";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static UserRole? GetRole(this ClaimsPrincipal user) =>
        Enum.TryParse<UserRole>(user.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public static bool IsStaff(this ClaimsPrincipal user) =>
        user.IsInRole(nameof(UserRole.Cook)) || user.IsInRole(nameof(UserRole.Admin));

    /// <summary>The claims stored in the sign-in cookie.</summary>
    public static ClaimsPrincipal ToPrincipal(this UserDto user, string authenticationType)
    {
        ArgumentNullException.ThrowIfNull(user);
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role.ToString()),
        ];
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));
    }
}

/// <summary>The signed-in user, cascaded to every component (null when browsing anonymously).</summary>
public sealed record CurrentUser(Guid Id, string Name, string Email, UserRole Role)
{
    public bool IsStaff => Role is UserRole.Cook or UserRole.Admin;

    public bool IsAdmin => Role == UserRole.Admin;

    public string FirstName => Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Name;

    public static CurrentUser? From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (principal.Identity?.IsAuthenticated != true || principal.GetUserId() is not { } id || principal.GetRole() is not { } role)
        {
            return null;
        }

        return new CurrentUser(id, principal.Identity.Name ?? string.Empty, principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty, role);
    }
}

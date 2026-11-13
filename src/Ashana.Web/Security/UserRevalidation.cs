using System.Globalization;
using System.Security.Claims;
using Ashana.Application.Users;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Ashana.Web.Security;

/// <summary>
/// A Blazor Server circuit can stay open for hours. Every few minutes we re-check the signed-in user:
/// if an admin blocked the account or changed the role, the open tab loses access without a reload.
/// </summary>
internal sealed class RevalidatingUserStateProvider(ILoggerFactory loggerFactory, AccountService accounts)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(2);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        await UserValidation.IsStillValidAsync(authenticationState.User, accounts, cancellationToken);
}

/// <summary>The same check for normal HTTP requests carrying the cookie (API calls, page loads).</summary>
internal static class UserValidation
{
    private const string LastCheckKey = ".ashana.validated";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(2);

    public static async Task<bool> IsStillValidAsync(ClaimsPrincipal principal, AccountService accounts, CancellationToken cancellationToken)
    {
        if (principal.GetUserId() is not { } id)
        {
            return false;
        }

        var user = await accounts.FindAsync(id, cancellationToken);
        return user is { IsActive: true } && user.Role == principal.GetRole();
    }

    public static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var now = DateTimeOffset.UtcNow;
        if (context.Properties.Items.TryGetValue(LastCheckKey, out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var last)
            && now - last < CheckEvery)
        {
            return;
        }

        var accounts = context.HttpContext.RequestServices.GetRequiredService<AccountService>();
        if (context.Principal is null || !await IsStillValidAsync(context.Principal, accounts, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        context.Properties.Items[LastCheckKey] = now.ToString("O", CultureInfo.InvariantCulture);
        context.ShouldRenew = true;
    }
}

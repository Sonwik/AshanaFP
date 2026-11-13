using System.Text.Json.Serialization;
using Ashana.Domain.Users;
using Ashana.Web.Realtime;
using Ashana.Web.Security;
using Ashana.Web.State;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;

namespace Ashana.Web;

/// <summary>Everything the web host adds on top of the application and infrastructure layers.</summary>
public static class WebServices
{
    public static IServiceCollection AddAshanaWeb(this IServiceCollection services, IHostEnvironment environment, IConfiguration configuration)
    {
        services.AddRazorComponents()
            .AddInteractiveServerComponents(options =>
            {
                options.DetailedErrors = environment.IsDevelopment();
                // How long a disconnected tab (for example, a phone that went to sleep) keeps its state on the server.
                options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
            });

        // ---- Authentication: cookie with the user's id, name and role ----
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/account/login";
                options.LogoutPath = "/account/logout";
                options.AccessDeniedPath = "/account/denied";
                options.Cookie.Name = "ashana.auth";
                options.Cookie.HttpOnly = true;                       // JavaScript cannot read it
                options.Cookie.SameSite = SameSiteMode.Lax;           // not sent with cross-site POSTs
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
                options.Events.OnValidatePrincipal = UserValidation.ValidatePrincipalAsync;

                // API callers get 401/403 instead of an HTML redirect.
                options.Events.OnRedirectToLogin = context => RedirectOrStatus(context.HttpContext, context.RedirectUri, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => RedirectOrStatus(context.HttpContext, context.RedirectUri, StatusCodes.Status403Forbidden);
            });

        // ---- Authorization: role-based policies ----
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Kitchen, policy => policy.RequireRole(nameof(UserRole.Cook), nameof(UserRole.Admin)))
            .AddPolicy(Policies.Admin, policy => policy.RequireRole(nameof(UserRole.Admin)));

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, RevalidatingUserStateProvider>();

        // ---- Per-tab state containers (scoped = one per Blazor circuit) ----
        services.AddScoped<CartState>();
        services.AddScoped<LiveMenu>();
        services.AddScoped<CanteenStatus>();
        services.AddScoped<Ticker>();

        // ---- Real time ----
        services.AddSignalR(options => options.EnableDetailedErrors = environment.IsDevelopment())
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddHostedService<HubBroadcaster>();

        // ---- API ----
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddOutputCache();
        services.AddProblemDetails();
        services.AddAshanaRateLimiting(configuration);

        return services;
    }

    private static Task RedirectOrStatus(HttpContext context, string redirectUri, int status)
    {
        if (AshanaApp.IsMachineRequest(context.Request.Path))
        {
            context.Response.StatusCode = status;
            return Task.CompletedTask;
        }

        context.Response.Redirect(redirectUri);
        return Task.CompletedTask;
    }
}

using Ashana.Application;
using Ashana.Infrastructure;
using Ashana.UI;
using Ashana.Web.Components;
using Ashana.Web.Endpoints;
using Ashana.Web.Realtime;
using Ashana.Web.Security;
using Microsoft.AspNetCore.Diagnostics;

namespace Ashana.Web;

/// <summary>
/// Composition root: the only place that knows every layer.
/// Program.cs calls it for the real app; integration tests call it with their own options
/// (in-memory storage, a fixed clock, a random port).
/// </summary>
public static class AshanaApp
{
    public static async Task<WebApplication> BuildAsync(WebApplicationOptions options, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);

        builder.Services
            .AddApplication(builder.Configuration)                              // use cases (Ashana.Application)
            .AddInfrastructure(builder.Configuration, builder.Environment)      // storage, security, background jobs
            .AddAshanaUi()                                                      // component library (Ashana.UI)
            .AddAshanaWeb(builder.Environment, builder.Configuration);          // Blazor, auth, SignalR, API

        configure?.Invoke(builder);

        var app = builder.Build();

        // Load saved data (or create the demo data) before the first request.
        await app.Services.InitializeStorageAsync();

        UsePipeline(app);
        return app;
    }

    private static void UsePipeline(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
            app.UseHttpsRedirection(); // local development and tests run on plain http://localhost
        }

        // Friendly HTML error pages for people. API, SignalR and health-check callers keep plain status codes:
        // the small middleware below switches the feature off for them. (Wrapping the call in UseWhen would
        // break re-execution, because a branch does not get the app's router.)
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.Use(static (context, next) =>
        {
            if (IsMachineRequest(context.Request.Path) && context.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
            {
                statusPages.Enabled = false;
            }

            return next(context);
        });

        app.UseSecurityHeaders();

        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseOutputCache();
        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapHealthChecks("/health");
        app.MapAshanaApi();
        app.MapAccountEndpoints();
        app.MapHub<OrderHub>(OrderHub.Path);
        app.MapRazorComponents<App>()
            // Blazor would send its own "frame-ancestors 'self'" policy; SecurityHeaders sends the full,
            // stricter Content-Security-Policy (including frame-ancestors 'none') for every page instead.
            .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null);
    }

    internal static bool IsMachineRequest(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs") || path.StartsWithSegments("/health");
}

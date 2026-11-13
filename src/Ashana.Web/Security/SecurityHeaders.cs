namespace Ashana.Web.Security;

/// <summary>
/// Adds browser security headers to every response.
/// The Content-Security-Policy only allows scripts from our own origin (no inline scripts),
/// which blocks most cross-site scripting attacks even if some HTML injection slipped through.
/// </summary>
public static class SecurityHeaders
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self'; " +
        "style-src 'self' 'unsafe-inline'; " + // Blazor and our components set style attributes
        "img-src 'self' data: https:; " +       // admins may link dish photos from other sites
        "font-src 'self'; " +
        "connect-src 'self' ws: wss:; " +       // Blazor Server and SignalR use WebSockets
        "manifest-src 'self'; " +
        "worker-src 'self'; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";
                headers.ContentSecurityPolicy = ContentSecurityPolicy; // one policy for every response, never a weaker one

                return Task.CompletedTask;
            });

            await next();
        });
}

using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Ashana.Web;

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimits";

    /// <summary>Sign-in and sign-up attempts per minute per IP address.</summary>
    public int AuthPerMinute { get; set; } = 10;

    /// <summary>JSON API requests per minute per IP address.</summary>
    public int ApiPerMinute { get; set; } = 120;
}

/// <summary>Built-in ASP.NET Core rate limiting: slows down password guessing and API abuse.</summary>
public static class RateLimits
{
    public const string Api = "api";

    public static IServiceCollection AddAshanaRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Sign-in and sign-up forms: a few attempts per minute per IP address.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var path = context.Request.Path;
                var isAuthForm = HttpMethods.IsPost(context.Request.Method)
                    && (path.StartsWithSegments("/account/login") || path.StartsWithSegments("/account/register"));

                return isAuthForm
                    ? RateLimitPartition.GetFixedWindowLimiter("auth:" + ClientKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.AuthPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    })
                    : RateLimitPartition.GetNoLimiter("none");
            });

            // JSON API: sliding window per IP address.
            options.AddPolicy(Api, context => RateLimitPartition.GetSlidingWindowLimiter("api:" + ClientKey(context), _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limits.ApiPerMinute,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            }));

            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
                await context.HttpContext.Response.WriteAsync("Too many requests. Please wait a minute and try again.", token);
            };
        });

        return services;
    }

    private static string ClientKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}

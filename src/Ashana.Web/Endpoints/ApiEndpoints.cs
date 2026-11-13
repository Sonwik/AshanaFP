using System.Security.Claims;
using System.Text;
using Ashana.Application.Analytics;
using Ashana.Application.Common;
using Ashana.Application.Menu;
using Ashana.Application.Orders;
using Ashana.Web.Realtime;
using Ashana.Web.Security;

namespace Ashana.Web.Endpoints;

/// <summary>
/// Small JSON API (minimal APIs). The Blazor UI does not need it (it calls services directly on the server);
/// it exists for the TV board, for monitoring and for a future mobile app. See docs/api.md.
/// </summary>
public static class ApiEndpoints
{
    public static IEndpointRouteBuilder MapAshanaApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireRateLimiting(RateLimits.Api);

        // Anonymous callers (TV, kiosks) share one cached copy for 30 s. The entry is evicted early when
        // stock or dishes change (see HubBroadcaster). Signed-in users bypass the output cache (default policy).
        api.MapGet("/menu", async (MenuService menu, CancellationToken ct) => TypedResults.Ok(await menu.GetMenuAsync(ct)))
            .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(30)).Tag(ApiCacheTags.Menu))
            .WithName("GetMenu");

        api.MapGet("/slots", async (SlotService slots, CancellationToken ct) => TypedResults.Ok(await slots.GetOverviewAsync(ct)))
            .CacheOutput(p => p.Expire(TimeSpan.FromSeconds(10)).Tag(ApiCacheTags.Slots))
            .WithName("GetSlots");

        api.MapGet("/board", async (OrderService orders, CancellationToken ct) => TypedResults.Ok(await orders.GetBoardAsync(ct)))
            .WithName("GetBoard");

        api.MapGet("/orders/mine", async (ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
                TypedResults.Ok(await orders.GetMyOrdersAsync(user.GetUserId()!.Value, 30, ct)))
            .RequireAuthorization()
            .WithName("GetMyOrders");

        api.MapGet("/orders/{id:guid}", async (Guid id, ClaimsPrincipal user, OrderService orders, CancellationToken ct) =>
            {
                var result = await orders.GetAsync(id, user.GetUserId()!.Value, user.IsStaff(), ct);
                return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblem();
            })
            .RequireAuthorization()
            .WithName("GetOrder");

        api.MapGet("/admin/orders.csv", async (DateOnly? from, DateOnly? to, AnalyticsService analytics, CanteenClock clock, CancellationToken ct) =>
            {
                var end = to ?? clock.Today;
                var start = from ?? end.AddDays(-6);
                var csv = await analytics.ExportCsvAsync(start, end, ct);
                // The UTF-8 BOM makes Excel open Cyrillic and ₸ correctly.
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"ashana-orders-{start:yyyy-MM-dd}-{end:yyyy-MM-dd}.csv");
            })
            .RequireAuthorization(Policies.Admin)
            .WithName("ExportOrders");

        return app;
    }

    /// <summary>Maps a domain <see cref="Error"/> to an RFC 7807 problem response.</summary>
    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var status = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status422UnprocessableEntity,
        };

        return Results.Problem(title: error.Message, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}

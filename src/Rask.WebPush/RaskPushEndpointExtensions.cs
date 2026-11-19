using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.WebPush;

/// <summary>
/// The three endpoints a browser needs to subscribe from outside the live socket — a WebAssembly client, a SPA:
/// <c>GET /_rask/push/key</c>, <c>POST /_rask/push/subscribe</c>, <c>POST /_rask/push/unsubscribe</c>.
/// </summary>
/// <remarks>
/// A component on the server host needs none of them: it calls <see cref="Push.Subscribe" /> with what the
/// browser API handed back. <c>RaskApp</c> maps these when the battery is on; a host assembled by hand calls
/// <see cref="MapRaskPush" /> after <c>MapRask</c>.
/// </remarks>
public static class RaskPushEndpointExtensions
{
    internal const string RoutePrefix = "/_rask/push";

    // No Rask.Core reference, like Rask.Storage (#1086): the SPA and meta lanes carry no Core, so the path base
    // arrives through the AppContext data MapRask sets rather than through LiveOptions.
    private const string PathBaseDataName = "Rask.PathBase";

    /// <summary>Maps the subscription endpoints. Anonymous — a visitor subscribes before signing in — and outside the API description.</summary>
    public static IEndpointConventionBuilder MapRaskPush(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (endpoints.ServiceProvider.GetService<IServiceProviderIsService>()?.IsService(typeof(IPush)) != true)
        {
            throw new InvalidOperationException(
                "MapRaskPush() needs the Web Push battery. Register it first: builder.Services.AddRaskWebPush<AppDbContext>();");
        }

        var pathBase = AppContext.GetData(PathBaseDataName) as string ?? "";
        var group = endpoints.MapGroup(pathBase + RoutePrefix);

        // The PUBLIC key only — the browser passes it to pushManager.subscribe as applicationServerKey. Empty
        // until a key pair is configured, so a page can say "push isn't set up yet" instead of erroring.
        group.MapGet("key", static (IPush push) =>
            Results.Json(new PushKey(push.PublicKey ?? ""), PushJson.Default.PushKey));

        group.MapPost("subscribe", SubscribeAsync);

        group.MapPost("unsubscribe", UnsubscribeAsync);

        group.AllowAnonymous();
        group.ExcludeFromDescription();
        return group;
    }

    // Held to the same door as subscribe: who may ask and how often. The endpoint is the browser's own secret, so
    // knowing it is what lets a signed-out browser turn its notifications off.
    private static async Task<IResult> UnsubscribeAsync(
        HttpContext context, IPush push, PushOptions options, SubscribeThrottle throttle)
    {
        if (Refused(context, options, throttle) is { } refused)
        {
            return refused;
        }

        var posted = await context.Request
            .ReadFromJsonAsync(PushJson.Default.PostedSubscription, context.RequestAborted)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(posted?.Endpoint))
        {
            return Results.BadRequest();
        }

        await push.Unsubscribe(posted.Endpoint, context.RequestAborted).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult? Refused(HttpContext context, PushOptions options, SubscribeThrottle throttle)
    {
        if (options.RequireUser && context.User.Identity?.IsAuthenticated != true)
        {
            return Results.Unauthorized();
        }

        return throttle.Admits(context.Connection.RemoteIpAddress) ? null : Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    // Open to anyone, and every new endpoint is a row in the app's own database: so who may ask, how often, and
    // how many rows that can come to are all bounded before anything is read or stored.
    private static async Task<IResult> SubscribeAsync(
        HttpContext context, IPush push, PushOptions options, SubscribeThrottle throttle)
    {
        if (Refused(context, options, throttle) is { } refused)
        {
            return refused;
        }

        var posted = await context.Request
            .ReadFromJsonAsync(PushJson.Default.PostedSubscription, context.RequestAborted)
            .ConfigureAwait(false);

        if (posted?.Subscription is not { } subscription || WebPushSender.Problem(subscription) is not null)
        {
            return Results.BadRequest();
        }

        try
        {
            await push.Subscribe(subscription, context.RequestAborted).ConfigureAwait(false);
        }
        catch (PushSubscriberLimitException)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return Results.NoContent();
    }
}

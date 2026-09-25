using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Wire;

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

        group.MapPost("subscribe", static async (HttpContext context, IPush push) =>
        {
            var subscription = await context.Request
                .ReadFromJsonAsync(PushJson.Default.PushSubscription, context.RequestAborted)
                .ConfigureAwait(false);

            if (subscription is null || WebPushSender.Problem(subscription) is not null)
            {
                return Results.BadRequest();
            }

            await push.Subscribe(subscription, context.RequestAborted).ConfigureAwait(false);
            return Results.NoContent();
        });

        group.MapPost("unsubscribe", static async (HttpContext context, IPush push) =>
        {
            var subscription = await context.Request
                .ReadFromJsonAsync(PushJson.Default.PushSubscription, context.RequestAborted)
                .ConfigureAwait(false);

            if (subscription is null || string.IsNullOrWhiteSpace(subscription.Endpoint))
            {
                return Results.BadRequest();
            }

            await push.Unsubscribe(subscription.Endpoint, context.RequestAborted).ConfigureAwait(false);
            return Results.NoContent();
        });

        group.AllowAnonymous();
        group.ExcludeFromDescription();
        return group;
    }
}

/// <summary>What <c>GET /_rask/push/key</c> answers.</summary>
/// <param name="PublicKey">The VAPID public key, or an empty string until one is configured.</param>
public sealed record PushKey(string PublicKey);

[JsonSerializable(typeof(PushKey))]
[JsonSerializable(typeof(PushSubscription))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class PushJson : JsonSerializerContext;

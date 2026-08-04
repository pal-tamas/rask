using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Rask.WebPush;

namespace Rask.Example.Shop.Features.Push;

/// <summary>The browsers currently subscribed to push, keyed by their endpoint URL.</summary>
public sealed class PushSubscriptionStore
{
    private readonly ConcurrentDictionary<string, PushSubscription> _subscriptions = new(StringComparer.Ordinal);

    public IReadOnlyCollection<PushSubscription> All => _subscriptions.Values.ToArray();

    public void Add(PushSubscription subscription) => _subscriptions[subscription.Endpoint] = subscription;

    public void Remove(string endpoint) => _subscriptions.TryRemove(endpoint, out _);
}

public static class PushEndpoints
{
    public static IEndpointRouteBuilder MapPushSubscriptions(this IEndpointRouteBuilder endpoints)
    {
        // The PUBLIC key only — the browser passes it to pushManager.subscribe as applicationServerKey.
        // The private key signs the request and must never leave the server.
        //
        // Resolved optionally, because Program.cs only registers Web Push once a key pair is
        // configured: before then this answers with an empty key rather than failing the request,
        // so the page can say "push isn't configured yet" instead of erroring.
        endpoints.MapGet("/_push/key", (IServiceProvider services) =>
            Results.Json(new
            {
                publicKey = services.GetService<WebPushOptions>()?.VapidKeys?.PublicKey ?? "",
            }));

        endpoints.MapPost("/_push/subscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
        {
            store.Add(subscription);
            return Results.NoContent();
        });

        endpoints.MapPost("/_push/unsubscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
        {
            store.Remove(subscription.Endpoint);
            return Results.NoContent();
        });

        return endpoints;
    }
}

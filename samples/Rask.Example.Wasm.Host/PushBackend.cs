using System.Collections.Concurrent;
using Rask.WebPush;

namespace Rask.Example.Wasm.Host;

// A minimal Web Push backend that demonstrates Rask.WebPush end to end. This host serves the WASM PWA
// (Rask.Example.Wasm) AND signs/encrypts pushes for it — the complete loop in one app: the page
// subscribes with IWebPush, POSTs the subscription here, and a click on "Send a test push" makes this
// backend deliver a notification through the browser's service worker. Subscription storage is an
// in-memory dictionary on purpose — persisting subscriptions is the host's concern; the library only
// sends.
internal static class PushBackend
{
    // A fixed demo VAPID key pair shared with the WASM client (PwaDemo.DemoVapidPublicKey). Generate
    // your OWN with VapidKeys.Generate() and load it from configuration/secrets in a real app — never
    // ship a checked-in private key.
    private const string DemoPublicKey = "BIl5ANiAgh51-r7wwTyN047Hn3FWTCgLl9cGff1qa5vrft1DmS3jSa-JhTf3PfC6qa_G33YNeNVKT-yyP_6Jqik";
    private const string DemoPrivateKey = "qk3s-Z42Sje9JY0eoXijUkDqUMxkw7T3ZJNeuc16my8";

    public static IServiceCollection AddPushDemo(this IServiceCollection services, IConfiguration configuration)
    {
        // Prefer keys from configuration (e.g. user-secrets), fall back to the demo pair.
        string publicKey = configuration["WebPush:PublicKey"] ?? DemoPublicKey;
        string privateKey = configuration["WebPush:PrivateKey"] ?? DemoPrivateKey;

        services.AddRaskWebPush(o =>
        {
            o.VapidKeys = new VapidKeys(publicKey, privateKey);
            o.Subject = configuration["WebPush:Subject"] ?? "mailto:admin@example.com";
        });
        services.AddSingleton<PushSubscriptionStore>();
        return services;
    }

    public static void MapPushDemo(this WebApplication app)
    {
        // These endpoints are intentionally unauthenticated for a runnable demo. In a real app, put
        // /_push/subscribe and /_push/send behind your auth (a signed-in user owns their subscriptions)
        // — otherwise anyone can register endpoints and trigger sends to them.
        //
        // The public VAPID key the browser passes to IWebPush.SubscribeAsync.
        app.MapGet("/_push/key", (WebPushOptions options) => Results.Json(new { publicKey = options.VapidKeys!.PublicKey }));

        // Store a subscription the browser just produced. The client posts { endpoint, p256dh, auth }.
        // Only accept absolute https endpoints (every real push service uses https) so an arbitrary
        // http/internal URL can't be parked in the store for /_push/send to POST to.
        app.MapPost("/_push/subscribe", (PushSubscription subscription, PushSubscriptionStore store) =>
        {
            if (!Uri.TryCreate(subscription.Endpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme != Uri.UriSchemeHttps)
                return Results.BadRequest("endpoint must be an absolute https URL");

            store.Add(subscription);
            return Results.NoContent();
        });

        app.MapPost("/_push/unsubscribe", (UnsubscribeRequest request, PushSubscriptionStore store) =>
        {
            store.Remove(request.Endpoint);
            return Results.NoContent();
        });

        // Deliver a notification to every stored subscription, evicting any the push service reports
        // as gone (ShouldDelete). Returns a per-subscription summary.
        app.MapPost("/_push/send", async (SendRequest request, IWebPushSender sender, PushSubscriptionStore store, CancellationToken ct) =>
        {
            var message = WebPushMessage.Text(
                string.IsNullOrWhiteSpace(request.Title) ? "Hello from Rask" : request.Title,
                request.Body ?? "Sent from the server with Rask.WebPush.",
                request.Url ?? "/pwa");

            var results = new List<object>();
            foreach (PushSubscription subscription in store.All)
            {
                try
                {
                    WebPushResult result = await sender.SendAsync(subscription, message, ct);
                    if (result.ShouldDelete)
                        store.Remove(subscription.Endpoint);
                    results.Add(new { status = result.Status.ToString(), statusCode = result.StatusCode });
                }
                catch (Exception ex) when (ex is ArgumentException or FormatException)
                {
                    // A malformed stored subscription can never be delivered — drop it and keep going
                    // so one bad entry can't abort the whole broadcast.
                    store.Remove(subscription.Endpoint);
                    results.Add(new { status = "Invalid", statusCode = (int?)null });
                }
            }

            return Results.Json(new { sent = results.Count, results });
        });
    }

    private sealed record UnsubscribeRequest(string Endpoint);

    private sealed record SendRequest(string? Title, string? Body, string? Url);
}

// Thread-safe in-memory subscription store keyed by endpoint. A real app would persist these.
internal sealed class PushSubscriptionStore
{
    // A demo guard so an unauthenticated /_push/subscribe can't grow the dictionary without bound.
    // A real app would persist subscriptions and scope them per signed-in user.
    private const int MaxSubscriptions = 10_000;

    private readonly ConcurrentDictionary<string, PushSubscription> _subscriptions = new();

    public void Add(PushSubscription subscription)
    {
        if (_subscriptions.Count >= MaxSubscriptions && !_subscriptions.ContainsKey(subscription.Endpoint))
            return;
        _subscriptions[subscription.Endpoint] = subscription;
    }

    public void Remove(string endpoint) => _subscriptions.TryRemove(endpoint, out _);

    public IReadOnlyCollection<PushSubscription> All => _subscriptions.Values.ToArray();

    public int Count => _subscriptions.Count;
}

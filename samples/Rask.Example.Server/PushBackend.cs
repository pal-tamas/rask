using System.Collections.Concurrent;
using Rask.WebPush;

namespace Rask.Example.Server;

// A minimal Web Push backend that demonstrates Rask.WebPush end to end FROM A Rask.Server app — the same
// send side that backs the WASM PWA host, now showing that push works on the Server transport too: the
// page subscribes with IWebPush, POSTs the subscription here, and a click on "Send a test push" makes
// this backend sign/encrypt and deliver a notification through the browser's service worker. Subscription
// storage is an in-memory dictionary on purpose — persisting subscriptions is the host's concern; the
// library only sends.
internal static class PushBackend
{
    // A fixed demo VAPID key pair shared with the client (ServerPwaDemo.DemoVapidPublicKey). Generate your
    // OWN with VapidKeys.Generate() and load it from configuration/secrets in a real app — never ship a
    // checked-in private key.
    private const string DemoPublicKey = "BIl5ANiAgh51-r7wwTyN047Hn3FWTCgLl9cGff1qa5vrft1DmS3jSa-JhTf3PfC6qa_G33YNeNVKT-yyP_6Jqik";
    private const string DemoPrivateKey = "qk3s-Z42Sje9JY0eoXijUkDqUMxkw7T3ZJNeuc16my8";

    public static IServiceCollection AddPushDemo(this IServiceCollection services, IConfiguration configuration)
    {
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
        // /_push/subscribe and /_push/send behind your auth (a signed-in user owns their subscriptions).
        app.MapGet("/_push/key", (WebPushOptions options) => Results.Json(new { publicKey = options.VapidKeys!.PublicKey }));

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

        app.MapPost("/_push/send", async (SendRequest request, IWebPushSender sender, PushSubscriptionStore store, CancellationToken ct) =>
        {
            var message = WebPushMessage.Text(
                string.IsNullOrWhiteSpace(request.Title) ? "Hello from Rask" : request.Title,
                request.Body ?? "Sent from the server with Rask.WebPush.",
                request.Url ?? "/server-pwa");

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

// Thread-safe in-memory subscription store keyed by endpoint. A real app would persist these per user.
internal sealed class PushSubscriptionStore
{
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

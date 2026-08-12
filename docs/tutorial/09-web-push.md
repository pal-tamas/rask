# Chapter 9 — Push notifications

> **Goal:** tell a customer their order shipped, on their phone, with the app closed.
> **You'll have run:** `rask new … --push`

Shop can email. Email is right for a receipt and wrong for "your driver is two minutes away". **Web Push**
delivers to a device whose browser isn't even open — and you can send it from your own server, on your own
keys, with no notification service in the middle.

`--push` wires this up. It implies `--pwa`, because a browser will only accept a push subscription through
a **service worker**, which is what the PWA registration installs.

## 1. What the scaffold gave you

```csharp
var vapidPublicKey = builder.Configuration["WebPush:PublicKey"];
var vapidPrivateKey = builder.Configuration["WebPush:PrivateKey"];
if (!string.IsNullOrWhiteSpace(vapidPublicKey) && !string.IsNullOrWhiteSpace(vapidPrivateKey))
{
    builder.Services.AddRaskWebPush(o =>
    {
        o.VapidKeys = new VapidKeys(vapidPublicKey, vapidPrivateKey);
        o.Subject = builder.Configuration["WebPush:Subject"] ?? "mailto:admin@example.com";
    });
}

builder.Services.AddSingleton<PushSubscriptionStore>();
```

…plus `Features/Push/PushSubscriptions.cs`: an in-memory store of subscribed browsers and three endpoints —
`/_push/key`, `/_push/subscribe`, `/_push/unsubscribe` — mapped **before** `UseRask<App>()`, since its
catch-all serves the SPA for anything unmatched.

Note what is and isn't gated. Sending needs keys, so `AddRaskWebPush` is behind the config check — a fresh
scaffold has to start before you've generated any. The store and its endpoints are always registered, so
`/_push/key` answers with an empty key rather than 500-ing, and the UI can say "push isn't set up yet".

## 2. Generate your VAPID keys

VAPID is how a push service knows the message really came from your server. You generate one keypair, once,
and keep it forever — rotating it invalidates every existing subscription.

```csharp
var keys = VapidKeys.Generate();
Console.WriteLine(keys.PublicKey);
Console.WriteLine(keys.PrivateKey);
```

```bash
dotnet user-secrets set "WebPush:PublicKey"  "<public>"
dotnet user-secrets set "WebPush:PrivateKey" "<private>"
dotnet user-secrets set "WebPush:Subject"    "mailto:you@example.com"
```

The **public** key is handed to the browser to subscribe with. The **private** key signs the request and
must never be served — which is why `/_push/key` returns only the public one.

## 3. Subscribe a browser

From a page, ask for permission and subscribe. `IWebPush` (in `Rask.Core.Browser`) wraps the browser API:

```csharp
public sealed class EnablePushButton(IWebPush push, HttpClient http) : Component
{
    protected override Component? Render() =>
        BsButton.OnClickAsync(SubscribeAsync)["Notify me about my orders"];

    private async Task SubscribeAsync()
    {
        var key = await http.GetFromJsonAsync<PushKey>("/_push/key");
        var subscription = await push.SubscribeAsync(key!.PublicKey);
        await http.PostAsJsonAsync("/_push/subscribe", subscription);
    }

    private sealed record PushKey(string PublicKey);
}
```

Browsers only show the permission prompt in response to a real user gesture, so this belongs on a button —
not in `OnMountAsync`. Asking on page load is also how you get permanently denied.

## 4. Send from the outbox handler

Chapter 7's handler already reacts to an order committing. Push is one more thing hanging off it:

```csharp
public sealed class OrderShippedHandler(IWebPushSender sender, PushSubscriptionStore store)
    : INotificationHandler<OrderShipped>
{
    public async Task HandleAsync(OrderShipped notification, CancellationToken cancellationToken)
    {
        var message = WebPushMessage.Text(
            "Your order shipped",
            $"Order {notification.Id} is on its way.",
            url: $"/orders/{notification.Id}");

        foreach (var subscription in store.All)
        {
            var result = await sender.SendAsync(subscription, message, cancellationToken);

            // A subscription that has expired (404/410) will never work again — drop it rather than
            // retrying forever. `ShouldDelete` and `ShouldRetry` map the status to the action, so the
            // typical loop never has to match on `WebPushStatus` directly.
            if (result.ShouldDelete)
            {
                store.Remove(subscription.Endpoint);
            }
        }
    }
}
```

Sending from the **outbox** handler rather than inline is deliberate, for the same reason as the email: the
notification is derived from the order committing, so it should not be able to go missing because the push
service was slow.

> **Server vs WASM.** A Rask **Server** app is installable and push-capable, but not an offline app — it
> renders over a live WebSocket, so offline navigations show `wwwroot/offline.html`. A **WASM** app is a full
> offline PWA. Both send and receive push the same way.

## Verify

- `GET /_push/key` returns your public key as JSON — and returns an empty string before you configure one,
  rather than failing.
- Clicking the subscribe button prompts, then `POST /_push/subscribe` stores the subscription.
- Shipping an order shows a system notification, with the app closed.
- **See it running:** [`samples/Rask.Example.Shop`](../../samples/Rask.Example.Shop) has the endpoints and
  the store wired; real delivery needs a browser push service, so the sample stops at the subscription.

**Learn more:** [Web Push](../webpush.md) · [PWA](../pwa.md) · [browser APIs](../apis/web-push.md)

Next → **[Chapter 10: Watching it run](10-ops.md)**

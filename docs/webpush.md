# Rask.WebPush — server-sent Web Push on your own keys

> **In practice:** [PWA & Web Push](pwa.md#push-notifications-iwebpush) (the browser subscribe side) · [cheat sheet](cheatsheet.md#code-idioms).

`Rask.WebPush` delivers a **Web Push notification from your backend to a subscribed browser** — signed with
your own VAPID keys ([RFC 8292](https://www.rfc-editor.org/rfc/rfc8292)) and payload-encrypted with aes128gcm
([RFC 8291](https://www.rfc-editor.org/rfc/rfc8291)) — with **zero external dependencies**. It's a standalone
server library: no UI, no transport dependency, no reference to the rest of Rask, so it works from a plain
ASP.NET app, a Rask Server app, or the ASP.NET host of a WASM PWA. It pairs with the browser-side
[`IWebPush`](pwa.md#push-notifications-iwebpush) that produces the subscription.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Push.Off());
> ```

## Why send from the server

A browser push subscription is useless until *something* pushes to it. The push service (FCM, Mozilla, …)
will only accept a message that is **VAPID-signed** by the application server that owns the subscription and
**encrypted** for that subscription's keys. `Rask.WebPush` does both and POSTs the result to the endpoint, so
you hand it a stored subscription and a message and get back a typed result telling you whether the
subscription is still good.

## Use

**1. Generate a key pair once** and store it in configuration/secrets (rotating it invalidates every existing
subscription):

```csharp
var keys = VapidKeys.Generate();   // dotnet-run once; persist keys.PublicKey / keys.PrivateKey
```

**2. Register the sender** at startup:

```csharp
builder.Services.AddRaskWebPush(o =>
{
    o.VapidKeys = new VapidKeys(config["WebPush:PublicKey"]!, config["WebPush:PrivateKey"]!);
    o.Subject   = "mailto:admin@example.com";   // a contact the push service can reach; mailto: or https:
});
```

**3. Subscribe on the client** with the **same public key**, and store what it posts up. Hand
`keys.PublicKey` to the browser's [`IWebPush.SubscribeAsync`](pwa.md#push-notifications-iwebpush); the client
POSTs three fields — `Endpoint`, `P256dh`, `Auth` — which you persist as a `PushSubscription`.

**4. Send** a notification, and act on the result:

```csharp
public sealed class Notifier(IWebPushSender sender, ISubscriptionStore store)
{
    public async Task NotifyAsync(Guid userId, string title, string body, string url, CancellationToken ct)
    {
        foreach (var sub in await store.ForUserAsync(userId, ct))
        {
            var result = await sender.SendAsync(sub, WebPushMessage.Text(title, body, url), ct);

            if (result.ShouldDelete) await store.RemoveAsync(sub, ct);   // 404/410 — subscription is gone
            else if (result.ShouldRetry) { /* 429/5xx — enqueue and try later (e.g. via Rask.Jobs) */ }
        }
    }
}
```

## How it works

- **`VapidKeys`** — a base64url P-256 key pair. `PublicKey` is exactly the `applicationServerKey` the browser
  passes to `pushManager.subscribe`, so the **same** string goes to the client's `IWebPush.SubscribeAsync`;
  `PrivateKey` stays secret on the server. `VapidKeys.Generate()` mints a fresh pair.
- **`PushSubscription(Endpoint, P256dh, Auth)`** — the server-side mirror of the browser's subscription: the
  push-service URL plus the client's ECDH public key and auth secret used to encrypt the payload. It's the
  package's own type (no dependency on `Rask.Wasm`/`Rask.Core`); the two sides just agree on the wire shape.
- **`WebPushMessage`** — typed fields (`Title`, `Body`, `Icon`, `Badge`, `Tag`, `Url`) serialize to the JSON
  the default service worker (`rask-sw.js`) expects — `{ title, body, icon, badge, tag, data: { url } }` — so
  a push shows a notification with **no service-worker changes**. `WebPushMessage.Text(title, body?, url?)` is
  the common case; `WebPushMessage.Raw(json)` (or setting `RawPayload`) sends a hand-built payload verbatim for
  your own worker. `Urgency` ([RFC 8030](https://www.rfc-editor.org/rfc/rfc8030) §5.3), `Ttl`, and `Topic` (a
  ≤32-char collapse key) map to the corresponding push-service semantics.
- **`IWebPushSender.SendAsync`** — signs (VAPID), encrypts (aes128gcm), and POSTs via an `IHttpClientFactory`
  typed client, returning a **`WebPushResult`**.
- **`WebPushResult`** — classifies the outcome so the caller knows what to do: `IsSuccess`, `ShouldDelete`
  (HTTP 404/410 — the subscription expired, remove it from your store), `ShouldRetry` (429/5xx — transient,
  retry later), or a permanent failure (usually a VAPID/config error — don't retry).

## Notes

- **Server-side and stateless.** `Rask.WebPush` sends; it does **not** store subscriptions. Persist the
  `PushSubscription` your client posts up in whatever store you like (a `Rask.Data` entity, a table, …), keyed
  by user. A browser holds **one subscription per service-worker registration**, and a user may have several
  devices — store and iterate all of them.
- **Keep the keys stable.** Generate one pair per application and reuse it for the app's lifetime; rotating the
  VAPID keys invalidates every subscription the old public key produced.
- **`Subject` is required** and must be a `mailto:` address or an `https:` URL — the push service uses it to
  reach you if your traffic causes problems. `AddRaskWebPush` validates the options at startup, so a missing
  key or subject fails fast rather than on the first send.
- **Prune expired subscriptions.** Act on `ShouldDelete` so your store doesn't accumulate dead endpoints, and
  consider running sends through [`Rask.Jobs`](jobs.md) so a `ShouldRetry` result is retried durably off the
  request thread.
- **The client half lives elsewhere.** Requesting permission, subscribing, and handling the notification are
  the browser's job — see [PWA & Web Push](pwa.md) for `IWebPush` and the default service worker.

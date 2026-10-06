# Rask.WebPush — Web Push on your own keys and your own database

> **In practice:** [PWA & Web Push](pwa.md#push-notifications-pushmanager) (the browser subscribe side) · [cheat sheet](cheatsheet.md#code-idioms).

`Rask.WebPush` delivers a **Web Push notification from your server to the browsers that asked for one**. It keeps
those browsers in a table on the app's own database, signs each message with your own VAPID keys
([RFC 8292](https://www.rfc-editor.org/rfc/rfc8292)) and encrypts it with aes128gcm
([RFC 8291](https://www.rfc-editor.org/rfc/rfc8291)) — with no external service and no dependency beyond .NET.

> Included in [`Rask.Server`](../README.md) — nothing to install. It is **on**; an app that does without it says so:
>
> ```csharp
> app.Configure(c => c.Push.Off());
> ```

## Send

```csharp
await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way", "/orders/1042"));   // everyone
await Push.Send(WebPushMessage.Text("Your order shipped")).To(order.CustomerId);             // one person's devices
```

Awaiting it returns how many browsers received the push. A subscription the push service says is gone —
HTTP 404 or 410, because the browser unsubscribed or the app was uninstalled — is dropped as the send finds
it, so the table does not fill with dead endpoints. `Push` is reached from anything in progress: a handler, a
render, a request, a job. Inject `IPush` where you would rather.

A send reaches one person's browsers because each subscription remembers who was signed in when it was made.
A person with a phone and a laptop has two rows, and `.To(userId)` reaches both.

## Subscribe

The browser side is MDN's own `PushManager`, from [`Rask.Web`](web-apis.md), reached through the service worker
the page registered. On the **server host**, a component asks the browser and keeps the answer:

```csharp
using System.Buffers.Text;
using Rask.Web;

public sealed partial class NotifyMe : Component
{
    protected override Component? Render() =>
        Ui.Button.OnClick(Subscribe)["Notify me about my orders"];

    private async Task Subscribe()
    {
        await using var worker = await Navigator.ServiceWorker.Ready;              // rask-sw.js, once active
        await using var subscription = await worker.PushManager.Subscribe(new()
        {
            UserVisibleOnly = true,
            ApplicationServerKey = Base64Url.DecodeFromChars(Push.PublicKey),     // the VAPID public key, as bytes
        });
        await Push.Subscribe(await subscription.ToJSON());                       // one row, for the signed-in user
    }
}
```

A **WebAssembly client or a SPA** posts the same `ToJSON()` — MDN's `PushSubscriptionJSON`, what `subscription.toJSON()`
answers in JavaScript too — to the endpoints `RaskApp` maps:

| Endpoint | What it does |
| --- | --- |
| `GET /_rask/push/key` | `{ "publicKey": "…" }` — empty until a key pair is configured |
| `POST /_rask/push/subscribe` | keeps `{ endpoint, expirationTime, keys: { p256dh, auth } }` for the signed-in user, when there is one (the flat `{ endpoint, p256dh, auth }` is read too) |
| `POST /_rask/push/unsubscribe` | forgets it (`{ endpoint }`) |

They are anonymous (a visitor may subscribe before signing in) and outside the API description. Subscribing
again from the same browser renews its row rather than adding one.

Because anyone may post one, a subscription is checked before it is kept: the endpoint has to be an `https`
URL of at most 2048 characters that names a public host — not an IP address, not `localhost` — and the
sender never follows a redirect. A real push service is always all of those. The name is checked again
when a send connects: one that resolves to a loopback, private, link-local or carrier-NAT address is
refused there, so a stored endpoint cannot aim the server's own POST at its network or at a cloud
metadata service. Where the server reaches the internet through an egress proxy (`HTTPS_PROXY`), the
connection is to the proxy and the proxy resolves the name, so that check cannot run — restrict
destinations at the proxy.

The same openness is why the route is bounded:

| `Rask:Push` | Default | What it bounds |
| --- | --- | --- |
| `MaxAnonymousSubscribers` | 10 000 | rows kept for visitors who are not signed in; past it a signed-out subscribe answers **429**. A signed-in user's rows are not counted. |
| `RequireUser` | `false` | turn on in an app that only pushes to its users: a signed-out subscribe answers **401** and no anonymous row is ever written |
| `SendTimeout` | 10 s | how long one send waits on a push service |

One client may also subscribe ten times a minute, then gets 429 (behind a proxy, wire
`UseForwardedHeaders` so that is the visitor's address). A broadcast sends to eight subscribers at a time,
so an endpoint that accepts and never answers costs it one timeout rather than holding every send behind
it. A row is still dropped only when the push service says it is gone.

## Keys

A scaffolded app already has a development pair: `rask new` mints one into `appsettings.Development.json`,
which the scaffold's `.gitignore` keeps out of the repository. Mint one by hand for an app Rask did not
scaffold:

```csharp
var keys = VapidKeys.Generate();   // dotnet-run once; persist keys.PublicKey / keys.PrivateKey
```

```jsonc
// appsettings.json — the contact is not a secret
{ "Rask": { "Push": { "Subject": "mailto:admin@example.com" } } }

// appsettings.Development.json — gitignored, so the private key stays out of the repository
{ "Rask": { "Push": { "VapidKeys": { "PublicKey": "…", "PrivateKey": "…" } } } }
```

**Deployed, the keys come from the environment**, and production should have a pair of its own:

```bash
rask deploy --env "Rask:Push:VapidKeys:PublicKey=<public>" \
            --env "Rask:Push:VapidKeys:PrivateKey=<private>"
```

The table and the subscribe endpoints work before any keys exist — a fresh clone of a scaffolded app has
none. Sending is what needs them, and a send without them fails naming the settings. Never regenerate a live
pair: every existing subscription was made against the old public key and stops working.

## Test

```csharp
using var push = Push.Fake();

await orders.Ship(order);

push.Sent().To(order.CustomerId).WithTitle("Order shipped").Once();
push.Sent().ToEveryone().None();
```

The fake stands in for this test's flow alone, so parallel tests never see each other's pushes.
`push.Subscribed()` answers the same way about the subscriptions a test kept.

## The message

`WebPushMessage.Text(title, body?, url?)` is the common case. Its fields — `Title`, `Body`, `Icon`, `Badge`,
`Tag`, `Url` — serialize to the JSON the default service worker (`rask-sw.js`) shows, so a push appears as a
notification with no service-worker code. `WebPushMessage.Raw(json)` sends a payload of your own verbatim, for
your own worker. `Urgency`, `Ttl` and `Topic` (a collapse key of up to 32 characters) map to the push
service's own semantics ([RFC 8030](https://www.rfc-editor.org/rfc/rfc8030)).

## One subscription, by hand

`Push.Send(subscription, message)` sends to a single `PushSubscription` and returns the `WebPushResult` —
`IsSuccess`, `ShouldDelete` (the subscription is gone) or `ShouldRetry` (429/5xx; send it through
[`Rask.Jobs`](jobs.md) to retry durably). `PushSubscription` lives in `Rask.Wire`: the endpoint and the two keys
the sender encrypts for, flat.

An app that keeps its subscriptions somewhere of its own registers the sender alone and uses `IWebPush`:

```csharp
builder.Services.AddRaskWebPush();   // refuses to start without a key pair and a Subject
```

A host assembled without `RaskApp` registers the whole battery and maps its endpoints itself:

```csharp
builder.Services.AddRaskWebPush<AppDbContext>();   // plus modelBuilder.AddRaskWebPush() in OnModelCreating
app.MapRaskPush();                                 // after MapRask
```

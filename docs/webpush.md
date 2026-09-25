# Rask.WebPush — Web Push on your own keys and your own database

> **In practice:** [PWA & Web Push](pwa.md#push-notifications-iwebpush) (the browser subscribe side) · [cheat sheet](cheatsheet.md#code-idioms).

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

On the **server host**, a component asks the browser and keeps the answer:

```csharp
public sealed partial class NotifyMe(IWebPush browser) : Component
{
    protected override Component? Render() =>
        Ui.Button.OnClick(Subscribe)["Notify me about my orders"];

    private async Task Subscribe()
    {
        var subscription = await browser.SubscribeAsync(Push.PublicKey!);   // the browser API
        await Push.Subscribe(subscription);                                // one row, for the signed-in user
    }
}
```

A **WebAssembly client or a SPA** posts the same record to the endpoints `RaskApp` maps:

| Endpoint | What it does |
| --- | --- |
| `GET /_rask/push/key` | `{ "publicKey": "…" }` — empty until a key pair is configured |
| `POST /_rask/push/subscribe` | keeps `{ endpoint, p256dh, auth }` for the signed-in user, when there is one |
| `POST /_rask/push/unsubscribe` | forgets it |

They are anonymous (a visitor may subscribe before signing in) and outside the API description. Subscribing
again from the same browser renews its row rather than adding one.

## Keys

A scaffolded app already has a development pair: `rask new` mints one into `appsettings.Development.json`,
which the scaffold's `.gitignore` keeps out of the repository. Mint one by hand for an app Rask did not
scaffold:

```csharp
var keys = VapidKeys.Generate();   // dotnet-run once; persist keys.PublicKey / keys.PrivateKey
```

```jsonc
// appsettings.json — the contact is not a secret
{ "Rask": { "WebPush": { "Subject": "mailto:admin@example.com" } } }

// appsettings.Development.json — gitignored, so the private key stays out of the repository
{ "Rask": { "WebPush": { "VapidKeys": { "PublicKey": "…", "PrivateKey": "…" } } } }
```

**Deployed, the keys come from the environment**, and production should have a pair of its own:

```bash
rask deploy --env "Rask__WebPush__VapidKeys__PublicKey=<public>" \
            --env "Rask__WebPush__VapidKeys__PrivateKey=<private>"
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
[`Rask.Jobs`](jobs.md) to retry durably). `PushSubscription` lives in `Rask.Wire`, one record for the browser
API and the server alike.

An app that keeps its subscriptions somewhere of its own registers the sender alone and uses `IWebPush`:

```csharp
builder.Services.AddRaskWebPush();   // refuses to start without a key pair and a Subject
```

A host assembled without `RaskApp` registers the whole battery and maps its endpoints itself:

```csharp
builder.Services.AddRaskWebPush<AppDbContext>();   // plus modelBuilder.AddRaskWebPush() in OnModelCreating
app.MapRaskPush();                                 // after MapRask
```

# Rask.WebPush

**Web Push from your own server, on your own keys and your own database.** The browsers that subscribed are
kept in a table on the app's database, and one line reaches them:

```csharp
await Push.Send(WebPushMessage.Text("Order shipped", "#1042 is on its way", "/orders/1042"));
await Push.Send(WebPushMessage.Text("Your order shipped")).To(userId);   // one person's devices
```

A subscription the push service says is gone (404/410) is dropped as the send finds it. Messages are
signed with [VAPID](https://www.rfc-editor.org/rfc/rfc8292) and encrypted with
[RFC 8291](https://www.rfc-editor.org/rfc/rfc8291) `aes128gcm`, using in-box `System.Security.Cryptography`.

Included in `Rask.Server` and on by default — `app.Configure(c => c.Push.Off())` to do without it.

## Subscribe

On the server host a component keeps what the browser API hands back:

```csharp
var subscription = await push.SubscribeAsync(Push.PublicKey!);   // IWebPush, the browser API
await Push.Subscribe(subscription);                             // the signed-in user's, when there is one
```

A WebAssembly client or a SPA posts it to the endpoints `RaskApp` maps: `GET /_rask/push/key`,
`POST /_rask/push/subscribe`, `POST /_rask/push/unsubscribe`.

## Keys

The key pair and the contact come from `Rask:WebPush`. A `rask new` app already has a development pair in
its gitignored `appsettings.Development.json`; deployed, both keys come from the environment
(`Rask__WebPush__VapidKeys__PublicKey`, `…__PrivateKey`). The table and the subscribe endpoints work without
keys; sending names the settings it is missing.

## Test

```csharp
using var push = Push.Fake();

await orders.Ship(order);

push.Sent().To(order.CustomerId).WithTitle("Order shipped").Once();
```

## By hand

```csharp
builder.Services.AddRaskWebPush<AppDbContext>();   // and modelBuilder.AddRaskWebPush() in OnModelCreating
app.MapRaskPush();                                 // after MapRask
```

`AddRaskWebPush()` without a context registers the sender alone — `IWebPush.Send(subscription, message)` —
for an app that keeps its subscriptions somewhere of its own.

Full documentation: <https://rask.sh/docs/guides/webpush>

# Chapter 9 — Push notifications

> **Goal:** tell a customer their order shipped, on their phone, with the app closed.
> **You'll have run:** `rask new Shop` — Web Push is standard

Shop can email. Email is right for a receipt and wrong for "your driver is two minutes away". **Web Push**
delivers to a device whose browser isn't even open — and you can send it from your own server, on your own
keys, with no notification service in the middle.

Web Push is a battery, on like the rest. It rides on the installable PWA, because a browser will only accept
a push subscription through a **service worker**, which the PWA battery serves — so turning the PWA off
(`c.Pwa.Off()`, or `--no-pwa`) takes push with it.

## 1. What the battery gives you

Nothing to register, and nothing in `Program.cs`. The battery keeps every browser that subscribed in a
`PushSubscriber` table in `app.db` — mapped by `RaskAppDbContext`, created by the first migration — and it
remembers who was signed in when each one subscribed, so you can reach one person's phone and laptop at once.
It reads `Rask:WebPush` from `appsettings.json`: the key pair, and the contact address the scaffold put there:

```jsonc
"Rask": {
  "WebPush": {
    "Subject": "mailto:admin@example.com"
  }
}
```

It also maps three endpoints, for a WebAssembly client or a SPA that can't call C# on the server —
`GET /_rask/push/key`, `POST /_rask/push/subscribe` and `POST /_rask/push/unsubscribe`. This app renders on
the server, so it won't need them.

Note what is and isn't gated. Sending needs keys; the table and the endpoints don't. An app whose keys are
missing still starts, `/_rask/push/key` answers with an empty key rather than a 500, and a send is what fails,
naming the settings to set.

## 2. Your VAPID keys

VAPID is how a push service knows the message really came from your server. One keypair, once, kept forever
— rotating it invalidates every existing subscription.

**You already have one.** `rask new` generated a development pair for this app and wrote it to
`appsettings.Development.json`, which is gitignored so the private key stays out of your repository. Open
the file if you want to see it; otherwise there is nothing to do here.

Lost it, or working in an app Rask didn't scaffold? Mint another:

```csharp
var keys = VapidKeys.Generate();
Console.WriteLine(keys.PublicKey);
Console.WriteLine(keys.PrivateKey);
```

```jsonc
// appsettings.Development.json
{
  "Rask": { "WebPush": { "VapidKeys": { "PublicKey": "…", "PrivateKey": "…" } } }
}
```

Change `Rask:WebPush:Subject` in `appsettings.json` to an address you read — it is not a secret. When you
deploy, the keys come from the environment instead, and production should have a pair of its own:

```bash
rask deploy --env "Rask__WebPush__VapidKeys__PublicKey=<public>" \
            --env "Rask__WebPush__VapidKeys__PrivateKey=<private>"
```

The **public** key is handed to the browser to subscribe with. The **private** key signs the request and
must never be served — which is why `Push.PublicKey` and `/_rask/push/key` hand out only the public one.

## 3. Subscribe a browser

From a page, ask the browser, then keep the answer. `IWebPush` wraps the browser's side; `Push` is the
battery's:

```csharp
using Rask.WebPush; // Push

namespace Shop.Features.Orders;

public sealed partial class NotifyMe(IWebPush browser) : Component
{
    protected override Component? Render() =>
        Ui.Button.OnClick(Subscribe)["Notify me about my orders"];

    private async Task Subscribe()
    {
        var subscription = await browser.SubscribeAsync(Push.PublicKey!);   // the browser's permission prompt
        await Push.Subscribe(subscription);                                // one row, for the signed-in user
    }
}
```

Browsers only show the permission prompt in response to a real user gesture, so this belongs on a button —
not in `OnMount`. Asking on page load is also how you get permanently denied.

## 4. Know whose order it is

To tell *the customer*, the order has to say who that is — and chapter 3's `Order` doesn't. Give it one more
property, filled in by `Place` from whoever is signed in:

```csharp
public Guid? CustomerId { get; private set; }
```

…and in `Place`, beside the other fields: `CustomerId = Current.UserId`. `Current.UserId` is the signed-in
user, or `null` for a visitor. A new column is a new migration:

```bash
rask db add AddOrderCustomer
rask db update
```

## 5. Send from the outbox handler

Chapter 7's handler already reacts to an order being placed. Shipping has the same shape: give `Order` a
`Ship()` method that raises an `OrderShipped` event — an `IOutboxEvent`, like `OrderPlaced` — and call it with
`Order.Update(id, o => o.Ship())`, which saves the change and the event in one transaction. Push is then one more handler hanging
off that event:

```csharp
using Rask.WebPush;

namespace Shop.Features.Orders;

public sealed class OrderShippedHandler : INotificationHandler<OrderShipped>
{
    public async Task Handle(OrderShipped notification)
    {
        var order = await Order.Read.Where(o => o.Id == notification.Id).FirstOrDefaultAsync(Current.Cancellation);
        if (order?.CustomerId is not { } customer) return;

        await Push.Send(WebPushMessage.Text("Order shipped", $"Order {order.Id} is on its way.", $"/orders/{order.Id}"))
            .To(customer);
    }
}
```

`.To(customer)` reaches every browser that customer subscribed from; leave it off and the push goes to
everyone. A subscription the push service says is gone — the browser unsubscribed, the app was uninstalled — is
dropped as the send finds it, so the table never fills with dead endpoints.

Sending from the **outbox** handler rather than inline is deliberate, for the same reason as the email: the
notification is derived from the order committing, so it should not be able to go missing because the push
service was slow.

## 6. Test it

No browser and no push service: `Push.Fake()` stands in for the battery for this test alone, and records
what would have been sent.

```csharp
using var push = Push.Fake();

await new OrderShippedHandler().Handle(new OrderShipped(order.Id));

push.Sent().To(customerId).WithTitle("Order shipped").Once();
```

> **Server vs WASM.** A Rask **Server** app is installable and push-capable, but not an offline app — it
> renders over a live WebSocket, so offline navigations show `wwwroot/offline.html`. A **WASM** app is a full
> offline PWA. Both send and receive push the same way.

## Verify

- `GET /_rask/push/key` returns your public key as JSON — and an empty string before you configure one,
  rather than failing.
- Clicking **Notify me** prompts, then a row appears in the `PushSubscriber` table, with your user id on it.
- Shipping an order shows a system notification, with the app closed.
- Real delivery needs a browser push service, so a local run can only take you as far as the subscription.

**Learn more:** [Web Push](../webpush.md) · [PWA](../pwa.md) · [browser APIs](../apis/web-push.md)

Next → **[Chapter 10: Watching it run](10-ops.md)**

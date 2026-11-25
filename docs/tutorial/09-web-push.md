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
It reads `Rask:Push` from `appsettings.json`: the key pair, and the contact address the scaffold put there:

```jsonc
"Rask": {
  "Push": {
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
  "Rask": { "Push": { "VapidKeys": { "PublicKey": "…", "PrivateKey": "…" } } }
}
```

Change `Rask:Push:Subject` in `appsettings.json` to an address you read — it is not a secret. When you
deploy, the keys come from the environment instead, and production should have a pair of its own:

```bash
rask deploy --env "Rask:Push:VapidKeys:PublicKey=<public>" \
            --env "Rask:Push:VapidKeys:PrivateKey=<private>"
```

The **public** key is handed to the browser to subscribe with. The **private** key signs the request and
must never be served — which is why `Push.PublicKey` and `/_rask/push/key` hand out only the public one.

## 3. Subscribe a browser

From a page, ask the browser, then keep the answer. The browser's side is MDN's own `PushManager`, from
`Rask.Web` (every app imports it already); `Push` is the battery's:

```csharp
using System.Buffers.Text; // Base64Url
using Rask.WebPush;        // Push

namespace Shop.Features.Orders;

public sealed partial class NotifyMe : Component
{
    protected override Component? Render() =>
        Ui.Button.OnClick(Subscribe)["Notify me about my orders"];

    private async Task Subscribe()
    {
        // The service worker the PWA battery registered — PushManager hangs off it.
        await using var worker = await Navigator.ServiceWorker.Ready;
        await using var subscription = await worker.PushManager.Subscribe(new()   // the browser's permission prompt
        {
            UserVisibleOnly = true,                                               // every push shows a notification
            ApplicationServerKey = Base64Url.DecodeFromChars(Push.PublicKey),     // your public key, as bytes
        });
        await Push.Subscribe(await subscription.ToJSON());                        // one row, for the signed-in user
    }
}
```

`worker` and `subscription` are objects the browser holds for you, so `await using` lets them go when the click
is done.

Browsers only show the permission prompt in response to a real user gesture, so this belongs on a button —
not in `OnMount`. Asking on page load is also how you get permanently denied.

## 4. Know whose order it is

To tell *the customer*, the order has to say who placed it — and chapter 3's `Order` doesn't. It takes the signed-in
account when it is placed, and says when it ships. Here is `Features/Orders/Order.cs` with both:

```csharp
namespace Shop.Features.Orders;

public readonly partial record struct OrderId(Guid Value);

// The signed-in account an order was placed by: the user's Guid, typed so it is never mistaken for another id.
public readonly partial record struct AccountId(Guid Value)
{
    public static AccountId? Of(Guid? user) => user is { } id ? new AccountId(id) : null;
}

public sealed partial record CustomerEmail(string Value) : Email(Value);

public sealed partial record TrackingNumber(string Value) : Text(Value, MaxLength: 40, Upper: true);

public sealed partial record GiftNote(string Value) : Text(Value, MaxLength: 300);

public sealed partial record ShippingAddress(string Street, string City, string PostCode) : Value
{
    protected override IEnumerable<Problem> Rules()
    {
        if (PostCode.Trim().Length is < 3 or > 10)
        {
            yield return Problem.At(PostCode, "A post code is 3 to 10 characters.");
        }
    }
}

public enum OrderStatus
{
    Placed,
    Shipped,
}

public sealed partial class Order : Aggregate<OrderId, Timestamps, Versioned, Deletable>
{
    public CustomerEmail Customer { get; private set; } = null!;

    public ShippingAddress ShipTo { get; private set; } = null!;

    public GiftNote? Gift { get; private set; }

    public OrderStatus Status { get; private set; }

    public TrackingNumber? Tracking { get; private set; }

    public AccountId? Account { get; private set; }

    public Children<OrderLine, OrderLineSpec> Lines { get; } = new();

    private static Order Place(CustomerEmail customer, ShippingAddress shipTo, GiftNote? gift, IReadOnlyList<OrderLineSpec> lines)
    {
        var order = new Order { Id = OrderId.New(), Customer = customer, ShipTo = shipTo, Gift = gift, Account = AccountId.Of(Current.UserId) };
        order.Take(lines);
        order.Raise(new OrderPlaced(order.Id, [.. lines.Select(l => new OrderedItem(l.Product, l.Quantity.Value))]));
        return order;
    }

    public void Change(ShippingAddress shipTo, Change<GiftNote?> gift, IReadOnlyList<OrderLineSpec> lines)
    {
        Refuse.Unless(Status == OrderStatus.Placed, "A shipped order cannot change.");
        (ShipTo, Gift) = (shipTo, gift.Applied(Gift));
        Take(lines);
    }

    public void Ship(TrackingNumber tracking)
    {
        Refuse.Unless(Status == OrderStatus.Placed, "This order has already shipped.");
        (Status, Tracking) = (OrderStatus.Shipped, tracking);
        Raise(new OrderShipped(Id));
    }

    private void Take(IReadOnlyList<OrderLineSpec> lines)
    {
        Refuse.When(lines.Count == 0, () => lines, "An order has at least one line.");
        _ = Lines.Sync(lines, l => l.Id, OrderLine.For, static (line, spec) => line.Take(spec));
    }

#pragma warning disable S1144 // found by convention: every delete asks it
    private IEnumerable<Problem> ValidateDelete()
    {
        if (Status == OrderStatus.Shipped)
        {
            yield return Problem.Whole("A shipped order stays on the books.");
        }
    }
#pragma warning restore S1144

    public bool ReceiptSent { get; private set; }

    public void MarkReceiptSent() => ReceiptSent = true;
}
```

- **`AccountId`** is the signed-in user's `Guid`, typed like every other id, so it can never be handed where an
  `OrderId` belongs. `Current.UserId` is the signed-in user, or `null` for nobody, and the factory reads it the way it
  reads its parameters.
- **`Ship` raises `OrderShipped`.** What happened is said once, by the aggregate, in the operation that made it happen.

Add the event beside `OrderPlaced`, in `Features/Orders/OrderEvents.cs`:

```csharp
using Shop.Features.Products;

namespace Shop.Features.Orders;

[LocalOnly]
public sealed record OrderPlaced(OrderId Order, IReadOnlyList<OrderedItem> Items) : IEvent;

public sealed record OrderedItem(ProductId Product, long Quantity);

[LocalOnly]
public sealed record OrderShipped(OrderId Order) : IEvent;
```

A new column is a new migration:

```bash
rask db add AddOrderAccount   # applied the next time the app starts
```

## 5. Send from the outbox

Push is one more durable handler of that event, `Features/Orders/OrderShippedHandler.cs`:

```csharp
using Rask.WebPush;

namespace Shop.Features.Orders;

public sealed class OrderShippedHandler : IDurableHandler<OrderShipped>
{
    public async Task Handle(OrderShipped e)
    {
        var order = await Order.Where(o => o.Id == e.Order).FirstOrDefault();
        if (order?.Account is not { } account)
        {
            return;   // placed by someone not signed in: nobody to tell
        }

        await Push.Send(WebPushMessage.Text("Order shipped", $"Order {order.Id} is on its way.", "/orders"))
            .To(account);
    }
}
```

`.To(account)` reaches every browser that account subscribed from; leave it off and the push goes to everyone. A
subscription the push service says is gone — the browser unsubscribed, the app was uninstalled — is dropped as the
send finds it, so the table never fills with dead endpoints.

It runs from the **outbox**, for the same reason as the receipt: the notification is derived from the order
committing as shipped, so it should not go missing because the push service was slow. On the read face the account
is its `Guid`, which is what `Push.Send(…).To(…)` takes.

## 6. Prove it

No browser and no push service: `Push.Fake()` stands in for the battery for this test alone, and records what would
have been sent. `Shop.Tests/Features/Orders/OrderShippedTests.cs`:

```csharp
using Rask.WebPush;
using Shop.Features.Orders;

namespace Shop.Tests.Features.Orders;

public sealed class OrderShippedTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Shipping_an_order_tells_the_account_that_placed_it()
    {
        await using var database = await TestDatabase.Start(cancellationToken: Ct);
        using var batteries = new FakeBatteries();
        using var push = Push.Fake();
        using var customer = Signed.As("shopper");
        var order = await Test.Make<Order>();

        await Order.Ship(order.Id, order.Version, TrackingNumber.Create("1Z999"), Ct);

        push.Sent().To(Current.UserId!.Value).WithTitle("Order shipped").Once();
    }
}
```

The test signs in, so the order made for it is that account's; shipping it through the entry runs the durable handler
in line, and the fake saw the push.

> **Server vs WASM.** A Rask **Server** app is installable and push-capable, but not an offline app — it
> renders over a live WebSocket, so offline navigations show `wwwroot/offline.html`. A **WASM** app is a full
> offline PWA. Both send and receive push the same way.

## Verify

- `GET /_rask/push/key` returns your public key as JSON — and an empty string before you configure one,
  rather than failing.
- Clicking **Notify me** prompts, then a row appears in the `PushSubscriber` table, with your user id on it.
- Shipping an order from its editor shows a system notification, with the app closed.
- `dotnet test` passes.
- Real delivery needs a browser push service, so a local run can only take you as far as the subscription.

**Learn more:** [Web Push](../webpush.md) · [PWA](../pwa.md) · [MDN's web APIs](../web-apis.md)

Next → **[Chapter 10: Watching it run](10-ops.md)**

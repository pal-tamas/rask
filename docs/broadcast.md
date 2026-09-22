# Broadcast — push a change to every open page

`IBroadcast` publishes a message on a topic, and every component subscribed to that topic — in every open session —
runs its handler and re-renders where it is. A new order appears on every admin's open order list the moment it is
placed, with no refresh, no polling and no infrastructure beyond the live connection each page already has.

> Included in `Rask.Core`: registered by the server host and the browser-WASM host, and injected like any other
> service.

## The whole of it

Declare a topic once, as a static field. It names the channel and fixes the message type:

```csharp
using Rask.Core.Messaging;

public static class Topics
{
    public static readonly Topic<OrderPlaced> Orders = new("orders");
}

public sealed record OrderPlaced(Guid Id, string Customer, decimal Total);
```

Publish from anywhere that can inject `IBroadcast` — an event handler, a CQRS handler, a background job:

```csharp
public sealed class PlaceOrderHandler(IBroadcast broadcast, IDbContextFactory<AppDbContext> contexts)
    : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Guid> HandleAsync(PlaceOrder command, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var order = Order.Create(command.Customer, command.Total);
        db.Add(order);
        await db.SaveChangesAsync(ct);

        await broadcast.PublishAsync(Topics.Orders, new OrderPlaced(order.Id, order.Customer, order.Total), ct);
        return order.Id;
    }
}
```

Subscribe in `OnMount`:

```csharp
public sealed partial class OrderList(IBroadcast broadcast) : Component
{
    private readonly List<OrderPlaced> _orders = [];

    protected override async Task OnMount() =>
        broadcast.Subscribe(this, Topics.Orders, order => _orders.Insert(0, order));

    protected override Component? Render() =>
        Ul[_orders.Select(o => Li.Key(o.Id)[$"{o.Customer} — {o.Total:C}"])];
}
```

That is all. There is no subscription to dispose and no `StateHasChanged` to call:

- **The subscription lives as long as the component.** When `OrderList` unmounts — the visitor navigates away, the
  tab closes, the session ends — the subscription goes with it. It is tied to the component's lifetime, not to a
  handler's cancellation token, so subscribing from inside an event handler does not end at that handler's timeout.
- **The handler runs like an event handler.** The component re-renders after it, and every state change it made
  paints in one frame. An `async` handler works too: `broadcast.Subscribe(this, Topics.Orders, async order => …)`.

Try it: the button publishes, and the two boards — which know nothing about the button or each other — each receive
every order.

<!-- demo:broadcast-orders -->

## How a message is delivered

- **In order with the page's own events.** Each session runs a message's handlers on its dispatch queue, behind the
  clicks and keystrokes already waiting there, under the same lock. A handler never races an event handler over the
  component's state.
- **Once per session.** Every subscriber a page has — three components on one page, say — handles the message in the
  same dispatch, and the page renders once.
- **Without holding the publisher up.** `PublishAsync` returns as soon as the message is queued for every subscribed
  session; it does not wait for their renders. A slow page cannot slow the code that published.
- **Skipped where the queue is full.** A session already holding `RaskServerOptions.MaxPendingHandlers` dispatches
  does not get this message, and Rask logs a warning. Its connection is left alone: the backlog is not the visitor's
  doing.
- **Kept for a page that is reconnecting.** A session whose socket has dropped still applies the message, and shows
  the result on the catch-up render when the browser reconnects.
- **Faults stay with the subscriber.** A handler that throws is logged, and the remaining subscribers still run.

## What it is not

- **Not a queue.** Delivery is *at most once*, to the subscribers that exist when the message is published. A
  component that mounts afterwards does not see earlier messages, and nothing is replayed. When a page needs the
  current state rather than the latest change, load it in `OnMount` and subscribe for what happens next.
- **Not across servers, unless you ask.** A message reaches the sessions this process holds, as a live object that is
  never serialized, so a topic can carry any type, including ones that cannot be. Behind a load balancer, a topic
  that should reach every instance's visitors opts in, [below](#across-servers).
- **One tab in a WebAssembly app.** In a browser-WASM app the whole app is one session, so a broadcast connects the
  components of that tab — as in the demo above — and never leaves the browser.

## Across servers

Behind a load balancer, each instance holds its own visitors' sessions, so a publish on one instance reaches only
the pages open on that one. `Rask.Redis` carries a topic's messages between instances over Redis pub/sub:

```bash
dotnet add package Rask.Redis
```

```csharp
builder.Services.AddRaskRedisBackplane();
```

The connection string is `Rask:ConnectionStrings:Redis` — `Rask__ConnectionStrings__Redis=redis:6379` in the
environment — and a missing one stops the app's start, naming that key. An app that already registers its own
`IConnectionMultiplexer` shares it instead.

**Only a topic that opts in crosses.** Crossing means serializing, so a cross-host topic names the JSON contract its
messages are written with — a source-generated one, so nothing is reflected and the app still trims:

```csharp
public static class Topics
{
    // Every instance's subscribers.
    public static readonly Topic<OrderPlaced> Orders = new("orders", AppJson.Default.OrderPlaced);

    // This instance's subscribers only — the message is never serialized.
    public static readonly Topic<CartTouched> Carts = new("carts");
}

[JsonSerializable(typeof(OrderPlaced))]
public sealed partial class AppJson : JsonSerializerContext;
```

Publishing and subscribing do not change. What does:

- **This instance first.** A publish is delivered to this instance's subscribers as before, then handed to Redis
  for the others. Redis hands every message back to the instance that sent it too; each message carries its
  sender's id, and an instance drops its own, so no page sees a message twice.
- **Each subscriber on another instance gets its own copy,** read back from JSON, rather than the instance the
  publisher passed.
- **Still at most once.** A message Redis could not take is logged and dropped: this instance's pages already have
  it, and the code that published — usually a request whose work is already saved — is not failed for it. An
  instance that is disconnected from Redis misses what is published meanwhile, and reconnects by itself.
- **One name, one type, across the app.** Instances match a cross-host topic by its name, so every instance must
  declare it with the same message type. Declaring one name for two types on one instance throws, naming both.
- **Topics are channels.** Each cross-host topic is the Redis channel `rask:broadcast:` + its name. Two apps that
  share one Redis server each set their own prefix in `Rask:Redis:ChannelPrefix`, or `AddRaskRedisBackplane(o =>
  o.ChannelPrefix = "shop:")`, so neither receives the other's messages.

A browser-WASM app has nothing to add: the whole app is one tab.

## Topics

A topic is its name *and* its message type. Two `new Topic<OrderPlaced>("orders")` instances are the same topic, so
declaring it twice by accident still works; a `Topic<string>("orders")` is a different topic, so a message can never
arrive as the wrong type. Keep topics in one static class per feature so a reader can find everything published in
it.

→ Related: [composition — callbacks & context](composition-callbacks-context.md) for parent–child communication on one
page · [live pages](render-modes.md) for how a session renders · [CQRS](cqrs.md) for publishing from a command handler

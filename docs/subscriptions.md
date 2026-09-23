# Subscriptions — push events to every open page

A component subscribes to an event the way it queries data: `QueryClient.Subscribe<OrderPlaced>()` in `Render`, and
every `OrderPlaced` published afterwards — by a command handler, a background job, a domain event after a save, another
server — lands in it and re-renders it. It is what tRPC calls a subscription, built on the CQRS notification Rask already
has, so one record reaches both the code that reacts to it and the screens that show it.

> Included in the [`Rask`](../README.md) package, with [CQRS](cqrs.md) and [Rask.Query](query.md). In a separate
> front end, [`Rask.Cqrs.Client`](cqrs.md#remote-dispatch--a-client-and-a-server-raskcqrsclient--raskcqrsserver) opens it on
> the server.

## The whole of it

The event is an ordinary notification:

```csharp
public sealed record OrderPlaced(Guid Id, string Customer, decimal Total) : INotification;
```

Publish it where it happens, through the dispatcher you already use:

```csharp
public sealed class PlaceOrderHandler(IDispatcher dispatcher) : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Guid> HandleAsync(PlaceOrder command, CancellationToken ct)
    {
        var order = await Order.Create(command.Customer, command.Total);
        await dispatcher.PublishAsync(new OrderPlaced(order.Id, order.Customer, order.Total), ct);
        return order.Id;
    }
}
```

Subscribe in the component that shows it:

```csharp
public sealed partial class NewOrders : Component
{
    protected override Component? Render()
    {
        var placed = QueryClient.Subscribe<OrderPlaced>().Keep(20);

        return Ul[placed.Items.Reverse().Select(o => Li.Key(o.Id)[$"{o.Customer} — {o.Total:C}"])];
    }
}
```

That is all. There is no subscription to dispose, no `OnMount` and no `StateHasChanged`:

- **It lives as long as the component.** The subscription closes when the component that read it unmounts — the visitor
  navigates away, the tab closes, the session ends.
- **Each value re-renders the component**, exactly as a query's result does when it lands.
- **Publishing still runs the handlers.** `PublishAsync` hands the notification to its `INotificationHandler`s *and* to
  every open subscription, so adding a screen never changes what the server does.

Try it: the buttons publish, and the two boards — which know nothing about the buttons or each other — each receive every
order. The line above them is scoped to one order and hears only its shipment.

<!-- demo:subscription-orders -->

## Where to declare one

In the same three places as a [query](query.md#a-query), with the same rules:

```csharp
// in Render — the same call is the same subscription every render, re-pointed when the key changes
var shipped = QueryClient.Subscribe<OrderShipped>(Id);

// in a property or the constructor, from a lambda — re-run at every read; null waits
Subscription<OrderShipped> Shipped => field ??= QueryClient.Subscribe<OrderShipped>(() => Selected);
```

A lambda that returns `null` means the key is not there yet: nothing is opened, and `IsLoading` is false, until it returns
one. A render that stops asking for a subscription sets it aside, and its next read opens it again.

## What a component reads

| | |
|---|---|
| `Data` | The latest value, or `default` until one arrives. Kept while reconnecting. |
| `Items` | With `.Keep(n)`: the last `n` values, oldest first — a chat, a log, the orders since the page opened. |
| `Status` | `Connecting` · `Live` · `Reconnecting` · `Ended` · `Error` — with `IsLive`, `IsReconnecting`, `IsEnded`, `IsError` |
| `IsLoading` | Connecting with nothing to show — the only state that warrants a spinner, as for a query. |
| `Error` | Why it is not live: the refusal, or what dropped the connection it is reopening. |

```csharp
var shipped = QueryClient.Subscribe<OrderShipped>(Id);

return shipped.IsLoading ? Spinner()
     : Div[Badge[shipped.Data?.Status ?? order.Data?.Status], shipped.IsReconnecting ? Small["reconnecting…"] : null];
```

It starts with the **last value published** for what it watches, so a page opened after the fact still shows it. On a
server render that value is in `Data` before the first paint.

## An event about one thing

Most events are about one record: this order shipped, this user's export is ready. Mark the property that says which, and
the event reaches only the subscribers watching that one:

```csharp
public sealed record OrderShipped([For<Order>] Guid OrderId, string Status) : INotification;

var shipped = QueryClient.Subscribe<OrderShipped>(Id);   // only this order's
```

`Order` is what the key identifies — usually the aggregate — and a **watch policy** for it decides who may watch:

```csharp
public sealed class WatchingOrders : IWatchPolicy<Order>
{
    public async Task<bool> CanWatchAsync(object key, CancellationToken ct) =>
        (await Order.Find((Guid)key)).CustomerId == Current.UserId || Current.Principal?.IsInRole("Admin") == true;
}
```

Write it anywhere in the project; the generator registers it, like a handler. It runs in the subscriber's own scope, so
`Current.UserId` is the person asking. One policy covers every event about orders.

**It fails closed.** A scope with no policy lets nobody watch it, so a forgotten policy never shows one customer another's
order. The subscription settles on `Error` with an `UnauthorizedAccessException` before anything arrives.

**Your own id needs no policy.** In a Rask app, every scope's default allows a signed-in user to watch the key that is their
own user id — the job reporting progress to the user who started it, a notification bell:

```csharp
public sealed record ExportProgress([For<User>] Guid UserId, int Percent, string? Url) : INotification;

var progress = QueryClient.Subscribe<ExportProgress>(Current.RequiredUserId);
```

The key is compared with `Equals`, and crosses the wire as its invariant string: a `Guid`, a number, a `string`, an enum,
or any id that implements `IParsable<T>`.

## Patching a query on screen

`.Into` edits a query's cached result with each value, so the list on screen changes in place with no round trip — the
same shape as an [optimistic edit](query.md#optimistic-updates):

```csharp
var orders = QueryClient.Query(new GetOrders(Page));

QueryClient.Subscribe<OrderShipped>()
    .Into(orders, (list, e) => [.. list.Select(o => o.Id == e.OrderId ? o with { Status = e.Status } : o)]);
```

Write the patch so that applying it twice changes nothing — replace by id rather than prepend — because the query's own
refetch may already include the value. After a dropped connection comes back, each query a subscription patches is
refetched once, so whatever was published meanwhile is not lost.

## A stream that is a function

Data that does not arrive as a notification — a price feed, a third-party stream — is a function returning an
`IAsyncEnumerable<T>`, opened for an input and reopened when a render passes a different one:

```csharp
var price = QueryClient.Subscribe(Symbol, (symbol, ct) => Prices.Stream(symbol, ct));
```

It runs where the component runs: on the server for a Server-host page, in the browser for a WebAssembly one. When it runs
out, `Status` is `Ended`; when it throws, the subscription reopens it.

## Publishing from anywhere

Everything that publishes a notification reaches subscribers, because they are the same notification:

- **A command handler** or any code with `IDispatcher` — `dispatcher.PublishAsync(new OrderShipped(id, "Shipped"))`.
- **A background job** — a job's handler publishes the same way, so progress and "your report is ready" reach the page that
  is waiting for them.
- **A domain event** raised by an aggregate is published after its save commits ([Rask.Data](data.md)), and one relayed by
  the [outbox](outbox.md) is published when it is relayed.

## Outside a component

`IDispatcher.SubscribeAsync` is the same subscription as an `IAsyncEnumerable<T>`, for code with no component — a hosted
service, a test:

```csharp
await foreach (var shipped in dispatcher.SubscribeAsync<OrderShipped>(orderId, ct))
    logger.LogInformation("{Order} is {Status}", shipped.OrderId, shipped.Status);
```

It starts with the last one published, asks the policy first, and ends when the token is cancelled.

## In a WebAssembly front end

In a [wasm-hosted](getting-started.md) app the page runs in the browser, and the events happen on the server. With
`AddRaskCqrsClient()`, a subscription opens **on the server**: a long-lived `GET` answered with server-sent events, over the
same origin and cookie as every other message. So a browser page hears what any visitor's command published, and an
event the page itself publishes travels to the server and comes back once, like everyone else's.

The server's `MapRaskCqrs()` serves it at `GET /_rask/cqrs/request/events/{name}?for={key}` — under the same prefix as the
messages, so the same CSRF header, the same authentication and any rate limit you attach to the group apply. Who may
subscribe from outside is decided on the server, and **closed unless opened**:

| The notification | A remote subscriber |
|---|---|
| is scoped with `[For<T>]` | may subscribe when its `IWatchPolicy<T>` allows the key; authenticated by default |
| carries `[Authorize]` / `[Authorize(Roles = "admin")]` itself | may subscribe when signed in / in the role |
| carries `[AllowAnonymous]` itself | may subscribe signed out |
| declares nothing | may not — `404`, the same as a name that does not exist |

The last row is deliberate: an app's auth events and domain events are notifications too, and none of them should be one
browser request away. A handler's `[Authorize]` still decides who may *publish* a notification from the browser; the
record's own decides who may *subscribe* to it. In an app that turned the endpoint's authentication off entirely
(`Rask:Cqrs:Server:RequireAuthenticatedUser` false — for an app with no accounts), a bare `[Authorize]` has nobody to
require, so it only opens the notification; name a role or a policy to mean more than that.

**Admitted once, at the open.** The policy runs when the stream opens, so a stream already running keeps delivering until
it drops — signing out elsewhere does not cut it mid-flight, and the next reconnect is refused. Where a revocation must
take effect immediately, scope the event to something the policy re-reads, or publish a change the page reacts to.

A reverse proxy that buffers responses would hold every event back, so the stream is sent with `X-Accel-Buffering: no`
(nginx) and response buffering off. A comment every fifteen seconds keeps a quiet stream open through idle timeouts.

## How values are delivered

- **Latest first.** A new subscription gets the last notification published for what it watches, then every one after.
  Only a type somebody has subscribed to is remembered, so a domain event nobody watches is never held, and at most 4,096
  are, oldest out first.
- **In order.** Values arrive in the order they were published, and a replay racing a new publish never lands after it.
- **Never holding the publisher up.** `PublishAsync` hands the notification over and returns; it does not wait for any page
  to render.
- **Bounded behind a slow reader.** A subscriber more than 256 values behind loses the oldest of them. A subscription shows
  the latest state, so the middle of a burst costs nothing on screen.
- **Reconnecting by itself.** A dropped connection is reopened with backoff, from half a second up to thirty; `IsLive` is
  false meanwhile and `Data` keeps the last value. A refusal — a policy saying no, a key the server rejects — is final, and
  is not retried.

Every number above is a setting, read from `Rask:Cqrs` first and overridable in code: `ReplayCapacity` (4096),
`SubscriptionBuffer` (256), `SubscriptionReconnectDelay` (`00:00:00.500`) and `SubscriptionReconnectCeiling` (`00:00:30`);
the event stream's keep-alive is `Rask:Cqrs:Server:EventKeepAlive` (`00:00:15`).

## What it is not

- **Not a queue.** Beyond the one replayed value, delivery is at most once, to the subscriptions open when it is published.
  When a page needs the current state rather than the latest change, query it, and patch the query with `.Into`.
- **Not beyond its own process** — below.

## One process, for now

Every publish and every subscription in a process meet in memory, and that is the whole of it: one app on one server
needs no configuration, no broker and no backplane.

What that means for more than one process — the two containers of a deploy, or several servers behind a load balancer —
is that each hears only what it published itself. A page on the other one catches up when its queries refetch, not the
moment the event happens. Carrying events between processes is [on the roadmap](roadmap.md); until then, keep anything
that must reach every visitor in the database the pages already read.

## Coming from `IBroadcast`

`IBroadcast` and `Topic<T>` are gone; a notification does what a topic did, and more.

| Before | Now |
|---|---|
| `public static readonly Topic<OrderPlaced> Orders = new("orders", AppJson.Default.OrderPlaced);` | `public sealed record OrderPlaced(…) : INotification;` — the record is the topic |
| `await broadcast.PublishAsync(Topics.Orders, order, ct);` | `await dispatcher.PublishAsync(order, ct);` |
| `broadcast.Subscribe(this, Topics.Orders, o => _orders.Insert(0, o));` in `OnMount` | `var orders = QueryClient.Subscribe<OrderPlaced>().Keep(20);` in `Render` |
| one tab only in a WebAssembly app | a wasm-hosted app subscribes on the server |

→ Related: [Rask.Query](query.md) for queries and commands · [CQRS](cqrs.md) for notifications and their handlers ·
[scaling](scaling.md) for running more than one server · [composition](composition-callbacks-context.md) for
parent–child communication on one page

# Rask.Query — the dispatcher, cached

`Rask.Query` wraps `IDispatcher` in a cache: request dedup, staleness, background refetch and
invalidation, for Rask components. It is TanStack Query's model in C#, down to the defaults — so an
app whose front end is React and whose admin pages are Rask components behaves the same way on both
sides.

```csharp
builder.Services.AddRaskQuery();
```

Registered **scoped**, which on the Server host means one cache per live session. Rask creates a
service scope per session, so one visitor can never be served another's data. That is not a setting
to get right; it is the only arrangement this package offers, because a process-wide cache in a
multi-user host is a data leak with a plausible excuse.

## A query

Inject `IQueryClient`, hold the result in a field, render it:

```csharp
private readonly Query<IReadOnlyList<Order>> _orders;

public OrdersPage(IQueryClient client) => _orders = client.Query(new GetOrders(Page: 1));

public override Component Render() =>
    _orders.IsLoading ? Spinner() : OrderTable(_orders.Data);
```

Re-point it from `OnPropsChanged` when its inputs change:

```csharp
protected override void OnPropsChanged() => _orders.SetMessage(new GetOrders(Page));
```

An unchanged key is a no-op, so calling it unconditionally is the safer habit.

## Keys

**The message is the key.** Rask messages are records, so structural equality comes free:
`new GetOrders(Page: 1)` written in two components is one entry and one round trip, with no key
string to invent and nothing to keep in sync when a property is added. That is the whole reason this
wraps the dispatcher rather than an arbitrary callback.

Underneath, a key is an **ordered list of parts** — the same shape TanStack Query uses. A message
derives `[typeof(GetOrders), message]`:

```csharp
QueryKey.Of(typeof(GetOrders), new GetOrders(1))
```

The type comes first so that **invalidation can match a prefix**:

```csharp
client.Invalidate<GetOrders>();   // every page, every filter — one prefix match
```

### Writing your own

Write a key when you want a hierarchy that spans message types, or for data that does not arrive
through CQRS at all:

```csharp
_list   = client.Query(new GetOrders(page), QueryKey.Of("orders", "list", QueryKey.Fields(("page", page))));
_detail = client.Query(new GetOrder(id),    QueryKey.Of("orders", "detail", id));

client.Invalidate(QueryKey.Of("orders"));   // both of them
```

Two rules, and they are TanStack's:

- **Order matters across parts.** `["orders", "list"]` is not `["list", "orders"]`. That is what
  makes a prefix mean anything.
- **Order does not matter inside a `Fields` part.** `Fields(("page", 1), ("status", "done"))` and
  `Fields(("status", "done"), ("page", 1))` are the same key, so two components writing the same
  filter differently share one entry instead of silently doubling the cache.

A `Fields` part in a *filter* is matched as a **subset**:

```csharp
client.Invalidate(QueryKey.Of("orders", QueryKey.Fields(("status", "done"))));
// every page of the done ones, whatever else their key carries
```

`QueryKey.Fields` takes named pairs rather than an anonymous object on purpose: reflecting over one
would warn under the trimmer on a WASM publish, and this package has to publish clean there.

A hand-written key can never collide with a derived one — the first part of a derived key is a
`Type`, and of a hand-written one a string — so both live in the same cache safely.

### Invalidating

| | |
|---|---|
| `Invalidate<GetOrders>()` | Every entry for that message type. |
| `Invalidate(QueryKey.Of("orders"))` | Every entry whose key starts with that. |
| `Invalidate(key, exact: true)` | That one entry. |
| `Invalidate(query.Key, exact: true)` | This query's own entry, without restating how its key is built. |
| `Invalidate(key => …)` | Whatever a prefix cannot say. |
| `InvalidateAll()` | Everything in this session. |

Invalidating marks entries stale rather than evicting them: anything on screen refetches at once,
anything not rendered refetches when something next observes it. The user keeps looking at the old
value until the new one arrives, which is the point.

Prefer a key that expresses the relationship over a predicate. A predicate is invisible to anyone
reading the query's own declaration.

## Mutations

A command declares what it makes out of date, on itself:

```csharp
[Invalidates(typeof(GetOrders), typeof(GetOrderCount))]   // two message types
[Invalidates("orders")]                                    // one key prefix
public sealed record ShipOrder(Guid Id) : ICommand;

await client.MutateAsync(new ShipOrder(id));
```

Several **types** are several prefixes; several **strings** are one path of several parts. The
asymmetry is deliberate — each reads the way its own thing is written — and the attribute allows
multiples so a command can declare both.

Declared on the command rather than passed at the call site, because the relationship belongs to the
thing that causes it: a new screen shipping the same command gets the same invalidation for free, and
adding an affected query is one edit in one place rather than a hunt through call sites. A stale list
after a save that clearly succeeded is the most common complaint about every cache of this kind, and
it is almost always a missing invalidation somebody had to remember to write.

For a command you want to *render* — whether it is in flight, whether it failed — hold a `Mutation`:

```csharp
private readonly Mutation<ShipOrder> _ship;

public OrdersPage(IQueryClient client) => _ship = client.Mutation<ShipOrder>();
```

## Defaults

TanStack's, deliberately: `StaleTime` 0 and `GcTime` five minutes. A query is stale the moment it
resolves — so anything that observes it again refetches in the background while showing what it has —
and an entry nothing is watching is collected five minutes later.

`QueryOptions` covers `StaleTime`, `GcTime`, `Retry`, `RefetchInterval` and `KeepPreviousData`.

## See also

- [`docs/cqrs.md`](cqrs.md) — the dispatcher this wraps.
- [`docs/spa.md`](spa.md) — the same model on the JavaScript side, through TanStack Query itself.

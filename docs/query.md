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

`QueryClient` reaches the session's cache from anywhere a component runs — its constructor, a property,
`Render` or an event handler — with nothing to inject. Declare a query in whichever of three places
suits the component; all three follow a route parameter, a query-string value or a prop by themselves,
so there is nothing to call when one changes.

**In `Render`, from the current values.** Route and query parameters and props are bound by the time
`Render` runs, so pass them as they are:

```csharp
[Route("/orders")]
public sealed partial class OrdersPage : Component
{
    [QueryParam] public int Page { get; set; } = 1;

    protected override Component? Render()
    {
        var orders = QueryClient.Query(new GetOrders(Page));
        return orders.IsLoading ? Spinner() : OrderTable(orders.Data);
    }
}
```

The same call returns the same query every render, re-pointed at whatever this render asks for — so
`?page=2` shows page two, and a render that asks for page one again costs nothing. A call is known by
where it is and which time it runs there in this render, so a query inside a loop is one per row, and one
inside an `if` does not disturb the others. A query a render stops asking for is set aside as that render
returns, and its entry is collected like any other nothing is watching.

**In a property or the constructor, from a lambda.** The lambda runs at every read, and a different
answer re-points the query. It first runs at the first read, never in the constructor, so it never sees
a parameter that has not been bound yet:

```csharp
Query<IReadOnlyList<Order>> Orders => field ??= QueryClient.Query(() => new GetOrders(Page));

// or
private readonly Query<IReadOnlyList<Order>> _orders;
public OrdersPage() => _orders = QueryClient.Query(() => new GetOrders(Page));
```

A lambda that returns `null` means the input is not there yet: the query is paused — nothing fetched, not
loading — until it returns a message. That is the whole of a dependent query:

```csharp
Query<Customer> Customer => field ??= QueryClient.Query(() => Selected is { } id ? new GetCustomer(id) : null);
```

**Injected.** `IQueryClient` offers the same queries and is what code with no component takes — a
hosted service, a test. `QueryClient` throws outside a session rather than guess at one, because a cache
that is not the session's would serve one visitor another's data.

### A query that is a function

Data that does not arrive through CQRS — a Rask.Data read face, a third-party HTTP call — is a function
under a key. Give it the value it depends on and the fetch is handed that same value, so a fetch still
running when the value changes caches under its own key, never the new one:

```csharp
// in Render
var hits = QueryClient.Query(QueryKey.For<Person>(), _search,
    (s, ct) => Person.Read.Where(p => p.Name.Contains(s)).ToListAsync(ct));

// in a property: the same, with a lambda for the value
Query<List<PersonRead>> Hits => field ??= QueryClient.Query(QueryKey.For<Person>(), () => _search,
    (s, ct) => Person.Read.Where(p => p.Name.Contains(s)).ToListAsync(ct));
```

The key is `[..prefix, value]`. An unchanged value is compared before any key is built, so a value type —
a tuple of several included, `() => (Tab, Page)` — costs a read nothing.

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
QueryClient.Invalidate<GetOrders>();   // every page, every filter — one prefix match
```

### Writing your own

Write a key when you want a hierarchy that spans message types, or for data that does not arrive
through CQRS at all:

```csharp
var list   = QueryClient.Query(new GetOrders(page), key: QueryKey.Of("orders", "list", QueryKey.Fields(("page", page))));
var detail = QueryClient.Query(new GetOrder(id),    key: QueryKey.Of("orders", "detail", id));

QueryClient.Invalidate(QueryKey.Of("orders"));   // both of them
```

Two rules, and they are TanStack's:

- **Order matters across parts.** `["orders", "list"]` is not `["list", "orders"]`. That is what
  makes a prefix mean anything.
- **Order does not matter inside a `Fields` part.** `Fields(("page", 1), ("status", "done"))` and
  `Fields(("status", "done"), ("page", 1))` are the same key, so two components writing the same
  filter differently share one entry instead of silently doubling the cache.

A `Fields` part in a *filter* is matched as a **subset**:

```csharp
QueryClient.Invalidate(QueryKey.Of("orders", QueryKey.Fields(("status", "done"))));
// every page of the done ones, whatever else their key carries
```

`QueryKey.Fields` takes named pairs rather than an anonymous object on purpose: reflecting over one
would warn under the trimmer on a WASM publish, and this package has to publish clean there.

A string key can never collide with a derived one — the first part of a derived key is a `Type`,
and of a string key a string — so both live in the same cache safely.

### About a type

Data with a type but no message — a Rask.Data aggregate's read face, say — takes a key that *starts*
with the type, so no string has to match between the query and whatever makes it stale:

```csharp
var people = QueryClient.Query(QueryKey.For<Person>("active"),
                       ct => Person.Read.Where(p => p.Active).ToListAsync(ct));
```

`QueryKey.For<Person>("active")` is `[typeof(Person), "active"]`, so `Invalidate<Person>()`,
`[Invalidates(typeof(Person))]` and `Command(invalidates: typeof(Person))` all reach it by prefix, and a
renamed type renames the key with it.

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

## Commands

What TanStack calls a *mutation* is a CQRS **command** here, and it is sent with the same verb the
dispatcher uses, `SendAsync`: the only thing the query client adds is the invalidation afterwards.

A command declares what it makes out of date, on itself:

```csharp
[Invalidates(typeof(GetOrders), typeof(GetOrderCount))]   // two message types
[Invalidates("orders")]                                    // one key prefix
public sealed record ShipOrder(Guid Id) : ICommand;

await QueryClient.SendAsync(new ShipOrder(id));   // throws if the handler does
```

Several **types** are several prefixes; several **strings** are one path of several parts. The
asymmetry is deliberate — each reads the way its own thing is written — and the attribute allows
multiples so a command can declare both.

Declared on the command rather than passed at the call site, because the relationship belongs to the
thing that causes it: a new screen shipping the same command gets the same invalidation for free, and
adding an affected query is one edit in one place rather than a hunt through call sites. A stale list
after a save that clearly succeeded is the most common complaint about every cache of this kind, and
it is almost always a missing invalidation somebody had to remember to write.

For a command you want to *render* — whether it is in flight, whether it failed — hold a `Command<T>`
(TanStack's `useMutation`):

```csharp
var ship = QueryClient.Command<ShipOrder>();   // in Render: the same command every render

Button.Disabled(ship.IsPending)
      .OnClick(() => ship.SendAsync(new ShipOrder(id)))
      [ship.IsPending ? "Shipping…" : "Ship"]
```

In a loop — a Ship button per row — pass `key: row.Id` so each row keeps its own pending state; without
it, one call site is one command however many rows it runs for. `Command<ShipOrder> Ship => field ??=
QueryClient.Command<ShipOrder>()` holds one in a property instead.

`Command<T>.SendAsync` does **not** throw: it runs from an event handler, where an exception has
nowhere to go, so the failure lands on `Error` and `Status` for the component to render. Use
`QueryClient.SendAsync` when you want the exception.

What a command can tell a render:

| | |
|---|---|
| `Status` | `Idle` (never sent, or reset) · `Pending` · `Success` · `Error` — with `IsIdle`, `IsPending`, `IsSuccess`, `IsError` |
| `Error` | What the last send threw |
| `Variables` | The command last sent — set as it is sent, so a pending render can say `$"Shipping #{ship.Variables?.Id}…"` |
| `Data` | `Command<TCommand, TResult>` only: the last successful result, there by the time `Status` says `Success` |
| `Reset()` | Back to `Idle`, clearing all of the above |

### Optimistic updates

An edit made at send time, from the query already on screen, so it sees what is being sent:

```csharp
var orders = QueryClient.Query(new GetOrders(Page));
var ship   = QueryClient.Command<ShipOrder>();

.OnClick(() => ship.SendAsync(new ShipOrder(id),
    orders.Optimistic(list => [.. list.Where(o => o.Id != id)])))
```

The row disappears at once. On success the command's invalidation refetches and the server's answer
replaces the guess; on failure every edit is put back, in reverse, and the error lands on `Error`. Pass
several — `ship.SendAsync(cmd, list.Optimistic(…), count.Optimistic(n => n - 1))` — and all of them are
snapshotted before any is sent, so a failure never leaves half of them applied. An edit is aimed at
whatever its query shows now, so a function query takes one as readily as a message query, and a function
command sends one the same way: `save.SendAsync(ct => Person.CreateAsync(…), people.Optimistic(…))`.
Nothing cached means nothing is edited: a row the server never confirmed is never invented.

### A function

Work that is not a CQRS record — a Rask.Data write, a third-party HTTP call — is a function. It has
nowhere to carry `[Invalidates]`, so the command is created with what it makes out of date, and handed
the work on every send, so the lambda captures what this click is about:

```csharp
var save = QueryClient.Command(invalidates: typeof(Person));

Button.Disabled(save.IsPending)
      .OnClick(() => save.SendAsync(ct => Person.CreateAsync(model, cancellationToken: ct)))
      ["Add"]
```

`SendAsync(ct => …)` never throws either; `SendAsync<T>` infers `T` from the lambda and returns what it
produced, or `default` on failure. Each key is a prefix, a string or a type converts to one, and several
are several prefixes: `Command(invalidates: [typeof(Person), "dashboard"])`.

Prefer a record wherever there is one: its invalidation travels with it to every screen that sends it.
A function's lives where the command is created. It runs where the component runs — on the Server host
that is the server, so a typed `HttpClient` injected through the constructor keeps its secrets there; in
a WASM app it is the browser, where CORS applies and nothing is secret. Calling your *own* backend over
HTTP from a component is the case to avoid: that is what a CQRS message is for.

### Writes refresh queries by themselves

In a Rask app a Rask.Data write needs no invalidation at all. Once a save commits, every query about the
types it wrote refetches on the screen of the session that made it:

```csharp
var people = QueryClient.Query(QueryKey.For<Person>("active"), ct => Person.Read.Where(p => p.Active).ToListAsync(ct));

.OnClick(() => Person.CreateAsync(model))   // the list above refetches — nothing else to write
```

It reaches exactly what `QueryClient.Invalidate<Person>()` reaches: a key built with `QueryKey.For<Person>`,
and anything a command names with `[Invalidates(typeof(Person))]`. A child entity counts as its aggregate —
adding an `OrderLine` refreshes `Order` queries. What it cannot reach is a message query such as
`GetPeople`: that is keyed by its own type, and the write cannot know which messages read the table, so the
command still names it with `[Invalidates(typeof(GetPeople))]`.

So a command around such a write names nothing — `QueryClient.Command()` — and is there for what a render
wants from it: `IsPending` to grey the button, `Error` to say what went wrong.

Three things it deliberately does not do:

- **Refresh another session.** The cache is per session, and another user's screen is not this write's to
  touch; it catches up on its next fetch.
- **Run outside a session.** A background job or a hosted service writes with no screen waiting, so nobody is
  told.
- **Report a write that might not stand.** Inside an explicit `BeginTransactionAsync` it waits for the
  commit, and a rollback tells nobody — a refetch before the commit could read the rows as they were and
  cache that.

## Try it

Every shape above on one small page. The parcel list is a query declared in `Render` that follows the URL's
`?page=` — watch the address bar — and keeps the previous page on screen while the next one loads. **Details** feeds
a dependent query that stays paused until you pick a parcel. The contents search is a function query keyed
`QueryKey.For<Parcel>(input)`. Each **Ship** button is its own keyed command: it disables itself while pending, and
its success refetches the page and the picked parcel, because `ShipParcel` names both with `[Invalidates]`.

<!-- demo:query-parcels -->

## Defaults

TanStack's, deliberately: `StaleTime` 0 and `GcTime` five minutes. A query is stale the moment it
resolves — so anything that observes it again refetches in the background while showing what it has —
and an entry nothing is watching is collected five minutes later.

`QueryOptions` covers `StaleTime`, `GcTime`, `Retry`, `RefetchInterval` and `KeepPreviousData`.

## See also

- [`docs/cqrs.md`](cqrs.md) — the dispatcher this wraps.
- [`docs/spa.md`](spa.md) — the same model on the JavaScript side, through TanStack Query itself.

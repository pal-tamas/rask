# Cached fragments

`Cached` draws what it wraps once and hands that markup to every later request that would draw the same —
ASP.NET's `<cache>` tag helper and Blazor's `CacheView`, for a Rask page. It is for the expensive part of a page
that changes rarely: a dashboard's aggregates, a filter's long option list, a price table.

```csharp
Cached.Name("sales-summary").For(10.Minutes)[
    SalesSummary                                   // its OnMount runs the aggregate query
]
```

The first request mounts `SalesSummary`, waits for its data as any first `GET` does, and stores the markup it drew.
Every request after that, for ten minutes, is handed the markup — `SalesSummary` is never mounted, so its query never
runs.

<!-- demo:cached-report -->

## What it varies by

A fragment is stored under its name plus everything it varies by. Three things are **always** in the key:

| Always | Why |
|---|---|
| The tenant (the `rask:tenant` claim) | No tenant ever sees another's markup — and there is no step that turns this off. |
| The signed-in user | One user's markup is never served to another, unless the fragment says `.AcrossUsers()`. |
| The culture and UI culture | Numbers, dates and words are formatted in them. |

Everything else is a step:

```csharp
Cached.Name("orders").VaryByQuery("status", "page")[OrderTable]        // ?status=…&page=…
Cached.Name("breadcrumbs").VaryByRoute()[Breadcrumbs]                  // the path, so every route value
Cached.Name("hero").VaryByHeader("User-Agent")[Hero]                   // a request header
Cached.Name("product").VaryBy(product.Id)[ProductSpecs]                // a value of your own
Cached.Name("filters").AcrossUsers()[VehicleFilter]                    // one copy per tenant, for every user
```

`.AcrossUsers()` is only for markup that says nothing about who is looking — a list of the tenant's vehicles, not
"your vehicles". When a user cannot be told apart (a principal still loading, a signed-in user with no id claim), or a
header or route it varies by cannot be read, the fragment is drawn live and **never stored**: no key is safer than a key
that could hand one visitor's markup to another.

## How long it is kept

```csharp
Cached.Name("rates").For(1.Hour)[Rates]                                // absolute: one hour after it was drawn
Cached.Name("rates").For(1.Hour).Sliding(10.Minutes)[Rates]            // …or ten minutes unread, whichever is first
```

Without `.For(…)` a fragment is kept five minutes (`CachedOptions.DefaultLifetime`).

## Forgetting

After a write that changes what a fragment shows, forget it by name or by tag — for every user, tenant and value it
varies by:

```csharp
Cached.Name("price-list").Tags("catalog")[PriceList]

await CachedFragments.Forget("catalog");      // every fragment tagged catalog
await CachedFragments.Forget("price-list");   // every copy of that fragment
```

## Where it is kept

| Host | Store |
|---|---|
| Server, with `AddHybridCache()` | The app's `HybridCache` — shared by every server behind it; `Forget` is its `RemoveByTagAsync`. |
| Server, without one | In process, bounded by `CachedOptions.SizeLimit` (64 MB); least recently read goes first. |
| WebAssembly | In the tab's memory. There are no other users to share with, so it saves re-drawing a page you come back to. |

```csharp
builder.Services.Configure<CachedOptions>(o =>
{
    o.SizeLimit = 128.Megabytes;      // the in-process store's bound
    o.MaxEntrySize = 2.Megabytes;     // a bigger fragment is drawn live every time
    o.DefaultLifetime = 10.Minutes;
});
```

A cache that cannot be read or written never takes the page down: the fragment is drawn live.

## What it may hold

What it keeps is **static markup**, served to later visitors where nothing would answer a click. So it refuses,
naming the fragment and the attribute it found:

- an event handler — `Button.OnClick(…)`, `Form.OnSubmit(…)`, a `Lazy`;
- a bound input — `Input.Bind(…)`;
- an element reference — `.Ref(…)`;
- a component that contributes to the page's head.

Written straight into the fragment, [RASK117](diagnostics.md#rask117) stops the build; inside a component it wraps, the
first render that would store it throws:

```
Cached.Name("cart") holds an event handler (data-rask-on-click). What it keeps is served to later visitors as plain
HTML, where nothing would answer it. Move the interactive part outside Cached.Name("cart")[ … ], next to it, and keep
only static markup inside.
```

Put the interactive part next to the fragment, not in it:

```csharp
Div[
    Cached.Name("vehicle-options").AcrossUsers()[VehicleOptions],   // the long, static list
    Ui.Button.OnClick(Apply)["Apply"]                                // the live part, outside
]
```

## In a live page

The stored markup is one verbatim frame in the page, which the diff leaves alone while it is unchanged — an opaque
island. The session that drew a miss keeps what it wraps live; every later one is handed the markup. A live page keeps
what it drew until the key changes (a query value it varies by, a sign-in), and then looks again.

**A hit mounts nothing**, so put the expensive work in a component the fragment wraps. A query written in the page's
own `Render`, in the markup handed to `Cached[…]`, has already run by the time the fragment is asked.

## See also

- [Cache](cache.md) — `Cache.Remember(…)` for values rather than markup.
- [Lifecycle](lifecycle.md) — the first `GET` waits for `OnMount`, which is what a stored fragment captures.
- [Multi-tenancy](multi-tenancy.md) — where the `rask:tenant` claim comes from.

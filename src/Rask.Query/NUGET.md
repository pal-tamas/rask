# Rask.Query

**Server state for Rask components** — the Rask.Cqrs dispatcher wrapped in a cache: request dedup,
staleness, background refetch and invalidation. It is TanStack Query's model in C#, for
[Rask](https://rask.sh/), a full-stack .NET web framework for teams of any size.

- **Static `QueryClient`** reaches the live session's cache from anywhere a component runs — `Render`, a
  property, the constructor or an event handler — with nothing to inject. `IQueryClient` is the injectable
  face for code with no component (a hosted service, a test).
- **The message is the key.** A query record such as `new GetOrders(Page)` is its own cache key, so two
  components asking for the same page share one entry and one round trip.
- **Queries follow their inputs.** Declared in `Render` from a route parameter, a query-string value or a
  prop, a query re-points itself when that value changes; there is nothing to call.
- **Commands.** `QueryClient.Command<T>()` hands back a `Command<T>` with `IsPending`, `Error` and `Status`
  to render, and a command record names what it makes out of date with `[Invalidates(typeof(GetOrders))]`.
- **Writes refresh queries by themselves.** A Rask.Data save refetches every query about the types it wrote,
  on the screen of the session that made it — no invalidation to write.
- **Scoped per live session**, so one visitor is never served another's data.
- **Subscriptions** — `QueryClient.Subscribe`: a published CQRS notification re-renders the pages watching it.

## Install

```bash
dotnet add package Rask.Query
```

## Use

```csharp
// Program.cs
builder.Services.AddRaskCqrs();
builder.Services.AddRaskQuery();
```

```csharp
[Route("/orders")]
public sealed partial class OrdersPage : Component
{
    [QueryParam] public int Page { get; set; } = 1;

    protected override Component? Render()
    {
        var orders = QueryClient.Query(new GetOrders(Page));   // ?page=2 shows page two, by itself

        return orders.IsLoading
            ? P["Loading…"]
            : Ul[orders.Data!.Select(o => Li[o.Number])];
    }
}
```

Guide: [Rask.Query](https://rask.sh/docs/guides/query) · [subscriptions](https://rask.sh/docs/guides/subscriptions)

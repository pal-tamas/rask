# Rask.Api

Ordinary ASP.NET Core **API controllers and minimal API endpoints**, hosted properly inside a Rask app.

```csharp
builder.Services.AddRaskApi();
...
app.MapRaskApi();
app.UseRask<App>();
```

Write the endpoint however you normally would — an `[ApiController]` class, or `app.MapGet(...)`. Nothing
about it is Rask-specific.

## What this actually fixes

Not ordering. Endpoint routing matches on **precedence**, never on registration order, and every route
you write is more specific than Rask's `/{**path}` catch-all — so your endpoints answer from either side
of `UseRask`, and always did.

What order cannot fix is a request under your API prefix matching **nothing**. Without a guard it
reaches the catch-all and renders the app, so a mistyped or deleted route answers `200` with a web page,
and the caller's JSON parse fails somewhere a long way from the cause. `MapRaskApi()` answers `404` with
an RFC 9457 problem document under `ApiOptions.Prefix` (default `/api`) and leaves every other path alone.

It is an ordinary endpoint rather than a fallback on purpose: a fallback sits at `int.MaxValue` and would
lose to the catch-all, while at the same order `/api/{**rest}` outranks `/{**path}` — and loses in turn to
every real route beneath it.

## The typed client

`Rask.Api.Client` generates a typed client from these same endpoints, so browser and component code calls
`await posts.Get(3)` instead of writing the URL, the verb and the response type out by hand a second time.

Documentation: <https://rask.sh>

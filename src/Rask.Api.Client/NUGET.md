# Rask.Api.Client

Call your own API controllers and minimal API endpoints from C# with **no URL at the call site**.

```csharp
// Server/PostsController.cs — ordinary ASP.NET, nothing Rask-specific
[ApiController, Route("api/posts")]
public sealed class PostsController(AppDb db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Post>> Get(int id) => ...;
}

// Any component. PostsClient is generated from the declaration above.
public sealed class PostDetail(PostsClient posts) : Component
{
    protected override async Task OnMountAsync() => _post = await posts.Get(Id);
}
```

```csharp
services.AddRaskApiClient();   // registers every generated client
```

The endpoint you already wrote is the source of truth. A source generator reads its routes, verbs,
parameters and response types, and emits one client class per controller — so the URL, the verb, the
request shape and the response shape are never restated. Rename a route on the server and the call site
stops compiling, instead of 404ing in production.

Minimal APIs are read from the `MapGet`/`MapPost` invocations themselves and grouped by route
(`/api/widgets` → `WidgetsClient`); `.WithName("…")` names a method. `TypedResults` — `Ok<T>`,
`Results<Ok<T>, NotFound>`, `NoContent` — carries the response type.

Reflection-free JSON throughout, so it publishes clean under the WASM/AOT trimmer. In a one-project
`--wasm` app the client crosses into the browser bundle automatically, even though the controller under
`Server/` is never compiled there.

## Failures

`ApiException.StatusCode` is `null` when the request never reached a server — DNS, TLS, a dropped
connection, a timeout — with the cause in `InnerException`. A status means the server answered and said
no. Blurring the two makes "is it down, or am I wrong?" unanswerable:

```csharp
catch (ApiException ex) when (ex.StatusCode is null) { /* offline: queue it */ }
catch (ApiException ex) when (ex.StatusCode == 404)  { /* gone: show empty  */ }
```

Pairs with `Rask.Api`, which hosts the endpoints. Documentation: <https://rask.sh>

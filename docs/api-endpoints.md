# HTTP APIs (`Rask.Api` / `Rask.Api.Client`)

Sometimes you need a real HTTP API — a mobile client, a partner integration, a webhook receiver, or
just `curl`. [CQRS remote dispatch](cqrs.md) deliberately gives you none of that: its whole pitch is
that there is *no* `/api/*` endpoint to write, and its route is a framework-internal path carrying a
CLR type name. That is the right answer when the only caller is your own browser code, and the wrong
one when the caller is somebody else.

So write the endpoint the ordinary ASP.NET way. Rask does two things with it: hosts it properly, and
generates a typed client so *your* code never writes the URL twice.

- [Hosting](#hosting) — `AddRaskApi()` / `MapRaskApi()`, and the 404 that was missing
- [The typed client](#the-typed-client) — one client per controller, generated from the declaration
- [Minimal APIs](#minimal-apis) — the same, read from the `MapGet` calls themselves
- [What gets a client method](#what-gets-a-client-method) — and what is reported instead

---

## Hosting

There is nothing to wire. Like every other battery, referencing the framework is what turns this on —
write a controller and it answers:

```csharp
var app = RaskApp.Create(args);
app.Run<App>();
```

Nothing about the endpoint itself is Rask-specific either:

```csharp
[ApiController]
[Route("api/posts")]
public sealed class PostsController(AppDb db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Post>> Get(int id) =>
        await db.Posts.FindAsync(id) is { } post ? post : NotFound();
}
```

### Order is not what makes this work

It is worth being blunt about this, because Rask's own docs used to say the opposite. Endpoint routing
matches on **precedence**, never on registration order, and every route you write is more specific than
Rask's `/{**path}` catch-all. Your endpoints answer from either side of `UseRask`. `MapEndpoints` is a
readable place to put them, not a fix for a bug.

What order *cannot* fix — and what `MapRaskApi()` does — is a request under your API prefix that
matches **nothing**. Without a guard it reaches the catch-all and renders the app, so a mistyped or
deleted route answers `200` with a web page and the caller's JSON parse fails a long way from the
cause. `MapRaskApi()` answers `404` with an RFC 9457 problem document under `ApiOptions.Prefix`
(default `/api`) and leaves every other path alone.

That guard is an ordinary endpoint rather than a fallback, deliberately: a fallback sits at
`int.MaxValue` and would lose to the catch-all, while at the same order `/api/{**rest}` outranks
`/{**path}` — and loses in turn to every real route beneath it.

```csharp
app.Configure(c =>
{
    c.Api.Off();                       // no HTTP endpoints in this app at all
    c.Api.Configure(o =>
    {
        o.Prefix = "/services";        // where this app's endpoints live
        o.NotFound = false;            // answer for unmatched paths yourself
        o.Controllers = false;         // minimal APIs only; never discover a controller
    });
});
```

An app assembled by hand, without the `Rask` meta-package, calls `AddRaskApi()` and `MapRaskApi()`
itself. `MapRaskApi()` returns the endpoint group, so rate limiting, CORS or output caching attach to
the whole API in one line: `app.MapEndpoints(e => e.MapRaskApi().RequireRateLimiting("api"));`

### What gets registered

`AddMvcCore().AddDataAnnotations()`, not `AddControllers()`. An API controller needs the core —
routing, model binding, the JSON formatters, the `[ApiController]` conventions — while `AddControllers`
layers on the API explorer, CORS services and formatter mappings: machinery for OpenAPI documents,
cross-origin policies and `.json`-style URL suffixes that an app gets whether it asks or not.

DataAnnotations is the one addition, because dropping it changes **behaviour** rather than weight: a
`[Required]` on a request body silently stops being enforced, and an endpoint that quietly accepts what
it used to reject is worse than a heavier registration. (Checked by
`A_required_member_is_still_enforced_by_the_lean_registration`, which goes red without it.) An app that
wants CORS or an OpenAPI document adds `AddCors()` or `AddApiExplorer()` — a line it would have written
anyway.

---

## The typed client

The endpoint you already wrote is the source of truth. `Rask.Api.Client` reads it and generates one
client per controller — `PostsController` becomes `PostsClient` — so a component asks for the data
rather than for a URL:

```csharp
host.Services.AddRaskApiClient();
```

```csharp
public sealed class PostDetail(PostsClient posts) : Component
{
    [Prop] public int Id { get; init; }

    private Post? _post;

    protected override async Task OnMountAsync() => _post = await posts.Get(Id);

    public override Component Render() =>
        _post is null ? Div["Loading…"] : Article[H1[_post.Title]];
}
```

Compare that with what `docs/http-and-files.md` describes, which is still exactly right for calling
*somebody else's* API:

```csharp
await http.GetFromJsonAsync<Post>($"/api/posts/{id}");   // four things that can drift
```

The URL, the verb, the request shape and the response shape are all restated at the call site, and
nothing checks them against the server. Rename the route on the controller and this compiles fine and
404s at run time. With the generated client it does not compile — which is the whole point.

The codecs are the reflection-free ones the CQRS wire uses, so a shape means the same thing on either,
and the client publishes clean under the WASM/AOT trimmer.

### In a one-project `--wasm` app

The client reaches the browser bundle on its own. That is worth spelling out, because the controller it
was generated from cannot: `Server/` is excluded from the bundle by design, so the generator running in
the browser half would see no controllers at all.

What crosses instead is the **file** the server's own generator wrote. `EmitCompilerGeneratedFiles`
puts it on disk, and the companion project compiles that exact file — so the two halves cannot disagree
about the client, because there is only one of it. The client runtime (`Rask.Api.Client`) is added to
the companion automatically when that file exists; you do not declare it twice.

Deliberately unlike [`Rask.Cqrs.Client`](cqrs.md), which must be kept *out* of the server: that split
exists because `AddRaskCqrsClient()` rewrites a process-wide registry, so a server holding it would
bounce its own messages straight back out. A typed API client has no such property — it dials routes
that are public anyway — and the server half genuinely needs it, since a component that calls its API
renders on both.

> **The shapes that cross the wire live outside `Server/`.** A request or response type declared
> *inside* `Server/` is invisible to the bundle, so the generated client returns a type the browser half
> cannot compile — and you meet it as `CS0246` inside generated code you never wrote. Same rule as a
> CQRS message record: the **handler** is server-only, the **shape** is shared. In practice: keep
> `Post`, `NewPost` and friends beside your components, and only `PostsController` under `Server/`.

### Securing an endpoint

`[Authorize]` works the way it does in any ASP.NET app — on the controller, on the action, with a policy
or roles, and `[AllowAnonymous]` to open one route on an otherwise protected controller:

```csharp
[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(AppDb db) : ControllerBase
{
    [HttpGet("{id:int}")]
    public Task<ActionResult<Order>> Get(int id) => ...;

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public Task Cancel(int id) => ...;
}
```

Nothing about it is Rask-specific, and the lean registration does not weaken it: enforcement is asserted
over real HTTP in `AuthorizeTests` — 401 anonymous, 403 for a role the caller lacks, 200 for one they
hold — because an `[Authorize]` that silently stopped being enforced would still *look* like protection
in the source.

Your app still calls `AddAuthentication`/`AddAuthorization` and `UseAuthentication`/`UseAuthorization`;
the [accounts battery](authentication.md) registers them, so a scaffolded app already has them. Their
absence fails loudly at startup rather than quietly per request.

On the client side, attach the token with `ApiClientOptions.ConfigureRequestAsync` — it receives the
request rather than the `HttpClient`, so a token is scoped to the call instead of becoming ambient state
shared by everything that resolves the same client.

Unlike CQRS remote dispatch, none of this is re-implemented: there, two shared endpoints cannot carry
per-message metadata, so `Rask.Cqrs.Server` reads `[Authorize]` off the handler at compile time and
enforces it imperatively. An API controller *is* its own endpoint, so ASP.NET's own authorization
applies and Rask stays out of the way.

### Failures

A call that does not succeed throws `ApiException`. `StatusCode` is `null` when the request never
reached a server at all — DNS, TLS, a dropped connection, a timeout — with the cause in
`InnerException`. A status means the server answered and said no. Those need different handling, and an
exception that blurred them would make "is it down, or am I wrong?" unanswerable:

```csharp
catch (ApiException ex) when (ex.StatusCode is null) { /* offline: queue it */ }
catch (ApiException ex) when (ex.StatusCode == 404)  { /* gone: show empty  */ }
```

`ApiClientOptions.ConfigureRequestAsync` is the hook for a bearer token or a tenant header. It receives
the *request* rather than the `HttpClient`, deliberately: a token on the client is ambient state shared
by everything that resolves it, while a token here is scoped to the call being made.

---

## Minimal APIs

Read from the `MapGet`/`MapPost`/… invocations themselves, with no attribute and no registration:

```csharp
app.MapEndpoints(e =>
{
    e.MapGet("/api/widgets/{id:int}", async (int id, AppDb db) =>
        await db.Widgets.FindAsync(id) is { } w ? TypedResults.Ok(w) : Results.NotFound());

    e.MapPost("/api/widgets", (Widget body, AppDb db) => db.Add(body));
});
```

A minimal API has no controller to be named after, and most live in a `Program.cs` whose enclosing type
is `Program` — a name that would tell a reader nothing. So they group by **route**: everything under
`/api/widgets` lands on `WidgetsClient`. The method name comes from the verb and the route past that
grouping segment, so `GET /api/widgets/{id}` becomes `GetById`. Chain `.WithName("…")` to choose it
yourself — that is ASP.NET's own way of naming an endpoint, not a Rask invention:

```csharp
e.MapDelete("/api/widgets/{id:int}/tag", (int id) => TypedResults.NoContent())
    .WithName("Untag");     // widgets.Untag(3)
```

**`TypedResults` is read properly.** `Ok<T>`, `NoContent` and `Results<Ok<T>, NotFound>` all carry the
response type in the signature, and the alternative carrying a body supplies the client's return type.
`Results.Ok(x)` — the untyped `IResult` — does not, and reports [RASK070](diagnostics.md#rask070).

---

## What gets a client method

Everything whose shape can be written as one, and a diagnostic naming the reason for everything else.
A method silently missing from a client reads as a broken generator, so nothing is skipped in silence.

| Written on the server | In the client |
|---|---|
| `ActionResult<Post>`, `Task<Post>`, `Post` | `Task<Post>` |
| `Task`, `void`, `NoContent` | `Task` |
| `Ok<Post>`, `Results<Ok<Post>, NotFound>` | `Task<Post>` |
| `IActionResult` + `[ProducesResponseType(typeof(Post), 200)]` | `Task<Post>` |
| `IActionResult` alone | nothing — [RASK070](diagnostics.md#rask070) |

Parameters follow ASP.NET's own `[ApiController]` binding rules: a name matching a route token binds
from the route, a simple type from the query, and the one complex type left is the body. `[FromRoute]`,
`[FromQuery]`, `[FromBody]` and `[FromHeader]` say so explicitly.

Anything the caller does not supply is **not** a client parameter — `[FromServices]`, `HttpContext`,
`CancellationToken`, `ClaimsPrincipal`, `IFormFile`. An injected service is recognised without
`[FromServices]` too: a parameter only reaches the client's signature if its type is one the wire can
carry, and an interface or abstract type is exactly the shape of something that comes from the
container.

Two things worth knowing because they are easy to get wrong by hand:

- **Optional parameters keep the server's default.** An `int page = 1` generates `page = 1`, not
  `page = default` — which would send `page=0` whenever a caller omitted it and quietly override the
  server's own default with a zero that type-checks everywhere.
- **The client asks for JSON.** An action returning `string` otherwise answers `text/plain`, because
  ASP.NET's `StringOutputFormatter` wins content negotiation, and `return "ok"` would arrive as `ok`
  rather than `"ok"`.

Diagnostics: [RASK067](diagnostics.md#rask067) (no wire encoding), [RASK068](diagnostics.md#rask068)
(endpoint skipped, with the reason), [RASK069](diagnostics.md#rask069) (two endpoints claiming one
client method), [RASK070](diagnostics.md#rask070) (response type not statically known).

---

See also: [CQRS](cqrs.md) for when the only caller is your own browser code and no HTTP surface needs to
exist, [HTTP & files](http-and-files.md) for calling APIs you did not write, and
[Authentication](authentication.md) for securing endpoints with `[Authorize]`.

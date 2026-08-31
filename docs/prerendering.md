# Build-time prerendering (WASM)

A browser-WebAssembly app has no server to render it per request, so the first thing **every**
visitor and every crawler receives is the boot shell: a spinner, and the word "Loading". The app's
real markup does not exist until several megabytes of runtime have downloaded and started. That is
what a search engine indexes and what a social card previews.

Prerendering renders each route to real HTML **at publish time** and writes it beside the bundle.
The bundle still boots and takes the page over exactly as before; the difference is only what
arrives before it does.

## Enable it

```xml
<PropertyGroup>
  <RaskPrerender>true</RaskPrerender>
</PropertyGroup>
```

That is the only knob. It is **publish-only**, like the WASM bundle itself — `dotnet build` is
unaffected, so the inner loop does not pay for it:

```bash
dotnet publish -c Release
```

## What gets written

One document per prerenderable route, as a **directory with an `index.html`** rather than
`about.html`:

| Route | File |
| --- | --- |
| `/` | `wwwroot/index.html` |
| `/about` | `wwwroot/about/index.html` |
| `/docs/intro` | `wwwroot/docs/intro/index.html` |

Directory-per-route so a static host serves the page at the URL the app actually routes to — with
no extension in it, and no per-host rewrite rule to configure.

Each page renders through the same root boundary both hosts install and through the same wave loop
a server's first response uses, so **a page whose `OnMountAsync` loads build-time data writes the
data, not its placeholder**. Each page gets its own DI scope, as a request would, so a page
injecting something scoped never sees the previous page's instance.

## What is skipped, and why you are told

A route is prerenderable when **every one of its segments is a literal** — decided on the parsed
segments, not by looking for a brace in the template. Anything else is skipped and **named in the
build log**:

```
[Rask.Prerender] 19 route(s) to render, 3 skipped
[Rask.Prerender]   skipped /guides/{slug} — its path is not known without data
[Rask.Prerender]   skipped /todos/{id:guid}/edit — its path is not known without data
```

A parameterised route cannot be enumerated without knowing the values, and a catch-all is a 404
page at best. The skipped list is reported rather than logged at debug, and reported **even when it
is empty**, because a pass that quietly covered a site's static half would read exactly like one
that had covered all of it.

## A page that throws or stalls is deliberately not written

Two more lines you may see:

```
[Rask.Prerender]   /media-devices threw — not written
[Rask.Prerender]   /fullscreen did not settle in 30s — not written
```

**This is the design working, not failing.** Both cases still hand back perfectly ordinary HTML — a
faulted render returns the root boundary's error document, and a timed-out one returns whatever
placeholder was on screen when the budget ran out. Writing either would publish it under the
route's own name with nothing saying so, and **a baked spinner is worse than no prerender at all,
because it looks prerendered**. The route is skipped and the bundle still serves it at runtime, so
this costs an optimisation rather than breaking the page.

The common cause is a page that injects a browser-only API — a media-device or fullscreen demo has
nothing to bind to off a browser. Guard such work behind a lifecycle hook that only runs in the
browser if you want the route prerendered.

If the pass writes **no** pages at all, the build raises a warning — asked of the output rather
than of the exit code, because the pass reports what it skipped and carries on, so "it ran" and "it
produced something" are different questions.

## How it runs

A browser-wasm assembly cannot execute on the desktop, so the app's own sources are compiled a
**second time for `net10.0`** into a companion project under `obj/rask-prerender/`, and that is
what renders. The companion carries the app's own `ProjectReference`s and `PackageReference`s, so
it reaches the framework exactly the way the app does.

It compiles **`Program.cs` too**, deliberately: that file is where the app registers its services,
and a page that injects anything would otherwise find nothing registered.
`WasmHostBuilder.RunAsync` sees the `RASK_PRERENDER_OUT` environment variable and prerenders
instead of booting — so the app's real entry point drives the pass, and there is no second place to
keep registrations in sync.

Generated files under `obj/` are rewritten on every publish; edit the app, never the companion.

## Using the engine directly

Both halves are public on `RaskPrerender` in `Rask.Core.Live`, for a caller that wants to drive its
own pass:

```csharp
var plan = RaskPrerender.PlanRoutes();          // .Paths and .Skipped
// seed RouteState.Path on the scope first — the caller holds the route table
var result = await RaskPrerender.RenderDocumentAsync(app, services, TimeSpan.FromSeconds(30));
```

`RenderDocumentAsync` deliberately takes no route: which page it renders is the caller's decision,
because the caller is what holds the route table. **Check `result.Faulted` and `result.TimedOut`
before writing anything to disk** — for the reason above, both return ordinary-looking HTML.

## Limits

- WASM only. A Server app already renders every request, and `RenderModes` covers serving a page
  that needs nothing live as a cacheable document — see [Render modes](render-modes.md).
- Parameterised and catch-all routes are never covered; there is no hook yet for supplying the
  values to enumerate them.
- The per-page budget is 30 seconds.

## See also

- [Render modes](render-modes.md) — the Server-side equivalents, and moving a page into WebAssembly
- [Mobile & PWA](pwa.md) — the rest of the standalone-WASM deployment story
- [Deployment](deployment.md) — publishing the bundle

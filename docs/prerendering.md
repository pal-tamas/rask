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

### The skipped routes still work — through `404.html`

A route that was skipped is still served by the bundle at run time, but only if the host hands the
browser something that can boot. That is not automatic, and it used to be actively broken by
prerendering itself: **the root route's own output IS `index.html`**, the same file a static host
falls back to for an unknown path. Once the pass had run, a deep link to a skipped route got the
**home page**, fully rendered, and the bundle booted into a document already describing a different
page. Before prerendering, that same link got an empty shell and routed correctly.

So the pass copies the untouched shell to **`404.html`** before it writes any page:

```
[Rask.Prerender] wrote the neutral boot shell to 404.html
```

`404.html` because every static host this targets already reaches for it — GitHub Pages, Netlify,
Cloudflare Pages, S3 — so it costs no configuration. With no boot shell to copy, none is written:
inventing one from a prerendered document would serve the home page for every unknown path, which is
the failure this exists to prevent rather than a milder version of it.

A parameterised route cannot be enumerated without knowing the values, and a catch-all is a 404
page at best. The skipped list is reported rather than logged at debug, and reported **even when it
is empty**, because a pass that quietly covered a site's static half would read exactly like one
that had covered all of it.

## A page that throws or stalls is deliberately not written

Two more lines you may see:

```
[Rask.Prerender]   /media-devices threw — not written: JSException: no browser here
[Rask.Prerender]   /fullscreen did not settle in 30s — not written
```

**This is the design working, not failing.** Both cases still hand back perfectly ordinary HTML — a
faulted render returns the root boundary's error document, and a timed-out one returns whatever
placeholder was on screen when the budget ran out. Writing either would publish it under the
route's own name with nothing saying so, and **a baked spinner is worse than no prerender at all,
because it looks prerendered**. The route is skipped and the bundle still serves it at runtime, so
this costs an optimisation rather than breaking the page.

**Read the exception on the `threw` line.** It is the only notice you get: the route ships as the boot
shell, which is correct for a visitor and blank for a crawler, so a skip is invisible in a browser and
visible only to search. The two causes worth knowing apart:

- *A browser-only API during render.* A media-device or fullscreen demo has nothing to bind to off a
  browser. Guard the work behind a lifecycle hook that only runs in the browser if you want the route
  prerendered.
- *Something the companion does not have.* The companion is a second compilation of your sources, and
  anything they READ at render time has to travel with them — an `EmbeddedResource` most often. Those
  are carried automatically; a `<Content>` file read off disk at render time is not, and reports here
  as a `FileNotFoundException` rather than as a build error.

If the pass writes **no** pages at all, the build raises a warning — because the pass reports what it
skipped and carries on, so "it ran" and "it produced something" are different questions.

The warning asks the **pass** how many pages it wrote, not the filesystem. Asking the filesystem
cannot work here: the root route's own output is `wwwroot/index.html`, which is exactly where the boot
shell already is. That file is present whether the pass wrote every page, one page, or none — so a
publish that prerendered nothing looked identical to one that worked, which is the single thing this
guard exists to tell apart. The pass therefore prints a line the build reads back:

```
[Rask.Prerender] result written=19 skipped=3
```

A run that reports no such line at all is warned about separately: that is a different failure from
reporting zero, and saying nothing would restore the silence the guard is for.

**A route table with no literal routes writes nothing.** The plan is built from the registered routes,
so an app whose root component carries no `[Route]` — one that simply does
`host.RunAsync<App>()` — has nothing to enumerate and produces no pages. Give the page a
`[Route("/")]` and let `App` render the `Router`.

## Each page is spliced into the boot shell, not written over it

The pass writes into the published `wwwroot`, where `index.html` is already the shell the WebAssembly
SDK has just filled in: the fingerprinted import map, the integrity-pinned preload, the
`<base href>`, and `<script src="main.js">`. **The shell is kept and the render is spliced into it.**

That is not a detail. On the Server the boot script comes from an `IRaskRuntimeScript` registration,
but the WASM host deliberately registers none — the runtime boots from the page shell — and the
import map's fingerprints and integrity hashes are minted by the SDK per publish, so managed code has
nothing to reproduce them from. Writing the rendered document over the shell would publish a page
with real markup and no way to become interactive, on **every** prerendered route.

What is taken from each side:

| From the shell | From the rendered page |
| --- | --- |
| `<base href>`, `<meta charset>` | `<title>`, and every other `<head>` contribution |
| the import map, the preload, every `<script>` in the body | the whole `<body>` |

The singleton tags are resolved rather than concatenated: a browser takes the **first** `<title>`, so
appending the page's head to the shell's would leave every page titled whatever the shell says. The
page's title wins; the shell's `<base>` wins.

The runtime then does what it always did — morphs its first real render onto the document, exactly as
it morphed over the boot spinner. The prerendered body is the placeholder that morph replaces; it is
simply a useful one.

If there is no shell in the output directory, the whole document is written instead and the pass says
so. That page will not boot, which is the right outcome for a caller driving
[the engine directly](#using-the-engine-directly) with no bundle to boot.

## How it runs

A browser-wasm assembly cannot execute on the desktop, so the app's own sources are compiled a
**second time for `net10.0`** into a companion project under `obj/rask-prerender/`, and that is
what renders. The companion carries the app's own `ProjectReference`s, `PackageReference`s, `<Using>` items and
`EmbeddedResource` items, so it reaches the framework exactly the way the app does and reads the same
resources at render time.

It compiles **`Program.cs` too**, deliberately: that file is where the app registers its services,
and a page that injects anything would otherwise find nothing registered.
`WasmHostBuilder.RunAsync` sees the `RASK_PRERENDER_OUT` environment variable and prerenders
instead of booting — so the app's real entry point drives the pass, and there is no second place to
keep registrations in sync.

Generated files under `obj/` are rewritten on every publish; edit the app, never the companion.

The companion builds with **warnings-as-errors off**, for the same reason its analyzers are off and
one stronger one: *its reference closure is not the app's*. Targeting `net10.0` makes a multi-targeted
dependency resolve its non-browser face — the `Rask` metapackage's `net10.0` face carries the
server-only pieces a browser app never saw — so two components that never met in the app can meet
here. That is a fact about the companion, not about your code, and the real build is what judges your
code.

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
before writing anything to disk** — for the reason above, both return ordinary-looking HTML — and
**report `result.Error`**, which carries what threw. Refusing a page is a decision the pass can make on
its own; explaining it is not, and a skipped route is invisible until someone checks the sitemap.

## In this repo

`site/Rask.Site` — the app behind [rask.sh](https://rask.sh) — is the in-repo consumer. All 20 of its
prerenderable routes are written; the three it skips are the two parameterised ones and the catch-all.

It is also where the resource bug above was found, and worth repeating as a shape: **16 of those 20
routes were being skipped**, on a green publish, because the companion did not carry the app's embedded
sources. Nothing in a browser looked wrong. The count on the `result written=` line was the only
symptom.

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

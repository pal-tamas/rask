# Meta framework front ends

Rask hosts a **meta framework** — Nuxt, TanStack Start, SolidStart, SvelteKit, Analog or Next.js —
that owns the *whole* front end: its own routing, its own rendering, its own Node server. Rask is the
backend it integrates with, and the two ship as **one container on one port**.

```xml
<RaskMetaFramework>nuxt</RaskMetaFramework>
```

```csharp
builder.Services.AddRaskMeta();

app.MapRaskCqrs();   // map your API FIRST
app.UseRaskMeta();   // everything else goes to the framework
```

That is the whole surface. The framework is named once, in the project file, because the build needs
it there anyway — and baking it into the assembly is what lets `AddRaskMeta()` take no argument and
still be certain it is fronting the framework that was actually built.

## Which lane is this

Rask has three ways to put a front end in front of your C#, and they are not variations on each other:

| | Front end | Node at runtime |
|---|---|---|
| [Islands](islands.md) | a `.tsx`/`.vue`/`.svelte` file as *one Rask component* | no |
| [TypeScript front ends](spa.md) | a static bundle Rask serves | no |
| **This page** | **the framework's own Node server** | **yes** |

Pick this one when you want the meta framework's server-side story — its router, its loaders, its
server functions — and Rask underneath it. Pick [the SPA lane](spa.md) when a static bundle will do:
it needs no Node in the deployed image at all, which is a real advantage to give up deliberately
rather than by accident.

## The shape

```
container, one public port :8080

  browser ──▶ Kestrel :8080
               ├─ /_rask/*        your API, in process
               ├─ built assets    served by Kestrel, never forwarded
               └─ /*  ──forward──▶ node :3000  (127.0.0.1 only)
```

Kestrel keeps the public port, so ASP.NET authentication, rate limiting, logging and health checks
still sit in front of every request. The framework's server is a **supervised child process** bound to
loopback: publishing the container's ports cannot expose an unauthenticated renderer beside your app.

`UseRaskMeta()` registers a **fallback**, so anything you mapped first still wins. Map your API
before it — the symptom of getting that backwards is an API call answered with a rendered page.

## Six frameworks, three server shapes

The frameworks converge, which is why this is a table rather than six integrations:

| `RaskMetaFramework` | Build | Server entry | Client assets |
|---|---|---|---|
| `nuxt` | Nitro | `.output/server/index.mjs` | `.output/public` |
| `tanstack-start` | Vite → Nitro | `.output/server/index.mjs` | `.output/public` |
| `solidstart` | Vite → Nitro | `.output/server/index.mjs` | `.output/public` |
| `analog` | Vite → Nitro | `dist/analog/server/index.mjs` | `dist/analog/public` |
| `sveltekit` | `adapter-node` | `build/index.js` | `build/client` |
| `nextjs` | `output: 'standalone'` | `.next/standalone/server.js` | `public`, `.next/static` |

All six read `PORT` from the environment and expose a single directly executable entry, which is why
the supervisor runs `node <entry>` and **never `npm start`** — npm would spawn the real server as a
grandchild and orphan it when the container stops.

**Next reads `HOSTNAME` where every other one reads `HOST`.** One word, and the kind of difference
that silently produces a server listening on `0.0.0.0` when the entire point is that only Kestrel can
reach it.

**TanStack Start is pinned to the Vite bundler**, not Rsbuild: Rsbuild emits a fetch-style entry that
needs a separate Node host in front of it, which would be a fourth server shape.

## Where the front end lives

```
MyApp/
  MyApp.csproj
  Program.cs
  Client/            <- the meta framework app
```

A `Client` **folder** inside the host, not a sibling `.Client` **project**: one project owns both
halves, because a meta framework app has no separate client artifact for a host to reference — it has
a server of its own.

Override with `<RaskMetaAppDir>`; the same value is where the built front end lands inside the publish
output, so one relative path is correct both when you `dotnet run` from the project and in the
published app.

## What the build does

`dotnet build` runs the framework's own toolchain — `npm ci` (or `npm install` when there is no
lockfile), then `npm run build`. Both steps are incremental, and `npm ci` has its own up-to-date check
because it is the expensive one.

`dotnet publish` copies the framework's build output next to the app, preserving its layout.

| Property | Default | |
|---|---|---|
| `RaskMetaFramework` | — | **Required.** Nothing happens without it. |
| `RaskMetaAppDir` | `Client` | Where the front end lives, relative to the project. |
| `RaskMetaBuild` | `true` | `false` skips node entirely — the app still compiles and its API still works. |
| `RaskMetaPublishDir` | `$(RaskMetaAppDir)` | Where the built front end lands in the publish output. |
| `RaskMetaMinimumNode` | `22.12.0` | The floor the build enforces, rather than letting the toolchain fail later. |
| `RaskMetaBuildCommand` | `npm run build` | |

Use the current **Active LTS** of Node. The floor above is a minimum, not a recommendation, and
several of these frameworks set their own bar well above it and enforce it themselves.

## Assets are served by Kestrel

Every one of these frameworks content-hashes its client assets, and Kestrel serves them directly:
one hop less per asset, and the immutable cache headers written for you.

This matters most for **Next**, whose standalone output deliberately omits `public` and `.next/static`
because it assumes a CDN in front. Here Kestrel *is* the thing in front, so the omission stops being a
problem instead of needing a hand-written `cp` in your Dockerfile.

The rule is **a file on disk**, not the shape of the URL. A generated `/sitemap.xml` or an API route
ending in `.json` still reaches the framework, because nothing that is not on disk is treated as
static.

## Calling your C#

Your message records are projected into TypeScript on every build, into the same directory the
browser layer lands in. The front end dispatches through them:

```ts
import { rask } from '@rask/client'
import { getOrder } from '@rask/messages'

const order = await rask.dispatch(getOrder({ id }))
```

`order` is typed from the C# record. Rename a property there and this stops compiling, which is the
entire point — there is no schema file to keep in sync and no wire name written at a call site.

This is the same generated wire [the SPA lane](spa.md) has had; the difference is that until now this
package did not deliver it. `RaskEmitTypeScript` was defaulted and made visible to the compiler only
by `Rask.Spa.Hosting`, so a meta host referencing `Rask.Cqrs.Server` got no contracts at all — no
error, no warning, nothing to notice.

### From the server render

The half that is specific to this lane. A route module in `Client/` runs in **Node** before it ever
runs in a browser, and two things differ there: a relative URL has no origin to resolve against, and
there is no cookie jar — so a dispatch from a loader is anonymous unless you say otherwise.

Both are options on the transport rather than new API:

```ts
import { createDispatcher, httpTransport } from '@rask/client'

// In a loader / server function, where `request` is the framework's incoming request.
const rask = createDispatcher(httpTransport({
  baseUrl: process.env.RASK_BASE_URL,
  onRequest: (outgoing) => {
    const cookie = request.headers.get('cookie')
    if (cookie) outgoing.headers.set('cookie', cookie)
    return outgoing
  },
}))
```

`RASK_BASE_URL` is injected into the Node process by the host, and carries whatever you set:

```csharp
builder.Services.AddRaskMeta(o =>
{
    o.Framework = MetaFramework.Next;
    o.BaseUrl = "http://127.0.0.1:5000";   // where this host listens
});
```

Point it at the loopback address this host is listening on and an SSR dispatch never leaves the
container. It is a configured value rather than one derived from the incoming request, and
deliberately so: a header an attacker can influence, turned into the destination of a request that
carries the visitor's cookie, is a confused deputy. Leave it unset and dispatch from the browser only
— `httpTransport` then resolves relative URLs against the page, as it does on the SPA lane.

## Browser APIs

Rask ships typed wrappers over the browser's Web APIs, and on a Rask component front end you inject
them as C# services. Here the front end is TypeScript, so you get the layer underneath them instead:
the same modules, imported directly.

```ts
import { getCurrentPosition } from '@rask/browser/geolocation'
import { prefersDark } from '@rask/browser/mediaQuery'

const fix = await getCurrentPosition({ enableHighAccuracy: true })
```

They are copied into your app on every build — into `app/rask/browser/` for Nuxt and Next, and
`src/rask/browser/` for SvelteKit, SolidStart, TanStack Start and Analog, because those are where each
framework keeps source. `RaskMetaGeneratedDir` moves them if your app is laid out differently.

**`@rask/*` is what you write, whichever framework you picked.** The physical directory differs; the
import should not. A `tsconfig.rask.json` is written beside the modules with the mapping, so one line
in your own `tsconfig.json` turns it on:

```json
{ "extends": "./src/rask/tsconfig.rask.json" }
```

**This is the same code Rask's own Server and WASM clients run.** It is not a TypeScript port kept in
step by hand: the C# `IGeolocation` reaches the browser by calling into these very modules, so a quirk
fixed for one caller is fixed for the other in the same commit.

**They are safe to import in a server render**, which on this lane is not a footnote — every route
module in `Client/` is loaded by Node before it is ever loaded by a browser. Nothing in the layer
touches `window` or `document` at import time, and a test asserts it by importing every module in a
process that has neither. Calling one still needs a browser, as it would anywhere.

For which APIs ship a module and which you should simply call on the platform — `navigator.clipboard`
and `localStorage` need no wrapper — see the third column of the
[capability matrix](browser-capabilities.md).

## Development

`rask dev` runs `dotnet watch` alongside the framework's own dev server, and **the browser talks to
the dev server** — so hot module replacement is native and full-speed, with Rask nowhere in its path.
The dev server proxies `/_rask` back to the host.

```
browser → :3000 (nuxt dev, native HMR)
            └── /_rask/* → :5000 (dotnet watch)
```

In production neither half of that exists: Kestrel owns the port and forwards to the supervised
process on loopback.

## When the front end will not start

- **No built front end** fails startup with a message naming the path it looked for. It is a
  configuration mistake, and it says so rather than dying later with something unrelated.
- **Before the port answers**, requests get `503` with `Retry-After` rather than a `502` from
  forwarding into a closed socket. For the first seconds of a container's life that state is normal.
- **A crash** is retried with capped exponential backoff. The budget counts *consecutive* failures, so
  a server that has been up for a week and crashes once is not mistaken for one that will not start.
- **When the budget is spent the host stops.** An orchestrator restarting the container is a better
  supervisor than a loop inside the app, and an exit is visible where a degraded process that still
  answers health checks is not.

Set `SuperviseNode = false` to forward to a front end you are running yourself.

## The honest cost

[The SPA lane](spa.md) can say that in production there is one process, one port and no Node at all.
This lane keeps the one port and the one container, but **your image now carries a Node runtime and a
second process for the life of the deployment**. That is inherent to asking a meta framework to render
your pages, not a gap in the framework — but it is the thing to weigh before choosing this over a
static bundle.

## See also

- [`docs/spa.md`](spa.md) — a TypeScript SPA with a typed connection to your C#, no Node at runtime.
- [`docs/islands.md`](islands.md) — a single front-end component inside a Rask page.
- [`docs/cqrs.md`](cqrs.md) — the mediator and the wire your front end dispatches through.

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

## Signing people in

The visitor's cookie reaches your Node process on every proxied request — and **Node cannot read it**.
It is an ASP.NET Data-Protection cookie: encrypted, signed, and openable only by a process holding the
key ring. Node is not a .NET process and has no key ring, so to the front end the cookie is an opaque
string it forwards and nothing more.

That is not a gap to work around. It is what keeps the session's authority on the side that can
enforce it.

### From the browser

Client-side code talks to the [accounts endpoints](authentication.md) directly, exactly as it would in
any other front end:

```
POST /api/auth/register   POST /api/auth/login   POST /api/auth/logout   GET /api/auth/me
POST /api/auth/forgot-password   POST /api/auth/reset-password   POST /api/auth/confirm-email
```

Same origin, so the `HttpOnly` cookie rides on its own; `X-Rask-Auth` is required on every
state-changing call. See [the SPA guide](spa.md#signing-people-in) — the contract is identical,
because it is the same contract.

> **Map them before `UseRaskMeta()`.** That call ends the pipeline with a fallback that forwards
> *everything* unmatched to Node, so an endpoint mapped after it never runs. Your own API has the same
> rule for the same reason.

### From server-side rendering

This is the part worth reading twice. When a page renders on the Node side and needs to know who is
looking at it, the front end **calls back into your C# app** — over loopback, carrying the visitor's
own cookie — and lets the side that can decrypt it answer:

```ts
// A server-side load function, in whichever framework's spelling.
import { auth } from './rask/browser'

const user = await auth.me({
  baseUrl: process.env.RASK_BASE_URL,
  headers: { cookie: request.headers.get('cookie') ?? '' },
})
// CurrentUser, or null when nobody is signed in.
```

The module runs here for the same reason it runs in the browser: nothing in Rask's browser layer
touches `window` at import time, so a server render can import it. `baseUrl` and `headers` exist for
exactly this call — node has no page origin and no cookie jar, so both are yours to supply.

`RASK_BASE_URL` is injected by the host (`MetaHostingOptions.BaseUrl`) and points at Kestrel on
loopback. Two properties of it are deliberate:

- **It is never derived from a request header.** A destination an attacker can influence, combined
  with a request that carries the visitor's cookie, is a confused deputy: you would be handing
  somebody else's session to a server of their choosing. It comes from configuration, so it cannot be
  moved by a request.
- **Node listens on `127.0.0.1` only.** Publishing the container's ports cannot expose the renderer,
  so nothing reaches it except through Kestrel — which is where authentication happens.

### What this buys

No token is ever held in JavaScript, on either side. The browser cannot read the cookie, the Node
process cannot open it, and the only code that resolves an identity is the code that also enforces
`[Authorize]`. A front end compromise leaks what the front end could already see, and no more.

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

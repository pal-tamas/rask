# TypeScript front ends

Rask hosts a **TypeScript** single-page app and gives it a typed connection to your C#. You write the
message records once, in C#; the TypeScript the browser imports is generated from them on every
build. There is no schema file to keep in sync, no client SDK to publish, and no wire name spelled
out at a call site.

The **framework** is yours to pick. The **language** is not: a client with no TypeScript
configuration is refused at build time with [RASKSPA004](#typescript-only), because the whole of what
this gives you is checked by a compiler you would not be running.

```bash
rask new Shop --template react     # or preact, solid, svelte, lit
cd Shop
rask dev
```

| `--template` | Scaffolded from | TanStack Query | TanStack Router |
|---|---|---|---|
| `react` | `create-vite --template react-ts` | `@tanstack/react-query` | ✅ `@tanstack/react-router` |
| `preact` | `create-vite --template preact-ts` | `@tanstack/react-query` ¹ | — |
| `vue` | `create-vite --template vue-ts` | `@tanstack/vue-query` | — |
| `solid` | `create-vite --template solid-ts` | `@tanstack/solid-query` | ✅ `@tanstack/solid-router` |
| `svelte` | `create-vite --template svelte-ts` | `@tanstack/svelte-query` | — |
| `lit` | `create-vite --template lit-ts` | `@tanstack/lit-query` | — |
| `angular` | `ng new` (the Angular CLI) | `@tanstack/angular-query-experimental` | — |

¹ There is no `@tanstack/preact-query`, and there does not need to be: create-vite's Preact template
already maps `react` and `react-dom` to `preact/compat` in its tsconfig, and `@preact/preset-vite`
does the same at build time, so the React adapter type-checks and bundles unchanged.

The set is exactly the frameworks **TanStack Query** ships an adapter for. Below the call site every
one of them is the same wire; the adapter is what makes the generated contracts worth having.

**Angular keeps its own CLI.** Angular's build *is* Vite-based — `@angular/build:application` has run
its dev server on Vite since v17 — but `create-vite` has no Angular template and the Vite config
belongs to Angular rather than to you. So `rask new --template angular` runs `ng new`, and three
things differ as a result: there is no `vite.config.ts` (the dev proxy is `proxy.conf.json`, pointed
at from `angular.json`), the dev server is `ng serve` on **4200**, and the bundle lands in
`dist/<project>/browser` — which the scaffolded host is told about with `RaskSpaDistDir`. The Angular
CLI also has its own Node floor, higher than Vite's; it says so itself if yours is too old.

### Which Node

**Use the current LTS.**

```bash
nvm install --lts && nvm alias default 'lts/*'
```

The build's own floor is **22.12**, and the build enforces it — an older Node fails with
`RASKSPA005` naming the version it found, rather than reaching `vite` and failing there with an
`engines` error nobody reads.

That number is not arbitrary. Vite asks for `^20.19.0 || >=22.12.0`, which is a range with a hole in
it: 21.x and 22.0–22.11 satisfy neither arm. 22.12 is the lowest version that satisfies it with no
hole — and the lowest still on a line Node patches, since Node 20 "Iron" reached end of life on
2026-03-24. A floor is a minimum rather than a recommendation, which is why it sits below what Rask
actually recommends: the scaffolded `Dockerfile` installs the current **Active LTS** (24 "Krypton"),
and so should you.

The scaffolders move faster than the floor does, and Angular's CLI moves fastest of all — 22.1.6 asks
for `^22.22.3 || ^24.15.0 || >=26.0.0`, well above anything Rask insists on. It enforces that itself
and says so in its own words; Rask does not try to track it.

Set `RaskSpaMinimumNode` if you want the build to insist on more than Rask does — it is a real
comparison, so raising it raises the bar.

**TanStack Router comes wired up for React and Solid**, because those are the two adapters it ships.
The routes are declared in code, in `src/router.tsx`, rather than through the file-based plugin —
that plugin wants to own `src/routes/`, and this client is scaffolded by somebody else. Nothing stops
you switching to it later. For the others Rask scaffolds no router at all rather than picking one on
the framework's behalf, in a template whose whole argument is that the framework's own conventions
win.

`rask new --template react` runs the framework's **own** scaffolder — `create-vite` — and overlays
four files onto what it produces. Everything else in the client is whatever Vite ships today. That
is deliberate: a React skeleton Rask maintained by hand would be a worse React skeleton within a
release or two, and it would not be what a React developer recognises.

It asks `create-vite` for its **TypeScript** template — `react-ts`, never `react`. The cost is
stated rather than hidden: these templates need **Node.js and a network** at `rask new` time, where
the C# templates need neither.

## TypeScript only

A client that generates no `tsconfig.json` fails the build:

```
error RASKSPA004: Rask.Spa.Hosting: 'Shop.Client' has no tsconfig.json, and Rask generates
TypeScript contracts into it. Rask supports TypeScript single-page app clients: scaffold the
client from its framework's TypeScript template (`npm create vite@latest -- --template react-ts`),
or point RaskSpaTypeScriptConfig at the config it does have.
```

This is a refusal rather than a warning because the alternative is worse than no support at all. A
JavaScript client *can* import the generated files — Vite transpiles a `.ts` module whatever the
project is — and gets none of what they are for: no inferred result type on `dispatch`, no compile
error when a C# property is renamed, no refusal when a command is handed to `raskQuery`. Every
guarantee on this page is a **compile-time** one. Half of it, delivered silently, reads exactly like
all of it right up to the moment the wire disagrees.

Two ways out, and both are honest ones:

- The client is TypeScript but keeps its config elsewhere — a monorepo base config, or a
  `tsconfig.app.json` with no plain `tsconfig.json` beside it. Name it:
  `<RaskSpaTypeScriptConfig>tsconfig.app.json</RaskSpaTypeScriptConfig>`.
- You want the **hosting** and not the contracts — an existing front end, in any language, that you
  would like served properly. Set `RaskEmitTypeScript=false`. Nothing is generated, the check does
  not apply, and `UseRaskSpa` serves the bundle exactly as before; it has no opinion about what
  produced it.

## What you get

| | |
|---|---|
| `Shop.Server/` | The ASP.NET host: your message records, their handlers, and the JSON endpoint the client dispatches through. |
| `Shop.Client/` | The client, as `create-vite` scaffolds it, plus Rask's overlay — at most four files: a Vite config for the dev proxy, an entry that installs the `QueryClient`, the component that dispatches, and (React and Solid) its routes. |
| `Shop.Client/src/rask/` | Generated on every build. Gitignored. |

## The call site

A message factory carries its own wire name and its own result type, so `dispatch` infers what
comes back:

```ts
import { rask } from './rask/client'
import { getGreeting } from './rask/messages'

const greeting = await rask.dispatch(getGreeting({ name: 'Ada' }))
//    ^? Greeting — inferred from the message, no cast
```

Rename a property on the C# record and this line stops compiling. That is the whole point of
generating the types rather than describing them.

With TanStack Query, which the template wires up:

```tsx
const { data, isPending } = useQuery(raskQuery(getGreeting({ name })))
const visit = useMutation({
  ...raskMutation(recordVisit),
  onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
})
```

`raskQuery` accepts only a **query**. Handing it a command is a compile error — the same thing the
server enforces by answering `405` to a command sent as a `GET`.

Invalidation uses `getGreeting.messageName` rather than a string literal, so renaming the record
moves the cache key with it.

### The one thing that differs per framework

`raskQuery` and `raskMutation` return plain options objects and import nothing from TanStack, so the
same two calls work under every adapter. What differs is how the adapter wants them:

```ts
useQuery(raskQuery(getGreeting({ name })))                  // React, Preact
useQuery(() => raskQuery(getGreeting({ name: name() })))   // Solid
useQuery(computed(() => raskQuery(getGreeting({ name: name.value }))))   // Vue
createQuery(() => raskQuery(getGreeting({ name })))        // Svelte
injectQuery(() => raskQuery(getGreeting({ name: this.name() })))         // Angular
createQueryController(this, () => raskQuery(getGreeting({ name: this.name })))   // Lit
```

The thunk is not a formality. It is what lets the options re-read the signal, the ref, the rune or
the reactive property and refetch when it changes — pass the object directly in Solid, Svelte, Vue or
Angular and it reads the value once, at setup, and never again. The scaffolded starter already does this correctly
for whichever framework you picked.

## Dates

The generated types give you real `Date` objects, and only where the C# type actually said so.

| C# | TypeScript | Why |
|---|---|---|
| `DateTimeOffset` | `Date` | A true instant, which is exactly what `Date` is. |
| `DateTime` | `Date` | Unambiguous only if its `Kind` is `Utc` or `Local` — see the warning below. |
| `DateOnly` | `DateOnly` (a `string`) | A calendar fact, not an instant. |
| `TimeOnly` | `TimeOnly` (a `string`) | A time of day. Seven fractional digits, which `Date` cannot parse. |
| `TimeSpan` | `Duration` (a `string`) | A length, not a point. `[-][d.]hh:mm:ss[.fffffff]`, not ISO-8601. |
| `byte[]` | `Base64` (a `string`) | Base64, as the wire carries it. |

**`DateOnly` stays a string on purpose.** `new Date("2026-08-25")` is parsed as UTC midnight, so
anyone west of UTC renders it as the **24th**. A date somebody picked in a calendar is not a point
in time, and making it one reintroduces a bug this repo has already fixed once.

**Prefer `DateTimeOffset` to `DateTime`** on anything a front end reads. A `DateTime` with
`DateTimeKind.Unspecified` writes an ISO string with no suffix, and modern JavaScript parses that as
**local** time — so the same payload means a different instant on every machine that reads it.

### How the revival works

Not with a regex. The usual `JSON.parse` reviver tests every string against a date-shaped pattern
and converts anything that matches — including a product code, an ETag, or a free-text field that
happens to look like a timestamp, silently.

Rask does not have to guess. The generator walks the same wire model the C# codec is built from and
emits a descriptor naming exactly the date-bearing properties:

```ts
export const shapes = {
  Order: { instants: ['placedAt'], nested: { lines: ['Line', 1] } },
  Line: { instants: ['shippedAt'], nested: {} },
} as const
```

The client revives precisely those. The number beside a nested shape is how many arrays or
dictionaries stand between the property and it — `Dictionary<string, Line>` and `Line` both arrive
as plain objects, and without the count the walk would revive a dictionary's own keys as if they
were the shape's properties.

### Sending one back

Nothing is needed. `JSON.stringify` already writes a `Date` through `toJSON`, which is
`toISOString()`: always UTC, always with a `Z`. So a value sent from the browser is never ambiguous.

One consequence worth knowing: a round trip **normalises** a `DateTime` with an unspecified `Kind`
into UTC.

### Displaying one

That is your app's job, and the browser already does it well:

```ts
new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(order.placedAt)
```

`undefined` means the visitor's own locale, and the browser's own time zone is the default — which
is the right answer on a front end, and the reason none of the C#-side timezone machinery applies.

## Development

`rask dev` starts two processes: `dotnet watch` for the host, and the client's own dev server. **The
browser talks to the dev server** — `http://localhost:5173` for Vite, `:4200` for Angular — and its
proxy forwards `/_rask` back to the host on `:5000`. Which script starts it (`dev` for Vite, `start`
for Angular) is read from the client's own `package.json`, and which port to open from the property
the scaffold baked into the host, so neither is assumed.

```
browser → :5173  (vite, native HMR)
            └── /_rask/* → :5000 (dotnet watch)
```

HMR is native and instant, and the browser only ever sees one origin — which is why there is no CORS
to configure. In production neither half of that exists: the host serves the built bundle and
answers `/_rask` itself, on one port.

The generated contracts are written on **every** build, including under `rask dev` — a dev server
compiling the previous build's contracts is exactly the failure this pipeline exists to prevent.

## Building and publishing

`dotnet build` runs the client's own toolchain: `npm ci` (or `npm install` when there is no
lockfile), then `npm run build`. Both steps are incremental. `dotnet publish` copies the bundle into
`wwwroot` next to the app, so a deployed container carries the front end rather than a path that
only existed on the build machine.

**Svelte is the one template whose `build` script Rask rewrites.** create-vite gives it a bare
`vite build`, with type checking in a separate `check` script — `tsc` cannot read a `.svelte` file.
Left alone, renaming a C# property would break nothing at build time and surface on the wire, which
is exactly what the generated contracts exist to prevent, so `build` becomes
`svelte-check --tsconfig ./tsconfig.app.json && vite build`. Every other framework's template already
runs its type-checker in `build`.

`-p:RaskSpaBuild=false` skips node entirely. The app still compiles, its API still works, and the
site serves a page saying there is nothing built yet. Use it on a machine with no node, or in a CI
job that only cares about the C#.

## Serving it yourself

`Rask.Spa.Hosting` works on any ASP.NET app, with or without the rest of Rask — and on this side of
the line the front end's language is its own business, since nothing is generated into it:

```csharp
app.MapRaskCqrs();   // map your API FIRST
app.UseRaskSpa();
```

**Order matters.** `UseRaskSpa` ends the pipeline with a fallback to `index.html`; an endpoint mapped
after it is shadowed by that fallback rather than reached, and the symptom is an API call answered
with HTML.

What it does beyond `UseStaticFiles` + a fallback:

- **A missing asset stays a 404.** The naive SPA fallback answers every unmatched request with
  `index.html`, so a missing module import arrives as HTML and the browser reports
  `Failed to load module script` — which reads as a broken framework rather than a missing file.
  Requests under a content-hashed prefix, and requests whose `Accept` asks for something other than
  HTML, are refused instead.
- **Cache headers per bundler.** The hashed prefix the bundler guarantees is consulted first, with a
  filename heuristic as the fallback for Angular (which hashes at the dist root). `index.html` is
  never cached, whatever the rules say — freezing it strands a visitor on the deploy they first saw.
- **Precompressed siblings.** A `.br` or `.gz` beside a file is served when the client accepts it,
  keeping the real content type.
- **In development, no build output is explained rather than failed.** 200 with a page naming the dev
  server, not a 503 — the bundler is serving the app, so a server error would send you hunting a bug
  that is not there. Outside development it is a 503, because then it is a real deployment fault.

### Options

```csharp
app.UseRaskSpa(configure: options =>
{
    options.DevServerUrl = "http://localhost:5173";
    options.ImmutablePathPrefixes.Add("/static/");
});
```

### MSBuild properties

| Property | Default | |
|---|---|---|
| `RaskSpaClientDir` | the `.Server` → `.Client` convention | Where the front end lives. |
| `RaskSpaDistDir` | `dist` | The bundler's output. Angular nests it: `dist/<app>/browser`. |
| `RaskSpaGeneratedDir` | `src/rask` | Where the generated contracts land, inside the client. |
| `RaskSpaBuild` | `true` | `false` skips node entirely. |
| `RaskSpaMinimumNode` | `22.12.0` | The Node floor the build enforces, as `RASKSPA005`. |
| `RaskSpaPublishDir` | `wwwroot` | Where publish puts the bundle. |
| `RaskEmitTypeScript` | on when a client is resolved | Whether the contracts are generated at all. `false` also lifts the TypeScript requirement — see [above](#typescript-only). |
| `RaskSpaTypeScriptConfig` | `tsconfig.json` | The client's TypeScript config, relative to the client. Its presence is what RASKSPA004 checks. |

## Adding a message

Add a record and a handler:

```csharp
public sealed record Order(Guid Id, DateTimeOffset PlacedAt, DateOnly DeliverBy);

public sealed record GetOrder(Guid Id) : IQuery<Order>;

public sealed class GetOrderHandler : IQueryHandler<GetOrder, Order>
{
    public Task<Order> HandleAsync(GetOrder query, CancellationToken cancellationToken) => /* … */;
}
```

The next build writes `getOrder` into `src/rask/messages.ts` and `Order` into `contracts.ts`. If a
property has no wire encoding, the build fails with **RASK053** naming it — a shape that cannot cross
is reported at compile time rather than on the wire.

A message that is never sent anywhere — a job payload, an outbox event — should say so with
`[LocalOnly]`, which exempts it from all of this.

## Styling

Tailwind works here too, and it works the way this ecosystem expects rather than the way the C#
hosts do: `@tailwindcss/vite` and `tailwindcss` land in the client's own `package.json`, the plugin
goes into its Vite config, and the entry stylesheet imports Tailwind.

```bash
rask new Shop --template react
```

The client already has Node, a bundler and a dev server with HMR, so routing its CSS through MSBuild
— which is what [the C# hosts do](tailwind.md) — would be strictly worse. The
scaffolded stylesheet **replaces** create-vite's starter CSS rather than sitting beside it, because
leaving it in would fight Tailwind's own reset.

Replacing it is only half the job, though, and the half that is easy to get wrong. Part of that
starter CSS styles the placeholder page the template has already overlaid away — but the rest styles
`body`, `h1` and `p` **by tag**, and those tags are exactly what the starter still renders. So the
stylesheet Rask writes puts that styling back, in a base layer:

```css
@import "tailwindcss";

@layer base {
  h1 {
    @apply text-3xl font-semibold tracking-tight text-slate-900 dark:text-slate-100;
  }
  /* …and the other elements the starter renders */
}
```

These are ordinary utilities, applied by element instead of spelled out in a `class` attribute —
the starter's markup carries no `class` attributes of its own. Move any rule into
your own markup and delete it; that is the same page. Delete the layer entirely and the page renders
as unstyled text, because Tailwind's preflight removes the browser's defaults on purpose.

## Browser APIs

Rask ships typed wrappers over the browser's Web APIs, and on a Rask component front end you inject
them as C# services. Here you are writing TypeScript, so you get the layer underneath them instead:
the same modules, imported directly.

```ts
import { getCurrentPosition } from './rask/browser/geolocation'
import { prefersDark } from './rask/browser/mediaQuery'

const fix = await getCurrentPosition({ enableHighAccuracy: true })
```

They arrive in `src/rask/browser/` the way `client.ts` does — copied out of the package on every
build, so upgrading Rask upgrades them. Import a module directly, as above, and your bundler keeps
only what you used; or take the namespace form, `import { geolocation } from './rask/browser'`.

**This is the same code Rask's own Server and WASM clients run.** It is not a TypeScript port kept in
step by hand: the C# `IGeolocation` reaches the browser by calling into these very modules. A quirk
fixed for one caller is fixed for the other in the same commit.

Available today — the layer is moving over one API at a time:

| Module | What it wraps |
| --- | --- |
| `browser/cookies` | `document.cookie` — `get`, `getAll`, `set`, `remove` |
| `browser/geolocation` | `getCurrentPosition`, and `watchPosition` returning its stop function |
| `browser/mediaQuery` | `matches`, plus `prefersDark` / `prefersReducedMotion` and a `watch` |
| `browser/networkInformation` | `navigator.connection`, through the vendor-prefixed fallback |
| `browser/permissions` | `query` — a permission's state without prompting for it |
| `browser/screen` | size, available size, colour depth, device pixel ratio |
| `browser/speechSynthesis` | `speak` / `cancel` |
| `browser/storageManager` | `estimate`, `persisted`, `persist` |
| `browser/visualViewport` | the viewport you can actually see once a soft keyboard opens |

Names are idiomatic TypeScript, and where the platform already has a name it keeps it —
`getCurrentPosition`, not `GetCurrentPositionAsync`. Subscriptions hand back a stop function rather
than a disposable:

```ts
const stop = watchPosition(fix => setPosition(fix))
// later, in a cleanup
stop()
```

**Everything the platform gives you already, you should keep taking from the platform.**
`navigator.clipboard.writeText` needs no wrapper in TypeScript, and `lib.dom.d.ts` types it better
than Rask could. These modules exist for the parts that are genuinely awkward — a callback API that
should be a promise, a live object that has to be snapshotted, a vendor-prefixed fallback chain, a
base64url ceremony — and for the parts with a server half, which is the next section.

**They are safe to import in a server render.** Nothing in `src/rask/browser/` touches `window` or
`document` at import time, so a module can be imported at the top of a file that also runs during
SSR. Calling one still needs a browser, as it would anywhere.

## Installable, and push-capable

`--pwa` makes the app installable; `--push` adds Web Push from the ASP.NET host.

```bash
rask new Shop --template react --push     # --push implies --pwa
```

Three files land in the client's `public/`, which every bundler copies to the bundle root verbatim:
`manifest.webmanifest`, `icon.svg`, and `rask-sw.js` — the service worker. `index.html` is patched
with the manifest link and the registration. All of it is the **client's**, so it works under the dev
server too; a host-served worker would 404 during `rask dev`, where the browser talks to Vite and only
`/_rask` is proxied — and a service worker that 404s once is not retried.

**Installable and push-capable, not offline.** The worker handles `push` and `notificationclick` and
nothing else. There is deliberately no app-shell cache: the bundler fingerprints every asset and
rewrites `index.html` each build, so a hand-rolled cache would serve a stale shell pointing at hashed
files that no longer exist — an app that breaks on deploy and heals only after an unregister. Reach
for `vite-plugin-pwa` when you want the offline half; it owns the build and can say what it cached.

Both URLs the patch writes are **root-absolute**, which matters more here than in a server-rendered
app. A SPA serves one document at every route, so a relative `manifest.webmanifest` would resolve
against the current path — 404 on any deep link — and `register("rask-sw.js")` would take its scope
from that path, controlling one sub-tree and never seeing a push.

### The subscription

`--push` also vendors `src/rask/push.ts`, the one browser API worth generating: the endpoints and the
payload belong to your host, not to the platform.

```ts
import { subscribeToPush, unsubscribeFromPush } from './rask/push'

await subscribeToPush()      // null if unsupported, unconfigured, or denied
```

It calls three endpoints the host maps: `GET /_push/key` for the **public** VAPID key, and
`POST /_push/{subscribe,unsubscribe}`. The private key signs and never leaves the server.

The reason it is a vendored file rather than a snippet in this page is one line of it.
`PushSubscription.toJSON()` nests the keys — `{ endpoint, keys: { p256dh, auth } }` — while the host
binds a flat `PushSubscription(Endpoint, P256dh, Auth)`. Post the browser's shape as-is and the
request **still answers 204**: `endpoint` binds, both keys arrive null, and every later send fails to
encrypt for a subscription that looked like it registered. `push.ts` flattens it.

Generate a key pair with `VapidKeys.Generate()` and put it in user-secrets; until you do, `/_push/key`
answers with an empty key and `subscribeToPush()` returns `null` rather than throwing. See
[Web Push](pwa.md).

## See also

- [`docs/tailwind.md`](tailwind.md) — Tailwind on a C# host, with no npm at all.
- [`docs/pwa.md`](pwa.md) — manifests, service workers and Web Push across every host.
- [`docs/cqrs.md`](cqrs.md) — the mediator, the wire protocol, and authorization.
- [`docs/cli.md`](cli.md) — `rask new`, `rask dev`, `rask deploy`.

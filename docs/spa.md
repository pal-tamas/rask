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

| `--template` | Scaffolded from |
|---|---|
| `react` | `create-vite --template react-ts` |
| `preact` | `create-vite --template preact-ts` |
| `vue` | `create-vite --template vue-ts` |
| `solid` | `create-vite --template solid-ts` |
| `svelte` | `create-vite --template svelte-ts` |
| `lit` | `create-vite --template lit-ts` |
| `angular` | `ng new` (the Angular CLI) |

The set is the frameworks `create-vite` ships a TypeScript template for, plus Angular through its own
CLI. Below the call site every one of them is the same wire.

**No data-fetching library, and no router.** The starter calls `rask.dispatch` directly and renders
one view. That is not an omission — a template that picks a cache and a router picks them for every
app scaffolded from it, and those are the two choices a front-end developer is most likely to already
have made. Adding TanStack Query, or a router, is an ordinary `npm install` away, and the generated
contracts are unaffected either way: they describe the wire, not how you call it.

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

`rask new --template react` writes a client that was imported from the framework's **own** scaffolder
— `create-vite`, asked for its **TypeScript** template (`react-ts`, never `react`) — and is committed
under `src/Rask.Templates/`. So it needs **no Node.js and no network**, like every other template, and
the same command produces the same app twice running.

That reverses an earlier decision, and the reasoning is worth stating. Fetching `create-vite@latest`
per scaffold kept the skeleton current, on the argument that one Rask maintained by hand would be
worse within a release or two. What it also did was make the skeleton **invisible**: no manifest in
the repository, nothing to review when the creator changed, and no way for Dependabot to bump a single
front-end dependency — three templates were installing an older Tailwind than the C# host downloads
and nobody could see it. A committed tree that Dependabot keeps current, and that
`scripts/refresh-templates.sh` re-imports on demand, answers the original argument without the cost:
drift becomes a reviewed commit instead of a different tree every morning.

## TypeScript only

A client that generates no `tsconfig.json` fails the build:

```
error RASKSPA004: Rask.Spa.Hosting: 'Shop/client' has no tsconfig.json, and Rask generates
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
| `Shop/` | The ASP.NET host: your message records, their handlers, and the JSON endpoint the client dispatches through. |
| `Shop/client/` | The client, as `create-vite` scaffolds it, plus Rask's overlay — a Vite config for the dev proxy, the entry, and the component that dispatches. No client-side data or routing library: the component calls `rask.dispatch` and holds its own state. |
| `Shop/client/src/rask/` | Generated on every build. Gitignored. |

**One project, with the front end as a folder inside it.** A C#-on-both-halves solution needs a
`.Shared` project because both halves are C# and must compile the same record — but here the client's
half of every contract is *generated TypeScript*, so the messages live in the host and there is
nothing for a second .NET project to hold. The host is `Shop`, not `Shop.Server`: with no sibling to
distinguish it from, that suffix named nothing.

This is the same shape [the meta framework lane](meta.md) uses, so `rask new` produces one
recognisable layout whichever front end you pick.

> **Moving an existing app.** Rename `Shop.Server/` to `Shop/`, move `Shop.Client/` to `Shop/client/`
> (lower case), rename the `.csproj`, and drop `.Server` from the root namespace. The build finds the
> client by convention again after that; `RaskSpaClientDir` is only needed if you keep it somewhere else.
> A capitalised `Client/` is still matched, so an app scaffolded before this rename keeps building.

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

### Adding a cache

The starter fetches on mount and refetches when its input changes, with an `AbortController` so a
slow earlier request cannot land after a later one. That is enough for a starter and deliberately
not a cache.

If you want one, `Rask.Spa.Hosting` vendors `src/rask/query.ts` beside the client — `raskQuery` and
`raskMutation` return plain options objects and import nothing from TanStack, so they work under
every adapter:

```tsx
const { data, isPending } = useQuery(raskQuery(getGreeting({ name })))
const visit = useMutation({
  ...raskMutation(recordVisit),
  onSuccess: () => queryClient.invalidateQueries({ queryKey: [getGreeting.messageName] }),
})
```

`raskQuery` accepts only a **query**. Handing it a command is a compile error — the same thing the
server enforces by answering `405` to a command sent as a `GET`. Invalidation uses
`getGreeting.messageName` rather than a string literal, so renaming the record moves the cache key
with it.

Solid, Svelte, Vue, Angular and Lit want the options wrapped in a thunk, and that is not a formality:
it is what lets them re-read the signal, the ref, the rune or the reactive property and refetch when
it changes. Pass the object directly and it reads the value once, at setup, and never again.

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

**Under VS Code's F5** the host starts the client's dev server itself — the same script on the same port —
because no `rask dev` runs beside an app the debugger launched, and VS Code opens the dev server's address
once it answers. See [debugging in VS Code](cli.md#debugging-in-vs-code).

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

### A Rask WebAssembly app

WebAssembly is a single-page app too, so the same call serves one. `UseRaskSpa` recognises a Rask
WebAssembly bundle from its files — `rask.wasm.js` beside the .NET runtime's `_framework/dotnet*.js` —
and applies what that publish guarantees instead of a bundler's rules:

| Path | Cached for ever | A missing file |
|---|---|---|
| `/_rask/a/*` — scoped CSS and JavaScript, named by content hash | always | 404 |
| `/_framework/*` — the runtime and your assemblies | only when the SDK fingerprinted the name | 404 |
| anything else in `wwwroot`, including an `assets` folder | never — it revalidates | the index document |

The default `/assets/` prefix does not apply to a WebAssembly bundle: it names Vite's hashed directory,
and an `assets` folder in a Rask app's `wwwroot` holds files you wrote. A prefix you add still applies.
Runtime files are served with `application/wasm` and `application/octet-stream`, and
`AddRaskSpaHost()` compresses both.

Reference the client project and the host's build does the rest:

```xml
<ProjectReference Include="..\Shop.Client\Shop.Client.csproj"
                  ReferenceOutputAssembly="false"
                  SkipGetTargetFrameworkProperties="true"/>
```

```csharp
builder.Services.AddRaskSpaHost();

var app = builder.Build();
app.MapRaskCqrs();   // your API first
app.UseRaskSpa();
```

`dotnet build` publishes the client — any referenced project declaring `<RaskWasm>true</RaskWasm>` — and
`dotnet publish` copies its bundle into the host's `wwwroot`, where `UseRaskSpa` finds it with no
arguments. A host serves one client: two WebAssembly clients are refused as `RASKSPA006`, and a
WebAssembly client beside a front-end `client` folder as `RASKSPA007`. The client's framework is read
from its own evaluation, so a `Directory.Build.props` or another .NET version is honoured; a client that
reports no framework is `RASKSPA008`, and one that builds for several is `RASKSPA009`, because a bundle is
published for one.

**`rask dev` serves the client's build output, not its publish.** A published bundle is trimmed, and
trimming turns hot reload off in the browser, so a dev session turns `RaskSpaBuild` off: the client's
publish is skipped, and the host serves what its ordinary build wrote, with hot reload.

**The operator dashboard can sit beside it.** Mount it above the app —
`app.UseRaskServer<RaskDashboardShell>("/_rask/{**path}")` — and both halves share `/_rask/a/{hash}`
safely: the dashboard's endpoint answers a hash its own process never registered from the web root,
where `UseRaskSpa` places the bundle's scoped assets.

### Options

```jsonc
// appsettings.json
{
  "Rask": {
    "Spa": {
      "DevServerUrl": "http://localhost:5173",
      "ImmutablePathPrefixes": [ "/static/" ]   // added to the defaults, not replacing them
    }
  }
}
```

A [Rask WebAssembly app](#a-rask-webassembly-app) reads the same section. Its `/_framework/` and `/_rask/a/`
rules apply whatever the section says, and the `/assets/` default is left out for it until you change the
list.

`Rask:Spa` is read when `UseRaskSpa` maps the app. The two delegates, `ExcludeFromFallback` and
`OnPrepareResponse`, can only be set in code, on the callback — which runs after the section and wins:

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
| `RaskSpaClientDir` | a `Client` folder in the host project | Where the front end lives. |
| `RaskSpaDistDir` | `dist` | The bundler's output. Angular nests it: `dist/<app>/browser`. |
| `RaskSpaGeneratedDir` | `src/rask` | Where the generated contracts land, inside the client. |
| `RaskSpaBuild` | `true` | `false` skips node entirely — and, for a WebAssembly client, its publish: the host serves the client's build output instead. |
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

Tailwind and [daisyUI](ui-kit.md) work here too, and they work the way this ecosystem expects rather
than the way the C# hosts do: `tailwindcss`, its adapter and `daisyui` land in the client's own
`package.json`, and the entry stylesheet loads them.

```bash
rask new Shop --template react
```

The client already has Node, a bundler and a dev server with HMR, so routing its CSS through MSBuild
— which is what [the C# hosts do](tailwind.md) — would be strictly worse. The
scaffolded stylesheet **replaces** create-vite's starter CSS rather than sitting beside it, because
leaving it in would fight Tailwind's own reset.

```css
@layer properties, theme, base, components, daisyui, utilities;

@import "tailwindcss";
@plugin "daisyui";

@layer base {
  body {
    @apply bg-base-200 text-base-content antialiased;
  }
}
```

`@plugin "daisyui"` resolves by name here — this lane has a package tree, unlike the standalone engine
a C# host compiles with. The **version is pinned to the copy `Rask.Ui` vendors**, so a project
scaffolded on a front end and one scaffolded on a C# host compile the same daisyUI and render alike.

The `@layer` statement is not decoration. daisyUI emits into a `daisyui` layer that Tailwind's own
import does not rank, so its position would fall out of wherever it first appears — which lands it
*above* utilities, and then `class="btn px-8"` gives you `.btn`'s padding and quietly ignores the
`px-8`.

**One base rule, where there used to be seven.** The starter's markup once carried no `class`
attributes at all, so the stylesheet had to reach `body`, `h1`, `input` and the rest by tag. It is
written in daisyUI's component classes now, so the only rule left is the one with nowhere else to live:
daisyUI paints `base-100` on `:root`, and something has to put the page's own background behind it.
Delete it and the page loses its ground, because preflight removes the browser's defaults on purpose.

## Browser APIs

Rask ships typed wrappers over the browser's Web APIs, and on a Rask component front end you inject
them as C# services. Here you are writing TypeScript, so you get the layer underneath them instead:
the same modules, imported directly.

```ts
import { getCurrentPosition } from '@rask/browser/geolocation'
import { prefersDark } from '@rask/browser/mediaQuery'

const fix = await getCurrentPosition({ enableHighAccuracy: true })
```

They arrive in `src/rask/browser/` the way `client.ts` does — copied out of the package on every
build, so upgrading Rask upgrades them. Import a module directly, as above, and your bundler keeps
only what you used; or take the namespace form, `import { geolocation } from '@rask/browser'`.

**`@rask/*` is a tsconfig path**, written for you as `src/rask/tsconfig.rask.json` on every build. Add
the mapping to your own `tsconfig.json`:

```json
{ "compilerOptions": { "paths": { "@rask/*": ["./src/rask/*"] } } }
```

Add it **to** whatever `paths` your template already has, rather than extending the generated file:
TypeScript does not merge `paths` across an `extends`, so a `paths` of your own would replace the
inherited mapping entirely and `@rask/client` would stop resolving. Vite reads these through
`vite-tsconfig-paths` where your template includes it; otherwise add a matching `resolve.alias`.

It is the same specifier [the meta lane](meta.md#browser-apis) uses, where the modules land in
whichever source directory that framework prefers — so the import reads the same in both, and moving
between them teaches you nothing new. Relative imports keep working if you would rather not.

**This is the same code Rask's own Server and WASM clients run.** It is not a TypeScript port kept in
step by hand: the C# `IGeolocation` reaches the browser by calling into these very modules. A quirk
fixed for one caller is fixed for the other in the same commit.

Available today — the layer is moving over one API at a time:

| | Modules |
| --- | --- |
| **Storage** | `indexedDb` · `originPrivateFileSystem` · `fileSystem` · `storageManager` · `cookies` |
| **Device** | `geolocation` · `deviceOrientation` · `deviceMotion` · `battery` · `gamepad` · `mediaDevices` |
| **Page** | `mediaQuery` · `visualViewport` · `screen` · `screenOrientation` · `fullscreen` · `pictureInPicture` |
| **Observers** | `intersectionObserver` · `resizeObserver` · `mutationObserver` |
| **Identity & crypto** | `webAuthn` · `crypto` · `permissions` |
| **Coordination** | `broadcastChannel` · `webLocks` |
| **Media & speech** | `mediaSession` · `speechSynthesis` · `speechRecognition` |
| **PWA** | `webPush` · `notifications` · `badge` · `wakeLock` · `installPrompt` |
| **Peer to peer** | `signaling` |
| **Other** | `networkInformation` · `performance` · `eyeDropper` |

Note what is **not** here, because the line matters more than the list. `clipboard` is
`navigator.clipboard.writeText`, `localStorage` is `localStorage`, and `element.animate()` is already a
method on the element — wrapping those would hand you a worse version of what `lib.dom.d.ts` already
types. The same goes for `RTCPeerConnection` and the device APIs (`serial`, `usb`, `hid`, `bluetooth`):
native, well typed, and yours to call. `signaling` is here precisely because it is the exception — the
relay it connects to is Rask's, so it is not something you could write against nothing.

These are the ones you would rather not write. `webAuthn` is the clearest: the platform deals in
`ArrayBuffer`s while every relying party speaks base64url, so the module takes and returns base64url on
both sides and a passkey ceremony is two calls. `originPrivateFileSystem` does ranged reads and writes
into the origin's private tree with `keepExistingData` set — without which a ranged write silently
discards every byte outside the range it wrote. `wakeLock` re-acquires the lock when the page becomes
visible again, because the browser takes it away when the page is hidden and does not give it back,
which is how a recipe left open quietly stops keeping the screen on.

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

## Signing people in

The [accounts battery](authentication.md) is on in the host, and `rask new` maps its endpoints — an app
with a database gets `app.MapRaskAuth()` in its `Program.cs`, before `UseRaskSpa()`, because that call
ends the pipeline with a fallback to `index.html` and an endpoint added after it would answer HTML
instead of JSON. The `rask dev` proxy forwards `/api/auth` alongside `/_rask`, so a sign-in works the
same in development, where the browser is talking to the bundler rather than to Kestrel.

This lane takes **`Rask.Auth.Api`** rather than `Rask.Auth` — the same battery, the same
`AddRaskAuth`/`MapRaskAuth`, the same `Rask.Auth` namespace, minus the built-in sign-in *pages* and
`IAuth`, both of which need a renderer this host does not have. See
[two packages, one battery](authentication.md#two-packages-one-battery).

**The screens are scaffolded too.** `/login` and `/register` are in the template, drawn with the same
daisyUI card the C# lane's sign-in uses, so the flow works on the first run rather than being the first
thing you have to write. One component behind a `mode`, and the path is read in the entry file — the
templates scaffold no router, because which one to use is a choice you have probably already made, and
deep links work anyway since the dev server and the host both fall back to `index.html`.

A TypeScript front end talks to them directly — there is no Rask client to install, because there is
nothing to install: they are ordinary JSON over ordinary `fetch`.

```
POST /api/auth/register          { email, password, firstRunToken? }
POST /api/auth/login             { email, password, remember? }
POST /api/auth/logout
GET  /api/auth/me                -> { id, email, roles }  |  204
POST /api/auth/forgot-password   { email }
POST /api/auth/reset-password    { userId, token, password }
POST /api/auth/confirm-email     { userId, token }
```

You can call them with `fetch`, but you do not have to: the [browser layer](#browser-apis) ships
a module for them, so each flow is a function.

```ts
import { auth } from './rask/browser'

const result = await auth.login({ email, password })

if (result.ok) {
  console.log(result.user.roles)      // typed CurrentUser
} else {
  result.failure.error                 // "InvalidCredentials", "LockedOut", …
}

const me = await auth.me()             // CurrentUser, or null when nobody is signed in
await auth.logout()

// Recovery. None of these signs anybody in, so they answer {ok} rather than a user.
await auth.sendPasswordReset(email)
await auth.resetPassword(userId, token, password)   // both read out of the emailed link's query
await auth.confirmEmail(userId, token)
```

It adds the required header, keeps the paths in one place, and gives you the response shapes typed —
the same `AuthApi` contract the C# clients speak, so a front end and a component are talking to one
API rather than to two that happen to agree today.

The emailed links point at the **host's** built-in `/reset-password` and `/confirm-email` pages unless
you change `AuthOptions.ResetPasswordPath` / `ConfirmEmailPath` to routes your front end owns. Point
them at your own, read `userId` and `token` off the query string, and call the two functions above.

Three things to know, and only three:

- **`X-Rask-Auth` is required on every state-changing call**, and `auth.ts` adds it for you. Cross-site
  markup — a form, an `<img>`, a `<script>` — cannot set a custom header, so requiring one is what
  keeps another origin from driving these endpoints with your visitor's cookie. Calling the endpoints
  by hand means adding it yourself; forgetting is a `400`, not a silent success.
- **You do not attach the cookie.** It is `HttpOnly`, so JavaScript cannot read it and does not need
  to: these calls are same-origin, and a same-origin `fetch` sends cookies by default. Nothing goes in
  `localStorage`, so there is no token for a script on the page to steal.
- **`/api/auth/me` answers `204`, not `401`, when nobody is signed in.** "Nobody" is a perfectly good
  answer to that question; treating it as a failure would fill your logs with errors on every
  anonymous page load.

Read it once when the app loads, and again after a successful login or logout — those are the only
three moments the answer changes.

### Protecting the server side

Client-side routing decides what a visitor *sees*, which is presentation rather than security. What
actually protects data is the endpoint: put `[Authorize]` on your controllers and minimal APIs, and
they answer `401` regardless of what the front end chose to render.

> **Map your API before `UseRaskSpa()`.** It ends the pipeline with a fallback that serves the bundle
> for anything unmatched, so an endpoint mapped after it is never reached — the same ordering rule
> `MapRaskCqrs()` has.

## See also

- [`docs/tailwind.md`](tailwind.md) — Tailwind on a C# host, with no npm at all.
- [`docs/pwa.md`](pwa.md) — manifests, service workers and Web Push across every host.
- [`docs/cqrs.md`](cqrs.md) — the mediator, the wire protocol, and authorization.
- [`docs/cli.md`](cli.md) — `rask new`, `rask dev`, `rask deploy`.

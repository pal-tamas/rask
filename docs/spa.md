# Single-page app front ends

A React, Vue, Svelte or Angular app that builds with npm can live inside an ASP.NET host, be built by
`dotnet build`, ship inside `dotnet publish`, and be served by one call:

```csharp
app.MapControllers();   // your API first
app.MapRaskSpa();
```

`Rask.Spa.Hosting` does this on any ASP.NET app, with or without the rest of Rask, and has no opinion
about the framework of the front end — it runs the client's own `npm` scripts and serves what they
wrote. A host with [remote messages](#a-typed-client-for-your-messages) also gets them as TypeScript.

To put a React or Vue **component** inside a Rask page instead, see [Islands](islands.md).

## Scaffolding one

```bash
rask new Shop --template react
cd Shop
rask dev
```

That writes one project: a `Rask.Server` host — `RaskApp.Create(args).Serve()`, every battery on — and
a React + TypeScript app in `client/`, built with Vite and styled with Tailwind. `rask dev` installs the
client's dependencies the first time, then runs the host and Vite together; Vite forwards `/_rask` and
`/api/auth` to the host.

`--template` takes seven frameworks: `react`, `preact`, `vue`, `angular`, `solid`, `svelte` and `lit`.
The host is the same in all of them, and so is the starter — one page and one sign-in screen, drawn with
the same Tailwind utilities in each framework's own idiom. No component library is installed.

| `--template` | The starter page | The sign-in screen |
|---|---|---|
| `react` | `src/App.tsx` | `src/Auth.tsx` |
| `preact` | `src/app.tsx` | `src/auth.tsx` |
| `vue` | `src/App.vue` | `src/Auth.vue` |
| `solid` | `src/App.tsx` | `src/Auth.tsx` |
| `svelte` | `src/lib/Greeting.svelte` | `src/lib/Auth.svelte` |
| `lit` | `src/my-element.ts` | the same element |
| `angular` | `src/app/app.ts` + `app.html` | `src/app/auth.ts` |

Lit's element renders into the light DOM, so the page's Tailwind sheet reaches it.

**Angular is the one that is not a plain Vite project**, and its host says so in four places:

- the dev server is `ng serve` on port 4200, started by `npm start` — the host's csproj sets
  `<RaskSpaDevServerUrl>http://localhost:4200</RaskSpaDevServerUrl>`, which is what `rask dev` opens;
- the proxy is `client/proxy.conf.json`, which `angular.json` points at, instead of a `server.proxy` block
  in `vite.config.ts`;
- the bundle lands in `dist/<app>-client/browser`, so the csproj sets `RaskSpaDistDir` to it;
- Angular's CLI wants a newer Node than Vite does (`^22.22.3 || ^24.15.0 || >=26.0.0`), so the csproj sets
  `<RaskSpaMinimumNode>22.22.3</RaskSpaMinimumNode>` and an older one fails with `RASKSPA005` instead of
  inside `ng build`. One number cannot say the whole range: on Node 24.0–24.14 it is the CLI that refuses.

In every template:

- **A query and a command.** `Features/Hello/` holds the starter's messages and handlers; the starter page
  dispatches them through the [typed client](#a-typed-client-for-your-messages) the build writes into
  `client/src/rask/`.
- **Sign-in screens.** `/login` and `/register`, over the host's accounts at `/api/auth`. They call `login`
  and `register` from `client/src/rask/browser/auth.ts`, which the build copies beside the typed client.
  The session is an HttpOnly cookie, so the page never holds a token.
- **Web Push.** `client/src/push.ts` exports `subscribeToPush()` and `unsubscribeFromPush()`: the browser's
  own `PushManager`, the worker in `client/public/rask-sw.js`, and the host's `/_rask/push` endpoints.
  `--no-push` leaves it out; `--no-pwa` leaves out the manifest and the worker too.

Batteries are turned off the same way as on any template — `--no-data`, `--no-ops` — except `--no-cqrs`:
the typed client *is* the CQRS wire. See [the CLI](cli.md#which-template-supports-which-flag).

## Where the front end lives

Reference the package from the host project, and put the front end in a `client` folder inside it:

```
Shop/
  Shop.csproj        <PackageReference Include="Rask.Spa.Hosting" />
  Program.cs
  client/
    package.json
    src/
```

A `client` folder holding a `package.json` is found by convention. A front end that lives anywhere
else is named:

```xml
<PropertyGroup>
  <RaskSpaClientDir>app</RaskSpaClientDir>   <!-- relative to the project -->
  <RaskSpaDistDir>build</RaskSpaDistDir>     <!-- where its build writes; dist by default -->
</PropertyGroup>
```

An app on `Rask.Server` already has `MapRaskSpa`, but not the build steps: reference
`Rask.Spa.Hosting` directly to get them.

## Building and publishing

`dotnet build` runs the client's own toolchain: `npm ci` (or `npm install` when there is no
lockfile), then `npm run build`. Both steps are incremental — an install re-runs when `package.json`
or the lockfile changes, a build when a source file does. `dotnet publish` copies the bundle into
`wwwroot` next to the app, so a deployed container carries the front end rather than a path that only
existed on the build machine.

`-p:RaskSpaBuild=false` skips node entirely. The app still compiles, its API still works, and the site
serves a page saying there is nothing built yet. Use it on a machine with no node, or in a CI job that
only cares about the C#.

### Which Node

Use the current LTS. The build's own floor is **22.12**, and it is enforced: an older Node fails with
`RASKSPA005` naming the version it found, rather than reaching the bundler and failing there with an
`engines` error. Set `RaskSpaMinimumNode` to move the bar — it is a real comparison, in both
directions, so a front end on an older toolchain can lower it. The Angular template raises it to
**22.22.3**, the lowest Node its CLI accepts.

## Development

`rask dev` starts two processes: `dotnet watch` for the host, and the client's own dev server — the
`dev` script in its `package.json`, or `start` where that is what it has. **The browser talks to the
dev server**, which is what serves the front end and what its hot reload reaches, and the dev server
forwards API calls to the host. That proxy belongs to the client — for Vite:

```ts
// client/vite.config.ts
export default defineConfig({
  server: { proxy: { "/api": "http://localhost:5000" } },
});
```

The browser only ever sees one origin, so there is no CORS to configure. The host, asked for a page
before anything is built, answers 200 with a page naming the dev server rather than a 503.

`rask dev` opens `http://localhost:5173`, Vite's default. A dev server that listens elsewhere is named
in the host's project file — `<RaskSpaDevServerUrl>http://localhost:3000</RaskSpaDevServerUrl>` — and
both `rask dev` and that page follow it. A dev session does not build the production bundle.

Under VS Code's F5 a host that calls `AddRaskSpaHost()` starts the client's dev server itself — the `dev` (or `start`) script from
the client's `package.json` — because nothing else runs beside an app the debugger launched.

## What `MapRaskSpa` does

More than `UseStaticFiles` and a fallback:

- **A missing asset stays a 404.** A naive SPA fallback answers every unmatched request with
  `index.html`, so a missing module import arrives as HTML and the browser reports
  `Failed to load module script`. Requests under a content-hashed prefix, and requests whose `Accept`
  asks for something other than HTML, are refused instead.
- **Cache headers follow what the bundler guarantees.** Files under a hashed prefix are cached for
  ever; `index.html` never is — freezing it strands a visitor on the deploy they first saw.
- **Precompressed siblings.** A `.br` or `.gz` beside a file is served when the client accepts it,
  keeping the real content type.
- **Your endpoints win.** The fallback has the lowest precedence, so a controller, a minimal API or a
  health check answers the paths it names whichever side of the call it is mapped on.

`MapRaskSpa(pathBase: "/app")` serves the bundle under a prefix and leaves the rest of the site alone.
A Rask WebAssembly client is served by the same call — see
[deployment](deployment.md).

### Options

```jsonc
// appsettings.json
{
  "Rask": {
    "Spa": {
      "DevServerUrl": "http://localhost:5173",
      "ImmutablePathPrefixes": [ "/static/" ]   // added to the default /assets/, not replacing it
    }
  }
}
```

`/assets/` is where Vite puts its hashed files. A bundler that hashes somewhere else — Create React
App writes `/static/` — is told so here. The two delegates, `ExcludeFromFallback` and
`OnPrepareResponse`, can only be set in code:

```csharp
app.MapRaskSpa(configure: options => options.ImmutablePathPrefixes.Add("/static/"));
```

### MSBuild properties

| Property | Default | |
|---|---|---|
| `RaskSpaClientDir` | a `client` folder in the host project | Where the front end lives. |
| `RaskSpaDistDir` | `dist` | The bundler's output. Angular nests it: `dist/<app>/browser`. |
| `RaskSpaBuild` | `true` | `false` skips node entirely. |
| `RaskSpaInstallCommand` | `npm ci` | Run when a lockfile exists. |
| `RaskSpaFirstInstallCommand` | `npm install` | Run when there is no lockfile yet. |
| `RaskSpaBuildCommand` | `npm run build` | What produces the bundle. |
| `RaskSpaMinimumNode` | `22.12.0` (`22.22.3` in the Angular template) | The Node floor the build enforces, as `RASKSPA005`. |
| `RaskSpaPublishDir` | `wwwroot` | Where publish puts the bundle. |
| `RaskSpaDevServerUrl` | none | Named on the "nothing built yet" page in Development. |
| `RaskEmitTypeScript` | on | `false` generates nothing, whatever the host declares. |
| `RaskSpaGeneratedDir` | `src/rask` | Where the generated client lands, inside the front end. |
| `RaskSpaTypeScriptConfig` | `tsconfig.json` | The client's TypeScript config; its presence is what `RASKSPA004` checks. |

## A typed client for your messages

A host that declares [remote messages](cqrs.md) gets their TypeScript written into its front end on every
build — you write the records once, in C#, and there is no schema file to keep in sync:

| File in `client/src/rask/` | |
|---|---|
| `contracts.ts` | The types: one per record that crosses the wire. |
| `messages.ts` | A factory per message, carrying its wire name and its result type. |
| `client.ts` | The dispatcher: `rask.dispatch`, uploads, downloads. Refreshed from the package. |
| `query.ts` | Options helpers for TanStack Query, importing nothing from it. |
| `browser/auth.ts` | Sign-in against `/api/auth`: `login`, `register`, `logout`, `me`, passkeys. |

The directory is generated; ignore it in git. Its files are written only when they change, so a
watching bundler is not woken for nothing, and they are written under `rask dev` too — a dev server
compiling the previous build's contracts is exactly the failure this exists to prevent.

**Only a host with messages gets any of this.** One that declares none is a static-file host for any
front end, in any language, and the build leaves its sources alone. The first remote message is what
asks the front end to be TypeScript: without a `tsconfig.json` the build stops with `RASKSPA004`,
because a JavaScript client would import the contracts, have nothing checked, and find a renamed
property on the wire instead of at the compiler. A client that keeps its config elsewhere names it —
`<RaskSpaTypeScriptConfig>tsconfig.app.json</RaskSpaTypeScriptConfig>` — and `RaskEmitTypeScript=false`
turns the generation off outright.

### The call site

A message factory carries its own wire name and its own result type, so `dispatch` infers what comes
back:

```ts
import { rask } from './rask/client'
import { getGreeting } from './rask/messages'

const greeting = await rask.dispatch(getGreeting({ name: 'Ada' }))
//    ^? Greeting — inferred from the message, no cast
```

Rename a property on the C# record and this line stops compiling. That is the whole point of
generating the types rather than describing them.

### Adding a cache

`rask.dispatch` is a call, not a cache. If you want one, `Rask.Spa.Hosting` vendors `src/rask/query.ts` beside the client — `raskQuery` and
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

## Adding a message

Add a record and a handler:

```csharp
public sealed record Order(Guid Id, DateTimeOffset PlacedAt, DateOnly DeliverBy);

public sealed record GetOrder(Guid Id) : IQuery<Order>;

public sealed class GetOrderHandler : IQueryHandler<GetOrder, Order>
{
    public Task<Order> Handle(GetOrder query) => /* … */;
}
```

The next build writes `getOrder` into `src/rask/messages.ts` and `Order` into `contracts.ts`. If a
property has no wire encoding, the build fails with **RASK053** naming it — a shape that cannot cross
is reported at compile time rather than on the wire.

A message that is never sent anywhere — a job payload, an outbox event — should say so with
`[LocalOnly]`, which exempts it from all of this.

## Moving an existing app onto Rask, a page at a time

An app that already has an API and a single-page front end does not have to move at once. Give Rask a
prefix, and leave everything else to the front end it already has:

```csharp
builder.Services.AddRask();

var app = builder.Build();
app.UseWebSockets();
app.MapControllers();                    // the API, unchanged
app.MapRask<App>(pathBase: "/new");      // pages that have moved
app.MapRaskSpa();                        // every other path: the old front end
```

Each answers only its own paths — a route the old front end owns still gets its `index.html`, a page
under `/new` is rendered by Rask, and the Rask runtime is served under the prefix and nowhere else. The
old front end links to a moved page with an ordinary `<a href="/new/…">`, a full page load.

Two things the move runs into first:

- **Sign-in.** A Rask page is rendered from the request, so it sees who is signed in only when the
  browser sends that with a navigation — a cookie. A front end that keeps its token in `localStorage`
  renders every Rask page signed out until sign-in also sets one. See
  [authentication](authentication.md).
- **Per-request services.** After the first response a Rask page works over its connection, where there
  is no `HttpContext`. A service that reads the tenant or the user from `IHttpContextAccessor` has to
  get it some other way — from the signed-in principal, for instance.

A component of the old front end can also move as it is, as an [island](islands.md) inside a Rask page.

## See also

- [Islands](islands.md) — a front-end component inside a Rask page.
- [Deployment](deployment.md) — a Rask WebAssembly client behind the same call.
- [Build errors](diagnostics.md#build-errors-from-msbuild) — `RASKSPA001`–`009`.

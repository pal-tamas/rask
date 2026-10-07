# Single-page app front ends

A React, Vue, Svelte or Angular app that builds with npm can live inside an ASP.NET host, be built by
`dotnet build`, ship inside `dotnet publish`, and be served by one call:

```csharp
app.MapControllers();   // your API first
app.MapRaskSpa();
```

`Rask.Spa.Hosting` does this on any ASP.NET app, with or without the rest of Rask, and has no opinion
about the framework or the language of the front end — it runs the client's own `npm` scripts and
serves what they wrote.

To put a React or Vue **component** inside a Rask page instead, see [Islands](islands.md).

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
directions, so a front end on an older toolchain can lower it.

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
| `RaskSpaMinimumNode` | `22.12.0` | The Node floor the build enforces, as `RASKSPA005`. |
| `RaskSpaPublishDir` | `wwwroot` | Where publish puts the bundle. |
| `RaskSpaDevServerUrl` | none | Named on the "nothing built yet" page in Development. |

## See also

- [Islands](islands.md) — a front-end component inside a Rask page.
- [Deployment](deployment.md) — a Rask WebAssembly client behind the same call.
- [Build errors](diagnostics.md#build-errors-from-msbuild) — `RASKSPA001`–`009`.

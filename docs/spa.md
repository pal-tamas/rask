# A WebAssembly app on its host

A Rask WebAssembly app is a single-page app: the browser downloads the runtime, your assemblies and a
static `index.html` once, and every page after that renders in the browser. It still needs a server for
its API, and the simplest place to serve the bundle from is that same server. `Rask.Spa.Hosting` does
it in one call:

```csharp
builder.Services.AddRaskSpaHost();

var app = builder.Build();
app.MapRaskCqrs();   // your API first
app.MapRaskSpa();
```

## Scaffolding one

```bash
rask new Shop --template wasm-hosted
cd Shop
rask dev
```

The browser app lives in `Client/`, inside the server's own project, and the server's whole
`Program.cs` is:

```csharp
RaskApp.Create(args).Serve();
```

`Serve()` is `Run<App>()` without a server-rendered root: every battery, the endpoints the browser app
dispatches to (`MapRaskCqrs`), the operator console at `/_rask`, and `MapRaskSpa()` last, answering every
other path. `dotnet publish` copies the client's bundle into the host's `wwwroot`, where `MapRaskSpa`
finds it with no arguments. `rask dev` serves the client's build output rather than its publish,
because a trimmed bundle turns hot reload off in the browser.

A standalone WebAssembly app with no server at all is `--template wasm`; see
[deployment](deployment.md#standalone-wasm-spa---template-wasm).

## What `MapRaskSpa` does

More than `UseStaticFiles` and a fallback:

- **A missing asset stays a 404.** A naive SPA fallback answers every unmatched request with
  `index.html`, so a missing module import arrives as HTML and the browser reports
  `Failed to load module script`. Requests under a content-hashed prefix, and requests whose `Accept`
  asks for something other than HTML, are refused instead.
- **Cache headers follow what the publish guarantees.** Scoped assets under `/_rask/a/` are named by
  their content hash and cached for ever; `/_framework/*` is cached for ever only where the SDK
  fingerprinted the name; `index.html` never is — freezing it strands a visitor on the deploy they
  first saw.
- **Precompressed siblings.** A `.br` or `.gz` beside a file is served when the client accepts it,
  keeping the real content type. `AddRaskSpaHost()` adds response compression for the runtime's
  `application/wasm` and `application/octet-stream` files.
- **Your endpoints win.** The fallback has the lowest precedence, so a controller, a minimal API or a
  health check answers the paths it names whichever side of the call it is mapped on.

`MapRaskSpa(pathBase: "/app")` serves the bundle under a prefix and leaves the rest of the site alone.
A host serves one client.

### Options

```jsonc
// appsettings.json
{
  "Rask": {
    "Spa": {
      "ImmutablePathPrefixes": [ "/static/" ]   // added to the defaults, not replacing them
    }
  }
}
```

The two delegates, `ExcludeFromFallback` and `OnPrepareResponse`, can only be set in code:

```csharp
app.MapRaskSpa(configure: options => options.ExcludeFromFallback = ctx => ctx.Request.Path.StartsWithSegments("/api"));
```

## A client in a project of its own

A host that is not a `RaskApp`, or a client kept in its own project, references the client project
for its build output only, and maps the API **before** the fallback, or the fallback answers the API
call with HTML:

```xml
<ProjectReference Include="..\Shop.Client\Shop.Client.csproj"
                  ReferenceOutputAssembly="false"
                  SkipGetTargetFrameworkProperties="true"/>
```

## See also

- [Deployment](deployment.md#serving-a-webassembly-app) — the published layout, cache table and Dockerfile.
- [Remote messages](cqrs.md) — the API a WebAssembly client dispatches to.
- [PWA](pwa.md) — making the browser app installable and offline.

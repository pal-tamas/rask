# Deployment

Rask apps are ordinary .NET apps, so every standard .NET hosting path works — `dotnet publish`
behind a reverse proxy, Azure App Service, a systemd unit, or a container. This guide covers
**containers**, which the templates scaffold for you — and `rask deploy`, which ships one to a single
host for you.

## One-command deploy — `rask deploy`

If you just want your app live on a box you own, `rask deploy` does the whole thing:

```bash
rask deploy --host deploy@box --domain app.example.com
# → builds the image on the host, runs it, and serves https://app.example.com with an auto-issued cert
```

It builds and runs the app **on the host over SSH** — every step is `docker -H ssh://<host> …`, so
there's no registry, no local Docker daemon, and no image tarball to copy; the build context ships to
the host's daemon and builds there. It deploys the [`--docker`](#scaffolding-a-dockerfile----docker)
Dockerfile below (override with `--dockerfile`).

- **Automatic HTTPS.** With `--domain`, Rask runs a shared [Caddy](https://caddyserver.com) reverse
  proxy on the box that obtains and renews a Let's Encrypt certificate — a live HTTPS site with nothing
  else to configure. (Point the domain's DNS `A`/`AAAA` record at the host first so the cert can issue.)
- **Zero-downtime.** Deploys are blue-green: the new container starts alongside the old, is
  health-checked, then Caddy is reloaded to point at it before the old one is removed. A new container
  that fails to start leaves the previous version serving.
- **Many apps, one box.** Each app is a separate `--domain`; the proxy's routing is regenerated from the
  host's live containers on every deploy, so a second app never disturbs the first.
- **No domain?** Omit `--domain` to publish the app on `--port` (default `8080`) and put your own
  reverse proxy / TLS in front — see the container sections below.

**Prerequisites:** the Docker CLI locally, and on the host a running Docker daemon plus key-based SSH
(so `ssh user@box` works non-interactively). The host, domain, and port are remembered in
`.rask/deploy.json` for repeat deploys — a bare `rask deploy` re-ships. Secrets are never stored there;
pass them with `--env KEY=VALUE` (repeatable) or `--env-file <path>`. Use `--dry-run` to print the exact
docker commands without running anything. Full flag reference: [`docs/cli.md`](cli.md#rask-deploy--ship-to-a-single-host-over-ssh).

## Scaffolding a Dockerfile — `--docker`

The three web templates take an opt-in `--docker` flag that drops a production-ready multi-stage
`Dockerfile` (+ `.dockerignore`) into the generated project:

```bash
rask new MyApp --docker                          # Kestrel app → aspnet:10.0 runtime image
rask new MyApp --template wasm --docker           # static WASM bundle → nginx:alpine
rask new MyApp --template wasm-hosted --docker    # WASM client + host → aspnet:10.0 runtime image
```

Without `--docker` no container files are emitted. The `native` template has no `--docker`
option: it builds a WebView-hybrid **iOS/Android app**, which is packaged as an `.ipa`/`.apk` and
distributed through the app stores — there is nothing to containerize.

The Dockerfile references your project by name (the template renames `Company.RaskServer.dll` to
`MyApp.dll` when it scaffolds), so `docker build` works with no edits.

## Server app (`--template server`)

A server-side Rask app is a Kestrel web server. The scaffolded Dockerfile builds on the .NET SDK
image and runs on `mcr.microsoft.com/dotnet/aspnet:10.0`, which already runs as a **non-root** user
and listens on **port 8080** (`ASPNETCORE_HTTP_PORTS=8080`).

```bash
docker build -t myapp .
docker run --rm -p 8080:8080 myapp
# open http://localhost:8080
```

**HTTPS behind a proxy.** The app calls `UseHttpsRedirection()`. Inside the container no HTTPS port
is configured, so the redirect **no-ops** — terminate TLS at your reverse proxy / ingress and forward
plain HTTP to `8080`. Rask renders and events flow over a WebSocket, so make sure your proxy forwards
the `Upgrade`/`Connection` headers (most do by default; for nginx set `proxy_set_header Upgrade` +
`Connection "upgrade"` and HTTP/1.1). To host under a sub-path, pass `app.UseRask<App>(pathBase:
"/myapp")` and route `/myapp/*` to the container.

## WASM-hosted app (`--template wasm-hosted`)

Three projects in one solution: `MyApp.Client` (the browser-WASM SPA), `MyApp.Server` (the ASP.NET host
that serves it), and `MyApp.Shared` (a class library both reference). The Dockerfile installs the
`wasm-tools` workload (needed to publish the browser client the Server host bakes in), builds the
projects, and runs **`MyApp.Server`** on the aspnet runtime image — same port/TLS story as the server app.

```bash
docker build -t myapp .
docker run --rm -p 8080:8080 myapp
# open http://localhost:8080  — /api/weatherforecast demonstrates the client↔host round trip
```

## Standalone WASM SPA (`--template wasm`)

A standalone Rask SPA has **no ASP.NET host** — `dotnet publish` emits a static bundle "to serve from
any static-file host." The scaffolded Dockerfile publishes the bundle on the .NET SDK image, then
copies it into a tiny `nginx:alpine` image with a bundled `nginx.conf`:

```bash
docker build -t myapp .
docker run --rm -p 8080:80 myapp     # nginx listens on 80 inside the container
# open http://localhost:8080
```

The `nginx.conf` does three things that matter for a Rask WASM bundle:

- **SPA fallback** — `try_files $uri $uri/ /index.html;` so client-side routes resolve.
- **`application/wasm` MIME** — the browser refuses to streaming-compile the Mono runtime `.wasm`
  under any other type.
- **`gzip_static on;`** — serves the `*.gz` siblings the publish step bakes next to each asset. (Rask
  also bakes `*.br`, but the stock `nginx:alpine` has no brotli module; gzip is universally accepted,
  so this alone keeps transfers small. Swap in an nginx image with `ngx_brotli` and add
  `brotli_static on;` if you want brotli.)

Because it's just static files, you can equally serve the `wwwroot` publish output from any CDN or
object-storage static host instead of a container. For GitHub Pages / sub-path hosting, publish with
`/p:RaskPathBase=/my-repo` — see [Mobile & PWA → Deploying](pwa.md#deploying-github-pages--sub-paths).

## Not containerized: `--template native`

The `native` template targets `net10.0-ios;net10.0-android` and produces a native mobile app
(your C# runs natively on the device; the UI renders in a platform WebView). It is built and
distributed as an app-store package, not a server image, so it has no Dockerfile. If you want a
**native shell over a remote Rask Server**, scaffold `rask new MyApp --template native --host server` and deploy
that server with the `--template server` container above. See [Native mobile](native.md).

# Deployment

> **In practice:** [Tutorial Ch 9](tutorial/09-deploy.md) · recipe [deploy and redeploy](recipes.md#deploy-and-redeploy) · [cheat sheet](cheatsheet.md).

Rask apps are ordinary .NET apps, so every standard .NET hosting path works — `dotnet publish`
behind a reverse proxy, Azure App Service, a systemd unit, or a container. This guide covers
**containers**, which the templates scaffold for you — and `rask deploy`, which ships one to a single
host for you.

## One-command deploy — `rask deploy`

If you just want your app live on a box you own, `rask deploy` does the whole thing — starting from a
VPS that has nothing on it but SSH:

```bash
rask deploy --host root@box --domain app.example.com
# → sets the box up (Docker, a deploy login, firewall, SSH hardening), builds the image on it,
#   and serves https://app.example.com with an auto-issued cert
```

It builds and runs the app **on the host over SSH** — every deploy step is `docker -H ssh://<host> …`,
so there's no registry, no local Docker daemon, and no image tarball to copy; the build context ships
to the host's daemon and builds there. It deploys the [`--docker`](#scaffolding-a-dockerfile----docker)
Dockerfile below (override with `--dockerfile`).

- **Automatic HTTPS.** With `--domain`, Rask runs a shared [Caddy](https://caddyserver.com) reverse
  proxy on the box that obtains and renews a Let's Encrypt certificate — a live HTTPS site with nothing
  else to configure. (Point the domain's DNS `A`/`AAAA` record at the host first so the cert can issue.)
- **Zero-downtime, health-gated.** Deploys are blue-green: the new container starts alongside the old,
  is waited on until its container is running **and answers an HTTP health check** (`GET /health` by
  default), then Caddy is reloaded to point at it before the old one is removed. A container that fails
  to start — or that starts but fails its probe (bad config, a failed migration) — is removed and the
  previous version keeps serving. Apps scaffolded with `rask new` ship the `/health` endpoint; probe a
  different path with `--health-path <path>`, or skip the probe with `--no-health-check`.
- **Durable SQLite database.** Every deploy runs a fresh container, so the database can't live inside it.
  `rask deploy` mounts a per-app named volume and points the app at it (`ConnectionStrings:App` →
  `Data Source=/data/app.db`), so your data persists across redeploys; the old container is stopped
  gracefully (SIGTERM) before removal so in-flight writes are checkpointed first. The `rask new --docker`
  Dockerfile prepares a writable `/data`; a custom Dockerfile needs the same
  (`RUN mkdir -p /data && chown $APP_UID:$APP_UID /data`). Pair it with `Rask.SQLite.Litestream` to also
  stream the database off the box for machine-loss recovery.
- **Many apps, one box.** Each app is a separate `--domain`; the proxy's routing is regenerated from the
  host's live containers on every deploy, so a second app never disturbs the first.
- **No domain?** Omit `--domain` to publish the app on `--port` (default `8080`) and put your own
  reverse proxy / TLS in front — see the container sections below.

**Prerequisites:** the Docker CLI locally, and a host you can `ssh` into non-interactively with a key.
That's it — **you never have to SSH in and prepare the box yourself**; see below. The host, domain, and
port are remembered in `.rask/deploy.json` for repeat deploys — a bare `rask deploy` re-ships. Secrets
are never stored there; pass them with `--env KEY=VALUE` (repeatable) or `--env-file <path>`. Use
`--dry-run` to print the exact docker commands without running anything. Full flag reference:
[`docs/cli.md`](cli.md#rask-deploy--ship-to-a-single-host-over-ssh).

### The first deploy to a bare box

Hand `rask deploy` a fresh VPS — root, an SSH key, nothing else — and it checks what the box has, shows
you exactly what it wants to change, and asks once:

```
'root@box' isn't ready to deploy to. Rask can set it up:

  • Install Docker
  • Start the Docker daemon
  • Create the 'deploy' login and give it Docker access
  • Enable the firewall (allow 22, 80, 443; deny everything else inbound)
  • Harden SSH (disable SSH password login and root login)

Set up root@box now? [Y/n]
```

Say yes and the box is ready; the deploy then runs normally. What it does:

- **Docker** — installed from Docker's own [get.docker.com](https://get.docker.com) script, then enabled.
- **A non-root login.** It creates `deploy` (change with `--deploy-user`, skip with `--no-deploy-user`),
  copies your `authorized_keys` to it, and adds it to the `docker` group. `.rask/deploy.json` is updated
  to `deploy@box`, so every later deploy uses it — including after root login is switched off.
- **A firewall.** `ufw` with the ports sshd actually listens on plus 80/443 (or your `--port`) allowed,
  everything else inbound denied.
- **SSH hardening.** Password login off, and root login off — but *only* once a working non-root login
  exists to replace it.

**Setup only ever happens to a box that can't already deploy.** Once Docker runs, `rask deploy` just
deploys — a host that's fine as it is (say, a least-privilege login with no sudo, behind a cloud
firewall rather than ufw) is checked and left alone, with no prompt and no nagging on every deploy. The
box is the source of truth, so re-running is a no-op. Pass `--setup-host` to prepare a host anyway —
that's the explicit way to add a firewall to a box that's already serving. Opt out per step with
`--no-firewall` / `--no-harden-ssh` / `--no-deploy-user`, or refuse to touch the host at all with
`--no-setup-host`.

**Nothing takes your access away until a new way in is proven.** The deploy login is tested with a fresh
connection before anything is hardened; if that fails, the firewall and SSH config are never touched.
The firewall and hardening themselves run behind a rollback timer armed *on the box* — if the CLI can't
get back in afterwards (or is killed), the host reverts both by itself after ~5 minutes. And when
something can't be done safely, it's skipped out loud rather than quietly: if sshd's real port can't be
read, the firewall is not enabled, because a firewall Rask can't prove is safe is worse than none.

> **On ufw and Docker.** Docker publishes container ports by writing its own iptables rules, which
> bypass ufw. For Rask that's not a hole — the only ports Docker publishes here are the ones meant to be
> public (Caddy's 80/443, or your `--port`), and ufw's job is everything *else* on the box. But it does
> mean that if you later run an unrelated container with `-p`, `ufw deny` will not hide it.

### Deploying from GitHub Actions

```bash
rask deploy --github-actions   # writes .github/workflows/deploy.yml and prints the secrets to add
```

The workflow runs the same `rask deploy` on every push to `main`. Host, domain and port come from the
committed `.rask/deploy.json`, so only two secrets are needed — the command prints the exact `gh` lines:

```bash
gh secret set RASK_SSH_PRIVATE_KEY < ~/.ssh/id_ed25519
gh secret set RASK_SSH_KNOWN_HOSTS --body "$(ssh-keyscan box.example.com 2>/dev/null)"
```

Pass app secrets to the container by adding `--env "Key=${{ secrets.SOMETHING }}"` to the workflow's
deploy step. The generated workflow deploys with `--no-setup-host` on purpose: **set the box up once
from your own machine**, where you can see what's about to change — a host that isn't ready should fail
the job, not get reconfigured by CI.

> **App secrets** — passwords, API keys, SMTP logins — have their own page: **[Secrets](secrets.md)**.
> `.rask/deploy.json` records the *names* of the variables your app needs (never their values), and a
> deploy that doesn't supply one of them refuses rather than starting the app misconfigured.

## Backups

`rask deploy` mounts a named volume so the database survives redeploys — but a volume is still **one copy
on one disk**. An app scaffolded with `--data` is already wired for [Litestream](sqlite.md#continuous-backup-with-litestream),
which streams the write-ahead log to object storage; it stays inert until you point it somewhere:

```bash
rask deploy --env "Litestream__ReplicaUrl=s3://your-bucket/app" \
            --env "AWS_ACCESS_KEY_ID=…" --env "AWS_SECRET_ACCESS_KEY=…"
```

With that set, a fresh box restores the database from the replica on startup — which is what makes "one
server" a reasonable place to keep your only copy. Until it is, every deploy says so:

```
! No Litestream replica configured — this app's database exists only on this box's disk.
```

The replica URL is remembered by name like any other variable, so once set, a later deploy that forgets it
[fails rather than silently dropping it](secrets.md).

## What `rask deploy` sets on the container

Beyond your own `--env` values, every deployed container gets:

| Setting | Why |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT=Production` | Selects `appsettings.Production.json` and turns off the developer exception page. Your own `--env` wins if you set it. |
| `ConnectionStrings__App=Data Source=/data/app.db` | Points the app at the mounted volume, so the database survives container replacement. |
| `--log-opt max-size=10m --log-opt max-file=3` | Docker's `json-file` logs are unbounded by default; on a one-box deploy a chatty app filling the disk takes down every other app sharing it. |
| `--security-opt no-new-privileges` | A compromised process can't gain rights through setuid binaries. Nothing a Rask app does needs to escalate. |
| `--restart unless-stopped` | The app comes back after a reboot or a daemon restart. |

The scaffolded app is set up to match: it honours forwarded headers (so `Request.Scheme` and the client
IP are the visitor's, not the proxy's), reports live-session capacity on `/health` (so a host that is
refusing sessions with `503` says so rather than answering a bare "up"), and shuts down within 15s — inside
the 20s grace period the deploy allows before `SIGKILL`, so in-flight requests drain and SQLite checkpoints
cleanly.

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
docker run --rm -p 8080:8080 myapp
# open http://localhost:8080
```

The `nginx.conf` does four things that matter for a Rask WASM bundle:

- **Listens on `8080`, and serves `/health`** — the same container port and readiness endpoint as the
  server and wasm-hosted templates, so [`rask deploy`](cli.md#rask-deploy--ship-to-a-single-host-over-ssh)
  can health-gate and proxy a static bundle exactly like any other Rask app.
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

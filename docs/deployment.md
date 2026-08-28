# Deployment

> **In practice:** [Tutorial Ch 11](tutorial/11-deploy.md) · recipe [deploy and redeploy](recipes.md#deploy-and-redeploy) · [cheat sheet](cheatsheet.md).

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
  HTTP requests are zero-downtime; **live sessions re-establish**, because a session is a component tree
  and a DI scope inside *that* container and cannot hand over to the next one. The retiring container
  announces its shutdown first, so open pages show "Updating…" and reconnect to the new instance
  immediately instead of reporting a timeout — reloading only if the host that answers cannot rebuild the
  page, and then at their previous scroll position, with the fields the user had edited restored. See
  [Shutdown and redeploy](configuration.md#shutdown-and-redeploy).
- **Durable SQLite database.** Every deploy runs a fresh container, so the database can't live inside it.
  `rask deploy` mounts a per-app named volume and points the app at it (`ConnectionStrings:App` →
  `Data Source=/data/app.db`), so your data persists across redeploys; the old container is stopped
  gracefully (SIGTERM) before removal so in-flight writes are checkpointed first. The `rask new`
  Dockerfile prepares a writable `/data`; a custom Dockerfile needs the same
  (`RUN mkdir -p /data && chown $APP_UID:$APP_UID /data`). Pair it with `Rask.SQLite.Litestream` to also
  stream the database off the box for machine-loss recovery.
- **Many apps, one box.** Each app is a separate `--domain`; the proxy's routing is regenerated from the
  host's live containers on every deploy, so a second app never disturbs the first.
- **No domain?** Omit `--domain` to publish the app on `--port` (default `8080`) and put your own
  reverse proxy / TLS in front — see the container sections below.
- **One box, and how far it goes.** [Scaling](scaling.md) has the measured numbers — sessions held and
  events served — plus where the wall actually is and what to do when you reach it.

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
  • Make Docker's published ports obey the firewall (allow 80, 443; deny every other container port)
  • Harden SSH (disable SSH password login and root login)

Set up root@box now? [Y/n]
```

Say yes and the box is ready; the deploy then runs normally. What it does:

- **Docker** — installed from Docker's own [get.docker.com](https://get.docker.com) script, then enabled.
- **A non-root login.** It creates `deploy` (change with `--deploy-user`, skip with `--no-deploy-user`),
  copies your `authorized_keys` to it, and adds it to the `docker` group. `.rask/deploy.json` is updated
  to `deploy@box`, so every later deploy uses it — including after root login is switched off.
- **A firewall.** `ufw` with the ports sshd actually listens on plus 80/443 (or your `--port`) allowed,
  everything else inbound denied — **including container ports**, which ufw does not cover on its own
  (see below).
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

> **On ufw and Docker.** Docker publishes a container port by writing its own iptables rules: a DNAT
> that is filtered through `FORWARD`, where ufw's `INPUT` rules never see it. On a stock box that makes
> `ufw deny` meaningless for anything `docker run -p` exposes — `ufw status` reports a port closed while
> the internet can reach it. Rask closes that gap as part of setting the firewall up, so the deny you
> were promised is the deny you get.
>
> It works through `DOCKER-USER`, the chain Docker consults before its own rules and never writes to
> itself. Rask points it at ufw's forward rules and then default-denies anything else being forwarded
> into a Docker bridge, allowing only what this deploy publishes. Three things worth knowing:
>
> - **The rules live in `/etc/ufw/after.rules`**, in a block fenced by `###RASK-DOCKER-BEGIN`/`-END`
>   that is rewritten whenever it goes stale and never touches the rest of the file. That file is where
>   they belong rather than a live `iptables` call, because raw chains do not survive a reboot.
> - **To open another container port, use plain ufw**: `sudo ufw route allow proto tcp from any to any
>   port 5432`. It has to be the port *inside* the container — Docker's DNAT has already rewritten the
>   destination by the time the firewall sees the packet, so a `-p 15432:5432` maps to `5432` here.
> - **Rask owns the `DOCKER-USER` chain** on a box it firewalls: the block replaces that chain's
>   contents so nothing can accept ahead of the default-deny. If you manage `DOCKER-USER` with another
>   tool, use `--no-firewall`.
>
> Only what's forwarded into a Docker bridge is denied, so a box that also routes for something else (a
> VPN, say) is unaffected — and containers can still reach out and reach each other.

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

Two different questions, two answers. **"Let me look at what production has"**, or "that migration was a
mistake, put last night's file back" — that is [`rask db backup` / `rask db restore`](cli.md#backup-and-restore):

```bash
rask db backup --remote                 # a consistent copy of the deployed database, pulled down
rask db restore last-good.db --remote   # stops the app, replaces it, starts it again
```

**"The server is gone"** is the rest of this section. `rask deploy` mounts a named volume so the database survives redeploys — but a volume is still **one copy
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
| `ConnectionStrings__Logs=Data Source=/data/logs.db` | Same volume, for [`Rask.Logging`](logging.md)'s own file. Ignored by an app that doesn't use it. |
| `--log-opt max-size=10m --log-opt max-file=3` | Docker's `json-file` logs are unbounded by default; on a one-box deploy a chatty app filling the disk takes down every other app sharing it. |
| `--security-opt no-new-privileges` | A compromised process can't gain rights through setuid binaries. Nothing a Rask app does needs to escalate. |
| `--restart unless-stopped` | The app comes back after a reboot or a daemon restart. |

The scaffolded app is set up to match: it honours forwarded headers (so `Request.Scheme` and the client
IP are the visitor's, not the proxy's), reports live-session capacity and readiness on `/health` (so a host
that is refusing sessions with `503` — because it is full, or because it is draining — says so rather than
answering a bare "up"), and gives itself a **15 s shutdown budget**, inside the 20 s the deploy allows
before `SIGKILL`.

Within that budget the app closes admission, tells connected browsers it is going away, lets in-flight
HTTP requests and event handlers finish, closes each WebSocket with a proper `1001` handshake, and
checkpoints SQLite.

### The shutdown ladder

Every rung fits inside the one above it, and hosted services stop **concurrently** — so each pillar's own
grace overlaps the others instead of queueing behind them:

| Rung | Budget | Set by |
| --- | --- | --- |
| `docker stop -t` before `SIGKILL` | 20s | `rask deploy` |
| App `HostOptions.ShutdownTimeout` | 15s | scaffolded `Program.cs` |
| Live-session drain | 5s | `RaskServerOptions.ShutdownDrainTimeout` |
| Litestream final WAL flush | 10s | `LitestreamOptions.ShutdownGracePeriod` |
| In-flight email send | 10s | `MailOptions.ShutdownGracePeriod` |
| In-flight job / outbox message | 5s | `Job`/`OutboxOptions.ShutdownGracePeriod` |

> **`ServicesStopConcurrently` is load-bearing, not a micro-optimisation.** Stopped one at a time — the
> .NET default — those inner graces *sum*: 10 + 10 + 5 + 5 = 30s against a 15s budget, so whichever hosted
> service stops last gets none of its grace at all, decided by the order of your `AddRaskX` calls in
> `Program.cs`. Stopped concurrently they overlap at 10s, leaving real headroom. The scaffold sets it
> alongside the timeout for exactly this reason.

These are budgets, not guarantees. Work that outlives its rung is cancelled: live sessions are aborted
(`rask.shutdown.sessions.abandoned`), and a job, outbox message or email is re-run from the top on the next
boot without counting a failed attempt (`rask.{jobs,outbox,mail}.interrupted`). Both are logged as warnings,
so a budget that is too short for your app says so rather than silently degrading.

One honest caveat of stopping concurrently: Litestream stops alongside a job that is still writing inside
*its* grace, so the very last rows such a job writes may not reach the replica. Sequential stop wouldn't
actually protect against that either — the order is reverse-registration, so nothing guarantees Litestream
is last today — and the exposure is narrow, since the Docker volume survives redeploys and Litestream only
matters for losing the machine itself.

### Your users stay signed in across a deploy

The same volume holds the **Data Protection key ring**, at `/data/keys`. This matters more than it looks.
Those keys sign the auth cookie, and the default ring is written inside the container — so a deploy, which
replaces the container, would mint a fresh ring and invalidate every cookie already issued. Every signed-in
user would be silently signed out by every deploy, with nothing in the logs to say why.

**`AddRask` does this for you**, and there is nothing to write. When `/data` exists — which is exactly when
`rask deploy` has mounted the volume — the key ring is persisted to `/data/keys` and the application
discriminator is pinned to the application name. Both halves are load-bearing: the default discriminator is
derived from the content root, which differs between the build and runtime images, so a persisted ring alone
would still fail to unprotect.

On a plain `dotnet run` there is no `/data`, so Rask declines to choose and ASP.NET's per-user development
ring applies exactly as before. Two knobs, both rarely needed:

| Setting | Effect |
| --- | --- |
| `Rask:DataProtection:KeyPath` | Put the ring somewhere other than `/data/keys`. |
| `Rask:DataProtection:KeyPath` = `""` | Opt out entirely, and manage the ring yourself. |

To take it over completely, configure Data Protection **after** `AddRask` — options setups run in
registration order, so yours is the last writer and wins:

```csharp
builder.Services.AddRask();
builder.Services.AddDataProtection()
    .PersistKeysToAzureBlobStorage(/* … */)
    .SetApplicationName("my-app");
```

If you're carrying an app that predates this and wrote the block by hand, you can delete it. An app that
never had it will sign everyone out once, when the persisted ring first replaces the ephemeral one, and
never again.

### The log store

[`Rask.Logging`](logging.md) keeps its own SQLite file rather than mapping onto your `DbContext`, so it needs
its own pointer onto the volume — which is what `ConnectionStrings__Logs` above is. Without it the log would
land in the container's writable layer and be destroyed by the very restart it exists to survive.

Two consequences worth knowing before an incident rather than during one:

- **It is not backed up.** `rask db backup` and Litestream cover `app.db`. `logs.db` is deliberately outside
  that — logs are expendable and high-churn, and including them would make every snapshot and every WAL
  replication more expensive. Copy the file yourself if you need it archived.
- **It shares the volume's disk.** Retention bounds it by age *and* row count (14 days / 100,000 entries by
  default), so it cannot grow without limit — but on a small box, size the volume with both databases in
  mind, and watch `rask.logs.dropped` to know whether the store is keeping up.

## Scaffolding a Dockerfile — `--docker`

The three web templates take an opt-in `--docker` flag that drops a production-ready multi-stage
`Dockerfile` (+ `.dockerignore`) into the generated project:

```bash
rask new MyApp                                   # Kestrel app → aspnet:10.0 runtime image
rask new MyApp --template wasm                   # static WASM bundle → nginx:alpine
rask new MyApp --template wasm-hosted            # WASM client + host → aspnet:10.0 runtime image
```

Without `--docker` no container files are emitted.

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

**Caching in front of the app.** Every page used to be `Cache-Control: no-store`, because the shell
carries a session id. With [`RenderModes.Static`](render-modes.md) on, a page that needs nothing live is
served without one and becomes browser-cacheable (`private, max-age=0, must-revalidate`), which is
what restores instant back/forward. It stays `private`, so a shared cache or CDN still holds nothing —
deliberately: the framework will not put a page in a shared cache on your behalf. Anything
authenticated, faulted, or `>= 400` stays `no-store` regardless.

If you do add a cache in front, respect the `Vary` the app sends. It carries `Cookie` — because
"anonymous" is itself a function of the cookie — and, on a localized app, `Accept-Language` too.
Dropping either from the key lets a cache serve one visitor's page to another.

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

### When the app doesn't start

A misconfigured static host is the commonest way a WASM bundle fails, and the symptom used to be the
worst possible one: the splash screen, spinning, for ever. Nothing in the console, nothing on the page.
A 404 on `_framework`, the wrong MIME type, and a genuinely slow connection all looked identical.

They no longer do. **A Rask app that cannot start replaces its splash screen with what went wrong** —
which step failed, and the exception, verbatim:

> **This app failed to start.**
> The .NET runtime could not be loaded. Check that the `_framework` assets are being served, with the
> correct `application/wasm` content type.

The full error is in the browser console too. The three failures worth recognising:

| What it says | What it usually means |
| --- | --- |
| The .NET runtime could not be loaded | `_framework` is 404ing, or `.wasm` is served as `application/octet-stream`. Check the two `nginx.conf` items above. |
| The Rask browser module could not be loaded | `rask.wasm.js` did not reach the client — usually a sub-path deploy without `/p:RaskPathBase`. |
| The app finished starting but never rendered | The app booted and returned without painting. Check that `Program.cs` **awaits** `host.RunAsync<App>()`. |

The failure panel appears only before the app has mounted. Once it has, an uncaught error is handled by
the [root error boundary](lifecycle.md) instead, so a working page is never replaced by this one.

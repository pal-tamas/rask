# Configuration

`AddRask` takes two optional callbacks: `configure` for **shared** runtime options
(`RaskLiveOptions`, used by both the Server and WASM runtimes) and `configureServer` for the
**server-host-only** limits (`RaskServerOptions` — the WebSocket caps and session grace periods that
only the ASP.NET host has):

```csharp
builder.Services.AddRask(
    live   => live.MaxSessions = 1000,
    server => { server.MaxInboundFramesPerSecond = 500; server.SessionGracePeriod = TimeSpan.FromSeconds(20); });
```

Every option has a production-safe default, so `AddRask()` with no callback is fully functional. The
defaults are unchanged from previous releases — these knobs only *expose* limits that were previously
hardcoded, so upgrading changes nothing until you set one.

## Binding from appsettings.json

Both options objects are plain POCOs — bind them from configuration inside the callback:

```csharp
builder.Services.AddRask(
    configureServer: o => builder.Configuration.GetSection("Rask").Bind(o));
```

```jsonc
// appsettings.json
{
  "Rask": {
    "MaxInboundFramesPerSecond": 500,
    "SessionGracePeriod": "00:00:20"   // TimeSpan: "hh:mm:ss" (or "d.hh:mm:ss")
  }
}
```

`TimeSpan` values bind from the standard `"[d.]hh:mm:ss"` format. `AddRask` validates the bound values
and throws `ArgumentOutOfRangeException` at startup on an out-of-range one (a negative grace period, a
non-positive `MaxInboundFrameBytes`), so a typo fails the boot rather than misbehaving at runtime.

## Shared options — `RaskLiveOptions` (`configure`)

Applied to both the Server and WASM runtimes.

| Option | Default | Purpose |
| --- | --- | --- |
| `DiffMode` | `Auto` | Wire payload shape — `Auto` ships a diff when smaller, `DisabledFull` always full HTML, `Forced` always a diff. |
| `PathBase` | `""` | URL prefix so two Rask apps share one origin (e.g. `/appA`). |
| `MaxSessions` | `0` (uncapped) | Hard cap on concurrent live sessions; a GET past the cap gets `503` + `Retry-After`. Pairs with the [health check](observability.md#health-checks). See [sizing it for a memory budget](#sizing-maxsessions-for-a-memory-budget). |
| `MinifyScopedAssets` | `null` (auto) | Minify the scoped-CSS bundle (strip comments + insignificant whitespace) before it's hashed and served. `null` = **auto**: on outside `Development`, off in `Development` (so hot-reloaded CSS stays readable) — resolved by `UseRask` from `IHostEnvironment`. Set `true`/`false` to force it. Minifying before hashing keeps the digest, immutable URL, and brotli/gzip caches all keyed off the minified bytes. Conservative: only the CSS bundle is minified (JS is served as-is), and only whitespace around `{ } ; ,` is stripped, so combinators and `calc()` are untouched. |

## Server-host-only options — `RaskServerOptions` (`configureServer`)

WebSocket safety caps and session grace periods — only the ASP.NET host has these.

| Option | Default | Purpose |
| --- | --- | --- |
| `MaxInboundFrameBytes` | `8 MB` | Cap on a single reassembled inbound WebSocket frame — bounds a fragmented-frame memory DoS. |
| `MaxPendingHandlers` | `512` | Max queued handler dispatches before the socket is closed (backpressure). `0` disables. |
| `MaxInboundFramesPerSecond` | `1000` | Per-connection inbound message rate cap over a sliding 1 s window — bounds a small-frame CPU DoS. `0` disables. |
| `SessionGracePeriod` | `30 s` | How long a session is retained after its socket disconnects, for reconnect. |
| `UnconnectedSessionGracePeriod` | `10 s` | How long a GET-minted session is retained before its first `hello` arrives. |
| `IdleSocketTimeout` | `0` (off) | Close a connected socket that sends no inbound frame for this long (the session survives for reconnect). Reclaims silently-idle connections. |
| `MaxPendingHandlerBytes` | `0` (off) | Aggregate-bytes companion to `MaxPendingHandlers` — bounds the queued cloned-payload *memory*, not just the queue length. |
| `SendTimeout` | `30 s` | How long one outbound frame may take before the socket is aborted. A client that stops reading TCP would otherwise pin its session's render lock — and its disposal — indefinitely. The session survives for the grace period, so a briefly-stalled link reconnects normally. `0` disables. |
| `SessionResume` | `true` | Let a client rebuild its page on a host that has never heard of its session — after a restart or a redeploy. See [surviving a restart](#surviving-a-restart-or-a-redeploy). Turn it off to keep the reload. |
| `ResumeTokenLifetime` | `1 h` | How long a resume record stays redeemable. Not the reconnect grace period: that covers a blip against the *intact* session, this covers the session being gone. |
| `HandlerTimeout` | `0` (off) | Cancel a handler's `Component.CancellationToken` after this long. A handler that threads that token into its async work unwinds cleanly instead of pinning the render pipeline (cooperative — a token-ignoring handler can't be force-aborted). |
| `ShutdownDrainTimeout` | `5 s` | Budget for the graceful shutdown drain: announce the shutdown, let in-flight handlers finish, close each socket with a real handshake, dispose the sessions. `0` disables the drain (abort immediately). See [Shutdown and redeploy](#shutdown-and-redeploy). |

### Shutdown and redeploy

On `SIGTERM` — a redeploy, a container recycle, `Ctrl+C` — Rask drains instead of severing, with no
wiring on your part:

1. **Admission closes.** New sessions are refused with `503` + `Retry-After: 1`, and the `ready`-tagged
   health check goes unhealthy so a proxy or load balancer with active probes stops routing here.
2. **Connected browsers are told.** Every live session gets a `{"type":"shutdown"}` frame, so the client
   shows **"Updating…"** rather than the reconnect spinner — and, because the drop is now *expected* rather
   than guessed at, it reconnects immediately instead of walking a 500 ms → 5 s backoff ladder first.
3. **In-flight handlers finish.** A click that is mid-`SaveChangesAsync` completes rather than being
   cancelled halfway.
4. **Sockets close cleanly** — WebSocket status `1001` ("going away"), not an abort — and the sessions
   are disposed before the host stops.

Anything still connected when `ShutdownDrainTimeout` elapses is aborted, and
`rask.shutdown.sessions.abandoned` counts it.

> **What happens to the page itself is the replacement server's decision, not the client's.** A live
> session is a component tree plus a DI scope living in *that* process, so the new instance cannot inherit
> it. The client therefore reconnects and lets the host that answers decide: if it can rebuild the page,
> nothing reloads at all; if it reports the session unknown, the client reloads — in ~250 ms, saying
> "Updating…", and restoring your scroll position, your focus, and whatever you had typed.
>
> The drain's job is to make that drop *expected*. Before it, the socket was aborted with no close frame,
> so the browser could not tell a deployment from a crash: it froze, backed off, and eventually announced
> a four-second "Your session timed out" for a session that had not timed out.
>
> **What the user had typed comes back too, as a three-way merge.** Only fields they actually edited are
> candidates, each carrying the value the server had rendered *before* they touched it. After the reload
> a field is re-applied only when the replacement rendered that same value — its state is unchanged, so
> the edit is still the newest thing anyone knows. If the replacement rendered something different it
> knows something the stale copy doesn't, so it wins and the edit is dropped silently. Whatever is
> restored is then pushed back over the socket, so the server's model holds what the page shows rather
> than the pristine values it just rendered.
>
> Two rules worth knowing when you build forms:
>
> - **A field needs an `id` or a `name` to be restorable.** A bound `Input` gets a `name` from the bound
>   property automatically, so this is usually free — but if a key matches more than one control on the
>   page, that field is skipped rather than guessed at.
> - **`data-rask-no-restore` opts a field (or a whole subtree) out.** Passwords, file, hidden and
>   one-time-code inputs, and anything with a `cc-*` / `current-password` / `new-password` `autocomplete`,
>   are excluded unconditionally and never reach `sessionStorage` in the first place.
>
> `<select>` is not restored yet. The lagging-frame guard it needs — to hold off the replacement's first
> catch-up render — now exists alongside the ones for `value` and `checked`, but the save/apply side does
> not cover it. A `<select multiple>` needs one more thing first: the change dispatch reports the select's
> single `value`, which is only its first selected option.

`ShutdownDrainTimeout` must fit inside `HostOptions.ShutdownTimeout`, which must fit inside whatever your
container runtime allows between `SIGTERM` and `SIGKILL` (`rask deploy` uses 20 s). Rask logs a warning at
startup if the first of those doesn't hold. Note that `HostOptions.ServicesStopConcurrently` is `false` by
default, so other hosted services' shutdown work is spent from the same budget first.

### Reconnect UX

When the WebSocket drops, the client reconnects with exponential backoff. The framework's built-in
overlay handles the user-facing side automatically — nothing to configure:

- **Debounced.** A sub-second blip reconnects before the overlay ever appears (≈700 ms grace), so a
  brief hiccup never flashes a full-screen freeze over the app.
- **Escalating.** If reconnection keeps failing — or the browser reports itself offline — the overlay
  escalates from a neutral spinner to an explanatory message ("You're offline…" / "Still trying…") with
  a manual **Retry now** button. Regaining connectivity (the `online` event) reconnects immediately.
- **Session-expiry aware.** If the drop outlasts `SessionGracePeriod` the server discards the session;
  the client then shows "Your session timed out. Reload to continue." with a **Reload** button (and a
  fallback auto-reload) instead of silently reloading and wiping in-progress UI state. Raise
  `SessionGracePeriod` if your users routinely background the tab longer than the 30 s default.
- **Deploy aware.** A drop caused by the server shutting down is *not* reported as a timeout — see
  [Shutdown and redeploy](#shutdown-and-redeploy).

> **Using `HandlerTimeout`:** thread `Component.CancellationToken` into the cancellable async work your
> event handlers start, so the timeout (or socket close) can unwind them. Inside a handler that token
> reflects the dispatch; in a lifecycle hook it's just the component's lifetime token.
> ```csharp
> Button(OnClickAsync: async () =>
>     _data = await http.GetFromJsonAsync<T>(url, CancellationToken))["Load"]
> ```

### File uploads

`RaskUploadOptions` (registered separately) caps uploads:

| Option | Default | Purpose |
| --- | --- | --- |
| `MaxFileSize` | `50 MB` | Maximum size of a single uploaded file. |
| `MaxFilesPerRequest` | `16` | Maximum files in one multipart upload request. |
| `MaxBytesPerSession` | `0` (off) | Maximum cumulative staged-upload bytes one session may hold at once; a request over the quota is rejected with `413`. Released when the session ends. |

```csharp
builder.Services.Configure<RaskUploadOptions>(o => o.MaxFileSize = 10 * 1024 * 1024);
```

## Surviving a restart or a redeploy

A live session is a component tree, a DI scope and a set of cancellation tokens. None of that can be
serialized, so a session cannot be moved or saved — when the process holding it goes away, it is gone.

That used to be the end of the story: every `rask deploy` replaces the container, so every connected
client got *"Your session timed out. Reload to continue."* The deploy was zero-downtime for HTTP and a
full reload for everyone actually using the app.

What travels instead is a small sealed record of **where the page was** and **what the app declared**, and
a host that receives it back **rebuilds** the page around it. Nothing resumes — the page is built again,
which is exactly why what you declare is what comes back:

```csharp
public sealed partial class OrdersPage(IPersistentState state) : Component
{
    private string _filter = "";

    protected override void OnMount() => state.TryGet<string>("filter", out _filter!);

    private void Search(string term)
    {
        _filter = term;
        state.Persist("filter", term);   // survives a rebuild; everything else does not
    }
}
```

Inject it through the **constructor** — a settable non-nullable property becomes a required factory
parameter ([RASK002](diagnostics.md)).

**Declare state, don't stream it.** The bag is capped at 16 KB across all keys and rides the wire inside
the render payload. Persist identifiers and selections — a filter, a wizard step, a draft — not the rows
they resolve to. Over budget the session keeps working but declares itself unresumable and falls back to
the reload it would have had anyway, and logs a warning saying so.

**Even declaring nothing is worth something.** The route alone turns a deploy from a full-page reload into
a re-render of the page the user was already on.

**What does not come back:** anything you didn't name. In-flight async work, undeclared fields, open
interop handles. And note that a value only *reads* back if the JSON still fits the type — if you change
the shape of a persisted type, change its key too, or a renamed property will read back as a default
rather than a miss.

### What makes it safe

The record lives in the browser, which is what makes it need no shared store, no sticky routing and no new
infrastructure. It is encrypted and authenticated under its own data-protection purpose, so it is opaque
and unforgeable to the client holding it. Expiry is enforced by ASP.NET's time-limited protector rather
than a field we compare, so an expired record cannot be opened at all. It carries **no principal** — a
reconnect authenticates from its cookie or bearer token exactly as before — but it is bound to the
identity it was issued to, so it cannot be replayed onto another account, and signing in or out
invalidates it. A rebuild takes a `MaxSessions` slot through the same atomic reservation a `GET` uses, so
the reconnect storm after a deploy sheds like ordinary traffic instead of walking past the cap.

> **This needs a persisted key ring.** A record sealed by the container you just replaced can only be
> opened by the replacement if both share data-protection keys. `rask new` scaffolds that (`/data/keys`,
> on the volume the deploy already mounts) — see [deployment](deployment.md#your-users-stay-signed-in-across-a-deploy).
> Without it, every record is refused after a deploy and you are back to the reload. Watch
> `rask.sessions.resume_rejected{reason="unprotect"}`: a spike right after a deploy is exactly that.

## Sizing `MaxSessions` for a memory budget

`MaxSessions` defaults to `0` (uncapped), which is only safe when something else bounds who can reach
the app. Every session pins a component tree, a DI scope, and several buffers, so an uncapped host
facing untrusted traffic can be pushed into memory exhaustion. To set the cap you need to know what a
session costs — measure it with the capacity report:

```bash
dotnet run -c Release --project benchmarks/Rask.Benchmarks -- session-footprint
```

Measured on the framework's own data-table page (Apple M4, .NET 10, Server GC, 200 sessions per row):

| Page | Page HTML | Unconnected | Connected | Sessions per GiB |
| --- | ---: | ---: | ---: | ---: |
| Empty shell | 292 B | 11 KB | 16 KB | ~66,000 |
| 5-row table | 1 KB | 35 KB | 52 KB | ~20,300 |
| 200-row grid | 29 KB | 1.01 MB | 1.39 MB | ~735 |
| 1,000-row grid | 147 KB | 5.3 MB | 7.0 MB | ~146 |

**Page size, not user count, is what moves this** — the same host holds ~66,000 sessions of a trivial
page or ~146 of a big grid, a ~450× swing. Sessions are cheap until the page isn't. A session retains
roughly:

- **two rendered-HTML buffers** — the current render plus the last-applied baseline, used for
  dedup and the head-compare. They're `char` arrays, so a page costs ~**4 bytes of RAM per HTML
  character** across the pair, and they're rented from a pool that rounds up to a power of two.
- **two frame buffers** (~40 B per node, when the diff codec is on — it is by default) and **two
  payload buffers**.
- **the component tree.** Usually the largest term on a big page. A subtree is compacted — its element
  graph released in favour of a frame snapshot — unless it contains a nested component, so most rows
  hold a compact snapshot rather than a graph of objects.

Every one of these is rented at the size the page turns out to need, then grows to a high-water mark
and is reused, so per-session cost converges. Cost is a function of your largest page, not of uptime.

These are steady-state figures, and steady state is what a session settles into: a soak of 100 sessions
over 200 updates each holds flat to the byte, and 500 create-and-dispose cycles leave nothing behind at
all. Teardown also hands every pooled array back, so the next session reuses them instead of paying to
allocate its own — worth ~19% of the allocation a create-render-dispose cycle costs on a 200-row page.

### Fitting is not the same as serving

The table above answers how many sessions a host can *hold*. What it costs to actually *use* them is a
different question, and a capacity number you can't serve isn't a capacity number:

```bash
dotnet run -c Release --project benchmarks/Rask.Benchmarks -- session-load
```

That drives real WebSockets against a real host and times the round trip a user actually feels — the
click, the render it causes, and the acknowledgement that closes it. Indicative figures (Apple M4, 20
concurrent sessions, closed loop):

| Page | Events/sec | p50 | p99 |
| --- | ---: | ---: | ---: |
| Empty shell | ~100,000 | 0.15 ms | ~1 ms |
| 5-row table | ~85,000 | 0.19 ms | ~1 ms |
| 200-row grid | ~26,000 | 0.53 ms | ~6 ms |

Read the shape, not the absolutes: **page size costs you throughput before it costs you memory.** An
empty shell and a 5-row page are within noise of each other; a 200-row grid costs roughly 4× the
per-event time and a quarter of the throughput, because every interaction re-renders and re-diffs the
whole page.

Two things the harness deliberately does not measure. It reports no memory — the load generator shares a
process with the host it drives, so a heap reading would count the client's own sockets; that is what
`session-footprint` is for. And it turns off `MaxInboundFramesPerSecond`, because a closed-loop generator
trips a per-connection DoS cap that no human ever will. Leave that cap on in production.

To pick a number: take the RAM you'll give the process, subtract the app's own baseline, and divide by
the connected cost of your **largest** page — then leave headroom, because `MaxSessions` also counts
sessions created by a bare `GET` whose WebSocket never arrived (they hold a slot for
`UnconnectedSessionGracePeriod`, 10 s), and because a rejected user gets a `503`.

```csharp
// ~2 GiB of session budget for an app whose heaviest page measures ~1.34 MB connected.
builder.Services.AddRask(live => live.MaxSessions = 1200);
```

Two caveats before you trust the table. These are **framework floors** — they exclude the WebSocket
transport (Kestrel adds ~32 KB of per-connection buffers) and, more importantly, your own scoped
services: one `DbContext` per session can dwarf everything above. And they're measured on one page
shape. Run the report against your own budget rather than quoting these numbers, and pair the cap with
the [live-session health check](observability.md#health-checks) so an orchestrator sheds load before
the host starts refusing sessions.

## A note on limits and reverse proxies

The frame-size / frame-rate / pending-handler caps are coarse per-connection backstops, not a
substitute for edge protection. For precise admission control and rate limiting, pair them with a
reverse proxy (nginx, Envoy, a cloud load balancer) and the `MaxSessions` cap + the
[live-session health check](observability.md#health-checks) so an orchestrator can shed load before
the host starts refusing sessions.

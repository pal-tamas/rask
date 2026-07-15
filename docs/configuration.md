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
| `HandlerTimeout` | `0` (off) | Cancel a handler's `Component.CancellationToken` after this long. A handler that threads that token into its async work unwinds cleanly instead of pinning the render pipeline (cooperative — a token-ignoring handler can't be force-aborted). |

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
over 200 updates each holds flat to the byte, and 500 create-and-dispose cycles leave under 100 bytes
behind.

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

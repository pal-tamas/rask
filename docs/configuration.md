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
| `PreloadScopedAssets` | `true` | Warm every scoped CSS/JS asset into `<head>` up front (vs. on first mount). |
| `MaxSessions` | `0` (uncapped) | Hard cap on concurrent live sessions; a GET past the cap gets `503` + `Retry-After`. Pairs with the [health check](observability.md#health-checks). |

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
| `HandlerTimeout` | `0` (off) | Cancel a handler dispatch's `Component.EventCancellationToken` after this long. A handler that threads that token into its async work unwinds cleanly instead of pinning the render pipeline (cooperative — a token-ignoring handler can't be force-aborted). |

> **Using `HandlerTimeout`:** thread `EventCancellationToken` into the cancellable async work your event
> handlers start, so the timeout (or socket close) can unwind them:
> ```csharp
> Button(OnClickAsync: async () =>
>     _data = await http.GetFromJsonAsync<T>(url, EventCancellationToken))["Load"]
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

## A note on limits and reverse proxies

The frame-size / frame-rate / pending-handler caps are coarse per-connection backstops, not a
substitute for edge protection. For precise admission control and rate limiting, pair them with a
reverse proxy (nginx, Envoy, a cloud load balancer) and the `MaxSessions` cap + the
[live-session health check](observability.md#health-checks) so an orchestrator can shed load before
the host starts refusing sessions.

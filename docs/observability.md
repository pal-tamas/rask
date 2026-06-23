# Observability

Rask's server host is instrumented for production operations with standard .NET primitives — no
extra packages, OpenTelemetry-exportable out of the box. Four surfaces:

1. **Structured logging** — framework faults flow into your `ILogger` pipeline.
2. **Metrics** — a `Meter` named `Rask.Server` with session, handler, and frame counters/histograms.
3. **Tracing** — an `ActivitySource` named `Rask.Server` spanning handler dispatch.
4. **Health checks** — a live-session capacity check.

Everything is on by default with zero configuration; you opt in to *exporting* it.

## Structured logging

`Rask.Core` and the WASM host take no dependency on `Microsoft.Extensions.Logging` — instead they
report diagnostics through an internal seam (`RaskDiagnostics`). On the server, `UseRask<TApp>()`
bridges that seam to your application's `ILogger` automatically. From then on every framework
diagnostic — a lifecycle hook that threw with no ancestor `ErrorBoundary`, a component `Dispose`
that faulted, a duplicate sibling `Key`, a malformed WebSocket frame, an event handler that threw —
is logged through an `ILogger` named for the diagnostic's category, at the matching level, with the
original exception attached:

| Category | Emitted for |
| --- | --- |
| `Rask.Lifecycle` | Faulting lifecycle hooks / disposes with no ancestor `ErrorBoundary` |
| `Rask.Diff` | Duplicate sibling `Key` (keyed reconciliation silently disabled) |
| `Rask.Live` | Handler / navigate / JS-invoke faults, malformed frames, coalesce-budget drops |
| `Rask.JsInvoke` | Failures surfacing a JS-invoke fault back to the caller |
| `Rask.HotReload` | Dev-time asset/source-watch rerender failures |

No wiring is needed — configure log levels for these categories like any other:

```jsonc
// appsettings.json
{
  "Logging": {
    "LogLevel": {
      "Rask.Live": "Warning",
      "Rask.Diff": "Warning"
    }
  }
}
```

When no `ILoggerFactory` is registered (e.g. a bare test host), the seam keeps its default behaviour
and writes the same diagnostics to `stderr`.

## Metrics

All metrics publish on the meter named **`Rask.Server`** (`RaskTelemetry.MeterName`).

| Instrument | Kind | Tags | Meaning |
| --- | --- | --- | --- |
| `rask.sessions.created` | Counter | | Live sessions created (component tree + DI scope). |
| `rask.sessions.rejected` | Counter | | Creations refused because `MaxSessions` was reached. |
| `rask.sessions.evicted` | Counter | | Sessions removed (disconnect grace elapsed / shutdown). |
| `rask.sessions.active` | Gauge | | Live sessions currently held. |
| `rask.handlers.dispatched` | Counter | | Client event handlers dispatched to user code. |
| `rask.handlers.faulted` | Counter | | Handler dispatches that threw (isolated; session survives). |
| `rask.handlers.timedout` | Counter | | Handler dispatches cancelled by `HandlerTimeout`. |
| `rask.handler.duration` | Histogram (ms) | | Wall-clock duration of an event-handler dispatch. |
| `rask.ws.frames.rejected` | Counter | `reason` = `size` \| `rate` \| `backlog` | Inbound frames refused by a safety limit. |

The `rask.ws.frames.rejected` counter is the headline DoS-visibility signal: a spike on
`reason=rate` or `reason=backlog` means a client is being throttled by the per-connection frame-rate
cap or the pending-handler backpressure breaker.

### Reading metrics locally

```bash
dotnet-counters monitor --counters Rask.Server --process-id <pid>
```

### Exporting via OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(RaskTelemetry.MeterName))
    .WithTracing(t => t.AddSource(RaskTelemetry.ActivitySourceName));
```

## Tracing

Each event-handler dispatch starts an `Activity` named `rask.handler.dispatch` on the
`Rask.Server` `ActivitySource`, tagged with `rask.handler.id` and marked `Error` (with the exception
message) when the handler throws. With no listener registered the span is never materialised, so the
instrumentation costs nothing. Subscribe with `.AddSource(RaskTelemetry.ActivitySourceName)` as
above (or any `ActivityListener`).

## Health checks

`RaskLiveHealthCheck` reports live-session capacity. Register it on your health-checks pipeline:

```csharp
builder.Services.AddRask(o => o.MaxSessions = 1000);
builder.Services.AddHealthChecks().AddRaskLiveSessions();
// ...
app.MapHealthChecks("/health");
app.UseRask<App>();
```

| Status | Condition |
| --- | --- |
| `Healthy` | Below 80% of `MaxSessions` (or `MaxSessions == 0`, i.e. uncapped). |
| `Degraded` | At or above 80% of `MaxSessions` — the host is filling up. |
| `Unhealthy` | At `MaxSessions` — new sessions are being refused with `503`. |

The active and maximum session counts are attached to the health-check result `data` for dashboards.
Pair the capacity cap with a reverse-proxy rate limit for precise admission control.

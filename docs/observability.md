# Observability

Rask is instrumented for production operations with standard .NET primitives — no extra packages,
OpenTelemetry-exportable out of the box. Four surfaces:

1. **Structured logging** — framework faults flow into your `ILogger` pipeline.
2. **Metrics** — a `Meter` named `Rask.Server` with session, handler, and frame counters/histograms, plus
   one meter per DB-backed pillar (`Rask.Jobs`, `Rask.Outbox`, `Rask.Mail`) carrying throughput, failures
   and **dead letters**.
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
| `Rask.HotReload` | Dev-time hot-reload failures: a registry refresh that threw, a repaint that faulted, or an undeliverable applied-notification |

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
| `rask.ws.frames.rejected` | Counter | `reason` = `size` \| `rate` \| `backlog` \| `idle` | Inbound frames refused by a safety limit. |
| `rask.sessions.resumed` | Counter | | Pages rebuilt on a host that had never heard of the session, from the client's [resume record](configuration.md#surviving-a-restart-or-a-redeploy). |
| `rask.sessions.resume_rejected` | Counter | `reason` = `malformed` \| `unprotect` \| `principal` \| `toolarge` \| `atcapacity` | Resume records refused. |
| `rask.shutdown.sessions.abandoned` | Counter | | Sessions still connected when the shutdown drain budget ran out; their sockets were aborted. |
| `rask.sessions.connected` | Gauge | | Sessions with a socket attached — people actually looking at the app, as opposed to `active`, which also counts GET-minted sessions whose socket never arrived and sessions riding out their reconnect grace. |
| `rask.handlers.pending` | Gauge | | Handler dispatches queued across all sessions. The backpressure breaker's *input* — `frames.rejected{reason=backlog}` is its output. |
| `rask.render.duration` | Histogram (ms) | | Time to render a session and write its frame. The framework's half of a slow interaction; `handler.duration` is your half. |
| `rask.payload.bytes` | Histogram (By) | | Size of a frame sent to a client. Watch the distribution: a page that quietly stops diffing jumps to its full size here long before anyone notices the bandwidth. |

A nonzero `rask.shutdown.sessions.abandoned` is the signal that `ShutdownDrainTimeout` — or the
`HostOptions.ShutdownTimeout` containing it — is shorter than your app's real shutdown, so some users saw
an abnormal disconnect instead of a clean "Updating…" reload. Because it is emitted *during* shutdown, its
delivery depends on your exporter's final flush; the same fact is logged, which is the more reliable read.

The `rask.ws.frames.rejected` counter is the headline DoS-visibility signal: a spike on
`reason=rate` or `reason=backlog` means a client is being throttled by the per-connection frame-rate
cap or the pending-handler backpressure breaker. `reason=idle` is the `IdleSocketTimeout` reclaiming a
silently-idle connection, not an attack.

`rask.sessions.resume_rejected` is worth an alert of its own. A steady trickle is normal — expired records
from laptops that slept. **A spike on `reason=unprotect` immediately after a deploy means your
data-protection key ring is not surviving the deploy**, so every user is getting a reload instead of their
page back (and, if you use cookie auth, being signed out). See
[surviving a restart](configuration.md#surviving-a-restart-or-a-redeploy).

## Pillar metrics

Each DB-backed pillar publishes on its own meter, registered by its `AddRaskX` call. The shape is the same
for all three — only the noun changes.

| Instrument | Kind | Tags | Meaning |
| --- | --- | --- | --- |
| `rask.jobs.processed` | Counter | `job.type` | Jobs that ran to completion. |
| `rask.jobs.failed` | Counter | `job.type` | Attempts that threw — **every attempt**, not every job. |
| `rask.jobs.deadlettered` | Counter | `job.type` | Jobs that exhausted `MaxAttempts`, counted once. |
| `rask.jobs.duration` | Histogram (ms) | `job.type` | Wall-clock duration of one execution. |
| `rask.jobs.pending` | Gauge | | Not yet processed and not yet exhausted. |
| `rask.jobs.deadletters` | Gauge | | **The number worth alerting on.** |

`Rask.Outbox` publishes the same six as `rask.outbox.*` tagged by `message.type`; `Rask.Mail` publishes
`rask.mail.sent` / `failed` / `deadlettered` / `duration` / `pending` / `deadletters`.

**`rask.*.deadletters` is the alert.** Delivery is at-least-once with backoff, so a pillar retrying itself
to death still shows a healthy `processed` rate — the dead-letter gauge is the one that says work has been
abandoned. Pair it with the [dashboard](dashboard.md), which shows *which* rows and why.

> **Why mail has no type tag.** Jobs and the outbox tag by their registered type, a closed set fixed at
> build time by a source generator. Mail's only per-message dimensions are subject and recipient, both
> unbounded — tagging by either would mint a time series per email sent.

> **The queue-depth gauges cost nothing until you subscribe.** They are sampled by the processor's existing
> poll and only while a listener is attached, rather than running `COUNT(*)` inside the observable-gauge
> callback — which would otherwise put read load on the app's database on the collector's schedule.

### Reading metrics locally

```bash
dotnet-counters monitor --counters Rask.Server,Rask.Jobs,Rask.Outbox,Rask.Mail --process-id <pid>
```

### Exporting via OpenTelemetry

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter(RaskTelemetry.MeterName)
        .AddMeter(JobMetrics.MeterName)
        .AddMeter(OutboxMetrics.MeterName)
        .AddMeter(MailMetrics.MeterName))
    .WithTracing(t => t.AddSource(RaskTelemetry.ActivitySourceName));
```

## Tracing

Each event-handler dispatch starts an `Activity` named `rask.handler.dispatch` on the
`Rask.Server` `ActivitySource`, tagged with `rask.handler.id` and marked `Error` (with the exception
message) when the handler throws. With no listener registered the span is never materialised, so the
instrumentation costs nothing. Subscribe with `.AddSource(RaskTelemetry.ActivitySourceName)` as
above (or any `ActivityListener`).

## Health checks

`AddRaskLiveSessions()` registers two checks, because they answer different questions: capacity
(`RaskLiveHealthCheck`, tagged `live`) and readiness (`RaskReadinessHealthCheck`, tagged `ready`).
Register them on your health-checks pipeline:

```csharp
builder.Services.AddRask(o => o.MaxSessions = 1000);
builder.Services.AddHealthChecks().AddRaskLiveSessions();
// ...
app.MapHealthChecks("/health");
app.UseRask<App>();
```

| Status | Condition |
| --- | --- |
| `Healthy` | Below 80% of `MaxSessions` (or uncapped), and memory below 80% of the limit. |
| `Degraded` | At or above 80% of `MaxSessions`, **or** memory at 80% of the limit. |
| `Unhealthy` | At `MaxSessions` (new sessions are being refused with `503`), **or** memory at 92%. |

**Memory is checked as well as the session count, and outranks it.** A cap alone can't keep a host
healthy, because what a session costs is a property of the page rather than of the user: the same host
holds ~66,000 sessions of a trivial page or ~735 of a 200-row grid. A cap sized for the small page is no
protection on the big one. The reading comes from `GCMemoryInfo`, which honours a container memory limit,
so it reflects the ceiling a deployed app actually runs under — and an uncapped host, which is what most
apps run, now reports something before an OOM does. A memory position the runtime won't disclose is
treated as healthy, not as full: a host must not shed load because it can't measure itself.

`activeSessions`, `connectedSessions`, `maxSessions` and `memoryLoad` are attached to the health-check
result `data` for dashboards. Pair the capacity cap with a reverse-proxy rate limit for precise admission
control.

### Readiness

`RaskReadinessHealthCheck` answers one question: *is this instance still accepting live sessions?* It is
`Healthy` normally and `Unhealthy` from the moment a graceful shutdown begins, so an aggregate `/health`
returns `503` while draining and a load balancer with active probes stops routing here.

It is kept separate from the capacity check on purpose — that one's `Unhealthy` already means "at
`MaxSessions`, refusing with `503`", and folding a second cause into it would make both readings
ambiguous exactly when someone is diagnosing an incident. Split them with the tags when you want distinct
probes:

```csharp
app.MapHealthChecks("/health/ready",
    new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapHealthChecks("/health/live",
    new HealthCheckOptions { Predicate = c => c.Tags.Contains("live") });
```

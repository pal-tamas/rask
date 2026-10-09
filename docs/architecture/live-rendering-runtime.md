# Live rendering — cache, head & dispatch

The per-session diff baseline, head/query-only navigations, handler ordering, and slow-connection affordances.

‹ Back to [Live rendering](live-rendering.md)

## `SessionRenderCache`: the two-buffer rotator

The per-session diff baseline lives in `SessionRenderCache` (`Live/SessionRenderCache.cs`).
It owns **two `FrameWriter` buffers** — one holds the "previous" snapshot the client
currently has, one is the render in flight — and rotates them. Both survive in pooled
storage, so steady-state allocation across renders is zero. The typical flow:

```csharp
var writer = cache.PrepareCurrentBuffer();        // reset the in-flight buffer
using (FrameSinkScope.Push(writer))
    HtmlSerializer.Serialize(root, htmlOutput);   // populate it during the render
bool haveDiff = cache.TryComputeDiff(ops, html);  // diff against previous, then ROTATE
```

> ### Invariant: `TryComputeDiff` rotates on every call
>
> `TryComputeDiff` rotates the buffers internally (promotes current → previous) on
> **every** call — including the first-render `false` return. So you must **never**
> call `Snapshot()` after it on the same render: `Snapshot()` also rotates, and a
> second rotation strands `_previous = null` and corrupts the next diff. `LiveSession`
> tracks this with a `diffPathEntered` flag and only calls `Snapshot()` on the
> full-HTML branch *when it did not enter the diff branch*:
>
> ```csharp
> if (!usedDiff)
> {
>     LivePayload.BuildPayloadUtf8WithRoot(...);
>     if (!diffPathEntered)            // TryComputeDiff already rotated otherwise
>         _renderCache?.Snapshot();
> }
> ```

`Snapshot()` (rotate without diffing) keeps the cache in lockstep when the session
ships full HTML for any reason — first paint, oversized diff, structural ops,
navigation. Skipping it would leave `_previous` stale and the next diff would apply
edits computed against a DOM the client has already moved past. There's also a
`rotate: false` overload used by the WASM coalescing loops, which build a payload
several times within one dispatch but commit exactly once via `Snapshot()` after the
loop settles (so intermediate builds don't diff against an un-sent render).

## Head changes and query-only navigations

The diff frame stream walks the **body** but suppresses head-asset frames (the head
registry pushes a `null` `FrameSinkScope`), so a `<title>`/scoped-asset change produces
zero body ops. Two helpers in `LiveDiffGate` bridge this:

- `HeadUnchanged(html, baseline)` — compares the `<head>…</head>` region of the fresh
  render against the last sent baseline byte-for-byte. A missing `</head>` returns
  `false` (treated as changed → safe).
- `ExtractHead(html)` — slices the full `<head …>…</head>` element out so the diff
  payload can carry it as the `"head"` field; the client morphs it into
  `document.head` alongside applying the body ops, instead of falling back to a whole
  document.

**Query-only / body-unchanged navigations** produce zero ops but still must `pushState`
the URL. The session ships the diff anyway when there's a `historyUrl` or a head change,
even with an empty op list (`LiveSession.cs`):

```csharp
if (_renderCache.TryComputeDiff(_diffOps, html)
    && (_diffOps.Count > 0 || historyUrl is not null || headChanged)
    && LiveDiffGate.DiffOpsAreClientSupported(_diffOps))
```

The `"history"` field carries `{ action: "push"|"replace", url }`.

## Handler dispatch ordering

Both transports hold a session-wide lock across the **whole awaited handler**, so an
async handler's continuation can't interleave with the next handler's start.

### Server: strict WS-arrival order

The WS receive loop is single-threaded per session. Each inbound handler is chained
onto `LiveSession.LastHandlerTask`: the new dispatch awaits the prior tail before
running, then stores its own continuation as the new tail
(`RaskEndpointExtensions.cs`, `ChainHandlerDispatchAsync`):

```csharp
capturedSession.LastHandlerTask = ChainHandlerDispatchAsync(
    capturedSession.LastHandlerTask, capturedSession, handlerId, root, ct);
```

This pins start-of-dispatch to WS-arrival order **without blocking the receive loop**
(so async handlers can still interleave with the `jsResult`/`dotNetInvoke` frames they
await). The comment explains why a `Task.Run` + `SemaphoreSlim.WaitAsync` shape was
wrong: `SemaphoreSlim` is FIFO on *WaitAsync* call order, not `Task.Run` order, so
under ThreadPool contention an `input`→`submit` pair could acquire the lock
`submit`→`input` and let `submit` read a stale `EditContext`.

### WASM: a single `SemaphoreSlim`

`WasmLiveSession` guards every dispatch with `_lock = new SemaphoreSlim(1, 1)` held
across the awaited handler (`WasmLiveSession.cs`). The render walk is single-threaded
per session; `InHandlerScope` is a plain instance bool (deliberately *not* `AsyncLocal`)
because the lock is owned by the session as a whole.

### Events of one task: one frame, one render

The events a browser task produces leave together. Sixty charts measured by one `ResizeObserver` callback
announce sixty sizes in one task; a typed value flushed ahead of a click leaves with that click. Both clients
hold handler events until the task ends (a microtask — `rask-batch.ts`) and send them as one frame:

```json
{"type":"batch","events":[{"id":"h4","type":"input","value":"150 50","seq":1}, …]}
```

An event alone in its task travels as the frame it always was, byte for byte. Anything that is not a handler
event — a navigation, an interop reply — goes at once, *behind* whatever was waiting, so nothing overtakes an
event that happened before it. At the end of the task rather than at the next animation frame: a click still
leaves in the task it happened in, and no patch can land between an event being read off the page and its being
sent.

The session answers a batch under **one** hold of its lock (`ChainBatchDispatchAsync` on the Server, one link of
the handler chain; `WasmLiveSession.DispatchBatchAsync` on WASM): the handlers run in the order their events
happened, each seeing the state the one before it left, and the page is rendered once after the last. One ack
goes back, for the newest `seq`, after that render — and on WASM the promise that holds typed text back resolves
only then.

**What a handler can tell stays as it was.** A render re-runs the components that are dirty and, through
changed props, the ones below them; a component that runs again makes its handlers again, closing over what
that render computed. So before each event of a batch the session asks whether a render made now would rebuild
that handler (`Component.HandlerOutlivesRender`): is its component, or any component above it, dirty? If so the
page is rendered first — exactly as when each event was a frame of its own — and the handler that runs is the
one that render registers. Only a handler whose whole path to the root is clean runs ahead of the render. In
practice: events that land in *different* components (charts, rows, cells that each own their state) are one
render; events that land in the *same* component, or under one an earlier event dirtied, are rendered between,
as before. These still render at once, mid-batch:

- a handler that navigates or signs in or out — the frame with the address is sent before the next event runs;
- a handler that awaits — the render made while it waits is sent as it always was (`RenderInScopeAsync`);
- a handler that throws into an error boundary — the boundary is dirty, so the fallback is rendered before the
  next event, which then finds no handler, as before.

One thing does differ, and only for a handler that ran ahead of a render: side effects of *another*
component's lifecycle hooks (`OnRendered`, a mount) that the skipped render would have run first have not
happened yet when it runs. State the handlers themselves wrote is always there.

A batch is bounded like the frames it replaces: at most 256 events (`EventBatch.MaxEvents`, what the client sends at
most — the rest go in a frame of their own), and each event is counted against `MaxInboundFramesPerSecond`, so
framing a flood as batches does not multiply how many handlers a client may run in a second. To
`MaxPendingHandlers` and `MaxPendingHandlerBytes`, which bound what is queued, a batch is one dispatch of its
frame's size. A longer batch ends the connection the way any tripped breaker does.

## Slow-connection affordances

Both transports give honest feedback on a slow link without changing the fast-path
behaviour — each indicator stays invisible until a latency threshold is crossed.

### WASM: boot progress

The page shell (`src/Rask.Wasm/Browser/index.html`) carries a hidden
`.rask-boot__progress` bar under the splash spinner. `Browser/main.ts` wires the runtime's
`onDownloadResourceProgress(resourcesLoaded, totalResources)` callback (via
`dotnet.withModuleConfig(...)`) and reveals the bar with a determinate
`loaded / total` percentage, so a slow link shows movement instead of an indefinite
spinner. Progress is **resource-count, not bytes**: framework assets are commonly
served Brotli/gzip precompressed, so a byte bar would have to reconcile encoded vs.
decoded sizes — counts sidestep that. When the runtime reports no usable total the
bar stays hidden and the spinner stands in. The App's first render morphs over the
whole shell, so there's no teardown.

### Server: the pending-action bar and the handler ack

`rask.js` installs a managed (`data-rask-managed`) 2px top-of-viewport bar that
appears when a handler round-trip outlives `PENDING_LATENCY_MS` (~300ms) and clears
when the reply lands — distinct from, and one z-index below, the full reconnect
overlay. It is driven by an **opt-in ack protocol**:

- The client stamps a monotonic `seq` on handler events only (click/input/change/
  submit/drag* — anything carrying an `id` that the server dispatches through its
  handler chain; `jsResult`/`navigate`/`dotNetInvoke`/`hello` are excluded). It tracks
  the highest outstanding seq, arms the latency timer, and a hard-timeout backstop.
- After each handler dispatch completes, the server replies `{"type":"ack","seq":N}`
  (`ChainHandlerDispatchAsync` → `SendHandlerAckAsync`, riding `SendOutOfBandAsync` so
  it serialises on the render lock and lands *after* that handler's render frame and
  *before* the next handler's). The ack fires **even when the render dedupes and ships
  no frame** (`RenderAndSendAsync`'s HTML/byte dedup returns silently) — without it a
  no-op click would wedge the bar.
- The client clears the bar on the matching (or any later) ack, synchronously on
  receipt (not inside the `_renderQueue`, so a CSS-gated deferred body swap can't keep
  it up). Reconnect resets the outstanding/acked counters and the reconnect overlay
  takes over.

**Opt-in:** the server only acks when the inbound handler carried a `seq`, so a
seq-less client gets byte-for-byte the prior frame contract (the render envelope is
unchanged — the ack is a separate tiny frame, not a payload field). See
`tests/Rask.Server.Tests/WebSockets/PendingAckTests.cs`.

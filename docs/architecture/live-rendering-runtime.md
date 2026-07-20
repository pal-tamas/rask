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

## Slow-connection affordances

Both transports give honest feedback on a slow link without changing the fast-path
behaviour — each indicator stays invisible until a latency threshold is crossed.

### WASM: boot progress

The page shell (`src/Rask.Wasm/Browser/index.html`) carries a hidden
`.rask-boot__progress` bar under the splash spinner. `main.js` wires the runtime's
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

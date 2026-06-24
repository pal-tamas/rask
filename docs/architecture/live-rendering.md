# Live rendering & the diff codec

How a Rask app stays live after first paint, and how it ships the smallest
possible payload on every state change. This document expands the *Live runtime &
diff codec* section of [`CLAUDE.md`](../../CLAUDE.md) with code grounding. File
references point at `src/Rask.Core/Live/`, `src/Rask.Server/`, and
`src/Rask.Wasm/`.

## Big picture: one component model, two transports

You write components once. The render walk, the frame stream, and the diff codec
all live in `Rask.Core` and are shared verbatim. Only the *transport* — how a
re-render reaches the browser DOM — differs by host:

```
                         Component tree (shared Rask.Core)
                                     │
                       HtmlSerializer.Serialize(...)
                                     │
              ┌──────────────────────┴──────────────────────┐
              │ HTML string                  RenderFrame stream │
              │ (StringBuilder)              (FrameWriter, parallel) │
              └──────────────────────┬──────────────────────┘
                                     │
                          FrameDiffer.Diff → List<EditOp>
                                     │
            ┌────────────────────────┴────────────────────────┐
            │ Server                              WASM         │
            │ LiveSession over a WebSocket        WasmLiveSession over JSImport/JSExport │
            │ (src/Rask.Server/LiveSession.cs)    (src/Rask.Wasm/WasmLiveSession.cs)     │
            └────────────────────────┬────────────────────────┘
                                     │
                  rask.js / rask.wasm.js → applyDiff / morph the DOM
```

- **Server** (`Rask.Server`): the runtime `<script>` opens a WebSocket. Inbound
  event-handler messages are dispatched in `LiveSession`; each render produces a
  payload that's pushed back down the socket.
- **WASM** (`Rask.Wasm`): there is no socket. `WasmLiveSession` is driven through
  `[JSImport]`/`[JSExport]` boundaries declared in `JSInterop.cs`. JS calls
  `[JSExport] DispatchAsync(byte[] json)` to deliver an event; .NET pushes the
  resulting payload back with the `[JSImport("applyRender")] ApplyRender(byte[])`
  import (the JSExport source generator doesn't support `Task<byte[]>` returns, so
  the result is *pushed*, not returned — see the comment in `JSInterop.cs`).

### The runtime `<script>` is auto-injected

`HtmlSerializer` injects the runtime as the **last child of `<body>`** — you never
write `RaskRuntimeScript()`. On serializing the `</body>` close it resolves the
host-registered `IRaskRuntimeScript` from DI and serializes its tag inline
(`HtmlSerializer.cs`, the `tagName == "body"` branch):

```csharp
if (tagName == "body" && live?.Services?.GetService<IRaskRuntimeScript>() is { } runtime
                      && runtime.Render() is { } runtimeScript)
{
    Serialize(runtimeScript, sb);
}
```

It is emitted **inline on every render** (not via a post-process sentinel) so its
bytes are stable across renders — the diff codec therefore never emits ops for it.
On **WASM no `IRaskRuntimeScript` provider is registered** (the runtime boots from
the page shell / `main.js`), so nothing is injected there.

## The render walk: HTML + a frame stream in parallel

`HtmlSerializer.Serialize(Component, StringBuilder, FrameWriter?)` does the work.
It always produces the HTML string. When `FrameSinkScope.Current` is set to a
`FrameWriter`, it *also* records a parallel `RenderFrame` stream — a compact
tagged-union modelled on Blazor's `RenderTreeFrame` but trimmed to the variants
Rask actually diffs (`Live/RenderFrame.cs`):

| `RenderFrameKind` | Role |
|-------------------|------|
| `Element` (1)     | Opens an element; close is implicit at `index + SubtreeLength` |
| `Attribute` (2)   | One name/value attribute on the last-opened element |
| `Text` (3)        | HTML-encoded text content |
| `Raw` (4)         | Verbatim markup (`Raw(...)`) |
| `Doctype` (5)     | The doctype declaration |
| `Component` (6)   | Marks the start of a user component's rendered subtree (carries the instance ref) |

Key fields on `RenderFrame`:

- `SubtreeLength` — total frames in the subtree (incl. self); `1` is a leaf. Patched
  in by `FrameWriter.CloseElement`/`CloseComponent`. Lets a consumer skip a subtree
  without recursion.
- `HtmlStart`/`HtmlEnd` — UTF-16 offsets into the rendered HTML string marking where
  this frame's output lives. The codec slices `[HtmlStart..HtmlEnd]` to ship the HTML
  fragment for an `InsertSubtree` op.
- `Value` on an `Element` frame holds the active scoped-CSS id, so an inserted element
  can be re-stamped `data-{scopeId}` client-side.

`FrameWriter` rents its backing `RenderFrame[]` from `ArrayPool`, so steady-state
appends amortize to zero allocation. `FrameSinkScope` is a `[ThreadStatic]` ambient
holder so the 48 `WriteAttributes(StringBuilder)` overrides can emit `Attribute`
frames without a signature change (`Live/RenderFrame.cs`).

> The `Component` frame's instance ref lets the diff short-circuit: an unchanged
> component instance that still yields a cached subtree can be skipped.

## The diff codec: ship a minimal edit-op list, not full HTML

`FrameDiffer.Diff(oldFrames, newFrames, output, ...)` walks the previous render's
frame stream against the current one and emits a minimal `List<EditOp>` that
transforms the old DOM into the new (`Live/FrameDiffer.cs`). Each `EditOp` names its
target by `Path` — a **child-index sequence from the document root counting only
DOM-relevant nodes** (Element / Text / Raw / Doctype). Attribute frames are not
counted; `Component` frames are *transparent* (their body contributes siblings at the
surrounding level). The op kinds (`EditOpKind`):

| Kind | # | Meaning |
|------|---|---------|
| `SetAttribute`     | 1 | set/replace an attribute |
| `RemoveAttribute`  | 2 | remove an attribute by name |
| `UpdateText`       | 3 | replace text/raw node content |
| `InsertSubtree`    | 4 | insert a new subtree (carries pre-serialized HTML fragment) |
| `RemoveSubtree`    | 5 | remove a contiguous run of `Length` sibling subtrees |
| `MoveSubtree`      | 6 | move an existing sibling node (detach at source, insert at dest) |
| `PermutationBatch` | 7 | a batch of sibling moves under one keyed parent (see below) |

### When a diff ships vs full HTML

`LiveDiffMode` (`Live/LiveOptions.cs`) is configured via
`AddRask(o => o.DiffMode = ...)` / `WasmHostBuilder.CreateDefault(o => ...)`:

- **`Auto`** (default) — *choose-smaller*. Ship the diff whenever it isn't larger
  than re-sending the body. Fall back to full HTML on the first render (no baseline),
  on untrusted structural ops, and on out-of-band side effects.
- **`DisabledFull`** — always full HTML; bit-for-bit pre-codec behaviour.
- **`Forced`** — always diff when one is computable, even when larger than the body;
  for tests/benchmarks.

The actual decision lives in `LiveSession.RenderAndSendAsync` (server) and the
equivalent WASM path. Sketch (`Rask.Server/LiveSession.cs`):

```csharp
if (_renderCache.TryComputeDiff(_diffOps, html)
    && (_diffOps.Count > 0 || historyUrl is not null || headChanged)
    && LiveDiffGate.DiffOpsAreClientSupported(_diffOps))
{
    LivePayload.BuildPayloadUtf8Diff(_writeBuffer, _diffOps, historyUrl, replace, jsInvokes, headHtml);
    var diffBytes = _writeBuffer.WrittenCount;
    if (diffMode == LiveDiffMode.Forced || diffBytes < html.Length)
        usedDiff = true;   // ship the diff
    else
        _writeBuffer.ResetWrittenCount();   // diff lost on bytes → full HTML
}
```

The byte fallback only kicks in for the pathological case where nearly every node
changed on a tiny page, so the op framing exceeds the body. Any genuine in-place
state change (counter tick, text edit, attribute, keyed-list edit) is far smaller
than the body and takes the diff path regardless of page size.

The diff path is **skipped entirely** when there's an out-of-band side effect —
`auth` or `download` instructions (both transports gate on
`auth is null && download is null`). Fire-and-forget `IJSRuntime` invokes (e.g. a
scoped-JS `OnRenderedAsync` hook) and navigation *do* ride the diff — they don't force
full HTML.

### Wire format

`LivePayload.BuildPayloadUtf8Diff` writes UTF-8 JSON directly (no UTF-16 string
materialisation), using `UnsafeRelaxedJsonEscaping` to avoid escaping `<`/`>`/`&` —
pure wire-size win, the client `JSON.parse` is identical. Envelope shape:

```json
{
  "kind": "diff",
  "names": ["class", "style"],
  "ops": [ ... ],
  "head": "<head>...</head>",
  "history": { "action": "push", "url": "/path" }
}
```

Each op is a **positional array**, not an object — `[kind, [path...], extra...]`,
where `extra` depends on the kind (`LivePayload.cs`):

```json
[1, [0, 2], "class", "active"]      // SetAttribute: name, value
[2, [0, 2], "class"]                // RemoveAttribute: name
[3, [1, 0], "new text"]             // UpdateText: value
[4, [1], "<li>…</li>", 1]           // InsertSubtree: html fragment, length
[5, [1], 2]                         // RemoveSubtree: length
[6, [1], 3]                         // MoveSubtree: source slot in Length
[7, [1], [dst0, src0, dst1, src1]]  // PermutationBatch: flat moves array
```

> The conceptual shape is `{k, p, n, v, l}` (kind, path, name, value, length); on the
> wire it's serialized positionally to save bytes.

**Name interning** (`names`): a pass-1 symbol table interns any attribute name that
appears 3+ times across the ops (break-even with table overhead), so an attribute
burst sharing one name (e.g. 100 ops on `class`) drops the duplicate name to a single
integer index per op. Small diffs pay no envelope.

The client (`rask.js` / `rask.wasm.js`) applies these via `applyDiff(ops)`. Its
`resolvePath` walks `parent.childNodes` **filtered to Element/Text/Doctype only**, so
the path coordinates line up with the server's DOM-relevant frame counting.

`applyDiff` and its helpers live in **one shared source**,
`src/Rask.Core/Resources/rask-dom.js`, spliced into both clients at build time at the
`RASK_DOM` marker — the same mechanism the full-HTML morph (`rask-morph.js`, `RASK_MORPH`
marker) already uses (see `_RaskBuildClientJs` in `Rask.Server.csproj` and
`_RaskSpliceClientJs` in `Rask.Wasm.csproj`). Keeping the codec in a single shared source
means both runtimes decode the C# `FrameDiffer` opcodes identically — they cannot drift. The
shared code is modern JS, with two splice constraints: its top-level helpers stay hoisted
`function` declarations (so `applyDiff` can call `reviveScript`/value-guard helpers regardless
of splice order), and it uses no `export`/`import` (it is spliced inside the Server's
classic-`<script>` IIFE, where module syntax is illegal).

## Keyed reconciliation: trusted structural ops

Give list items a stable `Key:` (Blazor `@key` parity — last optional factory param)
and the diff reconciles them by *identity* instead of position. This unlocks
**trusted** Insert/Remove/Move ops that preserve focus, selection, scroll, IDL
property state, and event listeners on surviving nodes. The client relocates a moved
node with the Atomic Move API (`Node.moveBefore`, Chromium 133+), which repositions it
**without disconnecting it from the document** — the distinction matters: `removeChild`
+ `insertBefore`, or even a bare `insertBefore` of a connected node, briefly detaches it
and so *blurs* a focused descendant and drops its selection/caret (the element survives,
but its interaction state does not). Where `moveBefore` is unavailable the runtime falls
back to `insertBefore`, preserving the node and its value but not its focus.

`EditOp.Trusted` marks an op produced by `FrameDiffer`'s keyed-matching branch. The
gate `LiveDiffGate.DiffOpsAreClientSupported` (`Live/LiveDiffGate.cs`) routes any
**untrusted** `InsertSubtree`/`RemoveSubtree`/`MoveSubtree`/`PermutationBatch` op to
the full-HTML morph path:

```csharp
if ((op.Kind == InsertSubtree || RemoveSubtree || MoveSubtree || PermutationBatch)
    && !op.Trusted)
    return false;   // → full HTML
```

The reasoning (verbatim from the source): positional structural ops can replace
mid-list elements the morph would have preserved — ungating them unconditionally broke
83/430 E2E tests in an earlier iteration. So **positional (keyless) structural changes
fall back to full HTML**; only **keyed/trusted** ops ship as diff. The
`SessionRenderCache.TryComputeDiff` overload surfaces `usedKeyedPath` so the session
knows whether the diff touched the keyed branch.

### Minimal moves via LIS

When a keyed list reorders, the keyed branch (`DiffKeyedSiblings` in
`Live/FrameDiffer.cs`) computes the permutation mapping each surviving item to its new
position and finds the **longest-increasing-subsequence (LIS)** of that permutation.
Items *on* the LIS are already in the right relative order and never move; everything
else is repositioned. This is the same minimal-moves strategy React/Vue/Inferno use:

```csharp
// targets[i] = new position of surviving[i]; lis = its longest increasing subsequence
ComputeLisIndexSet(targets, n, lis);
// walk new indices RIGHT-TO-LEFT, anchoring each off-LIS node before the
// already-final node at the next index — the standard correct minimal-move reconcile
```

**Complexity.** The LIS itself is `O(n log n)`. The move loop then repositions each off-LIS node
with a `live.IndexOf` + `live.Insert` on a `List<int>`, each `O(n)`, so the loop is
`O((n − |LIS|) · n)`. The number of off-LIS nodes — not `n` — is what matters: a **full reverse**
has `|LIS| = 1`, so all `n − 1` rows move and the loop degrades to `O(n²)` (this is the genuine
worst case, and it's deliberately pinned by `FrameDifferBenchmarks.ReverseReorder_ReusedScratch`).
**Realistic** edits keep a near-full LIS — a table sort that reranks only the head, a feed that
drops a few rows and appends new ones — so only a handful of rows are off-LIS and the loop stays
effectively linear (`TopNRerank_ReusedScratch` / `AppendWithDeletes_ReusedScratch` pin those shapes).
The quadratic case is bounded (one `List<int>` per parent, `n` capped by the rendered list size) and
hasn't warranted replacing the list with an `O(log n)` order-statistic structure; the benchmarks
guard against a regression that would make it matter. At 1000 rows the full reverse runs ~1.9× a
two-element swap, while the top-N rerank and append-with-deletes shapes run at ~1.0× — confirming the
cost is the off-LIS move count, not the list size.

### `PermutationBatch` (op kind 7)

Rather than emitting one `MoveSubtree` op per moved row — each re-emitting the full
parent path, which dominates the wire bytes of a large reorder — the run's moves are
accumulated as a flat `[dst0, src0, dst1, src1, …]` array and shipped as a **single
`PermutationBatch` op carrying the shared parent path once**. The array order is
load-bearing: each dst/src pair is computed against the live DOM as mutated by all
preceding pairs, so the client replays it strictly left-to-right. This is what
collapsed a 200-row reverse-sort from ~3.9 KB to ~1.1 KB (see the *Performance*
section of the [README](../../README.md)). Like `MoveSubtree`, it only ever comes from
the keyed path and is always `Trusted`.

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

## See also

- [`../diagnostics.md`](../diagnostics.md) — **RASK022** (warning) flags a keyless list
  item that would reconcile positionally; add a `Key:` to get trusted keyed structural
  ops instead of a full-HTML morph.
- [`CLAUDE.md`](../../CLAUDE.md) — *Live runtime & diff codec*, *Primitives*,
  *Children & factories*, *Page head* sections (the authoritative summary).
- [`../authentication.md`](../authentication.md) — the auth handshake that forces full
  HTML (the `auth` out-of-band instruction the diff path gates out).

# Live rendering — the render walk & diff codec

How the render produces a frame stream alongside HTML, and how the diff codec turns two frame streams into a minimal edit-op list.

‹ Back to [Live rendering](live-rendering.md)

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

### One frame per DOM node: text & `Raw`

`Path` indexing only works if **every DOM-relevant frame maps to exactly one browser
DOM node**. Two cases would otherwise break that 1:1 mapping, so the runtime normalizes
them (`Live/RenderFrame.cs`, `FrameWriter.Text`):

- **Adjacent text coalesces.** `Div()["a", value]` is two `Text` frames, but the browser
  merges adjacent text into a *single* DOM node. The frame writer concatenates contiguous
  text frames into one so the model matches — including text on either side of a
  transparent component (a `[...]` collection or `Context`), which emits no markup of its own. (Contiguity is
  detected by `HtmlEnd == htmlStart`: any tag or node between two texts advances the HTML,
  so element-separated text stays distinct.)
- **Empty text emits nothing.** An empty/`null` string child produces no HTML and no DOM
  node, so no frame is emitted for it — otherwise every following sibling's path would
  drift by one.

`Raw` is the exception that *can't* be normalized: its verbatim markup parses into an
unknown number of top-level nodes (zero, one, or many). A solitary `Raw` is safe (its
nodes span the whole parent — nothing is indexed after it), but a `Raw` that shares a
sibling level with other nodes makes every following position unreliable. When a changed
sibling level mixes a `Raw` with other nodes, the diff sets a force-full-HTML flag
(`DiffScratch.ForceFullHtml` → `SessionRenderCache.LastDiffForcedFullHtml`) and the render
falls back to the morph, which reparses the markup correctly rather than shipping a
mis-targeted positional op.

### When a diff ships vs full HTML

`LiveDiffMode` (`Live/LiveOptions.cs`) is configured via
`AddRask(o => o.DiffMode = ...)` / `WasmHostBuilder.CreateDefault(o => ...)`:

- **`Auto`** (default) — *choose-smaller*. Ship the diff whenever it isn't larger
  than re-sending the body. Fall back to full HTML on the first render (no baseline),
  on untrusted structural ops, and on out-of-band side effects.
- **`DisabledFull`** — always full HTML; bit-for-bit pre-codec behaviour.
- **`Forced`** — always diff when one is computable, even when larger than the body;
  for tests/benchmarks.

The chosen mode is snapshotted onto **each `LiveSession`** at construction (from the host's
`RaskLiveOptions` — the Server carries it on the `LiveSessionStore`, WASM on the host builder),
and read from that instance field on the render hot path. It is **not** a process-global static, so two
hosts in one process — and parallel tests — each render in their own mode instead of racing a shared
field. (`PathBase` / `MinifyScopedAssets` remain on the static `LiveOptions` because they back the
process-wide content-addressed asset registries, which build one shared bundle per process.)

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

`applyDiff` and its helpers live in **one shared module**,
`src/Rask.Core/Resources/rask-dom.ts`, which both client entry points `import` — the same
arrangement the full-HTML morph (`rask-morph.ts`) uses. Keeping the codec in a single shared
module means both runtimes decode the C# `FrameDiffer` opcodes identically; they cannot drift.
esbuild bundles each entry point into the runtime its host serves (`_RaskBundleClientJs` in
`Rask.Server.csproj` and `Rask.Wasm.csproj`).

This replaced an MSBuild `String.Replace` that pasted the shared files into each host at
`// @@RASK_*@@` markers, in an order the build had to get right, with cross-module calls going
through implicit `window.__rask*` globals. Nothing checked the order, the markers, or that a
caller and its callee agreed — and the two constraints it imposed (no `export`/`import`, and
top-level helpers kept as hoisted `function` declarations so splice order could not matter) are
gone with it.

## Keyed reconciliation: trusted structural ops

Give list items a stable `.Key(…)` (Blazor `@key` parity — an ordinary chain step)
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

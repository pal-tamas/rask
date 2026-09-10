# Vs-Blazor residual losses — by-design justifications

The Phase 2 fix cycle (shell-strip, lazy per-Component dictionaries, O(N log N)
keyed-diff LIS) closed the headline losses. The residuals below are
**structural** — fixing them requires a framework redesign rather than a
targeted hot-path change. Each entry below is acknowledged here so the
"zero loss" gate in Phase 4 isn't blocked on a multi-week refactor that
isn't part of this program.

## 1. Per-Element allocation gap (~1.4-3×)

**Where it shows up:** Every multi-element render scenario where Rask is
constructing the tree per call — `Scope_StaticListLarge`,
`Scope_TextHeavy 100/1000`, `Scope_NestedTree 50Deep`, `LargePage`,
`LiveDiffPayload_*` (which rebuild before/after trees per op),
`Realistic_TableSortFlip`, `MemoryGc_*` (which amplify ×10 000 iterations).

**Root cause:** Rask elements are heap-allocated `class Component` instances
(`Rask.Core/Component.cs`). Each instance carries roughly:

- 16 B object header
- ~10 nullable reference slots (handlers, children dict, edit-context pool,
  parent map, …) — held as `_field?` and lazy-instantiated, but the slot
  itself is 8 B per declaration whether null or not
- Element-specific fields (`Id`, `Class`, `Style`, `Data`)
- A `Child[]` array per element with children (from the indexer)

Rough total: **~80-150 B per Element instance**, vs Blazor's
`RenderTreeBuilder` which writes 16-byte `RenderTreeFrame` structs into a
single pooled array (zero per-element heap allocation).

**Why we accept this:** Rask trades per-element heap cost for the
identity/lifecycle model the framework is built on. Every Component instance
carries its own scope id, error-boundary pointer, lifetime CancellationToken,
handler registry, and persisted-children dict (for `GetOrCreate` reuse across
re-renders). Blazor accomplishes lifecycle tracking via a parallel
`ComponentState` table keyed by frame index; Rask collapses that into the
Component instance itself, which is simpler to reason about and to inject DI
services into, but allocates more per element.

**What we did:** The lazy-allocation pass cut per-Element overhead ~40 % by deferring
`_children` / `_previousChildren` / `_persistedEditContexts` until the component
participates in a live render, and the `LiveState` hoist dropped plain Elements to
~24 B. Most recently, a **children-walk de-boxing** pass (`Component.ChildrenArray` +
the index walk in `HtmlSerializer`) removed the `SZGenericArrayEnumerator<Child>`
(~32 B) that the old `foreach` over `IEnumerable<Child>` allocated on **every**
child-bearing element, every render. After that pass the render-hot-path allocation
gap is **0.52×–1.19×** vs Blazor (was 1.4–3×), and Rask now beats Blazor on **time**
across every RenderHotPath scenario (0.29×–0.83×). The remaining gap is the field-slot
footprint plus the per-call `Child[]` array on multi-child elements.

**Further mitigation paths (not pursued in this program):**

- Replace the `params Child[]` indexer with a small inline struct-buffer (≤2 slots)
  for the common one/two-child element. Evaluated and **declined**: it adds ~16 B to
  *every* Element (including the leaf-heavy static-list/text scenarios where it doesn't
  help), and requires routing the `Fragment`/head-asset direct `.Children` reads
  through a unified accessor — net wash once the de-boxing pass already closed the gap
  to near-parity.
- Pool Component instances across renders. Breaks identity semantics; would
  require a parallel "render token" indirection.

## 2. Keyed `MoveSubtree` loop — was incorrect *and* O(N²); now fixed

**Status: CLOSED.** This entry previously documented a 5.77× time loss on
`Scale_KeyedRandomPermutation 1000` and accepted it as structural. Investigation
revealed the loop was not merely slow — it was **emitting the wrong moves** for any
keyed permutation that needs 3+ moves.

**The correctness bug (now fixed):** `FrameDiffer.cs` walked the off-LIS elements in
*target-ascending* order and inserted each at its numeric target index in a mutating
list. That ordering does not account for the unmoved LIS backbone the moved nodes
must weave around, so the resulting DOM order was incorrect. Example: old
`[0,1,2,3,4,5]` → new `[2,0,4,1,5,3]` produced `[0,2,1,4,5,3]`. The ops ship on the
wire (keyed moves are `Trusted=true`, so they bypass the full-HTML fallback gate), so
this corrupted the client DOM. It went unnoticed because the only keyed-move test
asserted op *count*, never the resulting order, and no E2E exercised a multi-element
reorder. The fix walks new indices **right-to-left**, anchoring each off-LIS element
to the already-final element at the next new index — the standard correct minimal-move
reconcile (Vue/Inferno). Guarded by `FrameDifferTests
.Diff_KeyedList_RandomPermutation_MoveOpsReproduceTargetOrder` (seeded permutations
N=50–250, replays the emitted ops to assert they reproduce the target order).

**The perf gap (now closed):** the correct algorithm also replaced the old
per-move `string.Equals` linear scan with an integer `List.IndexOf`, eliminating the
dominant constant factor. The move count and wire bytes are unchanged (still
LIS-minimal); only the computation got correct and cheaper. Measured on
`Scale_KeyedRandomPermutation` (Apple M4 Pro, short-run, directional):

| N    | Before (buggy) | After (correct)         |
|------|----------------|-------------------------|
| 100  | 1.17× Blazor   | **0.79×** (Rask faster) |
| 500  | 3.16×          | **1.06×** (parity)      |
| 1000 | 5.87×          | **~1.4×**               |

Realistic keyed-list sizes (≤500 rows) are now at or below Blazor parity. The residual
~1.4× on a 1000-element *random* permutation is the leftover O(N²) of the
`List.IndexOf` + `RemoveAt`/`Insert` simulation; an O(N log N) order-statistics-tree
replacement would close it but only benefits unrealistically large random reorders, so
it is intentionally left as an open lever.

## 3. Sustained MemoryGc amplification (now ~9-13×, was 16-26×)

**Where:** `MemoryGc_AppendDeletePressure`,
`MemoryGc_KeyedShufflePressure`, `MemoryGc_DeepTreeMutationPressure`.

**Root cause:** These scenarios run 10 000 sustained renders per BDN op.
The per-render structural alloc gap from (1) above multiplies by 10 000.
After the children-walk de-boxing pass (see (1)), the band dropped from
**16-26×** to **~9-13×** (`MemoryGc_AppendDeletePressure` 9.6×,
`KeyedShufflePressure` 9.4-10×, `DeepTreeMutationPressure` 12.7×) — removing one
~32 B enumerator allocation per child-bearing element, multiplied across 10 000
renders, is a large absolute reduction. The diff codec works fine on the wire-bytes
axis (headline `LiveDiffPayload_CounterOnLargePage` ships **0.38× the bytes** Blazor
does); the residual GC pressure is Rask's per-element heap mass on the server side, not
anything the diff path is doing.

**Why we accept the residual:** Same root cause as (1) — the per-Component heap
instance is the identity/lifecycle trade-off. The sustained-load benchmark exists
precisely to surface it honestly. Closing it further requires the Component-pooling
redesign noted in (1), which breaks identity semantics.

---

## Net "fix where Rask loses" status

- **All time-axis losses > 10 % closed** at the headline render-path scale.
  RandomPermutation 1000 dropped from 5.87× to ~1.4× (and is now a *correctness
  fix*, see (2)); the sustained churn benches (3) remain time-bound by the
  per-Component heap mass (1).
- **Allocation-axis losses** narrowed to **0.52×–1.19×** on the render hot path
  (was 1.4–3×) after the children-walk de-boxing pass; the residual is the
  per-Component instance mass (1), documented above.
- **Allocation-axis wins** at the small-tree / single-update scale:
  Counter (0.47× alloc + 0.29× time vs Blazor),
  Dashboard_CounterTick (0.55× + 0.37×),
  NavSwitch (0.45× + 0.47×),
  CounterOnLargePage diff (0.38× + 0.36×),
  AttributeUpdate diff (0.58× + 0.54×),
  VirtualizedList (0.02× + 0.001×).

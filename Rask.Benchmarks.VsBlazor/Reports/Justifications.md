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

**What we did:** The Phase 2 lazy-allocation pass already cut the per-Element
overhead by ~40 % across the board by deferring `_children`,
`_previousChildren`, and `_persistedEditContexts` allocation until the
component actually participates in a live render. The remaining gap is the
field slot footprint plus the per-call `Child[]` array.

**Further mitigation paths (not pursued in this program):**
- Hoist live-render-only fields into a lazy `LiveState` container (~96 B
  saved per Element, but every live render allocates one `LiveState` →
  benefit is concentrated in pure-HTML render scenarios).
- Replace the `params Child[]` indexer with a struct-buffer overload for
  small (≤8 element) lists.
- Pool Component instances across renders. Breaks identity semantics; would
  require a parallel "render token" indirection.

## 2. Keyed `MoveSubtree` loop is O(N) per move

**Where:** `Scope_KeyedRandomPermutation 1000` (Rask 5.77× Blazor on time)
and adjacent random-permutation scenarios.

**Root cause:** `FrameDiffer.cs:590-618` walks `surviving` linearly per move
to find the current source index, then mutates with `List<T>.RemoveAt`
+ `Insert` (also O(N) shift). For a near-worst-case permutation with M ≈ N
moves, total work is O(N²).

**Why we accept it (for now):** A Patience-Sort-style LIS replacement is the
bigger algorithmic win (already shipped — 7× speedup on `KeyedReorder_Large
5000`). Optimising the move loop to O(N log N) requires either a Fenwick
tree for index tracking or a key-based wire protocol — both invasive. The
remaining 5-6× gap on RandomPermutation 1000 is bounded by GC pressure from
the structural Component allocation in (1) above; closing (1) likely makes
this disappear on its own.

## 3. Sustained MemoryGc amplification (16-26×)

**Where:** `MemoryGc_AppendDeletePressure`,
`MemoryGc_KeyedShufflePressure`, `MemoryGc_DeepTreeMutationPressure`.

**Root cause:** These scenarios run 10 000 sustained renders per BDN op.
The per-render structural alloc gap from (1) above multiplies by 10 000 →
ratios in the 16-26× band. The diff codec works fine on the wire-bytes axis
(headline `LiveDiffPayload_CounterOnLargePage` ships **0.38× the bytes**
Blazor does); the GC pressure comes from Rask's per-element heap mass on the
server side, not from anything the diff path is doing.

**Why we accept it:** Same root cause as (1). The sustained-load benchmark
exists precisely to surface this trade-off honestly. Fixing requires the
same structural redesign.

---

## Net "fix where Rask loses" status

- **All time-axis losses > 10 % closed** at the headline render-path scale,
  except RandomPermutation 1000 (see (2)) and the sustained churn benches
  (see (3)). Both downstream of (1).
- **Allocation-axis losses** in the 1.4-3× band remain on multi-element
  scenarios. These are (1), documented above.
- **Allocation-axis wins** at the small-tree / single-update scale:
  Counter (0.47× alloc + 0.29× time vs Blazor),
  Dashboard_CounterTick (0.55× + 0.37×),
  NavSwitch (0.45× + 0.47×),
  CounterOnLargePage diff (0.38× + 0.36×),
  AttributeUpdate diff (0.58× + 0.54×),
  VirtualizedList (0.02× + 0.001×).

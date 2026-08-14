---
name: rask-review
description: Review a Rask diff or PR for security, performance, memory usage, and .NET/C# best practices before merge. Use whenever reviewing changes in the Rask repo, or as the review step of rask-ship. Runs the built-in /code-review and /security-review, then adds Rask framework-aware checks.
---

# rask-review

First run the generic passes, then apply the Rask-specific lens below. Address findings before
opening/merging the PR.

```
/code-review          # correctness + reuse/simplification/efficiency on the diff
/security-review      # security scan of pending changes
```

## Security
- **XSS**: only `Text` HTML-encodes; `Raw` emits verbatim. Any new `Raw`/`HtmlSerializer` path
  with user-influenced content is a finding.
- **Auth handshake**: redeem rejects cross-origin (`IsSameOrigin`); redeem-ticket TTL respected;
  `SessionUserProvider.Clear()` invalidates; JWT only rides the WS as `?access_token=`.
- **Scoped assets** stay `.AllowAnonymous()` + `nosniff` + immutable cache; no secrets in payloads.
- Route guards: `[Authorize]`/`[AllowAnonymous]` and client redirects (`/login`, `/forbidden`) intact.

## Performance / memory
- Render hot path stays allocation-lean: no LINQ, no per-render closures, pooled
  `StringBuilder`/writers; prefer spans/UTF-8 literals.
- Diff codec must still win on bytes; **`SessionRenderCache.TryComputeDiff` rotates on every call
  — never `Snapshot()` after it on the same render**.
- Any hot-path change **requires `run-benchmarks` evidence** (Allocated delta) in the PR.

## .NET / C# best practices
- **Prefer standard .NET / BCL APIs over hand-rolled code — don't reinvent the wheel.** Flag any
  custom implementation of something the framework/BCL already provides.
- **Refactor opportunistically**: if the diff touches duplicated or unclear code, extract/clean it
  (within reason, with tests) rather than copy-pasting more.
- Nullable correctness; `sealed` types; file-scoped namespaces; modern pattern matching.
- No sync-over-async; propagate `CancellationToken` (esp. Virtualize `ItemsProvider`).
- Dispose/unsubscribe symmetry: `RouteState.Changed` / `IUserProvider.Changed` subscribed in
  `OnMount`, unsubscribed in `OnUnmount`; no `StateHasChanged()` in `OnUnmount`.

## Hold UX, security, and performance together
Judge every change on all three at once — a win on one must not silently regress another.
Good developer/user experience (clear APIs, helpful errors, no flicker), safe defaults, and a
lean hot path are co-equal acceptance criteria, not trade-offs to make quietly.

## Rask invariants
- Attribute render order (id, class, style, data-*, then tag-specific).
- Root renders into `<body>`, never the shell itself — RASK021. Keyed lists in projections — RASK022.
- Components constructed via the generated factory, not `new` — RASK014.
- Inject framework services through the ctor, not as non-nullable settable props (RASK002).

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
  `SessionUserProvider.Clear()` invalidates. The session is a cookie — a bearer token on the WS
  URL or a token in browser storage is a finding, not a feature.
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

## Design — SOLID / Clean Code
- **One responsibility per type and per file.** A file that reads a model, validates it AND emits/renders
  is three types. Split mechanically: partial files by responsibility first (`Type.Responsibility.cs`),
  then an internal type where the seam is a unit you could test on its own.
- **Open/closed through the existing seams** (chain groups, the runtimes table, battery wiring) — a new
  case is a new entry, not another `switch` arm in a god method.
- **Depend on abstractions via the ctor** (already the DI rule); a new interface stays small (ISP).
- **Small methods, intention-revealing names**; more than 7 parameters → a parameter record.
- **No copied helper**: reuse it, or move it to a shared home (`Rask.Generators.Shared` for generators).
- Refactors stay behaviour-preserving and public-API-frozen unless the change says otherwise.

## Domain code — the one DDD way (`docs/domain.md`, `docs/upgrading-to-domain.md`)
Apply to every change that adds or touches an aggregate, an entity, a value object, a form that saves one,
a job or event handler that writes, or a doc/template/site snippet that shows any of these. A snippet in
`docs/`, the tutorial, a template or `src/Rask.Site` is held to the same list: it is what readers copy.
- **No service layer.** A write is an aggregate method (an operation or a static factory) called
  through its generated entry (`Supply.Change(id, version, …)`), never a `*Service`/`*Manager`/handler
  that loads, mutates and saves. A rule that decides something lives on the aggregate it is about.
- **One root, one save.** An operation changes ONE aggregate; a second aggregate changes from a domain
  event (`On(E)` / `OnDelete()` on that aggregate, or a durable handler that calls its operation).
  A transaction that writes two is `manyAggregates: "<why>"` on the `Operation`/`Db.Transaction` with a
  real reason (RASK178) — never silent. The load sits inside the transaction that changes it (RASK185).
- **Refusals are `Refuse`.** `Refuse.When/Unless(cond, () => Field, message)` names the field a form
  shows it under; an `InvalidOperationException`/`ArgumentException` in a rule is a finding (RASK184).
- **Value objects carry the rule, built through `Create`.** Properties are value objects
  (`partial record X(string Value) : Text(Value, MaxLength: n)`, `Number(...)`, a several-value
  record with `Validate()`) or typed ids; only `bool`/`enum` stay primitive. Construction is
  `X.Create`/`TryCreate`/`CreateOptional`, never `new X(...)` that skips the rule; a rule stated twice
  (attribute + base, or a FluentValidation rule over a VO) is RASK181/RASK183.
- **Typed ids.** `public readonly partial record struct OrderId(Guid Value);` — a raw `Guid`/`int`
  key or a foreign id held as a primitive is a finding.
- **Aggregate shape.** `private set` only (RASK084), no constructor with arguments (it turns the generated creates off, RASK086 — a static factory instead),
  no `[Key]` (RASK143), children through `Children<TEntity, TSpec>.Sync` from the aggregate only
  (RASK180), no navigation to another aggregate (hold its id), time from `Clock` (RASK140), no service
  lookup in domain code (RASK141).
- **No SQL, no provider API.** No `FromSql*`/`ExecuteSql*`, no `HasColumnType("nvarchar…")`,
  `UseSqlServer`-only calls or provider-specific functions in app code — name the intent
  (`InTextOrder()`, `Db.Lock`, `.Covering(…)`) and let Rask write it for SQLite, SQL Server and
  PostgreSQL (RASK177). A raw `ExecuteUpdate/Delete` over aggregate rows is `UpdateWhere`/`DeleteWhere`
  (RASK187); an `X.Update(id, x => x.M(…))` is the generated entry (RASK186); no `IDbContextFactory`
  in a page, job or handler around an aggregate's own writes (RASK133).
- **The form is the operation's.** A form that saves an aggregate is `Form.For(X.Operation, id)` (rows
  for a list parameter, a field group for a several-value VO, `Change<T>` for keep/clear/set); a hand
  model class that copies the aggregate's fields plus a hand `.Validate(...)` repeating its rules is a
  finding. `Ui.EditorDialog` + `.Delete(X.Delete)` for create/edit/delete over a list.
- **Tested on SQLite in memory.** Each new rule or operation has a test on `TestDatabase.Start()`
  (a database per test, parallel-safe), refusals asserted at their field path
  (`Test.Refuses(...).At("Parts[1].Quantity")` / `.Only(...)`), form parity with `Test.FormShows`,
  test data from `Test.Make<T>()`, races with `Test.Concurrently`. No SQL Server fixture for a unit
  test, no mocked read face or `X.Find`.
- **Data changes are migrations.** Moving stored data is `b.Data(…)` in the migration
  (`docs/data-writes.md#moving-data-in-c-with-bdata`); a new unique index ships with a read-only duplicate-check SQL
  for the user to run before deploy.

## Hold UX, security, and performance together
Judge every change on all three at once — a win on one must not silently regress another.
Good developer/user experience (clear APIs, helpful errors, no flicker), safe defaults, and a
lean hot path are co-equal acceptance criteria, not trade-offs to make quietly.

## Rask invariants
- Attribute render order (id, class, style, data-*, then tag-specific).
- Root renders into `<body>`, never the shell itself — RASK021. Keyed lists in projections — RASK022.
- Components constructed via the generated factory, not `new` — RASK014.
- Inject framework services through the ctor, not as non-nullable settable props (RASK002).

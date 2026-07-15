# Changelog

All notable changes to Rask are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions are stamped at pack
time (`$(PackageVersion)`); this log groups changes by the pull request that introduced
them until tagged releases begin.

## [Unreleased]

### Security
- **SQLite is no longer vulnerable to CVE-2025-6965 — the shipped native library moves from SQLite 3.49.1 to
  3.50.4.** `Microsoft.Data.Sqlite` and EF Core Sqlite pin the SQLitePCLRaw `2.1.11` family, whose
  `lib.e_sqlite3` bundles SQLite 3.49.1 — a memory-corruption flaw (`GHSA-2m69-gcr7-jv3q`, High; fixed in
  SQLite 3.50.2) that reached every Rask SQLite package, the mobile heads included. Rask now references the
  SQLitePCLRaw **3.x** bundle explicitly wherever SQLite is used; that family drops `lib.e_sqlite3` in favour
  of `SourceGear.sqlite3` (SQLite 3.50.4), so the vulnerable package leaves the graph entirely rather than
  being bumped around. The `NuGetAuditSuppress` that accepted this advisory is removed, and `dotnet list
  package --vulnerable --include-transitive` is now clean across the solution. Verified end-to-end:
  `select sqlite_version()` through the real graph reports `3.50.4`.

### Fixed
- **Flaky E2E: `JwtServerAuthExampleTests.Journey_JwtLogin_AdminRoundTrip_ThenNonAdmin`.** It asserted the JWT
  isn't JS-readable with `DoesNotContain("eyJ", stored)`, but `stored` is a Data Protection ciphertext — so
  `eyJ` turning up *somewhere* inside its base64url bytes is pure chance (~1 run in 900), and it duly failed on
  a blob that was never a JWT. A raw JWT is identified by *starting* with the base64url `eyJ` header, so the
  assertion now checks `StartsWith` (matching the `WasmJwtAuthExampleTests` sibling, which had it right).
  Test-only; the property it guards is unchanged, and it still fails — naming the leaked token — when the
  sample is mutated to store the raw JWT.
- **`Rask.Wasm.Hosting`, `Rask.Validation.DataAnnotations` and `Rask.Validation.FluentValidation` could not be
  restored at all.** Each referenced `Rask.Core` without `PrivateAssets="all"`, and because Core is
  `IsPackable=false` their nuspecs declared a dependency on a `Rask.Core` package that exists on no feed — at
  version `1.0.0`, which MinVer never stamps. Every `dotnet restore` that touched one died with `NU1101: Unable
  to find package Rask.Core`, so the wasm-hosted template and both validation add-ons were unusable from
  nuget.org. The references are now private, matching `Rask.Server`/`Rask.Wasm`, which is what lets consumers
  pick up `Rask.Core.dll` from the host package's `lib/` instead. Nothing about the shipped assemblies changes.
  The in-repo projects that had been inheriting Core through those references now name it directly, as
  `Rask.Example.Server` already did, and a new repo-wide test fails if any packable project ever again depends
  on an unpublishable one.
- **The `Rask.Wasm` package never declared its `Microsoft.JSInterop` dependency.** The runtime uses JSInterop
  directly (`WasmJSRuntime`, `WasmLiveSession`, the typed `Browser/*` wrappers), but it only ever arrived
  transitively through the `PrivateAssets="all"` `Rask.Core` reference — which deliberately keeps Core out of the
  nuspec — and the WASM track has no `Microsoft.AspNetCore.App` framework reference to supply it instead. It is now
  surfaced at the package boundary like `Microsoft.AspNetCore.Authorization` already was, so consumers restore it.
- **The `rask` CLI and the `Rask.Data` / `Rask.Outbox` / `Rask.Testing` packages are now published.** All four
  were packable but missing from the release + nightly pack lists, so `dotnet tool install -g Rask.Cli` failed
  ("Rask.Cli is not found in NuGet feeds") and the packages that `rask generate feature --events`/`--outbox` and
  `--tests` add to a project couldn't be restored from nuget.org. They now pack and push alongside the rest.
- **Flaky `Rask.Outbox` test: `The_processor_drains_the_outbox_and_publishes_events`.** It waited for the
  in-process handler to run and then asserted on the *persisted* `ProcessedAt`, but the drain publishes the
  whole batch first and only writes `ProcessedAt` in a single end-of-batch `SaveChangesAsync` — so the
  assertion raced that write and lost whenever the disk was busy. It now waits on `ProcessedAt` itself (which
  implies the publish already happened). Test-only: the processor's ordering is the documented
  at-least-once behaviour and is unchanged.
- **A clean child could replay a stale forwarded `data-rask-key`.** A keyless component's first element
  adopts a keyed ancestor's forwarded key and bakes it into its cached frame snapshot. Nothing dirties
  that child when the ancestor's key changes, so it would re-emit the old identity and the diff would
  reconcile the subtree against the wrong sibling — moving the wrong DOM. The snapshot now records the
  forwarded key it was captured under and falls back to a walk when it no longer matches.

### Changed
- **`rask new` generates the `server`, `wasm` and `native` templates itself — no `dotnet new` / Rask.Templates.**
  The CLI is now the scaffolding authority: `rask new <name>` writes the project's files directly, bakes the
  `Rask.*` package references at the CLI's own version (falling back to the latest published stable for local/dev
  CLI builds), and runs `dotnet restore` so the output builds immediately. Every feature-flag combination (server:
  `--auth`/`--pwa`/`--cqrs`/`--docker`; wasm: `--auth`/`--pwa`/`--docker`) is covered by a build-the-output test.
  The native template takes `--host local|server` (default `local`) and emits the WebView-hybrid iOS + Android
  project, resolving the platform-manifest permission blocks per host. The one remaining template (`wasm-hosted`)
  still goes through `dotnet new` until its generator lands in a follow-up. Every generated app's home page now
  greets you and shows the `rask generate` / `rask dev` cheatsheet.
- **`rask generate feature` takes its fields positionally.** Write
  `rask generate feature Product Name:string Price:decimal` instead of
  `--fields "Name:string,Price:decimal"`. The legacy `--fields` form still works (you just can't combine the
  two), so existing scripts keep running. Quote specs with `?` or `(…)` so your shell doesn't expand them
  (`'Note:string?(500)'`). Extra positional arguments on `generate page`/`component` — which have no fields —
  are now an error instead of being silently ignored.
- **Live sessions now size their buffers to the page instead of pre-renting a fixed block — a small-page
  session costs ~4.6x less memory.** A session used to rent ~33 KB of `char` buffers, ~20 KB of frame
  buffers and ~8 KB of payload buffers up front, before knowing whether its page was 300 bytes or
  300 KB — most of what an idle session cost. All of them now rent on first use, at the size the page
  actually needs. Pages large enough to outgrow the old defaults re-rented anyway, so they are
  unaffected; small pages get several times denser. Measured with `session-footprint`: an empty-shell
  session drops **74 KB → 16 KB** (~14,500 → ~65,000 sessions/GiB) and a small page **98 KB → 51 KB**
  (~10,900 → ~20,600); a 200-row grid is unchanged. The wire format is byte-identical (the
  `payload-bytes` gate passes unchanged), diff-path allocation is byte-identical across every
  `FrameDifferBenchmarks` row, and session create→dispose allocation is marginally *lower*.
  The one public-API detail: `FrameWriter`'s default `initialCapacity` is now `16` rather than `256` —
  pass an explicit capacity when you build a short-lived writer whose frame count you already know.
- **Keyed subtrees are now eligible for the clean-subtree cache, making updates to keyed lists ~15%
  faster.** A component carrying a `Key` was excluded from the cache outright, because a `Key` is a
  reconciliation identity rather than a reactive prop — it can change without dirtying the component,
  and replaying a snapshot would then emit a stale `data-rask-key`. The snapshot now records the
  identity it was captured under and declines to replay under a different one, so keyed subtrees cache
  like any other. On a 1,000-row keyed list this is **−13% allocation and −15% time per update** (the
  element path re-walks the graph and re-stringifies every key on every render; a replay does neither).
  It costs ~4% more retained memory on such a page — a per-row snapshot runs slightly larger than the
  small element graph it releases — which is a deliberate trade: the ceiling moves a little, every
  interaction gets cheaper.
- **Handler-bearing subtrees are now cached too, making updates to interactive grids ~24% cheaper.** The
  last exclusion from the clean-subtree cache was any subtree containing an event handler — which is the
  shape most real data grids take (a keyed row with a button), so the pages with the most elements to
  release were releasing none. Handler ids are positional and reissued from zero every root render, so a
  replay used to be unsound twice over: it never re-registered its handlers (leaving the id absent from
  the freshly-cleared map — a dead button) and never advanced the counter (shifting every later sibling's
  ids into collisions). The snapshot now records the handler run and the counter value it was captured
  at; a replay re-registers the run and advances the counter, reproducing exactly what the walk did, and
  declines when the counter no longer lines up. On a 1,000-row interactive grid: **−23.6% allocation and
  −28% time per update** (200-row: −18.7% / −30.4%), for ~2.7% more retained memory.
  As part of this the cache's `LiveState` fields collapse into a single reference to a side object, so
  only components that actually cache pay for the state — an empty-shell session gets *smaller*
  (16,370 → 16,090 B) despite the added handler bookkeeping.

### Added
- **Showcase: a Gantt chart wrapping a real third-party JS library** ([#394](https://github.com/pal-tamas/rask/issues/394)).
  `samples/Rask.Example.Shared/Features/Gantt` wraps [frappe-gantt](https://github.com/frappe/gantt) (MIT,
  vendored) as an ordinary Rask component — typed `GanttTask`/`GanttHoliday`/`GanttViewMode` props in, plain
  C# delegates out for click / drag / progress — and is embedded in the **JavaScript interop** guide, whose
  third-party section now covers the whole recipe: give the library a childless leaf to own, mount in
  `OnRenderedAsync`, tag the nodes it creates `data-rask-managed` so a full-HTML frame's morph can't delete
  them, and route its callbacks back through a static `[JSInvokable]` keyed by an id. Showcase + docs only —
  no framework or package change (`Rask.Bootstrap` stays JavaScript-free).
- **`rask generate feature --fields` supports `date`, `time`, and `datetime`.** `date` maps to `DateOnly`,
  `time` to `TimeOnly`, and `datetime` to `DateTime` (the bound form inputs auto-render as `type="date"` /
  `type="time"`, and EF Core maps them to SQLite). Previously `date` was an alias for `DateTime`.
- **Live-session capacity benchmarks — "how many sessions fit in 1 GB?"** Two new reports in
  `benchmarks/Rask.Benchmarks`. `session-footprint` measures what one live session retains, sweeping
  page size and separating a GET-minted session whose socket never arrived from a connected one whose
  buffers have reached their high-water mark. `session-churn` soaks sessions under sustained updates
  and churns create→dispose cycles to prove the footprint converges and that nothing survives teardown
  (it doesn't: 500 cycles leave a constant 16 KB residue, and 100 sessions over 200 updates each hold
  flat to the byte). [`docs/configuration.md`](docs/configuration.md#sizing-maxsessions-for-a-memory-budget)
  turns the numbers into guidance for sizing `MaxSessions`, which defaults to uncapped and previously
  had no way to size it. Headline: session cost is driven by **page size, not user count** — ~65,000
  sessions/GiB on a trivial page vs ~150 on a 1,000-row grid. `session-churn` also reports the
  per-interaction cost (allocation + time per update), so a change that trades retained memory against
  update cost can be read against both.

## [0.17.0] - 2026-07-15

### Added
- **`BsDataGrid<T>` gained server-side paging/sorting and URL-owned state.** `Page`/`OnPageChange` and
  `Sort`+`SortDescending`/`OnSortChange` let the **caller** own the page and sort: the grid renders what it is
  given and reports what the user clicked rather than moving itself. Put those in `[QueryParam]` properties and
  the grid's state lives in the URL — shareable, bookmarkable, and replayed by browser back/forward for free.
  For large sets, `Data` now also accepts an **`IQueryable<T>`** — pass a `DbSet` and the grid orders it by each
  column's new `SortBy` expression, counts it and materialises only the current page, so `ORDER BY`/`COUNT`/
  `LIMIT` happen in the database (server hosts only: it runs the query in-process, synchronously, and needs a
  `DbContext` that outlives the render; a WASM app throws with the fix in the message). Or pass one
  already-fetched page plus **`TotalCount`** and await `CountAsync`/`ToListAsync` yourself — `OnSortChangeAsync`
  and `OnPageChangeAsync` are awaited by the grid, so async data needs no async machinery inside the component.
  A lazy `Data` sequence is now materialised once rather than re-enumerated for each of the count, sort and
  footer passes. The showcase's data table
  ([`/table`](https://github.com/pal-tamas/rask/blob/main/samples/Rask.Example.Shared/Features/Table/TablePage.cs))
  is now built this way and dropped ~120 lines of hand-rolled table, sort-header and pagination markup.
- **`BsDataGrid<T>` is now documented, demoed and accessible.** The grid — typed `BsColumn<T>` columns bound
  straight to the row type, click-to-sort headers, client-side paging, per-column footer totals computed over
  every row, custom cell `Template`s, an `Empty` placeholder and master-detail rows via `ExpandedContent` —
  has shipped for a while but was effectively undiscoverable: absent from the component table, no guide, no
  demo, and no coverage beyond a static-markup check. It now has a [Data grid](docs/data-grid.md) guide with
  three live demos, and its sort/page/expand transitions are unit-tested for the first time. New
  accessibility: `scope="col"` on every header, `aria-sort` on sortable headers, and an accessible name plus
  `aria-expanded`/`aria-controls` on the master-detail expander. `Id`/`Class` now reach the `<table>` —
  `BsDataGrid` derives from `BsBlock` like every other `Bs*` component, so it gains the passthrough it was
  missing. Closes [#372](https://github.com/pal-tamas/rask/issues/372).
- **New `Rask.Outbox` package + `rask generate feature --outbox` — a transactional outbox.** Domain events
  marked `IOutboxEvent` are written to an `OutboxMessage` table in the **same transaction** as the change
  that raised them (atomic; never written for a rolled-back change), and a hosted `OutboxProcessor` polls
  the table and publishes them through `Rask.Cqrs` — at-least-once, crash-safe, on the app's own database
  (no broker). A source generator registers each event type for reflection-free rehydration. `--outbox`
  makes the generated events `IOutboxEvent`, maps the table in the `DbContext`, adds `Rask.Outbox`, and
  wires `AddRaskOutbox<Ctx>()` (disabling the in-process publisher so events aren't delivered twice). See
  `docs/outbox.md`.
- **`rask generate feature --events` emits typed domain events.** The slice gains `<Entity>Created`/
  `Updated`/`Deleted` records (`INotification`) that the aggregate raises on create/update/delete, plus a
  sample `INotificationHandler` stub — published in-process after the change commits by `Rask.Data`'s
  `DomainEventInterceptor` (auto-registered by `AddRaskCqrs()`). Composes with the other flags.
- **`Rask.Data`'s `DomainEventInterceptor` now collects events in `SavingChanges` and publishes in
  `SavedChanges`.** Previously it collected after the save, which lost a hard-deleted entity's events (a
  deleted entity is detached once the save completes). It now drains events while entities are still
  tracked, publishes them post-commit, and discards them if the save fails.
- **`rask generate feature --concurrency` adds optimistic-concurrency protection.** The entity implements
  `IVersioned` (an `int Version` token that `ApplyRaskConventions` marks as the EF concurrency token and
  the auditing interceptor bumps on every update). The edit form round-trips the original `Version` through
  a hidden field, the update handler applies it as the tracked original value, and a resulting
  `DbUpdateConcurrencyException` is **caught and shown as an inline "this record changed — reload" message**
  rather than a raw error page. Composes with `--soft-delete`/`--bs`/`--modal`.
- **`rask generate feature` entities inherit `Rask.Data`'s `AggregateRoot<TId>`, and `--soft-delete` is
  new.** Every generated entity now inherits the base (Id + audit stamps + a domain-events buffer), the
  generated `DbContext` calls `modelBuilder.ApplyRaskConventions()`, and the delete handler always
  loads + `Remove`s (so it flows through the interceptors) instead of a set-based `ExecuteDelete`.
  `--soft-delete` makes the entity `ISoftDeletable` (a `DeletedAt` stamp): deletes become soft deletes,
  deleted rows drop out of the list behind a global query filter, and the list page gains a "Show deleted"
  toggle + a generated `Restore<Entity>` command/button for deleted rows. Auto-NuGet + the next-steps add
  `Rask.Data` + `AddRaskData()`. Every generated Create/Edit/modal page also now **handles errors
  gracefully** — the submit try/catches and shows a friendly inline alert (`BsAlert` / `role="alert"`),
  navigating only on success.
- **Rask is now positioned as "the .NET One Person Framework".** A new [doctrine doc](docs/one-person-framework.md)
  and [roadmap](docs/roadmap.md) frame Rask as a full-stack, solo-developer framework — one C# codebase, one
  server, SQLite as the production database — with the UI reach (Server/WASM/native) and the lean runtime as
  supporting proof. The README hero, getting-started intro, docs index, `llms.txt`, and the template `AGENTS.md`
  files lead with this identity.
- **New `Rask.Data` package — a DDD base entity + EF Core interceptors.** A provider-agnostic data layer
  for Entity Framework Core apps: an `AggregateRoot<TId>` base (Id, `CreatedAt`/`UpdatedAt` audit stamps,
  and a domain-events buffer), opt-in marker interfaces (`ISoftDeletable`, `IVersioned`), and three
  `ISaveChangesInterceptor`s — auditing timestamps, **transparent soft delete** (a `Remove` becomes a
  `DeletedAt` stamp behind a global query filter), and **after-commit domain-event publication** through
  `Rask.Cqrs`. `modelBuilder.ApplyRaskConventions()` wires the query filter + optimistic-concurrency token,
  and `AddRaskData()` registers the interceptors. This is the foundation the `rask generate feature`
  scaffolder's `--soft-delete`/`--concurrency`/`--events` output builds on. See `docs/data.md`.
- **`rask generate feature` now adds the required NuGet packages automatically.** After writing the
  slice it runs `dotnet add package` for EF Core + SQLite and `Rask.Cqrs` (plus `Rask.Bootstrap` with
  `--bs`, and the validation library with `--validation dataannotations`/`fluent`) so the code compiles
  without a manual reference step; pass `--no-restore` to skip. A failed add degrades to a warning — the
  files are written and the packages are still listed in the printed next-steps.
- **`rask generate feature --tests` scaffolds xunit tests for the slice.** A sibling `<Project>.Tests`
  project gets a domain test (`Create`/`Update` set every property; each value object rejects a blank
  value and accepts a valid one) and, when the `DbContext` is generated, a persistence test that
  round-trips the entity through a real SQLite file (proving the configuration + value-object converters).
- **`rask generate feature --bs` scaffolds with Rask.Bootstrap; without it the pages are plain HTML.**
  By default the generated pages are now plain, unstyled semantic HTML (no CSS classes at all), so they
  compile and work in any project regardless of its stylesheet. The `--bs` flag renders the pages with
  `Bs*` components, and **`--modal`** (which implies `--bs`) puts create + update in a `BsModal` on the
  list page instead of separate pages — the whole slice's CQRS then lives on the list page. The `Bs*`
  components — `BsCard`/`BsCardBody`, `BsTable`, `BsButton`, `BsIcon`, and the bound
  `BsInput`/`BsCheck` form controls (which carry their own label + validation feedback) — and lays them
  out with typed `Bs.Join(...)` utility classes (`Display.Flex()`, `Flex.Gap(3)`, `Shadow.Sm`, …) instead
  of raw class strings. Value-object / DataAnnotations / Fluent validation all compose with it. The
  printed next-steps add `Rask.Bootstrap` + a reminder to link `BootstrapStyles()`.
- **`rask generate feature` now emits value objects + an EF Core `IEntityTypeConfiguration`.** Each
  required (non-nullable) string field becomes a value object — a `readonly record struct <Entity><Field>`
  that owns its validation (`Validate` + `From`, a `MaxLength` const), reused by the form via
  `Input(…, Validate: <Entity><Field>.Validate)` and mapped to its column with `HasConversion`. The
  entity holds the value-object type; `Create`/`Update` take primitives and wrap them via `From`. This is
  the built-in, dependency-free default validation. Each feature also gets a
  `<Entity>Configuration : IEntityTypeConfiguration<<Entity>>` (`HasKey` + per-string mapping), applied via
  `ApplyConfigurationsFromAssembly` in the generated `DbContext`, so the domain model stays free of
  persistence attributes. `--validation dataannotations` or `--validation fluent` opt out of value objects
  in favour of a plain POCO entity validated by that library — `[Required]`/`[MaxLength]` on the request +
  a `DataAnnotationsValidator()`, or a generated `<Entity>RequestValidator : AbstractValidator<…>` +
  `FluentValidationValidator(…)` — wired into the create/edit forms (the respective `Rask.Validation.*`
  package is added to the printed next-steps).

### Fixed
- **A `BsDataGrid` inside a `<form>` submitted the form on every sort click.** The sortable-header control is
  a `<button>` with no explicit `type`, and HTML defaults that to `submit`. It is now `type="button"`.
- **`BsDataGrid`'s pager could render a negative range.** `BsPageItem`'s `Disabled` only adds a CSS class, so
  the "previous" control stayed clickable on the first page and the page index underflowed to `-1`, rendering
  `-1-0 / 3`. Page changes are now clamped to the available range.
- **`docs/bootstrap-cards.md` described `BsBlock` as "the layout primitive".** It is the abstract base class
  of every `Bs*` component, has no factory, and is not something app code constructs — the claim is removed
  rather than reworded.

### Changed
- **Git hooks auto-enable on first build.** A `Directory.Build.targets` target points git at `.githooks/`
  (`core.hooksPath`) on the first local `dotnet build`, so a fresh clone gets the `commit-msg` /
  `pre-commit` (format + unit) / `pre-push` (E2E) hooks with no manual `git config`. Idempotent and
  best-effort; skipped in CI (`CI=true`), during IDE design-time builds, and outside a git working copy
  (restored packages never trigger it). Hooks stay advisory (bypass with the git no-verify flag /
  `RASK_SKIP_UNIT=1` / `RASK_SKIP_E2E=1`).
- **Unit tests + formatting moved out of CI to a local pre-commit gate.** The unit/integration suite no
  longer runs in the ci/nightly/release pipelines. `scripts/run-unit-local.sh` builds the solution once,
  runs `dotnet format whitespace --verify-no-changes` (the whitespace pass, not full `dotnet format`, whose
  style/analyzer passes recompile the `Routes.*` source generator through their own workspace and spuriously
  flag CS1503 in the routing tests), then every test except the browser E2E; a new `.githooks/pre-commit`
  hook runs it whenever a commit stages code (`src/`, `tests/`, `benchmarks/`, `Rask.slnx`, `Directory.*`) —
  docs-only commits skip it (bypass with `git commit --no-verify` or `RASK_SKIP_UNIT=1`). CI now runs only the deterministic benchmark byte-gates, the native compile gate,
  commitlint, and CodeQL; branch protection no longer requires the `unit` check. (Releases and the nightly
  prerelease are no longer test-gated in CI — run `scripts/run-unit-local.sh` + `scripts/run-e2e-local.sh`
  locally before tagging.)
- **E2E moved out of CI to a local pre-push gate.** The browser-journey E2E
  (`tests/Rask.Examples.E2E.Tests`, Playwright) and the on-device native E2E
  (`tests/Rask.Native.Appium.Tests`, Appium) no longer run in the CI/nightly/release pipelines. They run
  locally: `scripts/run-e2e-local.sh` runs the browser journeys (build-once → publish samples → VSTest),
  and a new `.githooks/pre-push` hook runs that gate on `git push` (enable with
  `git config core.hooksPath .githooks`; bypass with `git push --no-verify` or `RASK_SKIP_E2E=1`). The
  on-device native suite is run manually against an emulator/simulator (see `docs/native.md`). CI keeps
  the unit/integration suite, the deterministic benchmark byte-gates, and the native **compile** gate.
  Removed the reusable `e2e.yml` and `native-ios-e2e.yml` workflows and the `native-appium` job.

### Added
- **A load harness for the SQLite packages (`benchmarks/Rask.Benchmarks.Sqlite`), and the numbers it
  produced are now in [`docs/sqlite.md`](docs/sqlite.md#load-test-numbers).** It drives sustained concurrent
  traffic and reports throughput, tail latency and error rates — which BenchmarkDotNet, measuring a burst's
  mean, cannot. Four workloads: write contention across all four write paths, read-under-write (with a
  rollback-journal control arm), realistic ~90/10 web traffic, and a soak. `check` is a regression gate over
  invariants and same-run ratios rather than absolute milliseconds, which are too noisy to gate on;
  `scripts/run-sqlite-load-local.sh` runs it locally and nightly runs it best-effort. Headlines: ~99k
  mixed ops/s at p99 10 ms on one file; WAL is worth ~95-154× read throughput under a concurrent writer; the
  non-blocking retry's payoff is a **bounded worst case** (~92× better max: 174 ms vs ~16 s); and
  `journal_size_limit` cannot cap WAL growth while a leaked read transaction pins it (3.16 GB in 90s).

### Fixed
- **`Rask.SQLite.EntityFrameworkCore`: a long-lived `DbContext` stopped retrying contended writes.**
  `RaskSqliteExecutionStrategy` started its retry clock on the first contention and never released it, and
  EF Core hands one `DbContext` a single strategy instance for its whole lifetime. Once `Timeout` of
  wall-clock had passed since that first contention, every later `SaveChanges` on the same context gave up
  with `database is locked` **without a single retry** — the failure got *more* likely the longer a context
  lived. The strategy now resets its clock in `OnFirstExecution`, so each operation gets the full `Timeout`.

### Added
- **`rask generate feature` — scaffold a CQRS + EF Core CRUD vertical slice.** `rask generate feature
  <Name> --fields "Name:string,Price:decimal,InStock:bool,Note:string?(500)"` writes a full slice under
  `Features/<Plural>/`:
  - an **encapsulated entity** (private setters + static `Create` / `Update`; **`Guid` id by default**,
    `--id int|long` for an identity key),
  - a feature-local **`DbContext`** (or, with `--context`, a reference to an existing one),
  - **CQRS** create/update/delete commands + list/get queries, each with a handler that owns the EF
    access (`AsNoTracking`, `ExecuteDeleteAsync`); create/update commands carry a **request object** the
    forms bind to,
  - **list / create / edit pages** that dispatch through `IDispatcher` (no direct data access), navigating
    with the type-safe generated `Routes.*()` URLs (clean under RASK033).

  Fields may be optional (`Note:string?`) and strings get a default max length (overridable, `Name:string(100)`),
  surfaced as `[Required]` / `[MaxLength]`. The whole slice compiles as-is (verified by building it into the
  EF Core sample); a printed next-steps note covers `AddRaskCqrs()` + `AddDbContextFactory` + the `dotnet ef`
  migration. Field types: `string`/`int`/`long`/`decimal`/`double`/`bool`/`DateTime`/`Guid` (plus aliases).
  The CLI also gains short aliases: `rask g` = `generate`, and `g f` / `g c` / `g p` = feature / component / page.
- **`rask generate` — scaffold pages and components.** The `rask` CLI gains a `generate` command:
  `rask generate page <Name>` writes a routed page `Component` to `Features/<Name>/<Name>Page.cs`
  (`[Route]` + a `Head` title; `--route` for a custom path) and `rask generate component <Name>` writes
  a plain `Component` to `Components/<Name>.cs`. It finds the owning `.csproj` by walking up from the
  working directory, derives the file's namespace from its folder (the C# convention), refuses to
  overwrite an existing file without `--force`, and supports `--output` and `--dry-run`. The generated
  code compiles as-is in any `dotnet new rask-*` project. A `generate feature` CRUD slice is next.
- **`Rask.Cli` — the `rask` command-line tool.** A new opt-in .NET tool
  (`dotnet tool install -g Rask.Cli`) that gives Rask a short, task-focused CLI over the .NET SDK.
  `rask new <name>` scaffolds a project — it maps a friendly `--template` (`server` (default) / `wasm` /
  `wasm-hosted` / `native`) to the matching `dotnet new` template, forwards only the feature flags that
  template supports (`--auth` / `--pwa` / `--cqrs` / `--docker`, rejecting unsupported combinations with
  guidance instead of passing them through), and installs `Rask.Templates` on demand. `rask dev` runs the
  app with C# Hot Reload (`dotnet watch run`; `--no-hot-reload` for a plain run, app args after `--`).
  `rask info` reports the CLI / .NET SDK / template / OS environment. The tool is dependency-free (pure
  BCL over `dotnet`) and its command surface is unit-tested through an injectable process-runner seam.
  See [docs/cli.md](docs/cli.md). First step of the CLI roadmap (`generate` / `db` / `deploy` to follow).
- **`Rask.SQLite` IMMEDIATE transactions + a non-blocking, fair-interval busy retry.** Completes Rails'
  SQLite concurrency story on top of the production pragmas. On the raw ADO.NET path,
  `IRaskSqliteConnectionFactory.ExecuteInImmediateTransactionAsync(...)` (and the
  `SqliteConnection.ExecuteInImmediateTransactionAsync`/`BeginImmediate` extensions) run your write in a
  `BEGIN IMMEDIATE` transaction, acquiring the write lock through the raw `sqlite3` handle with the native
  busy handler off — so the only waiting is an `await Task.Delay` at a **constant 1 ms fair interval**
  (ported from rails/rails#51958; not exponential backoff) that **frees the thread** instead of blocking
  one inside native code, the .NET equivalent of Rails' GVL-releasing busy handler. `BEGIN IMMEDIATE`
  takes the write lock up front, converting the otherwise **unretryable** deferred read-then-write
  dead-lock into a plain waitable wait. New `SqliteBusyRetryOptions` (5 s timeout, 1 ms interval by
  default) is configurable via `AddRaskSqlite(..., configureRetry:)`. For Entity Framework Core,
  `UseRaskSqlite(..., configureRetry:)` registers a fair-interval `RaskSqliteExecutionStrategy` so
  `SaveChanges`/queries retry on `SQLITE_BUSY`/`SQLITE_LOCKED` (turning `busy_timeout` off and lowering the
  command timeout so the async strategy owns the wait; the implicit `SaveChanges` transaction stays
  `DEFERRED`). The `Rask.Example.Sqlite` sample gains a non-blocking concurrent-IMMEDIATE-writers demo.
- **Opt-in `--docker` for the web templates.** `dotnet new rask-server`, `rask-wasm`, and
  `rask-wasm-hosted` take a `--docker` flag (default off) that scaffolds a production multi-stage
  `Dockerfile` + `.dockerignore`. The two Kestrel templates build on the .NET SDK image and run on
  `aspnet:10.0` (non-root, port 8080); the standalone `rask-wasm` bundle publishes then serves from
  `nginx:alpine` with a bundled `nginx.conf` (SPA fallback, `application/wasm` MIME, `gzip_static`).
  New `docs/deployment.md` covers containerizing each template (TLS-at-proxy, WebSocket upgrade,
  sub-paths) and why `rask-native` (a mobile app) isn't containerized.
- **Full HTML-attribute passthrough on the Bootstrap `BsInput`/`BsTextarea`.** `BsInput` now forwards
  `Min`/`Max`/`Step`/`Pattern`/`MaxLength`/`MinLength`/`List`/`Autofocus` (constraints & affordances),
  `Accept`/`Capture`/`Multiple` (file inputs), and `InputMode`/`EnterKeyHint`/`Spellcheck` (mobile-keyboard
  & a11y hints) to the core `Input`; `BsTextarea` forwards `Cols`/`MaxLength`/`MinLength`/`Autocomplete`/
  `Autofocus`. Previously a Bootstrap number/date/range/file field could not set any of these. (The HTML
  `size` attribute stays unexposed on `BsInput` — the base `Size` is Bootstrap control sizing.)
- **New mobile / accessibility attributes on the core `Input`.** `InputMode` (on-screen keyboard),
  `EnterKeyHint` (action-key label), `Spellcheck` (the enumerated `spellcheck="true|false"`), `Capture`
  (camera/mic for file inputs), and `Dirname` (submit text direction) join the `<input>` surface.
- **Gesture bridge — activation-gated browser APIs on the Server host.** New headless `GestureTrigger`
  and six typed wrappers generalise `Shareable`'s trick: they hand your element a `data-rask-gesture`
  bundle and the shared client runs the capability **inside the click gesture**, so the browser's transient
  user activation survives. That makes normally-WASM-only APIs reachable **declaratively on the Server host**
  (where a round-tripped service call would lose the activation). Ships `FullscreenTrigger`,
  `ScreenOrientationTrigger`, `EyeDropperTrigger`, `InstallTrigger`, `MediaCaptureTrigger`, and
  `PictureInPictureTrigger` (the last two target a `<video>` via its `ElementRef`). Capabilities that return
  a value post it back to an `OnResult` / `OnColor` / `OnOutcome` callback via a new `[JSInvokable]`. The
  `__raskFullscreen` / `__raskEyeDropper` / `__raskOrientation` / `__raskInstall` / `__raskMedia` / `__raskPip`
  DOM helpers moved from `rask-wasm-api.js` into the shared `rask-api.js` so they ship to Server too.
- **Native iOS/Android backends for the browser/device APIs, wired with one line.** `Rask.Native` now
  ships native C# implementations of ten interfaces and a platform module that installs them:
  `host.UsePlatform(new ApplePlatform(() => rootVc))` / `new AndroidPlatform(this)`. Injecting the
  ordinary interface then resolves the native backend on device — `IGeolocation` → `CLLocationManager` /
  `LocationManager`, `IClipboard` → `UIPasteboard` / `ClipboardManager`, `IVibration` → system vibration /
  `Vibrator`, `IWakeLock` → idle-timer / `FLAG_KEEP_SCREEN_ON`, `INetworkInfo` → `NWPathMonitor` /
  `ConnectivityManager`, `ISpeechSynthesis` → `AVSpeechSynthesizer` / `TextToSpeech`, `IScreenInfo` →
  `UIScreen` / `DisplayMetrics`, `IDeviceOrientation` / `IDeviceMotion` → CoreMotion / `SensorManager`, and
  `IShare` (already native) — instead of the WebView's JS, which is often gesture-gated or absent on the
  platform. Everything else falls back to the WebView automatically. The `rask-native` sample uses the new
  modules (and adds the location / network-state / vibrate permissions).
- **Native `INotifications` + `IBadge` backends — real OS notifications and app-icon badges on device.**
  `ApplePlatform` / `AndroidPlatform` now also wire `INotifications` → `UNUserNotificationCenter` /
  `NotificationManager` and `IBadge` → `UNUserNotificationCenter.SetBadgeCount` / a silent badge
  notification — two APIs a WebView fundamentally can't deliver (`WKWebView` has no `Notification`
  constructor; `navigator.setAppBadge` never touches a native app's icon). Injecting the ordinary
  interface resolves the native backend on device; permission is the real OS prompt. Android 33+ needs the
  `POST_NOTIFICATIONS` permission (declared in the sample/template manifests and requested up front by the
  activity). A new co-mounted **Notifications + Badge** showcase demo exercises both.
- **Central registration for the typed browser/device APIs, with native-first resolution.** The
  interface → implementation map for the 43 browser wrappers, previously hand-duplicated across the
  Server, WASM, and Native hosts, now lives in one place per assembly: `AddCoreBrowserApis` (the 31
  transport-agnostic wrappers, in `Rask.Core.Browser`), `AddClientBrowserApis` (the in-process `IShare`,
  in `Rask.Client.Browser`), and `AddWasmBrowserApis` (the 11 WASM-only wrappers, in `Rask.Wasm.Browser`).
  Each host calls the tiers it can serve at its own lifetime (Server `Scoped`, WASM/Native `Singleton`).
  Every registration uses `TryAdd`, so the JS-backed wrapper is now a **fallback**: a native backend — or
  an explicit app registration — made first wins, and the framework resolves the best implementation per
  host with no app-head wiring. Registrations use compile-time `typeof` only (reflection-free, trim-safe).
- **`NativeAppHost.UsePlatform(INativePlatform)`** — a native platform module (iOS/Android) contributes
  native C# backends for the browser/device interfaces; the host applies them before the JS fallbacks in
  `RunLocalAsync`, so any interface a platform backs natively wins and the rest fall back to the WebView.
- **`Rask.SQLite.Litestream` now fetches the `litestream` binary for you.** MSBuild `build/` targets in
  the package download the litestream binary for the target runtime at build/publish time (Linux
  x64/arm64/armv7, macOS x64/arm64, Windows x64/arm64), SHA-256-verify it against a pinned checksum,
  cache it under `~/.rask/litestream/<version>/<rid>`, and copy it next to the app — so a published app
  (and its Docker image) has litestream with nothing to install. The package stays tiny (no binaries
  shipped). The default `ExecutablePath` resolves to the bundled binary, then falls back to `PATH`. Opt
  out with `-p:RaskLitestreamDownload=false`; pin a different version with `RaskLitestreamVersion` +
  `RaskLitestreamSha256`. See [docs/sqlite.md](docs/sqlite.md#the-litestream-binary-is-fetched-for-you).
- **The live playground is now a real in-browser IDE.** The `samples/Rask.Example.Playground` editor gains
  three IDE features, all powered by Roslyn compiled to WebAssembly:
  - **IntelliSense** — Roslyn's `CompletionService` (via a new `Microsoft.CodeAnalysis.CSharp.Features`
    reference) drives Monaco completions that know the full BCL + `Rask.Core` surface *and* the generator's
    `Generated.Div(...)` factories, so the terse `Div()[…]` members complete as they would in a real project.
  - **As-you-type diagnostics** — CS errors and Rask's RASK hints squiggle on every edit, not only on Run.
  - **An example gallery** — a left-hand rail with **Counter**, **Form + validation** (built-in `Form<T>`
    validation) and a **Todo app** starter, one click to load; plus **Reset** and **Ctrl/Cmd + Enter** to Run.

  A new `PlaygroundWorkspace` backs the live features over an `AdhocWorkspace`; critically it **never `Emit`s
  or `Assembly.Load`s** (unlike the Run path), so as-you-type analysis can't leak assemblies on the Mono
  runtime — only pressing Run does. Monaco reaches it through static `[JSInvokable]` bridge methods. The
  diagnostics/completion mapping and every gallery snippet are unit-tested on the desktop runtime, and the
  Playwright journey now asserts a live squiggle appears before Run and that a gallery example loads + runs.
  Adds ~3.7 MB (brotli) to the untrimmed playground bundle for the Features/Workspaces assemblies.
- **`Rask.SQLite` — Rails-style production SQLite pragmas.** A new opt-in, standalone package that
  applies the Ruby on Rails 8 production pragma set — `journal_mode=WAL`, `synchronous=NORMAL`,
  `foreign_keys=ON`, `busy_timeout=5000`, `cache_size`, `mmap_size`, `journal_size_limit` (values
  verified against rails/rails#49349) — to **every** SQLite connection, so concurrent writers stop
  hitting `database is locked` and foreign keys are actually enforced. Register
  `services.AddRaskSqlite(cs)` and inject `IRaskSqliteConnectionFactory`. The per-connection pragmas are
  re-applied on every pooled open (only WAL persists in the file header); every value is overridable — or
  nullable to skip — via `SqlitePragmaOptions`. Depends only on `Microsoft.Data.Sqlite` and is
  reflection-free, so it works server-side, on mobile, and under trimming/AOT. New
  `samples/Rask.Example.Sqlite` shows the live pragma values and a concurrent-writes demo. See
  [docs/sqlite.md](docs/sqlite.md).
- **`Rask.SQLite.EntityFrameworkCore` — the EF Core integration.** `UseRaskSqlite(...)`, a drop-in for
  `UseSqlite` that also registers the pragma `ConnectionOpened` interceptor, lives in this companion
  package (which pulls in `Microsoft.EntityFrameworkCore.Sqlite`) — split out so the base `Rask.SQLite`
  pragma engine stays free of an EF Core dependency for mobile/AOT consumers.
- **On-device SQLite in the native showcase.** `samples/Rask.Example.Native`'s **Todos** tab now persists
  to a SQLite database in the app sandbox via `Rask.SQLite`'s raw connection factory (reflection-free, so
  it's safe under iOS full-AOT) — so todos survive an app restart on device, while Server/WASM keep the
  transient in-memory store. The shared `TodosPage` gained an `ITodoStore` seam (`InMemoryTodoStore`
  default; `SqliteTodoStore` on native). See [docs/sqlite.md](docs/sqlite.md#sqlite-on-mobile-rasknative).
- **`Rask.SQLite.Litestream` — managed [Litestream](https://litestream.io) backup.** A companion
  opt-in package that supervises the Litestream sidecar from inside the app: `AddRaskSqliteLitestream(…)`
  registers a hosted background service that continuously streams the WAL to S3/GCS/Azure Blob/file
  storage, and `RestoreSqliteFromLitestreamAsync()` restores the database from its replica on a fresh
  host (no-op when the local file exists). The `litestream` binary is driven via CliWrap; shutdown sends
  a graceful interrupt so the final WAL frames flush (a `ShutdownGracePeriod` before force-kill) — the
  right behaviour for SIGTERM-recycled platforms like Azure App Service Linux and Kubernetes. If the
  backup process exits or crashes it is restarted with capped exponential backoff (`RestartDelay`); a
  failure is logged at Critical and never crashes the app. Depends only on the Microsoft.Extensions
  hosting/DI abstractions and CliWrap. See [docs/sqlite.md](docs/sqlite.md#continuous-backup-with-litestream).
- **`Rask.SQLite.Snapshots` — scheduled consistent backups, no external binary.** A companion opt-in
  package that takes point-in-time file snapshots of a live SQLite database on a schedule using SQLite's
  Online Backup API (never an unsafe file copy), keeps the newest N, and writes them to a directory — or
  a pluggable `ISqliteSnapshotStore` for object storage. `AddRaskSqliteSnapshots(…)` runs a hosted
  service on an interval (with optional snapshot-on-startup); inject `ISqliteSnapshotter` to snapshot on
  demand (e.g. before a migration). Pure `Microsoft.Data.Sqlite` — works on minimal/distroless and
  Alpine images — plus the Microsoft.Extensions hosting/DI abstractions. Complements
  `Rask.SQLite.Litestream` (streaming) or stands alone. See
  [docs/sqlite.md](docs/sqlite.md#scheduled-snapshots).

### Changed
- **Consistent one-file-per-API layout.** Every browser/device wrapper now lives in an `I{Api}.cs` file
  holding the interface, its implementation, and its DTOs together (e.g. `Clipboard.cs` folded into
  `IClipboard.cs`; `Geolocation.cs`/`GeolocationOptions.cs`/`GeolocationPosition.cs` into `IGeolocation.cs`;
  the WASM wrappers renamed `Fullscreen.cs` → `IFullscreen.cs`, …). No public API or namespace changed.
- **Faster CI.** The browser-journey shards now run the prebuilt test assembly directly under VSTest
  (`dotnet test <dll>`) off the shared `e2e-build` artifact — no per-shard `dotnet restore` and no MSBuild
  graph evaluation — and the wasm-tools workload is installed only on the shards that actually need it
  (those whose fixture shells `dotnet run --no-build` on a `net10.0-browser` host). The setup block
  (setup-dotnet + NuGet cache + cached workload install + Playwright cache) is consolidated into a local
  composite action (`.github/actions/setup`), and the build-once + sharded-VSTest pipeline into a reusable
  workflow (`.github/workflows/e2e.yml`) shared by `ci`/`nightly`/`release` — so `nightly` and `release`
  also stop rebuilding the whole solution once per shard. In-process test/benchmark jobs use a shallow
  checkout with `-p:MinVerSkip=true`; jobs that pack, or that run a *published* app out-of-process (the
  E2E build-once server publish, the on-device native APK), keep full history so MinVer stamps the real
  version — a fallback version otherwise breaks the routes registry's cross-assembly load at startup. No
  gate changed — same tests, filters, warnings-as-errors build, and byte-regression benchmark gate.

### Fixed
- **Accessible names + validation semantics for the Bootstrap group/combobox form controls.** `BsSelect`
  (custom combobox) and `BsMultiSelect` named their visible label with a `<label for>` pointing at a
  `<div role="combobox">` — void, since a div is not a labelable element — so the control had no accessible
  name; both now associate the label via `aria-labelledby`, and `BsMultiSelect` gained the
  `aria-haspopup`/`aria-expanded`/`aria-controls` (+ `aria-invalid`/`aria-describedby`) contract `BsSelect`
  already had. `BsRadioGroup`/`BsCheckboxGroup` gained an optional `Label` that wraps the options in a
  `<fieldset>` named by a `<legend>` (the correct group semantics + accessible name), and their invalid
  feedback is now a `role="alert"` live region carrying an id the option inputs reference via
  `aria-describedby` (with `aria-invalid` on each) — matching the `BsFormControl` field contract.

### Documentation
- **Forms guide: full input-type set, file inputs, and the new form-control surface.**
  [`docs/forms.md`](docs/forms.md) now enumerates every `InputType` (and flags the string-only family
  as [RASK025](docs/diagnostics.md#rask025)), documents file inputs (`OnFiles`/`Accept`/`Capture`/
  `Multiple`) with a cross-link to [http-and-files.md](docs/http-and-files.md), and lists the mobile/a11y
  input attributes. [`docs/bootstrap-forms.md`](docs/bootstrap-forms.md) documents the `BsInput`/
  `BsTextarea` attribute passthrough and the accessible-group `Label`. The `FormControls` radio/checkbox
  samples showcase the named-group `<fieldset>`/`<legend>`.
- **Browser/device API capability matrix + per-API reference pages.** New
  [`docs/browser-capabilities.md`](docs/browser-capabilities.md) is a single table of all 43 wrappers
  showing where each works (Web / PWA / Native) and which have a native iOS/Android backend, linking to a
  dedicated reference page per API under `docs/apis/`. Reconciled the stale WASM-only classification of
  `IWebPush`/`INotifications`/`IBadge`/`IWakeLock` (they are transport-agnostic and register on Server).
- **Native bar styling (`NativeColor` + per-bar colors + `NativeTheme`).** The native chrome bars
  (`NativeHeaderBar` / `NativeTabBar` / `NativeToolbar`) can now be colored. A new **`NativeColor`** value
  type — the color sibling of `NativeIcon`, one authored value the head resolves to a `UIColor` / Android
  `Color` — offers `Hex`, `Rgba`, curated members (`White`/`Black`/`Clear`), a `System` default, and
  `Adaptive(light, dark)` for dark-mode-aware colors. Each bar gains optional `Background` / `Tint`
  (buttons / selected tab) / `TitleColor` slots (`NativeTabBar` also `UnselectedTint`); an app-wide
  **`NativeTheme`** registered on `host.Services` fills unset slots. Resolution is layered — per-bar wins,
  then the theme, then the platform default — so styling is fully opt-in and backward compatible (an unset
  color, or an explicit `NativeColor.System`, keeps the OS look). The iOS head projects colors through
  `UINavigationBarAppearance`/`UITabBarAppearance` (adaptive colors via a dynamic `UIColor`); the Android
  head tints its bars against the current night mode. Descriptor serialization + layering are unit-tested
  (`NativeColorTests`, `NativeChromeTests`); the `Rask.Example.Native` showcase brands its bars.
- **Native tab badges.** `NativeTab` takes an optional `Badge` string (an unread count like `"3"`/`"99+"`),
  projected to `UITabBarItem.BadgeValue` (iOS) and a small icon overlay (Android, no AndroidX dependency).
  Leave it null/empty for no badge; bind it to live state and it updates on the next render (the chrome
  re-pushes only when the badge changes). Unit-tested in `NativeChromeTests`; the showcase badges the Todos tab.
- **Native segmented control.** `NativeHeaderBar` takes optional `Segments` (2–3 labels) shown in place of the
  title — a `UISegmentedControl` as the nav bar's `titleView` (iOS) / a tint-styled button row (Android, no
  AndroidX). Controlled via `SelectedSegment` + `OnSegmentChanged(int)` (runs on the render thread and
  re-renders, reusing the `nativeTap` dispatch). Unit-tested in `NativeChromeTests`; the showcase shows an
  All/Active/Done filter on the Todos page that drives the Todos tab badge.
- **Native back button.** `NativeBackButton` (header `Leading`) now works — tapping it pops the WebView
  history like the hardware Back button (the platform back chevron on iOS, "‹" on Android), re-entering the
  router via the existing `popstate` → `navigate` path. Previously it rendered but did nothing. The showcase
  shows it on a drill-down guide page. Unit-tested in `NativeChromeTests`.
- **Native overflow menu.** A `NativeMenuButton` bar item (header `Leading`/`Trailing` or a toolbar's `Items`)
  opens a native pull-down of `NativeMenuItem`s — an iOS `UIMenu` on a `UIBarButtonItem`, an Android
  `PopupMenu` (framework, no AndroidX) — for secondary actions. Each entry has a `Title`, optional `Icon`,
  `OnClick`, and optional `Destructive` (iOS red); selections re-enter the `nativeTap` dispatch so `OnClick`
  runs on the render thread and re-renders. Defaults to a "⋯" (`NativeIcon.More`) glyph. Unit-tested in
  `NativeChromeTests`; the showcase adds an overflow menu to the header.

### Fixed
- **Native Android bars now render icons.** The Android chrome head rendered tabs and bar buttons as
  text-only labels — the `NativeIcon` Android drawable was never resolved (an iOS/Android parity gap). Tabs
  and icon buttons now resolve their drawable (`Resources.GetIdentifier`) and show a real icon (with the
  selected tab highlighted via the tint), matching the iOS SF-Symbol bars; unresolved names still fall back
  to text.
- **Native bar taps that change only chrome now update the bars.** A bar interaction whose handler changed
  *only* native chrome — a tab badge, a segmented-control selection, a menu action — left the HTML body
  identical, so in diff mode it produced no frame and `NativeLiveSession`'s no-frame early return skipped the
  chrome push, so the bars never updated. The chrome is now re-pushed even when the body has no diff (guarded
  by a diff-mode `NativeChromeTests` regression). The Android overflow `PopupMenu` is also now held while shown
  so its managed item-click callback isn't garbage-collected.
- **Native Back no longer lands on the boot shell.** The first native render now seeds WebView history with a
  *replace* of the app's initial route, so it supersedes the boot shell URL (`/index.native.html`). Previously
  the initial render emitted no history, so Back from the first navigation (a `NativeBackButton` or hardware
  Back) popped to `/index.native.html` — a 404 "Page not found". Guarded by a `NativeChromeTests` case.
- **Native Android bars are inset for the system bars.** The Android header/footer are drawn edge-to-edge (the
  colored bars fill behind the status/navigation bars); their content is now padded by the system-bar insets
  (framework `WindowInsets`, no AndroidX) so the title / segmented control / overflow button clear the status
  bar and the tab bar clears the navigation bar — parity with the iOS safe-area handling.

## [0.16.0] - 2026-07-14

### Added
- **Marketing landing site, built in Rask (`samples/Rask.Example.Site`).** The GitHub Pages front door
  at `https://pal-tamas.github.io/rask/` is now a standalone Rask WASM app that renders the whole page —
  hero, an animated packet-race `<canvas>`, a Blazor-vs-Rask benchmark chart, host/feature grids, and
  install tabs — dogfooding the framework it sells. The live counter and install tabs are genuine
  stateful Rask components (click → diff → re-render); the hero canvas, scroll reveals and theme toggle
  live in a sibling scoped `App.js`; the design system is a global stylesheet. The live feature showcase
  moved to `/demo/` and the playground stays at `/playground/`; the Pages workflow publishes all three as
  Rask WASM apps and the "live demo" links across README/NUGET/docs now point at `/demo/`. Covered by a
  new `SiteExampleTests` Playwright journey (renders in Rask, counter increments, tabs switch).
- **RASK033 — prefer the generated route URL over a hardcoded path for internal navigation.** A new
  analyzer (Warning) flags a string literal passed to internal navigation — `Navigator.NavigateTo("…")`
  or any `RouteUrl` slot (`NavLink`/`BsNavItem`/`NativeTab` `Href:`/`To:`, via the `string → RouteUrl`
  implicit conversion) — **only** when the path maps to a page's generated parameterless `Routes.<Page>()`
  factory. External URLs (`RouteUrl.External`, `https://…`), parameterised routes (`/users/42`), and
  secondary `[Route]` templates with no formatter (`/todos/new`) are left alone. Rename or remove the
  `[Route]` and the string is a silent dead link that still compiles, whereas `Routes.<Page>()` is a
  compile error — so the analyzer keeps type-safe navigation honest. Documented in `docs/diagnostics.md`;
  the sample apps' internal-nav call sites were converted to `Routes.*()`.
- **Live in-browser playground.** A new `samples/Rask.Example.Playground` WASM sub-app, published to GitHub
  Pages at `/playground/` next to the showcase (linked from the showcase navbar), where you write Rask
  component C# and see it compile & render **live in the browser** — no server. Because Rask components are
  plain C# (no Razor step), the pipeline is just `run the Rask ComponentFactoryGenerator → Roslyn
  CSharpCompilation → Emit → Assembly.Load → render`, all on the Mono WebAssembly runtime; the emitted
  component is mounted as a child of the playground's own tree, so its event handlers, state and live
  diffing run through the shared live session. Rask's analyzers (RASK001–032) run as a display pass and
  surface as inline Monaco squiggles. The app ships untrimmed with `WasmEnableWebcil=false` so Roslyn can
  read the shipped `_framework/*.dll` back as metadata references (downloaded once and cached); user code
  always runs interpreted. The compile pipeline (`PlaygroundCompiler`) is unit-tested on the desktop
  runtime, and a Playwright journey compiles the starter and drives its counter end-to-end. See
  [docs/playground.md](docs/playground.md).
- **A mounted page now costs ~30% *less* retained memory than Blazor — the one axis Blazor led is
  overtaken.** A pure-element, handler-free user component (the bulk of a real page — rows, cards,
  layout, text) no longer retains its rendered `Element` object graph between renders. On first render
  it snapshots its subtree as a compact `LeanFrame` span on its `LiveState` and **releases the element
  tree**; a clean re-render replays the span (`HtmlSerializer.ReplayLeanFrames` reconstructs
  byte-identical HTML and re-writes the frame stream in one pass) instead of re-walking a
  heap-object-per-element tree. The retained snapshot uses a slimmed 24-byte frame (the held copy drops
  the per-render HTML offsets and the diff-only component ref, which replay regenerates) rather than the
  full ~40-byte render frame, and the array is reused across re-renders — so per-update allocation is
  unchanged (a stateful page still allocates ~840 B/update, **50× less than Blazor**). Safety is
  conservative: a subtree is cached only when it has no nested user component (one could go dirty
  independently — replaying the parent would skip it), no event handlers (handler ids are positional),
  no page-level `Key`, no `Head` contribution, and isn't reading ambient state — otherwise it keeps the
  element-walk path, unchanged. The diff codec is untouched (component subtrees stay transparent in the
  flat frame stream; the span is tracked as a plain index range). Measured on the retained-footprint
  report against a real Blazor `ComponentBase`: the 200-row page drops from ~223 KB to **158,606 B/tree
  vs Blazor 223,888 B (Rask 29% less)** and the 100-row keyed list to **42,168 B vs Blazor 60,107 B
  (30% less)** — so Rask now beats Blazor on *every* measured axis (wire bytes, per-update allocation,
  and retained heap). Wire output and diff payloads are byte-identical.
- **Host-awareness on `Component` + a composed native-chrome family.** Any component can now branch its
  render on where it runs via three orthogonal, render-cache-safe accessors — `HostShell` Any component can now branch its
  render on where it runs via three orthogonal, render-cache-safe accessors — `HostShell`
  (`Web`/`Native`), `HostEngine` (`Server`/`Wasm`/`InProcess`), `HostPlatform` (`None`/`IOS`/`Android`) — plus
  the `IsNative`/`IsServer`/`IsWasm`/`IsIOS`/`IsAndroid` conveniences, so one page can render a web `BsNavbar`
  on Server/WASM and compose native bars under the native shell without a separate layout. The axes are
  independent (Native + Server on iOS is all three at once). Alongside it, a new **native-chrome family** in
  `Rask.Native`: an abstract `NativeComponent : Component` base and concrete components
  (`NativeHeaderBar`, `NativeTabBar`, `NativeToolbar`, `NativeBarButton`, `NativeTab`, `NativeBackButton`,
  and `NativeWebView`) with type-safe icons (`NativeIcon` — curated cross-platform SF-Symbol/drawable pairs
  plus `Custom`/`SfSymbol`/`Drawable` escape hatches). They are ordinary factory-built components composed in
  `Render()`, not base-class slots. The factory generator gained a `[assembly: RaskFactoryNamespace]` marker +
  a referenced-assembly scan so a consumer referencing `Rask.Native` gets the native factories via a global
  using automatically. New diagnostic **RASK032** errors when a native chrome component is nested inside the
  HTML tree (it belongs at the layout level, as a sibling of `NativeWebView`).
- **Native header & footer bars.** A native page is a small composed tree — the native bars
  (`NativeHeaderBar` / `NativeTabBar` / `NativeToolbar`) as siblings of a **`NativeWebView`** that hosts the
  ordinary page shell (`Doctype`/`Html`/`Head`/`Body`). On the `Rask.Native` mobile host the bars project to
  real platform chrome — a `UINavigationBar` + `UITabBar`/`UIToolbar` on iOS, a top bar + bottom tab/tool bar
  on Android — and the `NativeWebView`'s HTML is serialized into the WebView between them. Opt in by registering
  the platform WebView head as an `INativeChrome` backend (like `IShare`) and assigning `webView.ChromeView`;
  with none registered the bars are inert (they render nothing — backward compatible). Bars are collected during
  the render walk (so their factories are DI-correct and callbacks wire to their owner), the last bar of a kind
  wins, and an unchanged bar never re-pushes. Bar-button taps run their `OnClick`; tabs navigate their type-safe
  route. The `samples/Rask.Example.Native` showcase composes a native title bar + tab bar around the shared
  shell (dropping its web `BsNavbar` under `IsNative`). The `rask-native` template (`--host local`) scaffolds it
  too: the heads host `webView.ChromeView` + register `INativeChrome`, and the default `App` composes a native
  title bar + Home/Counter tab bar around a `NativeWebView`. Each projected bar view carries a stable
  **accessibility identifier** (the tab/button title, or `rask-native-header`) — addressable by screen readers
  and UI tests — and the **Appium on-device E2E now drives the native bars**: it asserts the native header + tab
  bar rendered as real platform views and that tapping a native tab navigates the WebView's route (the round
  trip through the bridge into the router). The `native-appium` (24 min) and nightly `native-appium-ios`
  (35 min) CI job timeouts were trimmed to match observed run durations.
- **Native geolocation backend.** The `rask-native` template's `--host local` heads add a
  `NativeGeolocation : IGeolocation` (iOS **CoreLocation** `CLLocationManager`, Android **`LocationManager`**),
  registered on `host.Services` before `RunLocalAsync` to override Rask's JS-backed `navigator.geolocation`
  default — the same *framework-default → native-head-override* pattern as the share sheet, now proven for a
  request/**response** (+ subscription: `WatchAsync`) capability. So `await geolocation.GetCurrentPositionAsync()`
  returns a native fix with the real OS permission prompt and `CLLocationManager` / `LocationManager` accuracy.
  The template adds `ACCESS_FINE_LOCATION` / `NSLocationWhenInUseUsageDescription` (Local-mode only) and
  `MainActivity` requests the runtime grant. **Verified on the Android emulator:** a mock GPS fix
  (`adb emu geo fix`) round-tripped through `IGeolocation` → `NativeGeolocation` → `LocationManager` and
  displayed the exact lat/long; both platform backends compile.
- **`Rask.Native` now ships the platform WebView heads — zero head duplication.** The iOS/Android bridges
  (`RaskWkWebView` / `RaskAndroidWebView`), the native share backends (`NativeShare`), the Native + Server
  controllers (`RaskServerViewController` / `RaskServerWebView`), and the default bundled-asset readers
  (`IosBundledAssets` / `AndroidBundledAssets`) all move **into `Rask.Native`** under `Platforms/{iOS,Android}`.
  Both the runnable examples and the `rask-native` template are now just thin entry points that compose them —
  no WebView/share/trust code is copied anywhere. `Rask.Native` gains a gated multi-target: it builds plain
  `net10.0` by default (so CI, unit tests, and the Ubuntu solution build are unchanged with no mobile
  workloads), and **`-p:RaskNativeHeads=true`** multi-targets `net10.0;net10.0-ios;net10.0-android` to produce
  the head assemblies. The published package carries all three `lib/` TFMs, so it is now packed on **macOS**
  (a dedicated `pack-native` release/nightly job) rather than Ubuntu. A shared, net10.0
  `NativeCapabilities.IsTrustedOrigin(origin, url)` replaces the per-head trust check (now compares the full
  origin — scheme + host + port — not just the host), and the iOS Server head diverts *every* off-origin web
  navigation to Safari (not only tapped links).
- **Runnable native showcase examples + framework asset serving.** Two new iOS/Android examples make native
  a peer of the Server and WASM showcase samples, both mounting the *same* `Rask.Example.Shared.App`:
  **`samples/Rask.Example.Native`** (Native + Local — in-process, the native peer of the WASM sample) and
  **`samples/Rask.Example.Native.Server`** (Native + Server — a thin native shell over a remote
  `Rask.Example.Server`, the native peer of the Server sample). They multi-target `net10.0-ios;net10.0-android`,
  so they stay out of `Rask.slnx` (the Ubuntu CI can't build those TFMs) and are built by a dedicated **macOS
  `native` CI job** across every project × TFM. On-device E2E moves to **Appium** (`tests/Rask.Native.Appium.Tests`,
  macOS `native-appium` job) — it installs and drives the *real* app on an Android emulator, switches into the
  WebView, and asserts the showcase rendered with its scoped CSS + Bootstrap. This **replaces the headless
  Playwright-in-Chromium native shim** (`NativeExampleTests` / `NativeServerSmokeTests` / `PlaywrightNativeWebView`
  / `NativeOriginServer`), which had masked a device-only bug now fixed: the boot shell loads at
  `/index.native.html`, a path `NativeOriginAssets` now serves. To make the *full* showcase serveable on-device,
  `Rask.Native` gains two public APIs —
  **`NativeOriginAssets`** (the origin request table: boot shell + client, scoped `/_rask/a/*` CSS/JS from
  `ScopedAssetRegistry`, and your `wwwroot`/Bootstrap/`data` via a caller-supplied bundled-asset reader) and
  **`NativeAssetHttpHandler`** (serves the in-process demo `HttpClient` from the same table, so data-driven
  pages work offline). A real native app now serves scoped CSS + Bootstrap with a one-line interceptor instead
  of the boot-only two-file handler.
- **On-device iOS E2E (nightly).** A new `native-ios-e2e.yml` workflow (nightly cron + manual
  `workflow_dispatch`) drives the *real* Native + Local showcase on an **iOS Simulator via Appium/XCUITest**
  on macOS — the iOS counterpart of the Ubuntu `native-appium` (Android) job, asserting the same on-device
  render (WebView + scoped CSS + Bootstrap through `NativeOriginAssets`). It is deliberately **off the per-PR
  path** (macOS minutes + the Microsoft.iOS↔Xcode SDK coupling are too costly/fragile to gate every PR — the
  nightly cadence MAUI uses for device UI tests); per-PR iOS stays the compile-only `native` job. The iOS
  Appium test now pins the booted simulator by **UDID** (`RASK_APPIUM_IOS_UDID`) — name-only device selection
  is unreliable on the ARM64 macOS runners, the reason MAUI's XHarness passes an explicit `--device UDID`.
- **Native + Server head — a remote Server app reaches device natives (the "superpower").** The
  `rask-native` template gains a **`--host server|local`** parameter. `--host server` scaffolds a thin native
  shell (`Platforms/{Android/ServerActivity,iOS/ServerAppDelegate}.cs`) that points its WebView at a remote
  Rask Server (`NativeAppHost.ConnectToServer`) and injects the **capability bridge** — origin-gated
  `NativeCapabilities.BridgeScript` at each navigation + the WebView's script-message handler routed to
  `NativeCapabilities.TryHandleAsync` with a native `NativeShare`. So the *same* server-rendered `Shareable`
  pops the device's native `UIActivityViewController` / `Intent.ACTION_SEND`. The bridge stays scoped to the
  trusted origin (off-origin links open in the system browser). **Verified on the Android emulator:** a
  server-rendered `Shareable`, loaded live from a remote host, fired the native chooser from `ServerActivity`;
  both platform heads compile. `--host local` (default) is the existing in-process app.
- **Toasts auto-dismiss.** `ToastOutlet` gains an opt-in `AutoDismissAfter` (`TimeSpan?`) — a one-shot
  timer per shown message that runs the same dismiss path, so any `Template` clears itself after the delay
  even when its element has no timer of its own (disposed on dismiss and on unmount). `Rask.Bootstrap`'s
  `BsToaster` now defaults `AutoHideMs` to 5000 ms (set `null`/`<= 0` to keep toasts sticky), and the
  showcase toast demo auto-dismisses after 5 s.
- **OS share sheet — declarative (all hosts) + imperative + native backend.** Two ways to share, one
  payload (`ShareData`, now in `Rask.Core.Browser`):
  - **`Shareable`** (`Rask.Core`, **all hosts including Server**) is a headless component — you render the
    trigger element, it hands you the `data-rask-share` attribute to spread onto it (via `Data`), and the
    shared client (`rask-events.js`, spliced into all three dialects) fires `navigator.share` **inside the
    click gesture** — no round-trip, so the transient user activation survives even on the Server WebSocket
    transport. In the native shell it upgrades to a native backend.
  - **`IShare`** (imperative, share from code) moves out of `Rask.Wasm.Browser` into a new **`Rask.Client`**
    assembly — the home for in-process client APIs the WASM and Native hosts share but Server can't (a
    mid-handler call has no live gesture). `Rask.Native` can't reference the browser-targeted `Rask.Wasm`,
    so it lives in `Rask.Client`, bundled into both host packages like `Rask.Core`.
  - On Native, the `rask-native` template heads add a **native `NativeShare`** backend (iOS
    `UIActivityViewController`, Android `Intent.ACTION_SEND`) registered on `host.Services` before
    `RunLocalAsync` (last-wins) — no activation needed, and it works even where the WebView lacks
    `navigator.share`. This establishes the reusable *framework-default → native-head-override* pattern for
    future native backends (geolocation, biometrics, push).
  - A **native capability bridge** (`window.__raskNative.capabilities` + `invoke(name, data)` →
    `{ type: "capability" }` message → the shared `NativeCapabilities.TryHandleAsync` dispatcher) lets the
    declarative `Shareable` reach the native backend on device — its click hits `IShare.ShareAsync` (the
    head's `NativeShare`), not the WebView's `navigator.share`, with no host-specific code. The same
    `NativeCapabilities` toolkit (`BridgeScript` + `TryHandleAsync`) lets a **Native + Server** head inject
    the bridge into a remote page, so a plain **Server** app reaches device natives too. Covered by
    `NativeCapabilitiesTests` + `NativeCapabilityBridgeTests`.

  A `Shareable` demo (guide `browser-share`) is added to the showcase and works on every host.
  **Breaking (pre-1.0):** `IShare` moves from namespace `Rask.Wasm.Browser` to `Rask.Client.Browser`;
  `ShareData` moves from `Rask.Wasm.Browser` to `Rask.Core.Browser`.
- **`Rask.Example.Native` showcase + headless native E2E.** The native host now sits under the same
  showcase + E2E net as Server and WASM. `samples/Rask.Example.Native` mounts the *same*
  `Rask.Example.Shared.App` onto a `NativeAppHost` (its `NativeExampleHost` composition root), and a new
  `NativeExampleTests` E2E shard drives the **real** `rask.native.js` client + `RunLocalAsync` pipeline
  in headless Chromium — no emulator — through a Playwright-backed `INativeWebView`
  (`PlaywrightNativeWebView`) whose route handler (`NativeOriginServer`) serves the shell + client +
  scoped `/_rask/a/*` assets + `global.css` + Bootstrap (the E2E stand-in for a device head's scheme
  handler). `NativeExampleTests` runs a focused native journey — boot + render, the sidebar (collapsible
  groups + mobile drawer), a showcase render walk, scoped CSS/JS applied over the bridge, element-ref
  focus through `IJSRuntime`, and WebView history (route→URL push, Back/forward, the URL-routed Todos
  dialog); it reuses the **same shared showcase walks** the browser hosts run (only the `HttpClient`-backed
  HTTP & files walk is skipped). A separate `NativeServerSmokeTests` shard covers the **Native + Server**
  mode (`NativeAppHost.ConnectToServer`): it asserts the shell-URL contract and loads the Server showcase
  in a mobile-emulated WebView context, confirming the thin-native-shell scenario renders and reacts live
  over the WebSocket.

### Changed
- **Third-party `<head>` injections are preserved automatically.** Rask treats `<head>` as authoritative
  (the live-diff reconciler morphs it back to the rendered head every update), which used to trim a
  `<style>`/`<link>`/`<script>` a JS library injects at runtime — a code editor's theme, a chart lib, a
  syntax highlighter, an analytics tag — on the next re-render. The client now watches `<head>` and tags
  whatever a library injects with `data-rask-managed` (the marker the reconciler already skips), so it
  survives with no app code. The **reconciliation itself is unchanged**: framework head mutations (a head
  morph, or an `applyDiff` InsertSubtree of a Head-declared script/link) are discarded from the observer's
  queue so they still reconcile normally, and `data-rask-key` nodes (the framework's keyed head links,
  incl. the scoped-CSS FOUC preload clone) are never tagged so they keep reconciling by key. The playground
  drops its bespoke Monaco head-guard as a result. See [docs/js-interop.md](docs/js-interop.md).
- **README refresh — a Rask-vs-Blazor scorecard, a wire-bytes chart, and a stale-number fix.** The `Why
  Rask` section now leads with an at-a-glance performance scorecard (wire bytes, allocation/update,
  retained heap, render hot path — sourced from the CI-enforced baselines) and a Mermaid bar chart of how
  many × fewer bytes than Blazor each scenario ships. Corrected the stale "up to 66×" wire-bytes claim to
  the current suite maximum, **56×** (Remove-100-rows, 37 B vs 2,080 B), and folded the package and
  documentation tables into collapsible `<details>` so the page scans faster.
- **Showcase restructure: a "Mobile & devices" guide group, a Welcome-free root, and a Bootstrapped
  Todos app.** The on-site `GuideCatalog` now groups **Browser APIs**, **Mobile & PWA**, and the
  newly-surfaced **Native (iOS/Android)** guide (`docs/native.md`, previously embedded but never listed)
  under one **Mobile & devices** section. The redundant Welcome landing page is gone — the guides index
  is served at `/`, the brand/404/native-tab links point there, and the sample `Todos` screen is migrated
  fully onto `Rask.Bootstrap`: primitives (`BsListGroup`/`BsListGroupItem`/`BsCheck`/`BsInput`/`BsModal` for
  the add/edit dialog — dropping the hand-rolled `<dialog>` + focus/Escape plumbing) and typed utility
  helpers (`Bs.Join(Display.Flex(), Flex.Justify(…), …)`) in place of raw Bootstrap class strings. Sample +
  docs only; no framework API change.
- **Renamed the flash-message API to "toast" (BREAKING, pre-1.0).** The transient consumed-once
  messaging types are renamed to match the visual metaphor Rask already renders (`BsToast`): `IFlash` →
  `IToaster`, `Flash` → `Toaster`, `FlashMessage` → `ToastMessage`, `FlashLevel` → `ToastLevel`,
  `FlashOutlet` → `ToastOutlet`, and `Rask.Bootstrap`'s `BsFlash` → `BsToaster`. The namespace
  (`Rask.Core.Messaging`), the enum members, and the message API (`Info`/`Success`/`Warning`/`Error`/
  `Add`/`Consume`/`Changed`) are unchanged — this is a pure rename with no behaviour change. Migrate by
  replacing the type names; the single-toast Bootstrap element `BsToast` is unaffected.
- **Per-host server limits (no more process-global statics).** The WebSocket safety caps and session
  grace periods (`RaskServerOptions`) are now projected into a per-host `RaskServerLimits` singleton
  that the WS endpoint resolves once per connection, instead of eight mutable `static` fields shared
  across every host in a process. Two hosts in one process each carry their own limits (the old code
  had the last `configureServer` win for all of them), and the server test suite no longer serialises
  around that shared state — cutting `Rask.Server.Tests` wall-clock substantially by letting its
  integration tests run in parallel. No API change; behaviour is unchanged for a single-host process.
- **Per-session diff mode (no more process-global `LiveOptions.DiffMode` static).** The wire-payload
  shape (`LiveDiffMode`) is now snapshotted onto each `LiveSession` at construction — from the host's
  `RaskLiveOptions` (Server carries it on the `LiveSessionStore`, WASM/Native on the host builder) — and
  read from that instance field on the render hot path, instead of a mutable `static` every render read.
  This matters most for the native host, which fans render continuations onto the thread pool: a shared
  mutable static read mid-render was doubly wrong there. Two hosts in one process — and parallel tests —
  now each render in their own mode; `Rask.Server.Tests`' nine diff-mode WebSocket classes drop their
  `[Collection("LiveDiffMode")]` serialization and run in parallel, and the native/WASM session tests no
  longer pin the global. The configuration surface is unchanged (`AddRask(o => o.DiffMode = …)` /
  `WasmHostBuilder.CreateDefault` / `NativeAppHost.CreateDefault`); `PathBase` / `MinifyScopedAssets`
  stay on `LiveOptions` (they back the process-wide content-addressed asset registries). Render
  benchmarks unchanged — allocation-neutral (a branch-condition read moved from a static to a field).
- **Faster tests, locally and in CI.** (1) The `-p:RaskWasm=false` "fast" build now genuinely skips the
  nested WASM publish — it was gated by an `XmlPeek` of the csproj rather than the property, so the
  `unit`/`benchmarks` jobs silently ran a full WASM publish they believed disabled — which also lets
  those builds run in parallel again (the `-m:1` Rask.Core.dll copy-race workaround is no longer needed
  there). (2) CI now builds the E2E graph **once** in a shared `e2e-build` job and hands the output to
  every browser-journey shard via artifact (`--no-build`), instead of each of the ten shards rebuilding
  the whole solution. (3) The documented local inner loop is build-once / test-with-`--no-build`.
  (4) The two server E2E shards (`ServerExampleTests`, `NativeServerSmokeTests`) each `dotnet publish`-ed
  the *same* `Rask.Example.Server` host from scratch at fixture startup — a duplicated restore+compile+
  publish that made them ~15 min while every other shard was 1–3.5 min. The `e2e-build` job now publishes
  that host **once** (`--no-build`, off the graph it just built) into the shared artifact, and the fixture
  boots that prebuilt DLL when present (falling back to an on-demand publish for local dev), cutting both
  shards to browser-journey time.

### Fixed
- **Foreign-`<head>` preservation hardening.** Follow-up to the head-injection preservation added above:
  the head `MutationObserver` now installs eagerly when the client bundle loads (so a library that injects
  into `<head>` before the first head morph is still caught, not only after it), re-arms if the live
  `<head>` element is ever replaced rather than morphed in place, and `applyDiff` flushes pending foreign
  injections before its end-of-frame discard so a library node injected during the same task as a diff
  isn't dropped instead of preserved.
- **Native Appium E2E asserted a stale native tab layout.** The on-device `NativeShowcaseAppiumTests`
  still expected a three-tab `Home`/`Guides`/`Todos` bar (with `Guides → /guides`), but the guides-first
  pivot dropped the Welcome/Home page and made `Guides` the site root (`/`), so `NativeShowcaseApp` now
  ships two tabs (`Guides → /`, `Todos → /todos`). The test failed at `WaitForNativeElement("Home")`. It
  now asserts the current two-tab bar and its round-trip (`Guides "/" → Todos "/todos" → Guides "/"`).
- **Sign-in landing page rendered stale (pre-sign-in) identity data.** After `IAuthSignIn.SignInAsync(principal, returnUrl)`,
  the server applied the `returnUrl` navigation immediately in the handler-dispatch tail — mounting the
  destination page **before** the reconnect re-seeded `SessionUserProvider`, so its `OnMount`/`OnMountAsync`
  ran under the *old* principal. Because children reconcile by `(Type, position)` and not `Key`, the
  post-reconnect render reused that instance without remounting, leaving any identity/tenant-scoped data it
  loaded at mount permanently stale (e.g. a multi-tenant admin who switches tenant saw the previous tenant's
  data on the landing page). The `returnUrl` navigation is now **deferred** until the reconnect `hello`
  applies it, right after the redeemed principal is set, so the destination mounts fresh under the new
  identity. The client URL update is unchanged (it rides the separate `history.replace` field). Applies to
  both sign-in and sign-out.
- **A submit button inside a click-handler element didn't submit the form on WASM.** On the WASM client
  (`rask.wasm.js`), a `<button type="submit">` nested in an element carrying a `data-rask-on-click` handler
  — e.g. `BsModal`'s `.modal-dialog` click-shield — had its native form submission cancelled by the
  ancestor's `preventDefault`, so a `Form<T>` inside a `BsModal` never submitted (and never validated). The
  server client (`rask.js`) already carved this out; the fix ports the same submit/reset-button guard to the
  WASM client so the two dialects match. Surfaced by the Bootstrapped Todos add/edit dialog.
- **A bound wrapper form control used outside a `Form` didn't re-render sibling derived UI.** A two-way
  bound `Bs*` control (`BsCheck`/`BsInput`/`BsSelect`/pickers/groups) rendered outside a `Form<T>` re-rendered
  only itself, so a sibling whose class/text derived from the same model property went stale on change — most
  visibly a `BsCheck(() => item.Done)` in a list next to a `Span` styled from `item.Done`. A raw inline core
  `Input` never had the bug (its handler owner is the authoring page). The binding-owner resolution
  (`IFormControl<T>.RegisterValidator`) now falls back to the control's **creating component** when the bind
  expression's root isn't itself a component (e.g. a loop local `() => item.Field`), recorded weakly at
  create time (`BindingConsumerRegistry`, keyed by the control — no per-render-node field added). One core
  change fixes every wrapper control and any custom `IFormControl<T>`, in and out of a `Form`, with no
  `StateHasChanged`/`AfterBind` on the user surface.
- **Playground editor lost its syntax colouring after the first Run.** Monaco injects its theme colours as
  a `<style class="monaco-colors">` in `<head>`; the live-diff morph reconciles `<head>` on every re-render
  and removes any child not marked `data-rask-managed`, so the first re-render (e.g. after clicking Run)
  stripped it and every token fell back to the inherited body colour — a faint, uncoloured editor. The
  playground now stamps Monaco's head-injected `<style>`/`<link>` nodes as `data-rask-managed` (the same
  marker the framework uses for its own scoped-asset head tags) and keeps a `MutationObserver` on `<head>`
  so any it adds later stays protected. An E2E assertion guards it.
- **Native `IJSRuntime` calls threw `NotSupportedException` on iOS.** Any component invoking a browser API
  with arguments (e.g. the guide-chrome scroll-spy) failed on iOS with *"JsonTypeInfo metadata for type
  'System.Object[]' was not provided"*. `NativeJSRuntime` added the reflection-based JSON resolver only when
  `RuntimeFeature.IsDynamicCodeSupported` — but iOS reports that `false` even on the simulator/interpreter, so
  the plain `object[]` invoke-args couldn't be serialized. Switched the guard to
  `JsonSerializer.IsReflectionEnabledByDefault` (the exact predicate `DefaultJsonTypeInfoResolver` needs, and
  still trim-substituted to `false` under a full-AOT publish). Verified on the iPhone 17 Pro simulator.
- **Native iOS app ran letterboxed (not full screen).** The `rask-native` template (and the native
  examples) shipped a `Platforms/iOS/Info.plist` that was never actually wired into the iOS build — .NET
  iOS finds the app manifest via a `None` item whose filename/`Link` is `Info.plist`, which the multi-target
  glob didn't provide, so the build emitted a default plist with **no launch screen**. Without a launch
  screen iOS renders the app at a legacy resolution (black bars + everything scaled up). Fixed by wiring the
  plist (`<None Include="Platforms/iOS/Info.plist" Link="Info.plist"/>`) and giving it a `UILaunchScreen`, so
  the app now fills the device screen at native resolution. Verified full-screen on the iPhone 17 Pro simulator.
- **De-flaked the native E2E copy-button step.** In the JS-interop guide journey the `CodeSample` "Copy"
  click round-trips (handler → `InvokeVoidAsync` → scoped JS flashes "Copied!"); over the native WebView
  bridge a single message can drop, so the shared journey occasionally never saw the flash. The (idempotent)
  copy click is now retried up to three times before the assertion fails. Test-only; no framework change.
- **Event callbacks on generic components now re-render the component (`BsMultiSelect` chip × works again).**
  A callback that captures `this` plus a local through a nested closure — e.g. `BsMultiSelect<T>`'s per-chip
  `() => ToggleAsync(item)` behind a `BsCloseButton` — is lowered by Roslyn to *generic* display classes when
  the owning component is generic. `DelegateOwner`'s closure-walk excluded generic types, so it never reached
  the captured `<>4__this`, resolved no owner, and `AutoCallback` left the callback unwrapped: clicking a
  chip's × removed the item from the model but the badge lingered on screen until an unrelated re-render
  (e.g. reopening the dropdown). The walk now recurses into generic display classes, so any generic
  component re-renders after its own nested-closure event fires. No API change.
- **`BsMultiSelect` floating label no longer overlaps the placeholder.** In floating mode the empty control
  still rendered its `Select…` placeholder span *inside* the box, and the floating CSS centres the label
  in that same box — so the two texts collided. It now blanks the box when floating + empty (the centred
  floating label serves as the placeholder), matching `BsSelect` and native `.form-floating`.
- **Floated `.bs-floating` label now straddles the top border like a native floating input.** The custom
  floating-label controls (`BsSelect`/`BsMultiSelect`/pickers) zero the label's top padding in the empty
  state to flex-centre the placeholder, but never restored it when the label floated — so the shrunk label
  translated up from a `0` baseline and hugged/crossed the top border. The filled/`:focus-within` rule now
  restores the native `padding-top: 1rem`, so the floated label sits the same small gap below the border as
  a normal Bootstrap floating input.
- **`BsSelect`/date-time picker clear (×) no longer shows through another select's open dropdown.** The
  clear × carried a hard `z-index: 1000` (so an *open* control's × stays clickable above its own
  click-outside backdrop at 999). But Bootstrap's open `.dropdown-menu.show` is *also* `z-index: 1000`,
  and nothing isolates each control's stacking context — so a *closed* select whose × came later in the
  DOM painted over another select's open menu. The raised z-index now lives on a `.bs-clear-open`
  modifier applied to the × only while its own control is open; a closed control's × drops to
  `z-index: auto` and sits behind any open menu.
- **Native concurrent-render race (`Collection was modified` under the root error boundary).** The native
  host runs async lifecycle/handler continuations on the thread pool (`HandlerSyncContext.Post` uses
  `Task.Run`), so a mid-await render (`RenderInScopeCoreAsync`, or a second continuation's render) could run
  concurrently with the dispatch's render — and two renders walking the component tree at once raced
  `ComponentLifecycle.DisposeComponentTree`'s `PersistedChildren` enumeration, throwing
  `InvalidOperationException` mid-render and tripping the root error boundary (which intermittently wiped a
  complex page after an interaction). `NativeLiveSession` now serializes every render+emit behind a
  `_renderLock`, matching the Server host (WASM is single-threaded, so it needs none). With the race gone,
  the native E2E now runs the full shared showcase journey reliably instead of a focused subset.
- **Native `IJSRuntime` invokes with arguments, and native WebView history.** Both were bugs the new
  native E2E surfaced. (1) An out-of-render `IJSRuntime` invoke (one issued from an event handler that
  awaits its result) embedded `argsJson` into the bridge call as a raw JS literal instead of a string, so
  the client's `JSON.parse(argsJson)` failed — every handler-issued invoke carrying arguments (element-ref
  focus, storage set/get, …) broke. `NativeJSRuntime.DispatchOutsideRender` now quotes it, matching the
  frame-invoke path (regression-guarded by `NativeJsInteropTests`). (2) The native client
  (`rask.native.js`) now drives its own WebView history: `applyHistory` pushes/replaces each route change
  and a `popstate` listener feeds hardware Back/forward into the router, so `location`/URL tracks the
  route and URL-routed UI (the Todos dialog, `Navigator.SetQuery`) works.
- **`Rask.Native` — native mobile host (foundation).** A new host that runs a Rask app on iOS/Android
  inside a platform WebView, driven by the *same* render → diff → payload pipeline as the Server and WASM
  hosts (it subclasses `LiveSessionBase`). Two modes: **Native + Local** (`NativeAppHost.RunLocalAsync<App>`)
  runs the app in-process on the device for an offline, store-distributable app; **Native + Server**
  (`NativeAppHost.ConnectToServer`) points the WebView at a remote Rask Server over `wss://`. The platform
  WebView is abstracted behind the `INativeWebView` bridge (implemented per-platform in the app head), so
  the library builds and unit-tests on plain `net10.0` with no iOS/Android SDK workloads. Ships the native
  client dialect `rask.native.js` (the shared diff/morph/interop modules spliced with a native transport
  shim) and the boot shell. This release lays the transport-agnostic foundation. See `docs/native.md`.
- **`dotnet new rask-native` template — a runnable native iOS + Android app.** The template scaffolds a
  project that multi-targets `net10.0-ios;net10.0-android` with shared Rask components and two platform
  heads: `Platforms/iOS` (an `INativeWebView` over `WKWebView` with a `raskapp://` scheme handler +
  script-message bridge) and `Platforms/Android` (an `INativeWebView` over `android.webkit.WebView` with
  an asset-serving `WebViewClient` + a `@JavascriptInterface` bridge). Both heads boot a `NativeAppHost`,
  serve the embedded shell + client from a real app origin (so secure-context device APIs work), and run
  end-to-end — verified booting, rendering, routing, and updating live on both an Android emulator and an
  iOS simulator. Build/run with `dotnet workload install ios android` then
  `dotnet build -t:Run -f net10.0-android` (or `-f net10.0-ios`). Native device *backends* remain a
  follow-up; the host is preview / pre-1.0.

### Changed
- **Client parity — the three JS client dialects now share the transport-neutral DOM helpers.** The
  rAF input/scroll coalescing, scoped-CSS FOUC gating, and keyboard + core drag handlers were inline
  and duplicated across `rask.js` (Server) and `rask.wasm.js` (WASM), and only partially hand-ported
  to the newer `rask.native.js`. They are now single-source shared modules
  (`Rask.Core/Resources/rask-input.js`, `rask-scoped.js`, and the keyboard/drag handlers folded into
  `rask-events.js`) spliced into all three clients at build time — so the **native client reaches
  parity** (it gains input/scroll coalescing, scoped-CSS FOUC gating, and keyboard + drag it lacked)
  and the former Server↔WASM copy collapses to one. Behaviour is unchanged on Server/WASM (verbatim
  extraction). The scoped-JS `Rask.*` invoke gate and file input/download stay host-specific for now
  (they have genuinely diverged / are transport-coupled) — tracked in the `docs/native.md` roadmap.
- **Conditional content now ships a diff instead of the whole page.** Toggling an element in or out
  (a validation message appearing, a "show details" panel, a row appended to a list) emits a positional
  `InsertSubtree`/`RemoveSubtree`. Previously *all* positional structural ops were untrusted and routed
  to the full-HTML morph — so a keystroke that flipped one field's validation state re-sent the entire
  form. The diff codec now trusts the safe subset: a **pure tail append/truncate at a nested,
  replace-free level**, where the client's positional apply is provably identical to the full-HTML morph
  (Rask serialises nested content without the whitespace/comment nodes that would shift the client's
  `childNodes` slot). Mid-list replacements and top-level ops (where the WASM shell's comment nodes live)
  still take the full-HTML path, so DOM identity is preserved exactly as before.
  Measured effect: a form-validation-churn update drops from ~1.4 KB (full form) to ~110 B on the wire.
- **`Raw`/CodeSample-heavy pages now ship a scoped subtree morph instead of re-rendering the whole
  document.** When a `Raw` frame shares a sibling level with other nodes (the shape of every guide,
  markdown, or syntax-highlighted-code page), its verbatim markup parses into an unknown DOM-node count,
  so the following siblings' positional paths can't be trusted — the diff used to discard the whole render
  and morph `document.documentElement`. It now emits one trusted `MorphSubtree` op at the Raw-owning
  parent, carrying just that element's new inner HTML; the client reconciles only that subtree with the
  same `morph()` engine (correctness identical to the old full-document morph, localised). Benefits all
  three hosts — biggest win on **native**, where the frequent full-document morph on complex pages was the
  expensive, historically flaky update path. Measured on a minimal guide shell: the wire payload drops
  from a 1770 B full-document frame to a 661 B single-op diff (and the gap widens with the surrounding
  shell, since the diff stays proportional to just the changed container).
- **Leaf elements retain less memory: three cold reference fields moved off the base `Component`.** The
  error-boundary pointer, render handle, and lifetime-token source are only ever set on live-render
  roots and user components (which already allocate a lazy `LiveState`), yet every `Element` in a mounted
  tree — the bulk of a rendered page — carried all three as mostly-null references. They now live in
  `LiveState`, so a plain `Div`/`Span`/text node sheds 24 bytes each and a component that never touches
  its cancellation token allocates no `LiveState` at all. Reduces retained heap for a mounted tree
  (~9% on the 100-row keyed-list footprint benchmark), narrowing the one axis where Blazor's dense frame
  structs still lead. No behavioural change — the fields are read through the same members, now backed by
  `LiveState`.
- **Every node in a mounted tree sheds another ~24 bytes: packed per-node flags + a leaner key slot.**
  Two more slices off the same retained-heap axis. (1) The per-node booleans — the reads-ambient-state
  latch on `Component` and `Draggable` on `Element` — now live in a single packed `_flags` byte instead
  of a `bool` plus a padded `Nullable<bool>` field, reclaiming the alignment padding each cost. (2) The
  key slot dropped its two value→string cache fields (`_cachedKeyValue`/`_cachedKeyString`, 16 B on
  *every* node): the cache only ever hit for a keyed component reused in place, but a keyed list rebuilds
  its element instances each render, so it was cold-missing there anyway — a bad trade against a rare
  `ToString` on reused nodes when this is a footprint path. `Key` stays inline and `KeyString` recomputes;
  non-keyed nodes (the majority) still short-circuit to `null` and allocate nothing. No behavioural change,
  no per-render allocation added. Measured before/after on one machine via the `mem-footprint` report,
  retained heap per mounted tree drops a further **~13%** on the 200-row page and **~9%** on the 100-row
  keyed list, and per-update allocation is unchanged-to-slightly-better; the pinned `Baselines/vs-blazor.md`
  absolute figures will be refreshed by the dedicated run. Both rows still trail Blazor's dense frame
  structs — closing that gap is the next, architectural step.
- **A live update no longer allocates the whole page as a string.** Every render materialised the full
  document via `StringBuilder.ToString()` — the dominant managed allocation of a small update on a large
  page — even when the shipped payload was one `UpdateText` op that never reads the HTML. The session now
  renders into a reused, double-buffered `char[]` (`RenderedHtmlBuffers`) and threads
  `ReadOnlySpan<char>` through the no-op-render dedup, the diff-vs-full head-compare, and the
  `InsertSubtree` fragment slice; only the first-render / full-HTML-fallback path (which ships the whole
  body anyway) still materialises a string. The wire payload is byte-identical. Measured on the 200-row
  counter-update benchmark, the end-to-end serialize+diff+payload cycle drops from ~45.3 KB to ~1.1 KB
  per update (−97%); combined with the lazy attribute-path change it is ~98% below the pre-optimisation
  baseline.
- **The live-diff codec no longer allocates a path array for every element it walks.** `DiffAttributes`
  received a freshly-built `int[]` element path on each call — one per element, every render — even
  though the overwhelmingly common case is an element whose attributes are unchanged, which emits no op
  and never uses the path. On a large page where a single text node mutates, that speculative
  allocation dominated the whole diff's managed footprint. The path is now built lazily on the first
  emitted attribute op and reused for any further ops on the same element, so an idle element's
  attribute pass is allocation-free. The emitted op stream is byte-identical (all ops on one element
  still share one path instance). Measured on the 200-row counter-update benchmark: the diff step's
  allocation drops ~96% (25.2 KB → 1.1 KB per update) and the end-to-end serialize+diff+payload cycle
  drops ~35% (69.4 KB → 45.3 KB per update).

### Fixed
- **Bound/controlled native `<select>` no longer snaps back to its old value after a change.** When
  marking the matching `<option>` selected, `Select<T>` cloned it but dropped its reconciliation `Key`,
  so the selected option's key shifted on every render, keyed diffing mismatched, and the browser's live
  `selected` IDL property was never synced — the model updated but the box reverted visually. The marked
  `<option>`/`<optgroup>` now keeps its `Key`. Affects every bound/controlled `Select<T>` (and so
  `BsSelect(Native: true)`).
- **Flaky `LiveTicker` lifecycle tests no longer time out on a cold CI thread pool.** The
  background-async waits in `LiveTickerTests` gave the synthetic poll loop only 2 s to land its
  first tick; on the nightly `unit` job — which compiles every WASM sample bundle immediately
  before running tests — the thread-pool hill-climber injects workers slowly, so a correct-but-slow
  continuation occasionally slipped past that deadline and failed the run. The waits now use
  generous named budgets (`Settle` 10 s, `FillToCapacity` 20 s); since `WaitFor.True` returns the
  instant the condition holds, a healthy run is unaffected. Test-only change — no framework code touched.
- **Bs dropdown-family popovers no longer get clipped by an `overflow` ancestor.** The Popper-less
  `.dropdown-menu` of `BsDatePicker`/`BsTimePicker`/`BsDateTimePicker`, `BsDropdown`, and `BsMultiSelect`
  was `position: absolute`, so opening one inside a card or scroll region (anything with
  `overflow: hidden/auto`) cut it off. A tiny declarative runtime helper (`data-rask-popover`, alongside
  the focus trap) now re-anchors an open menu with `position: fixed` and viewport-computed coordinates —
  below the trigger, flipping above when it doesn't fit, clamped into the viewport, right-aligned for
  `BsDropdown(AlignEnd: true)` — so it escapes every overflow-clipping ancestor and tracks the trigger on
  scroll/resize. (Caveat: an ancestor with a CSS `transform`/`filter`/`contain` becomes the fixed
  containing block and re-clips the popover — a browser rule.) Added a `BsDropdown` showcase demo.
- **`rask-native` template: content no longer renders under the notch / status bar.** The boot shell
  requests an edge-to-edge viewport (`viewport-fit=cover`), so the template's `App.cs` now pads `Body`
  by the device safe-area insets — `padding:env(safe-area-inset-top) … env(safe-area-inset-left)` — to
  clear the status bar, notch / Dynamic Island, and home indicator.
- **`Rask.Native` is now packed and published.** The package was never in the release/nightly pack
  lists, so `dotnet add package Rask.Native` (and `dotnet new rask-native`, whose generated project
  references it) would fail to restore from nuget.org. The `release.yml` and `nightly.yml` pipelines now
  pack and push `Rask.Native`, and the project is marked `IsPackable` with a package-specific `NUGET.md`
  — matching the other host packages.

### Bootstrap

- **`BsSelect<T>` is now a custom combobox by default (single-value twin of `BsMultiSelect`).** The box is a
  `.form-select` display `<div role="combobox">` (showing the option's rich `OptionLabel`) that opens a
  zero-JS `.dropdown-menu` listbox. Pass a **`Filter` predicate** (`(item, text) => bool`) to add a **search
  field inside the dropdown** that narrows the options as you type (with a "No matches" row); with no
  `Filter` it is a plain dropdown. `Native: true` falls back to the plain OS `<select>`. **Breaking:**
  options are data-driven — pass `Options` (+ optional `OptionLabel`/`Filter`/`Placeholder`) instead of
  `Option(...)` children.
- **`BsMultiSelect<T>` gains the same opt-in dropdown search** (via a `Filter` predicate); the box shows the
  chosen items as chips.
- **`BsSelect<TValue, TItem>` value selector** — bind a projected field while the options are objects:
  `BsSelect(() => model.PersonId, people, OptionValue: p => p.Id, OptionLabel: p => Text(p.Name))`. The
  bound value is `OptionValue(selectedOption)`; the label/search still see the whole object.
- **Date/time pickers are now hand-editable.** `BsDatePicker`/`BsTimePicker`/`BsDateTimePicker` render an
  editable `<input>`: focus opens the popover and typing commits live per keystroke via culture-aware
  parsing (a partial/invalid entry is kept as-is, not reverted; blur normalises to the value's format). The
  calendar/clock popover still works for pointer selection.
- **Nullable selects/pickers get an `×` clear** in the box that resets the value to null (the null state
  shows the `Placeholder`); and a **float-only-when-filled** floating-label mode across
  `BsSelect`/`BsMultiSelect`/pickers (the label sits as the in-box placeholder when empty, floats up when
  filled or focused).
- **Fixed a `BsDateTimePicker` crash on the trimmed/AOT WASM build** (`System.ArgumentException: … DateTimeOffset
  … DateTime` on write). A generic base's cached `static readonly` type field mis-resolved `typeof(T)` under
  Mono AOT; it is now computed fresh, and the composed value is boxed to the bound property's actual type.
- **Time-picker selection now uses the brand colour** (`var(--bs-primary)`) instead of Bootstrap's baked-in
  blue, with a slimmer scrollbar and tighter item padding.
- **Picker/select UI polish.** The floating label now anchors to the top-left corner (a centred scale origin
  made it land low/misaligned); the `×` clear on a non-floating `BsSelect`, and the caret/`×` on a
  non-floating picker, now centre vertically in the box (they were anchored to the whole `.dropdown` — label
  above included — so they rode up onto the box's top edge); the editable-picker caret is larger; and in the
  `BsDateTimePicker` popover the hour/minute columns now match the calendar grid's height instead of stopping
  short.
- **New variant-gallery examples** for `BsSelect` (basic, floating, searchable, clearable, `OptionValue`
  projected id, native, native-nullable, disabled), `BsMultiSelect` (basic, searchable, floating, disabled),
  and the date/time pickers (default, floating, native, min/max/disable, nullable) — each bound with a live
  readout, in the Bootstrap guide (`docs/bootstrap.md`).
- **The Bootstrap guide is split into three pages.** The single co-mounting guide (which mounted all 14
  live component demos on one long page) is now **Bootstrap components** (setup, content/layout, utilities),
  **Bootstrap navigation & overlays** (navbar/nav, tabs, modal, toast, dropdown), and **Bootstrap forms &
  inputs** (inputs, selects, multiselect, date/time pickers) — three entries under the Bootstrap sidebar
  group (`docs/bootstrap.md`, `docs/bootstrap-navigation.md`, `docs/bootstrap-forms.md`).
- **The Bootstrap guide is now one small page per component group.** `docs/bootstrap.md` becomes a thin
  hub (setup + color modes + a component map + versioning), and each group gets its own focused page:
  buttons & badges, cards/lists/tables, alerts/spinners/progress, icons, navbar & nav, modals/offcanvas/
  dropdowns, tabs/accordion/collapse, toasts, form controls, selects & multiselect, date/time pickers,
  and utility classes — all under the Bootstrap sidebar group. The live demos are unchanged (they move
  with their prose; demo resolution is keyed by demo-id, not page).
- **Open combobox dropdown fixes.** An open `BsSelect`/`BsMultiSelect` menu no longer **stretches to the
  viewport width** — a `.w-100` menu carries `width:100% !important`, which beat the popover helper's inline
  width pin once the menu went `position:fixed`, so `100%` resolved against the viewport; the pin is now
  written `!important` and matches the trigger. Opening a **searchable** select now **moves focus into its
  filter input** so you can type immediately (and returns focus to the trigger on close). And the combobox
  navigation/commit keys (**Enter**, Esc, arrows, Home/End, PageUp/Down) now act **only inside the open
  dropdown** — in particular **Enter picks the highlighted option instead of submitting/validating the
  surrounding form**. All three live in the shared popover helper (`rask-dom.js`), so every
  `data-rask-popover` control (selects, multiselects, date/time pickers) benefits.

## [0.15.1] - 2026-07-08

### Changed
- **The `Component` collection-expression builder is renamed `Create` → `__Fragment`.** The
  `[CollectionBuilder]` target has to be a public static, but named `Create` it was reachable via
  base-member lookup and **shadowed the generated factory of a user component named `Create`** — the
  call bound to the fragment builder instead of the component. The double-underscore name keeps it a
  valid builder while freeing `Create` (and other terse verbs) for user component names. `[a, b]`
  render bodies are unaffected (the compiler emits the call from the attribute); the only break is for
  code that called `Component.Create(...)` explicitly — use `Component.__Fragment(...)`.

## [0.15.0] - 2026-07-08

### Added
- **`Rask.Testing` — a public package for unit-testing components.** Until now the live-render + handler-
  dispatch seam that makes component tests possible (`RenderAsLiveRoot` / `TryInvokeHandlerAsync`) was
  `internal`, and the in-repo `Rask.TestSupport` harness was non-packable — so a consumer had **no
  supported way to test a Rask component's behavior** (only static `ToHtml()`). The new `Rask.Testing`
  package closes that gap: `RaskTest.Render(component, services?)` returns a `RenderedComponent` whose
  `.Html` reflects the current state, and `.ClickAsync()` / `.InvokeAsync(handlerId, jsonPayload?)`
  dispatch a handler (optionally with a JSON event payload) and re-render — so a consumer can render a
  stateful component, simulate a click/input/submit, and assert on the resulting markup, with no browser
  or server. `.HandlerId(domEvent)` / `.Attr(name)` query the current HTML. Validated by a
  consumer-shaped test project that uses only the public API (it is deliberately *not* in Core's
  `InternalsVisibleTo` allowlist). See [docs/testing.md](docs/testing.md).
- **`dotnet new rask-server --cqrs` scaffolds the Rask.Cqrs mediator.** The Server template gained a
  `--cqrs` switch: it adds a sample `GreetingQuery` + handler and a `/greeting` page that injects
  `IDispatcher` and dispatches it (under `Cqrs/`), the `Rask.Cqrs` package reference, and the
  `AddRaskCqrs()` wiring in `Program.cs` — turning the already-shipped, tested `Rask.Cqrs` package into a
  one-flag starting point. Without the flag nothing changes (default `false`). See
  [docs/cqrs.md](docs/cqrs.md) and [docs/getting-started.md](docs/getting-started.md).
- **RASK030 — prefer named arguments on factory calls with many positional args.** A new Hidden analyzer
  flags a Rask factory call that passes three or more leading positional arguments (e.g.
  `Div("main", "container", "color:red")`). Beyond one or two, positional calls read poorly and are
  fragile: Rask orders generated factory parameters by inheritance depth then file ordinal + span, so a
  later edit — adding a base-class property, renaming a partial file — can reorder parameters and
  silently rebind such a call. The first one or two positional arguments (the primary content) stay
  idiomatic. Hidden severity: no build output and no effect on the warnings-as-errors build — the IDE
  surfaces it as a suggestion. See [docs/diagnostics.md](docs/diagnostics.md#rask030).
- **The default error page now offers a recovery affordance.** After an uncaught fault the root error
  boundary rendered "Something went wrong" (plus the exception, and in development a stack) but no way
  back — the user was stranded and had to hunt for the browser's reload. It now shows a **"Reload this
  page"** button; the runtime wires any `data-rask-reload` element to `location.reload()` (delegated and
  CSP-clean, on both the Server and WASM hosts), and if the runtime never loaded the browser's own reload
  remains the fallback. Present in production too, where it's the primary recovery (no stack is shown).
- **RASK031 — two pages resolving to the same route are now flagged.** Two different top-level pages that
  resolve to the same URL made the active one arbitrary — a silent bug the generator didn't catch (it
  only deduped by type name and enforced a single `[NotFound]`). The `RoutesGenerator` now warns
  (`RASK031`) on every colliding page after the first, naming the page it collides with. Templates are
  compared the way the runtime router matches them — case-insensitive literals, trimmed slashes, and
  parameter names/`:constraints` ignored — so `/Products` ↔ `/products`, `/x` ↔ `x/`, and
  `/item/{id:int}` ↔ `/item/{id:guid}` all collide. It's a **warning**, not an error, so upgrading never
  hard-breaks a build that compiled before (the app still runs, just picks arbitrarily). Restricted to
  pages without a `[ParentRoute]` (whose template is the full path); parent-composed paths aren't
  resolved, so the check under-reports rather than risk a false positive.
  See [docs/diagnostics.md](docs/diagnostics.md#rask031).
- **`BsDatePicker<T>` / `BsTimePicker<T>` / `BsDateTimePicker<T>` — custom-popover date/time pickers.**
  Each opens a calendar/clock popover (a month grid + hour/minute lists) driven entirely by Rask
  live-diff view state — no `bootstrap.js` — and binds `DateOnly`/`TimeOnly`/`DateTime` (plus their
  nullable and `DateTimeOffset` forms) through `IFormControl<T>`, so two-way binding, validation and the
  `.invalid-feedback` display come for free. Fully keyboard-navigable (arrow keys move a virtual cursor
  via `aria-activedescendant`, Page Up/Down change month, Home/End the week, Enter selects, Esc closes)
  with ARIA grid/listbox roles; the weekday order/names and month label localize from
  `CultureInfo.CurrentCulture` while the bound value round-trips invariant. `Min`/`Max`/`Disable` grey out
  unavailable days, a nullable value gains a clear (×) button, and `Native: true` degrades to the native
  `<input type=date|time|datetime-local>`. A supplemental `rask-bootstrap.css` styles the grid/columns
  using Bootstrap CSS variables (light/dark aware).
- **IDE quick-fixes for Rask diagnostics.** A new `Rask.Generators.CodeFixes` assembly ships Roslyn
  `CodeFixProvider`s (the lightbulb / `Ctrl`+`.`) for two diagnostics: **RASK001** adds the `required`
  modifier to a property the generator already treats as a required factory parameter, and **RASK023**
  inserts `Alt: ""` on an `Img` missing its alt text. The code-fix assembly is packed alongside the
  analyzers in the `Rask.Server` and `Rask.Wasm` packages, so consumers get the fixes with no extra
  reference. It is a separate assembly from `Rask.Generators` (code fixes reference
  `Microsoft.CodeAnalysis.Workspaces`, which an analyzer assembly must not) and is wired in build-order
  only, so the warnings-as-errors build is unaffected. See [docs/diagnostics.md](docs/diagnostics.md).
- **Bootstrap form controls are now accessible to screen readers when invalid.** `BsInput`,
  `BsTextarea`, `BsSelect`, and `BsCheck` previously signalled validation failure with the visual
  `.is-invalid` border only — no programmatic state, no announcement, no association between the error
  text and its field. A bound field with validation messages now also renders `aria-invalid="true"` on
  the control, an `aria-describedby` linking it to the error message's `id` (and to the help-text `id`
  when `HelpText:` is set), and the `.invalid-feedback` message as a `role="alert"` live region so
  assistive tech announces the error the moment validation fails. Valid fields with `HelpText:` gain
  `aria-describedby` to the help text. No API or visual change; attributes emit in the canonical
  `aria-*` slot so the documented attribute order is preserved. See
  [docs/accessibility.md](docs/accessibility.md#form-validation).
- **Accessible focus trapping for overlays, and `BsModal` opts in.** A new runtime behavior (in the
  shared `rask-dom.js`, so it works on both the Server and WASM hosts) manages focus for any element
  carrying `data-rask-focus-trap`: focus moves into it on open (its `[autofocus]` element, else the
  element itself), `Tab`/`Shift+Tab` cycle within it so focus can't reach the inert page behind, focus
  returns to the previously-focused element on close, and `Escape` dismisses it by clicking a
  `data-rask-dismiss` control (no per-keystroke server round-trip). A single document `MutationObserver`
  tracks appear/disappear and only re-scans when a trap is actually added/removed, so it stays cheap on
  unrelated diff morphs. `BsModal` now opts in automatically — an open modal traps focus, is labelled
  (`aria-labelledby` its title when an `Id` is set, else `aria-label` from the title text), and closes on
  `Escape` (kept inert for a static backdrop, matching Bootstrap). Previously a keyboard or screen-reader
  user could `Tab` straight out of an open modal into the page behind, had no `Escape` to close it, and
  lost their place when it closed. See [docs/accessibility.md](docs/accessibility.md#focus-trapping-overlays).

### Changed
- **Routing diagnostics now tell you how to fix them, not just what's wrong.** The route/param analyzers
  RASK004–RASK010 and RASK012 previously stated the problem but stopped there (e.g. *"Route segment
  '{seg}' has no matching public settable property"*). Each message now ends with the remedy — add the
  property, adjust the constraint, remove the conflicting attribute, break the `[ParentRoute]` cycle,
  etc. — so the fix is visible in the IDE error list and build output without opening the docs. Message
  text only; the diagnostic IDs, severities, and `docs/diagnostics.md` `**Fix:**` guidance are unchanged.
- **The per-component render walk skips a guaranteed-miss scope lookup when no scoped CSS exists.**
  `LiveRenderContext.PushScope` runs for every user component on every render and called
  `ScopedAssetRegistry.TryGetScopeId` (a `ConcurrentDictionary` probe) unconditionally — but on the
  common app with no scoped CSS that probe always misses. It now short-circuits behind a lock-free
  `ScopedAssetRegistry.HasAnyScopedCss` (`IsEmpty`) check, removing the probe on the hot path (e.g.
  ~30k eliminated probes/second on a 500-component page at 60 fps). The `MountedTypes` set is still
  populated for every component (a public per-render contract), so behavior is unchanged; the saving
  is per-component wasted work rather than a single measurable render delta.
- **Inbound WebSocket dispatch no longer allocates a string per frame.** The server dispatch loop
  (`RaskEndpointExtensions`) called `JsonElement.GetString()` on every inbound frame's `type` field —
  a fresh heap string — only to `==`-compare it against four constants (`hello`/`navigate`/`jsResult`/
  `dotNetInvoke`). It now matches the UTF-8 bytes in place with `JsonElement.ValueEquals(...u8)`. This
  runs on every keystroke, 60 Hz scroll tick, and click, so the per-frame string was pure waste. The
  type-match step drops to **0 B allocated** (was 40 B) and is ~34% faster (8.5 ns → 5.7 ns); behavior
  is unchanged (`handlerId` still resolves via `GetString`, which is a genuine dictionary key).
- **Reconnect UX no longer flashes on blips, stalls silently, or wipes state without warning.** The
  Server live-runtime reconnect overlay had three rough edges: it threw a full-screen blurred `inert`
  freeze over the app on the *first* socket `close` (so a sub-second network blip flashed a heavy modal),
  it showed an identical "Reconnecting…" spinner forever with no escalation or manual control, and on
  session eviction (a drop outlasting `SessionGracePeriod`) it did a silent `location.reload()` that
  wiped all in-progress UI state with no warning. Now: the **visible** blur overlay is **debounced**
  (~700 ms grace) so a fast reconnect never flashes a modal (interaction is still frozen immediately, so
  the debounce cannot open a double-submit window); it **escalates** after repeated failures or when
  `navigator.onLine` is false to an explanatory message plus a **Retry now** button, and collapses the
  backoff to reconnect on the `online` event (without resetting the attempt counter, so a flapping
  network still backs off); and session expiry surfaces **"Your session timed out. Reload to continue."**
  with a Reload button (plus a fallback auto-reload) instead of yanking the page. `connect()` is now
  single-flight so the retry/online paths can't spawn a duplicate socket. Auth handshakes still show
  "Authenticating…" up front. See [docs/configuration.md](docs/configuration.md#reconnect-ux).

- **Duplicate `data-rask-key` siblings are now reported at Error, not Warning.** When the live diff finds
  two sibling elements sharing a `Key:`, keyed reconciliation is disabled for that list and it falls back
  to a positional walk that can graft a node's DOM state (focus, input value, scroll) onto the wrong
  sibling on reorder — a latent state-corruption bug, not a cosmetic nit. The one-time diagnostic
  (routed through the `RaskDiagnostics` seam, deduplicated per key) now logs at `Error` so it surfaces
  loudly. No behavior change beyond the log level; fix the duplicate keys to silence it.
- **Navigation now shows progress and is accessible.** On the Server live runtime, a client-side route
  change (`navigate`) carries no handler seq/ack, so a slow server-side render used to show **no progress
  indicator at all**, and on commit focus stayed on the now-removed nav link with no announcement — a
  screen-reader user got no "navigated to X". Now a forward or back/forward navigation reuses the top
  progress bar (after the same ~300 ms grace as a slow handler round-trip, so a fast nav never flashes
  it), and a forward, whole-page navigation moves focus into the new page's `<main>` (or first `<h1>`) and
  announces the new page title through a polite `aria-live` region. The bar stays up while either a
  handler round-trip or a navigation is outstanding. Server host only for now (the WASM navigation path is
  a follow-up). See [docs/accessibility.md](docs/accessibility.md#navigation).

### Fixed
- **RASK002 no longer fires for a component that has a DI constructor and a `required` factory
  parameter.** The diagnostic wrongly treated "DI constructor, no parameterless constructor" as unable
  to honor `required`. In fact the generated factory builds such a component with
  `ActivatorUtilities.CreateInstance` (which runs the DI constructor, so injected services are set) and
  then post-assigns every factory parameter — so a `required` property with no member initializer *is*
  honored at runtime. RASK002 now fires only in the genuinely broken shape: a component with **both** a
  DI constructor and a parameterless constructor **and** a `required` property carrying a member
  initializer (the factory emits `new C() { … }` whose object initializer excludes the initializer-
  carrying property, so the consumer build hits `CS9035`). The RASK001 quick-fix, which was withheld in
  the mis-flagged case, is now offered there too. See
  [docs/diagnostics.md](docs/diagnostics.md#rask002).

- **`BsDropdown` menus now show in Safari and `AlignEnd` works.** Two Popper-less dropdown bugs in the
  supplemental `rask-bootstrap.css`: (1) the `0.14.1` table clip fix used `overflow-x: clip;
  overflow-y: visible`, which Chromium honours but WebKit/Safari clips on the "visible" axis too — so a
  dropdown opened inside a scrollable `BsTable(Responsive: true)` vanished in Safari. It now uses plain
  `overflow: visible` while a menu is open, which every engine honours. (2) `BsDropdown(AlignEnd: true)`
  (and `AlignStart`) was inert because Bootstrap 5.3 gates `.dropdown-menu-end` / `-start` on a
  `[data-bs-popper]` attribute only Popper's JS sets; the alignment is now applied statically, so a menu
  anchored to a right-hand toggle right-aligns and stays within the row instead of opening off the right
  edge. No consumer change beyond picking up the release.

## [0.14.1] - 2026-07-07

### Fixed
- **`BsDropdown` inside a scrollable `BsTable` is no longer clipped.** A row-level dropdown menu was cut
  off by the `.table-responsive` scroll container (Bootstrap's `overflow-x:auto` forces `overflow-y:auto`,
  and Rask dropdowns are Popper-less so the menu can't be portalled out). `BootstrapStyles()` now also
  links a small supplemental `rask-bootstrap.css` that lets the container overflow vertically while a menu
  is open (clipping horizontally so wide tables can't spill). No consumer change beyond picking up the
  release.

## [0.14.0] - 2026-07-07

### Added
- **`BsModal` full-screen dialogs.** `Fullscreen: true` renders an edge-to-edge `.modal-fullscreen` at
  every width; `FullscreenBelow: Bp.Sm` (or any `Bp`) renders `.modal-fullscreen-{bp}-down` so the dialog
  fills the screen below that breakpoint and stays a sized, centered dialog above it — ideal for forms on
  phones. Composes with `Size` (sized at/above the breakpoint, full-screen below).
- **`Sizing.MinVW100` / `Sizing.MinVH100`** typed utility tokens (`min-vw-100` / `min-vh-100`) — completes
  the viewport-sizing family (alongside `VW100`/`VH100`) for min-height layouts such as a sticky footer.

### Changed
- **The live-root render no longer allocates the whole page twice.** Every server/WASM live
  render — the initial GET, every event re-render, reconnect recovery, and hot reload — went
  through `Component.ToHtml()` (one full-page `string`) and then `HeadAssetRegistry.ApplyTo`,
  which copied the entire page into a second builder to splice the deduplicated `<head>` assets
  in at the sentinel (a second full-page `string` + a whole-body `IndexOf` scan). The path now
  serializes straight into a pooled `StringBuilder`, records the sentinel offset as it is written
  (no scan), and splices the head-asset block in place (`ApplyInPlace`), so the page is
  materialized to a `string` exactly once. Measured on the live-root benchmark (ShortRun,
  net10.0): per-render allocation drops **~48%** on a small page (7.7 KB → 4.0 KB), **~50%** on a
  500-row page (171.8 KB → 86.0 KB), and **~33%** on a 1500-row page (788.7 KB → 528.1 KB), with
  ~16–17% less wall-clock on the large pages. Output HTML and diff-frame offsets are byte-identical
  (existing serializer/diff replay tests validate both). A new `RenderPageXLarge` benchmark isolates
  the per-render cost at scale.

## [0.13.0] - 2026-07-06

### Added
- **Opt-in full WASM AOT.** Publish with `-p:RaskWasmAot=true` (needs the `wasm-tools` workload) to
  AOT-compile the browser bundle IL→WASM; the default keeps the Mono interpreter, so existing builds
  are unchanged. The framework is now trim + AOT analyzer-clean: a reflection-free `TypedParserRegistry`
  seeds every BCL `IParsable` primitive and resolves route/query/form values without runtime generics;
  the `RoutesGenerator` auto-registers custom `IParsable` route/query param types; and a new public
  `RaskBinding.RegisterParsable<T>()` registers custom form-model value types. The runtime assemblies
  build under `IsAotCompatible` and the WASM sample under `EnableAotAnalyzer`, so every
  warnings-as-errors build enforces AOT-safety. See [docs/aot.md](docs/aot.md).
- **New "Component tiers" section in the Composition guide** contrasting the three ways to author a
  reusable unit — a Tier-0 static method, a Tier-1 stateless `Component`, and a Tier-2 stateful
  `Component` — with a decision table, the static-method context caveat, and a rule of thumb. Backed
  by a live, code-embedded showcase demo (`component-tiers`) that renders all three side by side; only
  the stateful counter holds state and re-renders on click with no `StateHasChanged()`.

### Changed
- **Large keyed-list reorders no longer scale O(n²).** The keyed-reconciliation move loop in
  `FrameDiffer` mutated a `List<int>` with `IndexOf`/`RemoveAt`/`Insert` (each O(n)) per moved row, so a
  full/near-full reversal of a large keyed list (a data-grid sort/reverse) was O(n²) — ~3 ms for a
  5000-row reversal, on the render→diff→send hot path. Above a 256-row threshold it now uses an
  allocation-free order-statistics treap (`PositionIndex` — rank and insert-at-rank in O(log n)),
  making the loop O(n log n): a 5000-row reversal drops to ~1.3 ms (**~2.4× faster**) and a 1000-row
  one to ~199 µs, with the allocation profile unchanged. Smaller lists keep the cache-friendly `List`
  path (no regression). The emitted move positions are identical, so the existing replay-to-target
  tests validate both paths.
- **Form binding no longer compiles a lambda per render.** `ExpressionAccessor.Parse` (run inside
  `WriteAttributes` for every bound `Input`/`Select`/`Textarea`/`Bs*` control, on every render) used to
  call `Expression.Compile()` to read the bind target once, then discard the delegate. It now walks the
  expression with reflection instead — same result, no runtime code generation. Measured on the parse
  hot path: **~600–680× faster and ~20× less allocation** (simple bind 21.7 µs / 4.4 KB → 33 ns /
  216 B; nested chain 27 µs → 44 ns; list-indexer 58 µs / 4.8 KB → 86 ns / 272 B). Undocumented
  expression shapes fall back to `Expression.Compile()`, so behavior is unchanged.
- **The WASM live runtime no longer allocates a `byte[]` per rendered frame.** `WasmLiveSession` used
  to `ToArray()` its write buffer on every frame to hand the payload across the `applyRender` JS
  boundary and to dedup against the previous frame. It now double-buffers two `ArrayBufferWriter`s and
  compares spans directly (mirroring `Rask.Server`), and pushes the payload to JS zero-copy via a
  `MemoryView` (`ApplyRender(Span<byte>)`) instead of a marshalled `byte[]`. The per-frame allocation
  drops to **0 B** (was the full payload size — e.g. ~4 KB for a document frame) and the emit/dedup
  step is ~2× faster; the one remaining copy is a JS-side `.slice()` that materialises the frame for
  `TextDecoder`. Intermediate publish-renders are fully allocation-free; the two byte-returning entry
  points (`InitialRenderAsync`/`DispatchAsync`) keep a single `ToArray` for their unit-test seam.
- **Unified the double-buffered send between the Server and WASM hosts.** The dedup-and-swap mechanic
  (skip byte-identical frames, hand the buffer to the transport, swap the sent buffer to the dedup
  baseline) now lives once in `LiveSessionBase.TryEmitFrameAsync`, with the transport as an abstract
  `SendFrameAsync` seam — a WebSocket send on Server, a zero-copy `MemoryView` `applyRender` on WASM.
  Internal refactor, no behavior or public-API change; removes the duplicated buffer bookkeeping the
  WASM zero-copy work had introduced. WASM's send stays synchronous (allocation-free).

### Fixed
- **`Rask.Server` no longer crashes at static-init under NativeAOT.** `RaskEndpointExtensions` built its
  constant "session unknown" WebSocket payload with `JsonSerializer.Serialize(anonymous)`, which throws
  `InvalidOperationException` under NativeAOT (reflection-based JSON is disabled) and took down `UseRask`
  before the host started. It is now a UTF-8 string literal — byte-identical, no serializer, no
  reflection. (This removes the *first* NativeAOT startup blocker; full AOT boot additionally requires
  the framework's in-library endpoint registrations to be AOT-safe — see the next entry.)
- **A Rask Server app now NativeAOT-compiles, boots, and renders.** The framework's own endpoints
  (WebSocket, runtime script, PWA manifest/service-worker, content-addressed assets, upload/download,
  auth redeem) were registered via the minimal-API `Map*(…, Delegate)` overloads, which route through
  `RequestDelegateFactory` (`RequiresDynamicCode`). Since the Request Delegate Generator only covers the
  app assembly, these library endpoints fell back to the runtime factory and a `Task`-returning handler
  crashed at startup under NativeAOT. They are now registered as `RequestDelegate`s (services resolved
  from `HttpContext.RequestServices`, route values from `RouteValues`), which also clears the 16 `Map*`
  `IL3050` warnings. `UseRask<TApp>` additionally annotates `TApp` with
  `[DynamicallyAccessedMembers(PublicConstructors)]` so the app component's constructor survives trimming
  for `ActivatorUtilities.CreateInstance`. `Rask.Server` is now IL-warning-clean under the AOT analyzer;
  verified end-to-end — `dotnet publish -p:PublishAot=true` produces a native binary that boots and
  serves `HTTP 200`.

## [0.13.0] - 2026-07-06

### Added
- **NuGet packages now ship XML documentation, an icon, and per-package titles.** Every framework
  package emits its `///` API docs (`GenerateDocumentationFile`), so `AddRask`/`UseRask`, the `Bs*`
  factories, forms and the rest of the public surface now light up IntelliSense tooltips and parameter
  hints for consumers. The host packages (`Rask.Server`/`Rask.Wasm`) additionally bundle `Rask.Core.xml`
  next to the `Rask.Core.dll` they already carry, so the Core public API (`Component`, `Element`,
  factories, forms, routing) surfaces docs too. Each package also gains a square gallery/IDE icon
  (`rask-icon.png`) and a human-readable `<Title>`. `CS1591` (missing doc on a public member) is
  suppressed so partial doc coverage ships as-is without forcing a full-coverage sweep.

### Changed
- **Per-package NuGet identity for the standalone packages.** `Rask.Cqrs`, `Rask.WebPush` and both
  `Rask.Validation.*` packages now carry their own `PackageTags` instead of inheriting the framework
  default (`web component framework wasm net10`), which mis-signalled scope and hurt search — e.g.
  `Rask.Cqrs` (a reflection-free mediator) and `Rask.WebPush` (a transport-neutral server sender) are
  no longer tagged `wasm`/`component`. `Rask.Cqrs` and `Rask.WebPush` also ship a **package-specific
  `NUGET.md`** (install + quickstart for that package) rather than the shared framework overview a
  consumer landed on before.
- **The `rask-server` template README points to the WASM variants** — since the bare `dotnet new rask`
  alias resolves to the server template, the generated README now notes `rask-wasm` / `rask-wasm-hosted`
  for a client-side app.
- **Form controls auto-opt-out of the render cache when they read validation state.** Reading an
  `EditContext`'s per-field validation messages / entries / validating flags during `Render()` now
  latches the same render-cache opt-out that `Context.Get` consumers get (`Component._readsAmbientState`),
  so a control that bakes feedback into its own output always repaints when a message is produced later
  in the submit pipeline — instead of serving a stale pre-submit frame. This removes the need for the
  manual `BypassRenderCache` override on `ValidationMessage` / `ValidationSummary` / `ValidatingIndicator`
  and the `Rask.Bootstrap` form controls, and — the DX win — means **custom** form controls that read
  validation state need no `StateHasChanged()` or `BypassRenderCache` of their own. No public API change;
  behavior is unchanged, the correctness now comes for free.

### Fixed
- **An event handler that captures a loop variable *and* `this` now re-renders the component that
  defined it, even when nested inside a composite.** A per-row handler like
  `items.Select(x => BsButton(OnClick: () => Handle(x)))` is lowered by Roslyn to *nested* display
  classes — the delegate's immediate target holds the loop item, while the captured `this` lives on an
  outer closure. `DelegateOwner.Resolve` only inspected the immediate target's `<>4__this`, so it
  missed the owner and fell back to the element's render-owner (the composite wrapper, e.g. the
  `BsButton`). Firing the handler then dirty-marked only the wrapper, so the page that owns the state
  never re-rendered and the button appeared dead — most visibly, edit/delete buttons inside a `BsTabs`
  table row (a row rendered inside a `BsButton` inside a tab pane) did nothing when clicked. Resolve now
  walks the captured closures to recover the defining component. Fast paths (method group, this-only
  lambda, directly-captured `this`) are unchanged; the walk runs only when the direct lookup misses.
  on doc validation — a dangling `<summary>` on `Component.ToHtml`, `paramref`s to non-existent
  parameters, an `&nbsp;` entity undefined in XML, and several unresolved/ambiguous `cref`s across
  `Rask.Core`, `Rask.Server`, `Rask.Wasm` and `Rask.Wasm.Hosting`.
- **`dotnet pack` now fails loud if the source generator DLL is missing** from the host packages
  (`Rask.Server`/`Rask.Wasm`) instead of silently shipping a package with no analyzer — a `<None>`
  include of an absent file packs nothing and emits no error, which would leave a consumer's factories
  never generating. A pack-time guard errors clearly if the hardcoded `Rask.Generators.dll` path is
  empty (build-order itself is already ensured via `Rask.Core`'s analyzer reference).
- **`Rask.Wasm.Tasks` is now `IsPackable=false`** — a `dotnet pack` over the whole solution no longer
  emits a stray, empty package for the build-only MSBuild task assembly.
- **Diagnostic-range references now read `RASK001–029`** (the current maximum), fixing stale
  `RASK001–024`/`–026`/`–022` strings in `docs/README.md`, `docs/best-practices.md`, `llms.txt`,
  `CONTRIBUTING.md` and `CLAUDE.md`.

## [0.12.1] - 2026-07-04

### Fixed
- **`Rask.Bootstrap` no longer declares a phantom `Rask.Core` package dependency.** Its `Rask.Core`
  `ProjectReference` was missing `PrivateAssets="all"`, so `dotnet pack` emitted a NuGet dependency on
  `Rask.Core 1.0.0` — a package that does not exist (`Rask.Core` is `IsPackable=false`; its DLL ships
  bundled inside the host packages `Rask.Server`/`Rask.Wasm`). An external consumer restoring
  `Rask.Bootstrap` alongside a host package hit `NU1101: Unable to find package Rask.Core`. Adding
  `PrivateAssets="all"` (as `Rask.Server`/`Rask.Wasm` already have) drops the phantom dependency;
  `Rask.Core` continues to flow in bundled with the host package.

## [0.12.0] - 2026-07-03

### Added
- **Source-generated CQRS/mediator (`Rask.Cqrs`)** — a new opt-in, standalone package for structuring
  work as queries, commands and notifications. Define `IQuery<TResult>` / `ICommand` / `ICommand<TResult>`
  / `INotification` messages with their handlers, then dispatch through `IDispatcher.DispatchAsync`
  (one method for queries and commands, response type inferred) and `PublishAsync` for notifications —
  a single injectable interface. Register once
  with `services.AddRaskCqrs()` — **host-agnostic**, the same call works on the Server and WASM hosts.
  A dedicated Roslyn generator (`Rask.Cqrs.Generators`) wires every handler at compile time via a
  `[ModuleInitializer]` registry and closed-generic invokers, so dispatch does **no runtime reflection
  and no assembly scanning**, and handler constructors are kept under the trimmer with
  `[DynamicDependency]` — a WASM app using it publishes with **zero IL warnings** (the reason a
  reflection-based mediator like MediatR can't fit the browser runtime). Pipeline behaviors
  (`IPipelineBehavior<TRequest, TResult>`, registered with `AddOpenBehavior` / `AddBehavior`) are the
  decorator hook for cross-cutting logging/validation/transactions; none ship. Notifications fan out
  `Sequential` (default) or `WhenAll`. Two compile-time diagnostics guard the wiring — **RASK028**
  (ambiguous handler) and **RASK029** (unregisterable handler). Depends only on
  `Microsoft.Extensions.DependencyInjection.Abstractions`, so it works in any .NET app, not just Rask.
  Documented in the new [CQRS](docs/cqrs.md) guide with a live demo in the showcase; self-contained
  optional package like the validation libraries.

### Changed
- **Docs — guides-first migration wrap-up (phase 4).** With every foldable example page now living inside a
  `docs/*.md` guide as an inline live demo, the doc index is reconciled: `llms.txt` gains the missing
  **Elements & the DSL** entry (all 24 guides indexed), the README documentation table adds **HTTP & files**
  and **CQRS**, and `docs/ai-agents.md`'s guide list is refreshed to a representative-not-exhaustive form so it
  stops going stale. Verified no doc/artifact references a deleted example route, and `GuideCatalog` ↔ `docs/`
  ↔ `llms.txt` ↔ README all line up. Only `TodosPage` and the unlisted `[QueryParam]` `/table` example remain
  standalone. This closes the guides-first epic.
- **Examples site — fold the data-grid pages (phase 3, final cluster).** The **Master-detail** page
  (`/master-detail`) is removed; its grid becomes a new `MasterDetailDemo` embedded in **`docs/composition.md`**
  under "Keyed lists" (registered `master-detail`) — it's a keyed-reconciliation showcase (expand inserts a
  keyed detail `<tr>`; sibling open rows keep their own inner sort). `OrdersPageTests` → `MasterDetailDemoTests`.
  The **Data table** (`/table`) teaches `[QueryParam]`-driven, shareable-URL state, which can't be a co-mounted
  live guide demo, so it's shown **code-only in `docs/routing.md`** (registered `routing-querytable`) and its
  `/table` route stays as a real, unlisted example the guide links to. Both are removed from the sidebar/home
  nav. This completes the phase-3 example-folding; `TodosPage` remains a runnable capstone.
- **Examples site — fold the Live ticker page into the Lifecycle guide (phase 3).** The standalone
  `/realtime/{Symbol}` page is removed; its reusable `LiveTicker` widget (poll loop in `OnMountAsync`, a symbol
  switch that fires `OnPropsChanged*`, `OnRendered` first-paint, `OnUnmount*`, `CancellationToken`, and a
  zero-JS server-rendered SVG chart) is embedded in **`docs/lifecycle.md`** under "When `OnPropsChanged*`
  refires" via a new `LiveTickerDemo` (registered `lifecycle-ticker`). Because a co-mounted guide demo can't
  own a live `[RouteParam]`, the BTC/ETH/SOL switcher now flips `Symbol` via internal state instead of URL
  navigation — re-rendering reconciles the same `LiveTicker` instance and fires `OnPropsChanged` exactly as
  the route-param switch did. `LiveTicker.cs` / `PricePoint.cs` are unchanged; `LiveTickerPageTests` →
  `LiveTickerDemoTests`; the sidebar row and home card repoint at the guide.
- **Examples site — fold the User components page into Getting started (phase 3).** The standalone
  `/components` page is removed; its three demos (`Greeting`, `WeatherCard`, `SkipFactoryCounter`, already in
  their own `*Demo.cs`) are registered in `DemoRegistry` (`components-greeting` / `-di` / `-skipfactory`) and
  embedded as inline live demos in **`docs/getting-started.md` §6 (factory generation)** — the section that
  already documents the required/optional/excluded property rules and `[SkipFactory]`. The sidebar's User
  components row and the home card repoint at the Getting started guide; the E2E `WalkUserComponentsGuideAsync`
  drives the greeting + `[SkipFactory]` counter on that guide. No new guide file.
- **Examples site — fold four Components-group example pages into their existing guides (phase 3).** The
  standalone `/events`, `/flash`, `/toast` and `/user` pages are removed; their demos (already in their own
  `*Demo.cs`) are registered in `DemoRegistry` and embedded as inline live demos in the guides that already
  document them: **Events** and **Flash** → `docs/composition.md`, **Toast** → `docs/bootstrap.md`, and the
  **User & auth** gating demos (imperative `IUserProvider` gate + declarative `Authorize`) → `docs/authentication.md`.
  The sidebar's Events/Toast/Flash/User rows and the home page's cards repoint at those guides. The E2E journey
  drives the moved demos on their guide walks (`TestCompositionGuideAsync`, `WalkBootstrapGuideAsync`, and a new
  `WalkAuthGuideAsync`); the Server slow-link / WS-reconnect steps that used `/events` now use the composition
  guide's folded click-counter demo. No new guide files. `ComponentsPage`, `LiveTicker`, `TablePage`,
  `OrdersPage` and the `Todos` capstone app are untouched (later clusters).
- **Examples site — a new HTTP & files guide (phase 3).** The three standalone "Data & files" example
  pages — `/http` (HttpClient + DI), `/upload` (file upload), `/download` (file download) — are removed;
  a **new guide `docs/http-and-files.md`** ("HTTP & files", Integration group) folds them in as four inline
  live demos (register + fetch, upload, download). The `*Demo.cs` components are unchanged — they're now
  registered in `DemoRegistry` (`data-http-register` / `data-http-fetch` / `data-upload` / `data-download`)
  and embedded by the guide (verified by `GuideEmbeddingTests`); the sidebar's Data/Files groups and the
  home page's Data & files cards repoint at the guide. The E2E journey's scattered `/http` `/upload`
  `/download` walks are replaced by a single `WalkHttpAndFilesGuideAsync` (which also guards the WASM
  base-address fix — the `HttpClient` fetch runs from the two-segment `/guides/http-and-files` route). The
  existing `docs/data-access.md` (EF Core persistence) is unchanged; the new guide covers data/file
  *transfer* and cross-links to it. The `Data table` and `Master-detail` example pages are untouched.
- **BREAKING: `Component` is now the framework's single rendering currency — `RenderResult` and
  `Child` are removed.** `Render()` and the `Head` override now return `Component?` (symmetric;
  return `null` to render nothing, replacing the old empty-`Fragment`/`default` sentinel). Children
  are `IEnumerable<Component?>` and the children indexer takes `params Component?[]`, so a `null`
  child renders nothing and needs no placeholder. The heterogeneous-literal converters
  (`string`/`int`/`bool`/`DateTime`/… → a `Text` node) moved from the deleted `Child` struct onto
  `Component`, so `Div()["Score: ", 42]` is unchanged. `Component` is itself a collection-expression
  target (via `[CollectionBuilder]` + a public `Component.Create`), so `Render() => [Doctype(),
  Html(...)]` and `Head => [Title(), Meta()]` keep working; a bare component passed to the indexer is
  still a single child (nesting is not flattened — `Component` exposes only a pattern `GetEnumerator`,
  not `IEnumerable<Component>`). **`Fragment` is now internal** — express multi-root / grouped content
  with a `[...]` collection expression instead of `Fragment()[...]`. Public delegate props that used
  `Child` now use `Component`: `ErrorBoundary.Fallback`, `Authorize.{Authorized,NotAuthorized,
  Authorizing}`, `ValidationSummary.Template`, `FlashOutlet.Template`, and the Bootstrap
  `Bs{CheckboxGroup,RadioGroup,MultiSelect}.OptionLabel`. Migration: replace `RenderResult` with
  `Component?`, `Child` with `Component`, `Fragment()[a, b]` with `[a, b]` (and `Fragment()[list]`
  with `[.. list]`), and `(Component)Fragment()` / `default` "render nothing" branches with `null`.
- **README slimmed to a landing page; docs realigned to the `Component` model.** The README is cut from
  ~1600 lines to a lean front door — pitch, install, and a doc-links table — that routes readers to the
  **[live demo](https://pal-tamas.github.io/rask/)**, the **`docs/`** guides, and the **`samples/`** apps
  instead of duplicating a book-length "Core concepts" tour inline (the deep-dive content already lives in
  `docs/`). Removed the `RenderResult` / `Child` / `Fragment` public vocabulary from the guides
  (`elements.md`, `building-form-controls.md`, `live-rendering.md`, `llms.txt`): the primitives are now
  `Text` / `Raw` / `Doctype`, and multi-root / sibling content is described as a `[...]` collection
  expression returning a `Component`.
- **A constant member initializer now becomes the factory parameter's default value instead of
  excluding the property.** A prop written `public string Tag { get; set; } = "x";` (or `= 3`,
  `= true`, `= BsColor.Danger`) is emitted as an optional factory parameter defaulting to that
  literal, so callers override it by name and omit it otherwise. Only *constant* initializers
  qualify — a `new(...)` initializer still excludes the property — and **`init`-only properties are
  excluded**: the factory reassigns every parameter on the reused persisted-component path
  (`__c.Prop = prop;`), which an init-only setter cannot satisfy (CS8852).

### Fixed
- **Bootstrap form controls now repaint their `.invalid-feedback` when validation runs on submit.**
  `BsInput`/`BsSelect`/`BsTextarea` bake the per-field message straight into their own render output from
  the `EditContext`'s mutable message list — state the render cache doesn't observe. Submitting a form with
  no `OnInvalidSubmit` (the common case) populated the messages but left the controls cache-pinned to the
  pre-submit empty state, so the errors never appeared. `BsFormControl` now sets `BypassRenderCache` (the
  same opt-out `ValidationMessage`/`ValidationSummary`/`ValidatingIndicator` already use), so a submit-time
  validation pass paints its field messages.
- **`BsFormControl` boxes each field (label + control + help + `.invalid-feedback`) in one wrapper `<div>`.**
  Previously the feedback was a bare sibling of the control, so in a flex/grid form
  (`.d-flex.flex-column.gap-3`) it became a separate gap-spaced item a full row below its input; it now sits
  tight under the control. `Required: true` additionally marks the field's `<label>` with a red asterisk
  (`<span class="text-danger ms-1">*</span>`).
- **A plain `Button(OnClick:)` with no explicit `type` again fires its click handler on the Server host.**
  The recent submit/reset guard in the Server client (added so a modal's backdrop-shield click handler can't
  hijack a native form submit) keyed off `button.type` — but a bare `<button>` reports `type === "submit"`
  by default, so the guard swallowed the click on *any* button that didn't set `type="button"`, including a
  button carrying its own `data-rask-on-click` (e.g. a master-detail expander toggle). The guard now bails
  only when the resolved handler lives on an **ancestor** of the submit/reset button (the actual hijack case),
  so a handler on the button itself still runs. WASM was unaffected (its client has no such guard).
- **An uncontrolled `<input>`'s value is no longer wiped by a full-document morph.** A full-HTML reply
  (initial paint, scoped-CSS delivery, or a reconnect resync) reconciles the whole page through `morph()`,
  which reset **every** input to the rendered `value` — treating a *missing* `value` attribute as `value=""`.
  But an input with no rendered `value` is *uncontrolled* (the framework isn't managing it), so any full reply
  that landed while the user had typed into one silently cleared their text (e.g. a form's `FormData` then read
  blank). The morph now only syncs an input when the render actually provides a `value` (controlled/bound
  inputs always render one, even `value=""`), leaving uncontrolled inputs' client-owned values alone. Guarded
  by an E2E that types into an uncontrolled field, forces a reconnect resync, and asserts the value survives.
- **WASM `WasmHostBuilder.BaseAddress` is now the app root, independent of the current route.** It read
  `document.baseURI`, which — once the SPA has navigated and the `<base>` element is no longer in the
  live DOM — reflects the *current route*. A singleton `HttpClient` whose `BaseAddress` is resolved
  lazily therefore baked whatever route was active at first resolution into every later relative fetch:
  from a two-segment route like `/guides/elements`, `GetFromJsonAsync("data/posts-1.json")` resolved
  against `/guides/` and 404'd (single-segment routes like `/http` happened to resolve correctly, which
  masked it). `getBaseAddress()` now derives from the boot-cached, route-independent `getBasePath()`
  (`new URL(getBasePath(), location.origin)`), so it stays the app root — carrying any sub-path — for
  the app's lifetime. Covered by the standalone/plain WASM showcase journeys (the `HttpClient + DI` page
  after the guides walk).
- **Guides site — prose code fences are syntax-highlighted, deep links scroll on refresh, and the
  mobile navbar stays one line.** Three showcase-guide polish fixes: (1) fenced ```` ```lang ```` blocks
  in the guide prose (rendered by Markdig, which has no highlighter) are now tokenized server-side with
  the same ColorCode pipeline the demo code panes use — `csharp`, `js`, `css`, `html`, and `bash`/shell
  (a small custom lexer, since ColorCode ships none) all colour; unknown languages stay plain. (2) A hard
  load / refresh of a guide URL carrying a `#fragment` now scrolls to that section (`GuideChrome`
  previously only smooth-scrolled on in-page anchor clicks). (3) The top navbar no longer wraps to two
  lines on phones — it stays single-line and drops the decorative brand badges below `md`.
- **Guides site — the mobile nav drawer's filter box stays pinned while the list scrolls.** The
  sidebar filter was a `position: sticky` child of the drawer's flex column, which does not stick in
  Safari (a long-standing WebKit limitation for sticky flex children): on iOS/Safari the filter scrolled
  away with the list, leaving a half-clipped row above it. The sidebar body is now a non-scrolling flex
  column — a pinned filter header with a hairline divider over a single scrolling list
  (`.side-nav-scroll`) — so the filter stays put in every browser and rows scroll cleanly beneath it.
- **A radio/checkbox click is no longer reverted by a lagging live-diff render.** The client applied a
  form control's `.checked` property unconditionally in both apply paths (the full morph and the diff
  codec), while `.value` was already protected by a pending-edit guard. On a busy page a re-render the
  runtime computed *before* the click reached it could land afterwards and flip the just-clicked
  radio/checkbox back — most visibly on the slower standalone WASM bundle, where clicking a radio in
  the Forms guide's group demo reverted to unchecked. `.checked` now uses the same guard: the change
  dispatch records the pre-click state (the `checked` attribute a native click leaves untouched — for a
  radio, the whole same-name group), and a lagging frame carrying that stale state is suppressed until
  an authoritative frame (the echo of the new state, or a server correction) arrives and releases it.
- **Live components embedded in a lazy child sequence are now reconciled (state persists).** A
  component built inside a *lazy* `IEnumerable<Child>` (a `yield`/LINQ pipeline passed to an element's
  `[...]` children indexer) was evaluated during serialization — after the owning component's
  child-reuse bookkeeping had run — so it was **re-created every render and silently lost its state**
  (an input's value, a toggle, any `_field`). The `IEnumerable<Child>` indexer now materialises a lazy
  sequence immediately, during `Render()`, so embedded component factories run while reconciliation is
  live and the same instance is reused across renders. Already-materialised collections
  (`Child[]`/`List<Child>`/…) pass through unchanged (no copy), so the render hot path is unaffected.
  This is what lets a guide page co-mount many live demos (built from a `yield`-generated list) and have
  every one stay interactive, including on the Server (WebSocket) transport.

### Changed
- **Examples site — a new Elements & the DSL guide (phase 3).** The 12 standalone DSL / HTML-element
  example pages (`/tags`, `/primitives`, `/props`, `/svg`, and the eight `/elements/*`) are removed; a
  **new guide `docs/elements.md`** ("Elements & the DSL", in the Core group) folds them in as 26 inline
  live demos — the four primitives, tag factories, universal props, typed SVG, and the HTML-element
  catalog by category. Demos are reused via `DemoRegistry` (each already lived in its own `*Demo.cs`); the
  DSL and HTML-elements sidebar groups are gone (the new guide's sidebar entry replaces them), the
  HomePage DSL cards and the hero CTA point at the guide, and the E2E drives the demos on the one page.
- **Examples site — Bootstrap examples folded into the guide (phase 3).** The 9 standalone
  `Rask.Bootstrap` example pages (`/bootstrap/nav`, `/buttons`, `/cards`, `/alerts`, `/icons`, `/modal`,
  `/tabs`, `/forms`, `/utilities`) are removed; every component demo — navbar/nav, buttons & badges,
  cards, dismissible alerts, icons, the zero-JS modal, tabs & accordion, `IFormControl<T>` forms, and
  the typed utility classes — is now an **inline live demo in the Bootstrap guide** (`docs/bootstrap.md`).
  Demos are reused via `DemoRegistry` (they already lived in their own `Bs*Demo.cs`); the standalone
  Bootstrap sidebar section is gone (the guide's own sidebar entry stays), and the E2E drives the demos
  on the one guide page.
- **Examples site — demo `CodeSample` stacks the code above the live result.** The source pane and the
  live result were side-by-side columns (`col-md-7` / `col-md-5`); they now stack vertically, code first
  then result, separated by a hairline — reads top-to-bottom and never squeezes either pane into a narrow
  column. No CSS selectors or E2E locators changed (the `.sample-code-col` / `.sample-result-body` class
  names are preserved).
- **Examples site — routing & JS-interop examples folded into the guides (phase 3).** The 7 standalone
  example pages — `/routing`, `/users/{id}`, `/navigator`, `/element-ref`, `/scoped-css`,
  `/asset-loading`, `/jsruntime` — are removed. The **Routing** guide (`docs/routing.md`) gains a live
  Navigator query-mutation demo (route/query-param binding stays documented in prose and unit-tested; the
  showcase's own sidebar navigation *is* the live routing). The **JavaScript interop** guide
  (`docs/js-interop.md`) gains inline live demos for element refs, scoped CSS (two components, same
  selector), the `IJSRuntime` `sessionStorage` round-trip, and the asset-loading bundle story (basic
  scoped CSS, scoped JS with module state, twin-selector bundling, lazy mount with no FOUC). Demos are
  reused via `DemoRegistry`; `NavigatorQueryDemo` was promoted out of the former page. The E2E drives
  both guides in place.
- **Examples site — lifecycle examples folded into the guide (phase 3).** The 4 standalone example pages
  (`/lifecycle`, `/disposal`, `/cancellation`, `/background`) are removed; every lifecycle demo — the
  hook-order probe, the mount/unmount cycle, `IDisposable` / `IAsyncDisposable`, `OnUnmount` vs
  `IDisposable`, lifetime-token cancellation, and the decoupled background-service feed — is now an
  **inline live demo in the Lifecycle guide** (`docs/lifecycle.md`), which gains Disposal and Background
  service sections. The mount/unmount widgets that lived inline in the pages moved into their own demo
  components (`LifecycleCycleDemo`, `DisposalSyncDemo`/`DisposalAsyncDemo`/`DisposalUnmountDemo`,
  `CancellationDemo`, `BackgroundMetricsDemo`) so they stay runnable inline; the lifecycle probes are
  unchanged. Demos reused via `DemoRegistry`; the E2E lifecycle demos run on the one guide page.
- **Examples site — composition examples folded into the guide (phase 3).** The 6 standalone example
  pages for context, callbacks, virtualize, keyed lists, drag & drop, and error boundaries are removed;
  each is now an **inline live demo in the Composition guide** (`docs/composition.md`), which gains
  "Keyed lists" and "Error boundaries" sections. The keyed-lists demo's interactive widget (keys
  on/off, reorder, focus-preserving rows) moved from the page into `KeyedListsReorderDemo` so it stays
  runnable inline. Demos reused via `DemoRegistry`; the E2E composition demos run on the guide page
  (locators scoped where badges/panes repeat).
- **Examples site — Forms folded into the guide (phase 3).** The 7 standalone forms example pages
  (`/binding`, `/form-controls`, `/validation`, `/floating-labels`, `/nested-forms`, `/form-groups`,
  `/multi-select`) are removed; every forms demo — the binding matrix, the control matrix, the full
  validation set (inline/DataAnnotations/FluentValidation, async, cross-field, programmatic), nested
  models, radio/checkbox groups, and multi-select — is now an **inline live demo in the Forms &
  validation guide** (`docs/forms.md`). Demos are unchanged (reused via `DemoRegistry`); the E2E forms
  walk drives them on the guide page (locators scoped where option values repeat across demos).
- **Examples site — Browser APIs folded into the guide (phase 3).** The 27 standalone Browser-API
  example pages (`/browser/*`) are removed; every typed wrapper is now an **inline live demo in the
  Browser APIs guide** (`docs/browser-apis.md`), grouped by capability (Storage · Environment ·
  Location/sensors · Observers · Media/crypto/files). Demos are unchanged (reused via `DemoRegistry`) —
  27 sidebar entries collapse into one guide that runs every wrapper live.
- **Examples site — guides-first navigation (phase 2).** The showcase now leads with the guides. The
  sidebar is reordered **Guides → Examples → Bootstrap** with the guide categories (Start here / Core /
  Integration / Advanced) expanded by default as the primary spine, and the interactive example pages
  demoted (collapsed) below — nothing is removed, so every example route still resolves. The landing
  page (`/`) opens with the grouped **guides index** (shared `GuideCards`, reused by `/guides`) above a
  demoted "Browse the component examples" map, and the hero's primary call-to-action is now **Read the
  guides**. The examples pipeline (fold demos into guides, then delete the standalone pages) lands in
  later phases.

### Added
- **Examples site — Rails-guides-style guide pages with inline live demos (phase 1).** A guide now
  renders in a documentation layout modelled on rubyonrails.org/docs: a numbered **Chapters** table of
  contents built from the guide's headings, a sticky **On this page** rail that scroll-spies the current
  section (client-only, via `IntersectionObserver` — no round-trips), **prev/next** book-navigation
  following the `GuideCatalog` order, and a version/source banner. Guides can now **embed a live demo
  inline** with an HTML-comment marker — `<!-- demo:key -->` — which the `Markdown` component splits on
  and mounts the matching demo (code + live result) from a new `DemoRegistry`; the marker is invisible
  when the same `docs/*.md` renders on GitHub, so the guides stay dual-purpose. The Routing and Forms
  guides are wired as the pilot; the visual identity stays on-brand (violet, Space Grotesk). This is the
  first step toward a guides-first showcase.
- **C# Hot Reload → live re-render (`dotnet watch`).** Editing a component's `Render()` (or anything it
  calls) and saving now repaints the running live session automatically — the last gap in Rask's
  `dotnet watch` story, alongside the existing scoped-CSS/JS hot reload. A new `ComponentHotReloadHandler`
  (`[MetadataUpdateHandler]`) re-renders every active session: it marks the whole component tree dirty (so
  cached subtrees re-execute against the freshly-applied IL — even edits to a helper/static a component
  calls) and requests a normal render, shipping a diff over the existing transport. Sessions are tracked
  **weakly** and only under `dotnet watch` (`MetadataUpdater.IsSupported`), so a normal/published run pays
  nothing and the code trims away. The nearest a compiled framework gets to Rails' no-build, edit-and-refresh
  loop. See `docs/getting-started.md`.
- **Scoped-CSS bundle minification.** The single content-hashed scoped-CSS bundle is now **minified**
  (comments + insignificant whitespace stripped) before it's hashed and served — completing the asset
  pipeline that already bundles, fingerprints (`/_rask/a/{hash}.css`, `immutable`), and brotli/gzip-compresses
  scoped CSS. New `RaskLiveOptions.MinifyScopedAssets` (`bool?`): `null` (default) = **auto** — on outside
  `Development`, off in `Development` so hot-reloaded CSS stays readable (resolved by `UseRask` from
  `IHostEnvironment`); set `true`/`false` to force it. Minification runs **before hashing**, so the digest,
  immutable URL, and compressed caches all key off the minified bytes (no double representation). The
  built-in minifier is deliberately **conservative** — it only strips whitespace around the self-delimiting
  `{ } ; ,`, leaving descendant/child combinators, `calc()` operators, selector colons, and string/`url()`
  contents untouched — and only the CSS bundle is minified (JS is served as-is). See `docs/configuration.md`.
- **Rails-grade developer error page.** The built-in `DefaultErrorPage` (shown when a fault escapes every
  `ErrorBoundary`) now renders a rich view **in Development**: parsed stack frames (via the trim-safe
  `DiagnosticMethodInfo`, not reflection), a ±5-line **source excerpt** around each throwing line with the
  line marked, and the full **inner-exception chain** in collapsible sections. **Production is unchanged and
  security-gated** — only the outermost type + message are shown; no stack, no source, no file paths, and no
  inner-exception detail ever reach the response. All text is HTML-encoded, and source reads fail closed
  (missing/stripped paths degrade to just the frame line). The Development gate still reads
  `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` with no `Microsoft.Extensions.Hosting` dependency.
- **Flash messages — the injectable `IFlash` service (Rails-style `flash`).** Queue transient user
  messages from any component or handler (`flash.Success("Saved")` / `Info` / `Warning` / `Error` /
  `Add(level, …)`); a single headless `FlashOutlet` drains them (`Consume()`, consumed-once) on mount and
  on `Changed`, rendering each via a caller-owned `Template` with a dismiss callback. `IFlash` is
  registered **scoped** per session on both hosts, so a message queued just before a client-side
  `NavigateTo` survives the navigation and shows once on arrival. `Rask.Bootstrap` adds `BsFlash` — a
  ready-made fixed toast-container of `BsToast`s (mount one in your layout). New **Flash messages**
  showcase page (`/flash`); see `docs/composition.md`.
- **Examples site — on-site Guides (the repo's `docs/*.md` rendered in the showcase).** A new
  **Guides** section renders the framework's narrative documentation in-app via a reusable `Markdown`
  component (Markdig, with the rendered HTML cached). `/guides` lists the guides as grouped cards and
  `/guides/{slug}` renders one; the guides' relative `*.md` cross-links are rewritten to SPA-routed
  `/guides/{slug}` anchors, and links up to the repo root point at GitHub. High-traffic demo pages gain
  a "See also" row linking to the matching guide. The guides are embedded from `docs/`, so they never
  drift from the repo. (Markdig renders at build/runtime on the host; the WASM showcase still publishes
  trim-clean.)
- **Rask.Bootstrap navigation primitives — `BsNavbar`, `BsNav`, `BsNavItem`.** Typed navbar/nav
  containers; each `BsNavItem` with an `Href` renders a core `NavLink`, so links are SPA-routed
  (`data-rask-nav`) and light up their `.active` class by matching the current route — no client JS,
  no manual active tracking. New **Navbar & nav** Bootstrap showcase page (`/bootstrap/nav`); see
  `docs/bootstrap.md`.
- **`BsOffcanvas` responsive mode (`Responsive: Bp.Md`).** Renders `.offcanvas-{bp}` so the panel is a
  slide-in drawer below the breakpoint and a static, in-layout column at/above it — the canonical
  pattern for a sidebar that collapses to a hamburger on mobile. Drawer chrome (header, backdrop) is
  hidden where the panel turns static.
- **`NavLink.Match`.** An optional path the active-state comparison uses instead of `Href`, so a link
  can point at one route yet stay active across a whole section (`Href: "/realtime/BTC"`,
  `Match: "/realtime"`, `ActiveMatch: Prefix`).
- **`AddRaskPwa(manifest)` — opt-in PWA for the Server host.** The server-side counterpart to the WASM
  host's `UseManifest(...)`. It serves the installable manifest at `{PathBase}/rask/manifest.webmanifest`
  (URLs rooted at the app base path), emits `<link rel="manifest">` + `<meta name="theme-color">` directly
  into the server-rendered `<head>`, and serves a service worker at `{PathBase}/rask-sw.js` that handles
  Web Push and serves a static `offline.html` on failed navigations. The Server service worker deliberately
  does **not** cache the server-rendered shell (it carries a one-shot session id and is `no-store`), so a
  Server PWA is installable + push-capable but **not** an offline app — there is no background sync and no
  install-prompt replay (those stay WASM-only). The transport-neutral push/notificationclick service-worker
  handlers are shared from `Rask.Core/Resources/rask-sw-shared.js` across both the WASM and Server SWs.
  The transport-agnostic client helpers (`__raskPush`/`__raskNotify`/`__raskBadge`/`__raskWakeLock`) are
  shared from `Rask.Core/Resources/rask-pwa.js` and spliced into both the Server and WASM clients; only the
  manifest injector and install-prompt capture stay WASM-only.
- **`dotnet new rask-server --pwa` + Server PWA showcase.** The Server template gains a `--pwa` flag
  (scaffolds `AddRaskPwa`, `wwwroot/icon.svg`, and `offline.html`). The Server sample
  (`samples/Rask.Example.Server`) is now an installable PWA with a new **Server PWA** showcase page
  demonstrating the full `INotifications`/`IBadge`/`IWebPush` subscribe→send loop (via `Rask.WebPush`),
  and an E2E test asserting the manifest, service worker, and the offline-fallback behaviour.
- **PWA on the Server host.** The transport-agnostic PWA browser APIs — `IWebPush` (push subscribe),
  `INotifications`, `IBadge`, `IWakeLock` — and the typed `WebAppManifest` now live in
  `Rask.Core.Browser` and are registered on the Server host too, so a Server app can subscribe to push,
  show local notifications, set the app badge, and hold a screen wake lock. `WebAppManifest` gains a
  `ToJson(basePath)` overload that roots relative manifest URLs at the app's base path (the server-side
  analogue of the WASM host's boot-time resolution). The remaining browser APIs that need transient
  activation, a live document/handle, or the installed-PWA instance (`IShare`, `IFullscreen`,
  `IInstallPrompt`, `IEyeDropper`, `IPictureInPicture`, `IMediaDevices`, `IScreenOrientation`,
  `IIdleDetector`, `ISerial`, `IUsb`, `IHid`, `IBluetooth`) stay WASM-only.

### Changed
- **Examples site — visual refresh.** A cohesive design pass over the showcase: a real type system
  (Space Grotesk display, Inter body, JetBrains Mono for code and nav/section labels, loaded from the
  font CDN), violet-tinted neutrals and a unified deep-ink code surface, a sharpened hero (mono eyebrow
  + dot-grid texture), refined feature cards, an inset accent bar on the active nav item, and a pulsing
  "Live result" marker on the code samples. Keeps the .NET-violet brand; motion respects
  `prefers-reduced-motion`. No DOM/behaviour changes.
- **Examples site (GitHub Pages) — redesigned, mobile-first navigation.** The left sidebar is now a
  fixed-width responsive offcanvas built from the `Bs*` navigation primitives: ~90 links are grouped
  into collapsible sections (only the active route's group is open by default), with a search filter at
  the top, replacing the always-expanded ~33%-wide column. On mobile it collapses to a hamburger-driven
  drawer; on desktop the list scrolls inside a viewport-bounded region. The shell now dogfoods
  `BsNavbar`/`BsNav`/`BsNavItem`/`BsOffcanvas`.
- **`WasmHostBuilder.UseManifest` renamed to `UsePwa`** for naming parity with the Server host's
  `AddRaskPwa` (both now read as "enable PWA from this manifest"). `UseManifest` remains as an
  `[Obsolete]` alias that forwards to `UsePwa`.
- **Breaking:** the shared PWA types `WebAppManifest` (and its nested manifest records/enums),
  `IWebPush`/`WebPush`/`PushSubscription`/`NotificationPermission`, `INotifications`/`Notifications`/
  `NotificationOptions`, `IBadge`/`Badge`, and `IWakeLock`/`WakeLock`/`IWakeLockSentinel` moved from the
  `Rask.Wasm.Browser` namespace to `Rask.Core.Browser`. Update the corresponding `using` in WASM apps
  (`WasmHostBuilder.UseManifest` is unchanged). Accepted at `0.x` pre-release.

## [0.11.0] - 2026-06-30

### Added
- **Rask.Bootstrap — typed Bootstrap 5.3 component library (new optional package).** Discoverable C#
  factories that emit correct Bootstrap markup, with typed enums replacing stringly-typed variants
  (`BsColor`/`BsSize`/`BsTheme`; `BsIconName` covers every Bootstrap Icons glyph). Content components
  (`BsButton`/`BsBadge`/`BsAlert`/`BsCard` + sections/`BsSpinner`/`BsProgress`/`BsListGroup`/
  `BsPagination`/`BsBreadcrumb`/`BsPlaceholder`/`BsTable`/`BsCloseButton`/`BsIcon`); **interactive
  components driven entirely by Rask's live runtime with zero JavaScript** — controlled state, no
  `bootstrap.js` — (`BsModal`/`BsOffcanvas`/`BsCollapse`/`BsAccordion`/`BsTabs`/`BsDropdown`/`BsToast`);
  and `IFormControl<T>`-bound form controls with `.is-invalid`/`.invalid-feedback` built in
  (`BsInput<T>`/`BsTextarea<T>`/`BsSelect<T>`/`BsCheck`/`BsRadioGroup<T>`/`BsCheckboxGroup<T>`/
  `BsMultiSelect<T>`/`BsFormGroup`/`BsFormLabel`/`BsInputGroup`). **Typed utility classes** —
  `Bs.Join(Shadow.Sm, Border.None, Margin.Bottom(4, Bp.Md))` across the Shadow/Border/Margin/Padding/
  Display/Flex/Rounded/Txt/Font/Sizing/Position/Bg families with responsive `Bp` breakpoints. Bootstrap
  5.3.8 + Bootstrap Icons 1.13.1 ship as static web assets under `_content/Rask.Bootstrap`; link them
  with `BootstrapStyles()`. Self-contained optional package (like the validation libraries). New
  **Bootstrap** showcase section (`/bootstrap/*`). See `docs/bootstrap.md`. The sample apps now dogfood
  the `Bs*` primitives throughout, and `BsRadioGroup`/`BsCheckboxGroup`/`BsMultiSelect`/`BsToast` are
  promoted from the samples into the package.
- **Server-side Web Push (`Rask.WebPush`)** — a new opt-in package that sends Web Push notifications from
  your backend, completing the loop with the WASM-only `IWebPush` client. Register with
  `services.AddRaskWebPush(o => { o.VapidKeys = …; o.Subject = "mailto:…"; })`, then inject `IWebPushSender`
  and call `SendAsync(subscription, WebPushMessage.Text(title, body, url))`. It signs the request with VAPID
  (ES256 JWT, RFC 8292) and encrypts the payload with `aes128gcm` (ECDH P-256 + HKDF + AES-128-GCM,
  RFC 8291), POSTing to the subscription endpoint; the typed message serializes to the exact JSON the default
  `rask-sw.js` shows. `WebPushResult` classifies the outcome (`ShouldDelete` on 404/410, `ShouldRetry` on
  429/5xx). `VapidKeys.Generate()` mints a key pair. **Transport-neutral and zero external dependencies** —
  all crypto is in-box `System.Security.Cryptography`. The hosted WASM sample (`Rask.Example.Wasm.Host`) wires
  up the full subscribe → send → notify loop; documented in the [Mobile & PWA](docs/pwa.md) guide.
- **Web Bluetooth (`IBluetooth`, `Rask.Wasm.Browser`)** — pair with a Bluetooth Low Energy device and talk to
  its GATT services from C# in the browser (heart-rate monitors, thermometers, fitness sensors, custom
  hardware). `RequestDeviceAsync(BluetoothRequestOptions)` shows the chooser (filters / `AcceptAllDevices` +
  `OptionalServices`) and returns a disposable `IBluetoothDevice` (or `null` if dismissed);
  `GetDevicesAsync()` returns already-granted devices. A device exposes `Info`, `ConnectAsync`/
  `DisconnectAsync`/`IsConnectedAsync`, `GetCharacteristicAsync(service, characteristic)`, and
  `WatchDisconnectAsync(onDisconnect)`; an `IBluetoothCharacteristic` does `ReadAsync`, `WriteAsync(data,
  withResponse)`, and `WatchAsync(onValue)` for notifications. Notifications and GATT-disconnect are
  **pushed** to your callbacks (static `[JSInvokable]`s, rooted for the WASM trimmer); the live GATT objects
  stay JS-side under framework-minted ids (one cached wrapper per physical device / characteristic); values
  cross base64-encoded. `IsSupportedAsync` gates the UI. **WASM-only** — `requestDevice` needs transient user
  activation, the live device handle, and a secure context; Chromium-family only at the time of writing. New
  `/bluetooth` showcase page in the WASM sample; documented in the [Browser APIs](docs/browser-apis.md) and
  [Mobile & PWA](docs/pwa.md) guides.
- **WebHID (`IHid`, `Rask.Wasm.Browser`)** — talk to a human-interface device no higher-level API covers
  (custom gamepads, sim controls, keyboards with extra keys, point-of-sale hardware) straight from C# in the
  browser. `RequestDevicesAsync(filters)` shows the chooser and returns the granted devices (empty if
  dismissed); `GetDevicesAsync()` returns already-granted devices without a prompt. Each `IHidDevice` exposes
  its descriptor (`Info`), `OpenAsync`/`CloseAsync`, `SendReportAsync`, `SendFeatureReportAsync` /
  `ReceiveFeatureReportAsync`, and `WatchInputReportsAsync(onReport, onDisconnect?)` — input reports are
  **pushed** to your callback (via a static `[JSInvokable]`, rooted for the WASM trimmer) with an optional
  unplug signal. The live `HIDDevice` stays JS-side under a framework-minted id (deduped per physical device);
  report payloads cross base64-encoded. `IsSupportedAsync` gates the UI. **WASM-only** — `requestDevice` needs
  transient user activation, the live device handle, and a secure context; Chromium-family only at the time of
  writing. New `/hid` showcase page in the WASM sample; documented in the
  [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **WebUSB (`IUsb`, `Rask.Wasm.Browser`)** — pair with and drive a USB device (custom hardware, dev boards,
  instruments) straight from C# in the browser. `RequestDeviceAsync(filters)` shows the device chooser and
  returns a disposable `IUsbDevice` (or `null` if dismissed); `GetDevicesAsync()` returns already-granted
  devices without a prompt. The device exposes its descriptor (`Info`: vendor/product id, manufacturer,
  product, serial) and the full I/O lifecycle — `OpenAsync`, `SelectConfigurationAsync`, `ClaimInterfaceAsync`
  / `ReleaseInterfaceAsync`, `TransferInAsync` / `TransferOutAsync` (bulk/interrupt) and
  `ControlTransferInAsync` / `ControlTransferOutAsync`, `CloseAsync`. The live `USBDevice` stays JS-side under
  a framework-minted id; transfer payloads cross base64-encoded. `IsSupportedAsync` gates the UI.
  **WASM-only** — `requestDevice` needs transient user activation, the live device handle, and a secure
  context; Chromium-family only at the time of writing. New `/usb` showcase page in the WASM sample;
  documented in the [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **Web Serial (`ISerial`, `Rask.Wasm.Browser`)** — talk to a serial device (Arduino / microcontroller,
  GPS, USB-to-serial adapter) straight from C# in the browser. `RequestPortAsync(SerialOptions, onData,
  onClosed?)` shows the port chooser, opens the chosen port (baud rate, data/stop bits, parity, flow
  control, optional USB vendor/product `SerialPortFilter`s), starts a read loop, and returns a disposable
  `ISerialPort` — `null` if the user dismisses the chooser. Inbound bytes are **pushed** to the `onData`
  callback (via a static `[JSInvokable]`, rooted for the WASM trimmer); `onClosed` fires if the device is
  unplugged; `WriteAsync(byte[])` sends (concurrent writes are serialized); dispose the port to stop reading
  and release it. `IsSupportedAsync` gates the UI. **WASM-only** — `requestPort` needs transient
  user activation and the live port stream (and a secure context); Chromium-family only at the time of
  writing. New `/serial` showcase page in the WASM sample; documented in the
  [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **Passkeys / WebAuthn (`IWebAuthn`, `Rask.Core.Browser`)** — register and sign in with a passkey (a
  platform biometric or roaming security key) instead of a password. `CreateAsync(options)` runs the
  registration ceremony and returns an `AttestationResult`; `GetAsync(options)` runs the authentication
  ceremony and returns an `AssertionResult` — both `null` if the user cancels. Typed
  `PublicKeyCredentialCreationOptions` / `PublicKeyCredentialRequestOptions` (relying party, user,
  algorithms, authenticator selection, allow/exclude credentials); all binary fields (challenge, ids,
  attestation/assertion buffers) cross the boundary as **base64url** strings, ready to POST to a
  relying-party backend (which issues the challenge and verifies the result — the security-critical half).
  `IsSupportedAsync` / `IsPlatformAuthenticatorAvailableAsync` gate the UI. **Shared** — works on both
  transports. New `/browser/webauthn` showcase page; documented in the
  [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **Camera / microphone / screen capture (`IMediaDevices`, `Rask.Wasm.Browser`)** — capture the camera,
  microphone, or screen and show it in a `<video>`, for photo capture, video calls, QR scanning, or screen
  recording. `GetUserMediaAsync(MediaConstraints)` / `GetDisplayMediaAsync()` return a disposable
  `IMediaStreamHandle` with `AttachToAsync(ElementRef video)` and `StopAsync()`; `EnumerateDevicesAsync()`
  lists cameras/mics/speakers. The live `MediaStream` stays JS-side under a framework-minted id — dispose
  the handle to stop every track and release the hardware (the camera indicator turns off). **WASM-only** —
  `getUserMedia` needs transient user activation, the live document, and a secure context. New
  `/media-devices` showcase page in the WASM sample; documented in the
  [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **Browser-API quick-win batch — `IGamepad` (shared) + `IEyeDropper` / `IPictureInPicture` /
  `IIdleDetector` (WASM-only)** — four more typed Web-API wrappers, injected through the constructor like
  the rest. `IGamepad` (`Rask.Core.Browser`, **both transports**) reads connected controllers — sticks,
  triggers, buttons — via a `requestAnimationFrame` poll the framework runs, pushing a `GamepadReading`
  through the shared static `[JSInvokable]` only when a pad's state changes. The other three live in
  `Rask.Wasm.Browser` because they need *transient* user activation: `IEyeDropper.OpenAsync()` picks a
  color from anywhere on screen (sRGB hex, or `null` on cancel); `IPictureInPicture` floats a
  `<video>` into an always-on-top miniplayer (`RequestAsync(ElementRef)` / `ExitAsync` / `IsActiveAsync`);
  `IIdleDetector` watches for the user going idle or the screen locking (`RequestPermissionAsync` then
  `WatchAsync(onChange, thresholdSeconds)`, pushed via a static `[JSInvokable]` in the `Rask.Wasm`
  assembly). New `/browser/gamepad` showcase page (shared sample) plus `/picture-in-picture`,
  `/eyedropper`, and `/idle` pages in the WASM sample; documented in the
  [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **File System Access (`IFileSystemAccess`, `Rask.Core.Browser`)** — open a file from disk, edit it, and
  save it *back to the same file* (not just download a copy), or work against a whole directory — for
  in-browser editors and file managers. `OpenFileAsync` / `OpenFilesAsync` / `SaveFileAsync` (typed
  `FilePickerOptions` / `SaveFilePickerOptions`) and `OpenDirectoryAsync` return disposable `IFileHandle` /
  `IDirectoryHandle` wrappers with `ReadTextAsync` / `WriteTextAsync` (and bytes, base64 over the wire) and
  `ListAsync` / `GetFileAsync`; cancelling a picker returns `null` rather than throwing. The opaque browser
  handles live JS-side under a framework-minted id (dispose the wrapper to release). **Shared** — works on
  both transports, Chromium-family only (gate on `IsSupportedAsync` and fall back to upload/download). New
  `/browser/file-system` showcase page (a tiny open→edit→save editor); documented in the
  [Browser APIs](docs/browser-apis.md) and [Mobile & PWA](docs/pwa.md) guides.
- **Expanded `WebAppManifest` (`Rask.Wasm.Browser`)** — typed support for the richer manifest members,
  all optional and omitted when unset: `Categories`, `Orientation` (`ManifestOrientation`),
  `DisplayOverride` (`DisplayOverrideMode`, incl. `window-controls-overlay`), `Shortcuts`
  (`ManifestShortcut`), `Screenshots` (`ManifestScreenshot`), `ShareTarget` (`ShareTarget`/
  `ShareTargetParams`), and `FileHandlers` (`FileHandler`). Existing manifests are unaffected. The WASM
  sample now ships `categories` + a `shortcuts` entry; documented in the
  [manifest guide](docs/pwa.md#installable--the-web-app-manifest).
- **PWA install prompt (`IInstallPrompt`, `Rask.Wasm.Browser`)** — show a custom "Install app" button
  instead of the browser's default mini-infobar. The framework captures and defers the
  `beforeinstallprompt` event at boot; `CanInstallAsync()` reports when a deferred prompt is available,
  `PromptAsync()` replays it and returns the user's `InstallOutcome` (Accepted/Dismissed/Unavailable),
  and `IsInstalledAsync()` reports whether the app is running standalone. **WASM-only** — the install
  flow needs the live document and transient activation. New `/install` showcase page in the WASM sample
  and a [Custom install button](docs/pwa.md#custom-install-button-iinstallprompt) guide section.
- **Device sensors (`IDeviceOrientation` + `IDeviceMotion`, `Rask.Core.Browser`)** — read the
  gyroscope/compass tilt and the accelerometer/rotation rate, e.g. for tilt-controlled UIs, an AR
  overlay, a compass, or shake gestures. `IsSupportedAsync()`, `RequestPermissionAsync()` (the iOS
  user-gesture gate; returns `Granted` where no prompt is required), and `WatchAsync(handler)` →
  `IAsyncDisposable` delivering `OrientationReading` (`Alpha`/`Beta`/`Gamma`/`Absolute`) or
  `MotionReading` (acceleration X/Y/Z, rotation rate, interval) pushed from JS via the shared static
  `[JSInvokable]` wiring. **Shared** — works on both Server and WASM. New `/browser/device-sensors`
  showcase page.
- **Media Session (`IMediaSession`, `Rask.Core.Browser`)** — publish now-playing metadata to the OS
  (lock screen, media hub) and handle hardware media keys / lock-screen controls, so in-page audio or
  video feels like a native player. `SetMetadataAsync(MediaMetadata)` and
  `SetPlaybackStateAsync(PlaybackState)` are one-shot setters; `SetActionHandlerAsync(MediaSessionAction,
  handler)` → `IAsyncDisposable` is a subscription pushed from JS via the shared static `[JSInvokable]`
  wiring; `ClearAsync()` resets it. **Shared** — works on both Server and WASM. New
  `/browser/media-session` showcase page.
- **Mutation Observer (`IMutationObserver`, `Rask.Core.Browser`)** — be notified when an element's
  children, attributes, or text content change, e.g. to react to DOM written by a third-party script or
  a portal you don't own. `ObserveAsync(ElementRef, handler, MutationOptions?)` → `IAsyncDisposable`;
  `MutationOptions` toggles `ChildList`/`Attributes`/`CharacterData`/`Subtree` + an optional
  `AttributeFilter`, and each `MutationEntry` reports the record `Type`, added/removed counts, and the
  changed attribute name. Completes the observer family alongside `IIntersectionObserver` and
  `IResizeObserver`, sharing the same static `[JSInvokable]` push wiring. **Shared** — works on both
  Server and WASM. New `/browser/mutation` showcase page.

### Changed
- **The scoped-asset endpoint (`/_rask/a/{hash}.{ext}`) now serves brotli/gzip.** It negotiates the
  client's `Accept-Encoding` and serves a compressed representation (brotli preferred) with a `Vary:
  Accept-Encoding` header and an encoding-suffixed `ETag`; each compressed representation is built once
  and cached by content hash (the bytes are immutable), so the scoped bundle ships small while keeping
  the `immutable` zero-revalidation caching. Shared by the Server and the published-WASM host endpoints.
- **Scoped CSS/JS now ship as one content-addressed bundle each, not one asset per component.** The
  framework concatenates every registered scoped CSS into a single bundle (and every scoped JS into
  another), hash-sorted so the bytes — and the immutable `/_rask/a/{hash}.{ext}` URL — are deterministic
  across builds. The page `<head>` emits exactly one `<link rel="stylesheet">` and one `<script defer>`
  (keyed `rsk-css` / `rsk-js`) instead of one tag per mounted component, and `BakeScopedAssetsTask` writes
  the two bundle files so any static-asset host (`MapStaticAssets`, a CDN) serves them. Because the whole
  bundle ships up front, a later mount (client-side navigation, a conditionally rendered section) is styled
  the instant its node is inserted — so the per-component lazy fetch, the `rel="prefetch"` pre-warming, and
  the navigation FOUC apply-gate are all gone. **Removed** the now-meaningless `PreloadScopedAssets`
  option (`RaskLiveOptions.PreloadScopedAssets` / `LiveOptions.PreloadScopedAssets`).
- **Server hosts and the `rask-server` template serve static assets via `app.MapStaticAssets()`.** The
  .NET 9/10 static-asset pipeline replaces `app.UseStaticFiles()` for the showcase server hosts and the
  template, bringing build-time fingerprinting, brotli/gzip and immutable caching — including for package
  `_content/*` assets such as Rask.Bootstrap's bundled CSS linked via `BootstrapStyles()`. The Server E2E
  fixture now runs the host **published** so the slow-network journey exercises production asset serving
  (compressed + revalidation-aware) rather than the dev static-asset handler.
- **Render cache is now children-aware for composite components.** A non-`Element` component's children
  arrive via the `[...]` indexer (not a factory parameter, so absent from the prop-change check) and are
  baked into its `Render()` output, so when the child set changed but its props didn't — e.g. a
  conditional alert appearing inside a wrapper while the wrapper's own classes stayed fixed — the stale
  cached render was reused and the update was dropped. `RenderForLive` no longer serves a non-`Element`
  component that has children from the cache, so composite wrappers behave like the inline elements they
  wrap. Allocation-neutral on the render benchmarks. (`Element`s were never affected — their children are
  walked at serialization, not embedded in the cached result.)
- **Event-handler re-render resolves through a captured closure.** A handler that closes over `this` *and*
  a local — e.g. `() => _active = index` inside a `Select((item, index) => …)` loop — is lowered by the
  compiler to a closure, so its delegate `Target` is the closure, not the component. Previously the live
  runtime fell back to the element's render-owner for such handlers, so when the element was nested inside
  a composite wrapper (a `Bs*` card/button around it) the wrapper re-rendered instead of the component that
  *defined* the handler — silently dropping the consumer's update (e.g. a `CodeSample` tab click, a parent
  rating callback). Handler-owner resolution (and `AutoCallback`) now unwrap the closure's captured `this`
  when it is a user component, so a handler always re-renders its defining component however deeply it is
  wrapped. The unwrap is scoped to non-`Element` components: a form control (`Input`/`Select`/`Textarea`)
  closes over `this` too, but its consumer re-render is owned by the form machinery, so those fall back to
  the element's render-owner as before. Allocation-neutral on the render benchmarks (the common
  method-group / `this`-only handler path is unchanged; only closures pay a one-time, cached field lookup).

### Fixed
- **WASM app on a plain static host could render blank** (keyed `<head>` reconciliation crash). A WASM
  app served from a static file host (GitHub Pages and the like) hydrates against the SDK `index.html`,
  whose `<head>` carries SDK-injected nodes the App doesn't render (`<base>`, the importmap `<script>`).
  The App's scoped-bundle `<link data-rask-key="rsk-css">` promotes the whole `<head>` to keyed
  reconciliation; when a non-matching SDK node was removed, the shared client morph (`rask-morph.js`)
  left its `anchor` pointing at the removed node, so the next insert threw
  `insertBefore … reference node is not a child` and the runtime never finished its first morph (blank
  page). The keyed reconciliation now advances the anchor past a node before removing it. The Server is
  unaffected (its `<head>` is fully framework-rendered, with no foreign nodes to skip).
- **Scoped CSS/JS 404'd on a WASM-hosting ASP.NET app serving a published bundle.** The in-process
  `/_rask/a/{hash}.{ext}` endpoint serves from the host's `ScopedAssetRegistry`, but a host that serves a
  *published* bundle only registers assets from assemblies it actually loads — a strict subset of the
  in-WASM-runtime set — so its hash for the single concatenated bundle didn't match the browser's request
  and the endpoint returned 404, shadowing the correct baked file (`UseStaticFiles` is skipped once
  routing matches the endpoint). The endpoint now falls back to the baked `/_rask/a/{hash}.{ext}` file in
  the published bundle on a registry miss (honouring a precompressed `.br`/`.gz` sibling), so scoped
  styles and component JS load under `Rask.Wasm.Hosting`. A static-file-only host (e.g. GitHub Pages) was
  always fine — it serves the baked files directly.
- **Device notification fan-out hardening (`IHid` / `IBluetooth`)** — when several watchers subscribe to one
  HID device / BLE characteristic, each pushed value/report is now delivered as its own `byte[]` copy (a
  mutating callback can no longer corrupt another subscriber's bytes), and each callback is isolated so one
  that throws no longer starves the rest of the fan-out. Documented that a Bluetooth device handle is shared
  per physical device (`RequestDeviceAsync`/`GetDevicesAsync` return the same instance — dispose from a single
  owner).
- **Web Serial (`ISerial`) byte marshalling** — `WriteAsync` and the inbound read loop sent raw `byte[]`
  across the WASM JS bridge, which doesn't carry byte arrays (the base `JSRuntime`'s `ByteArrayJsonConverter`
  expects Blazor's byte-array side-channel that the bridge doesn't implement), so writes shipped no usable
  data. Bytes now ride the boundary base64-encoded (`Convert.To/FromBase64String` in C#, `btoa`/`atob` in JS),
  matching `IFileSystemAccess`. The public `ISerialPort` API (`byte[]` in/out) is unchanged.
- **WebUSB (`IUsb`) handle ref-counting** — the JS helper dedups the same physical `USBDevice` to one id
  (`requestDevice`/`getDevices` return the same object), but `close()` evicted it immediately, so disposing
  one `IUsbDevice` handle tore down a device a second handle still held (subsequent calls threw "device handle
  is closed or unknown"). The shared device is now ref-counted — it closes only once every handle to it has
  been disposed.

### Added
- **[Browser APIs overview](docs/browser-apis.md)** — a new guide mapping the whole typed browser-API
  surface (20 shared in `Rask.Core.Browser` + 7 WASM-only in `Rask.Wasm.Browser`): what each wraps,
  one-shot vs subscription, the inject-from-constructor pattern, and the shared `[JSInvokable]` push
  mechanism behind the observer/broadcast/geolocation-watch subscriptions. Linked from the docs index,
  README, `js-interop.md`, and `CLAUDE.md`.
- **IndexedDB key/value store (`IIndexedDb`, `Rask.Core.Browser`)** — a persistent, asynchronous,
  large-capacity store (far beyond localStorage's ~5 MB), for caching app data offline:
  `IsSupportedAsync()` and `OpenStoreAsync(name)` → `IKeyValueStore` with `SetAsync`/`GetAsync`/
  `DeleteAsync`/`KeysAsync`/`ClearAsync` (string values — serialize objects to JSON). Each store is its own
  IndexedDB database (single object store, cached connection); each operation is transaction-wrapped. The
  full IndexedDB API (indexes, cursors, schema migrations) is intentionally out of scope. **Shared** —
  works on both Server and WASM. New `/browser/indexeddb` showcase page.
- **Performance / Navigation Timing (`IPerformance`, `Rask.Core.Browser`)** — a high-resolution monotonic
  clock and page-load timing from C#: `NowAsync()` (`performance.now()`, sub-ms) and
  `GetNavigationTimingAsync()` → `NavigationTiming?` (TTFB, DOM interactive, `DOMContentLoaded`, load,
  duration), e.g. to time an operation or report real-user metrics. **Shared** — works on both Server and
  WASM. New `/browser/performance` showcase page.
- **Web Crypto (`ICrypto`, `Rask.Core.Browser`)** — cryptographically strong randomness and hashing from
  C#: `RandomUuidAsync()` (`crypto.randomUUID`), `RandomBytesAsync(length)` → `byte[]`
  (`crypto.getRandomValues`), and `DigestHexAsync(HashAlgorithm, text)` → lowercase hex
  (`crypto.subtle.digest`, SHA-1/256/384/512). **Shared** — works on both Server and WASM; needs a secure
  context. New `/browser/crypto` showcase page.
- **Live geolocation tracking — `IGeolocation.WatchAsync` (`Rask.Core.Browser`)** — continuous position
  updates (`navigator.geolocation.watchPosition`): `WatchAsync(Func<GeolocationPosition,Task> onPosition,
  GeolocationOptions?)` returns an `IAsyncDisposable` and fires for the initial fix plus every update; the
  browser **pushes** each fix to the C# handler via a static `[JSInvokable]`, so one implementation serves
  **both** Server and WASM (rooted for the WASM trimmer). Pairs with the one-shot `GetCurrentPositionAsync`.
  New `/browser/geolocation-watch` ("Live location") showcase page.
- **Resize Observer (`IResizeObserver`, `Rask.Core.Browser`)** — be notified when an element's size changes,
  for container-responsive layouts or re-laying-out a canvas/chart (the sibling of `IIntersectionObserver`):
  `ObserveAsync(ElementRef element, Func<ResizeEntry,Task> onChange)` returns an `IAsyncDisposable` and fires
  once initially with the current size; the browser **pushes** each `ResizeEntry` (`Width`, `Height`) to the
  C# handler via a static `[JSInvokable]`, so one implementation serves **both** Server and WASM (rooted for
  the WASM trimmer). **Shared.** New `/browser/resize` showcase page.
- **Intersection Observer (`IIntersectionObserver`, `Rask.Core.Browser`)** — be notified when an element
  enters/leaves the viewport, for lazy-loading, infinite scroll, reveal-on-scroll, or impression tracking:
  `ObserveAsync(ElementRef element, Func<IntersectionEntry,Task> onChange, IntersectionOptions?)` returns an
  `IAsyncDisposable`; the browser **pushes** each change (`IsIntersecting`, `Ratio`) to the C# handler via a
  static `[JSInvokable]`, so one implementation serves **both** Server and WASM (rooted for the WASM
  trimmer). **Shared.** New `/browser/intersection` showcase page.
- **Broadcast Channel (`IBroadcastChannel`, `Rask.Core.Browser`)** — same-origin cross-tab messaging from
  C#: `OpenAsync(name, Func<string,Task> onMessage)` returns an `IBroadcastChannelConnection`
  (`PostAsync(message)` + `IAsyncDisposable`); a connection receives messages posted by *other* connections
  of the same name (other tabs/windows, or other connections in the page). Unlike the one-shot wrappers,
  the browser **pushes** each message back to the C# handler — wired through a static `[JSInvokable]`
  (`window.DotNet.invokeMethodAsync`) so a single implementation serves **both** Server and WASM (and is
  rooted for the WASM trimmer). Great for cross-tab sync (sign-out, theme, "data updated"). **Shared.**
  New `/browser/broadcast` showcase page.
- **HTML elements showcase — every one of the 111 standard elements, live.** A new "HTML elements" section
  in the samples (`/elements/text`, `/grouping`, `/sections`, `/forms`, `/tables`, `/media`, `/interactive`,
  `/metadata`) demonstrates every element with a live example and its source via `CodeSample`, grouped by
  MDN category. The document/metadata elements (html/head/body/title/base/link/meta/style/script/noscript)
  are shown via serialized output since they build the page shell.
- **Visual viewport (`IVisualViewport`, `Rask.Core.Browser`)** — read the actually-visible viewport via
  `IsSupportedAsync()` and `GetAsync()` → `VisualViewport?` (visible width/height, offsets, page offsets,
  pinch-zoom scale), e.g. to keep an input above the on-screen keyboard or react to zoom. Distinct from
  `IScreenInfo` (the physical display). **Shared** — works on both Server and WASM. New
  `/browser/visual-viewport` showcase page.
- **Storage estimate (`IStorageEstimator`, `Rask.Core.Browser`)** — read the origin's storage budget:
  `IsSupportedAsync()` and `EstimateAsync()` → `StorageEstimate?` (`Quota`/`Usage` bytes plus a computed
  `UsageRatio`), e.g. to budget an offline cache or warn before filling up. **Shared** — works on both
  Server and WASM; returns `null` where unsupported. New `/browser/storage-estimate` showcase page.
- **The full DOM `GlobalEventHandlers` surface on every element.** Beyond the previous click/scroll/
  keyboard/drag subset, every element now exposes the complete event set as typed sync `OnX` + async
  `OnXAsync` callback pairs: mouse (`OnMouseDown/Up/Move/Enter/Leave/Over/Out`, `OnDoubleClick`,
  `OnContextMenu`), `OnWheel`, pointer (`OnPointerDown/Up/Move/Enter/Leave/Over/Out/Cancel`), touch
  (`OnTouchStart/End/Move/Cancel`), focus (`OnFocus/OnBlur/OnFocusIn/OnFocusOut`), clipboard
  (`OnCopy/OnCut/OnPaste`), the remaining drag events (`OnDrag/OnDragEnter/OnDragLeave`), and
  `OnBeforeInput/OnSelect/OnInvalid/OnReset`. Each carries a typed payload — `MouseEventArgs`,
  `WheelEventArgs`, `PointerEventArgs`, `TouchEventArgs`, `ClipboardEventArgs` — parsed from the client
  event. `Audio`/`Video` additionally expose the `HTMLMediaElement` events (`OnPlay`, `OnPause`,
  `OnTimeUpdate`, `OnEnded`, `OnVolumeChange`, …) with a typed `MediaEventArgs`. Wired by one
  capture-phase delegated listener per event in a shared client module (`rask-events.js`, spliced into
  both the Server and WASM runtimes), and `OnClick`/`OnScroll` (previously tag-local) and the keyboard/
  drag handlers are now unified through one event store on `Element`. Handlers take a bare lambda or
  method group (`OnMouseMove: e => { _x = e.OffsetX; }`, `OnKeyDown: OnKey`) — the named parameter gives
  the lambda its type, so no `new Callback<T>(…)` wrapper is ever needed.
- **`RASK027` analyzer — both the sync and async handler set for one event.** Errors when a factory call
  wires both `OnX` and `OnXAsync` for the same event (e.g. `Button(OnClick: …, OnClickAsync: …)`); only
  one handler runs (sync wins), so supplying both is almost always a mistake. Passing `null` for the
  sibling is allowed. See [docs/diagnostics.md](docs/diagnostics.md#rask027).
- **Screen / display info (`IScreenInfo`, `Rask.Core.Browser`)** — read the display via `GetAsync()` →
  `ScreenInfo` (`Width`/`Height`, `AvailWidth`/`AvailHeight`, `ColorDepth`, `PixelRatio`), e.g. to pick
  retina image resolution or for analytics. **Shared** — works on both Server and WASM. New
  `/browser/screen` showcase page.
- **Speech synthesis / text-to-speech (`ISpeechSynthesis`, `Rask.Core.Browser`)** — speak text aloud from
  C# (the SpeechSynthesis API): `IsSupportedAsync()`, `SpeakAsync(text, SpeechOptions?)` (optional `Lang`,
  `Rate`, `Pitch`, `Volume`), `CancelAsync()`. For accessibility or audible notifications. **Shared** —
  works on both Server and WASM. New `/browser/speech` showcase page.
- **Media queries (`IMediaQuery`, `Rask.Core.Browser`)** — evaluate CSS media queries from C# (the
  `matchMedia` API): `MatchesAsync(query)` plus `PrefersDarkAsync()` / `PrefersReducedMotionAsync()`
  conveniences. Branch component logic on viewport size or user preferences the way CSS branches styles.
  **Shared** — works on both Server and WASM. New `/browser/media-query` showcase page.
- **Network Information API (`INetworkInfo`, `Rask.Core.Browser`)** — read the connection quality to adapt
  loading: `IsSupportedAsync()` and `GetStatusAsync()` → `NetworkStatus?` (`EffectiveType` (`slow-2g`…`4g`),
  `Downlink` Mbps, `Rtt` ms, `SaveData`). **Shared** — works on both Server and WASM; returns `null` where
  the API is unsupported (Firefox/Safari). Pairs with `INavigatorInfo.OnLineAsync()`. New
  `/browser/network` showcase page.
- **Bootstrap Toast showcase example** (`samples/Rask.Example.Shared`, `/toast`) — a reusable `Toast`
  component plus a live demo that shows, stacks, dismisses, places and auto-hides toasts driven entirely
  by Rask state: no `bootstrap.bundle.js`, no `data-bs-dismiss`, no `setTimeout`. Auto-hide is a one-shot
  `System.Threading.Timer` started in `OnMount` and disposed in `OnUnmount`; the close button fires an
  `OnClose(Id)` callback bound as a host method group, so the framework re-renders the owning host.
- **Fullscreen API (`IFullscreen`, `Rask.Wasm.Browser`)** — present an element or the whole page
  fullscreen from C#: `IsSupportedAsync`, `IsActiveAsync`, `RequestAsync(ElementRef? element = null)`
  (pass an `ElementRef` to fullscreen just that element, or omit it for the page), `ExitAsync`. WASM-only —
  `requestFullscreen` needs transient user activation (like `IShare`). Pairs with `IScreenOrientation`:
  request fullscreen first, then `LockAsync` (most browsers only allow the orientation lock in fullscreen).
  New `/fullscreen` showcase page.
- **Two-way bindings now re-render derived UI automatically — even outside the `Form`.** A bound write
  (`Bind` / `() => model.X`) re-renders the component that *authored* the binding, so a readout or summary
  the consumer renders as a sibling of the control (or the `Form`) updates live with **no
  `StateHasChanged`** and **no `AfterBind` hook**. The framework records the binding's authoring component
  (the bind expression's closure root) on the `EditContext` and re-renders it on `NotifyFieldChanged` — the
  bound-mode counterpart of the controlled-`OnChange` consumer re-render. Custom `IFormControl<T>` controls
  get this for free.
- **`RASK026` analyzer — redundant `StateHasChanged` in a Rask callback.** Warns when you call your own
  `StateHasChanged()`/`StateHasChangedAsync()` from inside a generated-factory event/binding callback
  (`OnChange`/`OnClick`/`OnInput`/`OnSubmit`/`AfterBind`/…); Rask already re-renders the callback's owner
  after it runs (the tell-tale anti-pattern is `AfterBind: _ => StateHasChanged()`). Self-calls only;
  lifecycle hooks, async loops, `feed.Updated += StateHasChanged`, and calls on a *different* component are
  left alone. See [docs/diagnostics.md](docs/diagnostics.md#rask026).
- **Form controls showcase page (`/form-controls`).** Every control — `Select`, `Input`, `Textarea`,
  `RadioGroup`, `CheckboxGroup`, `MultiSelect` — shown in both shapes side by side (controlled `Value +
  OnChange` and two-way `Bind`), each with a live readout that updates on every change with zero
  `StateHasChanged` in the demo source.
- **Native-feel PWA capabilities (WASM-only, `Rask.Wasm.Browser`)** — three typed wrappers that round out
  the installed-app experience, all injected through the constructor:
  - **`IBadge`** — set/clear a count on the installed app's icon (the Badging API): `IsSupportedAsync`,
    `SetAsync(int? count = null)` (no count = a plain dot), `ClearAsync`. A silent no-op in a normal tab.
  - **`IWakeLock`** — keep the screen awake (the Screen Wake Lock API): `RequestAsync()` returns an
    `IWakeLockSentinel` (`IAsyncDisposable`); dispose to release. Held locks are re-acquired when the
    page returns to the foreground (browsers auto-release them when it's hidden).
  - **`IScreenOrientation`** — read the orientation (`GetAsync()` → `OrientationInfo`) and lock/unlock it
    (`LockAsync(OrientationLock)` / `UnlockAsync`; locking usually requires fullscreen).
  The WASM showcase gains `/wake-lock` and `/orientation` pages, and the `/pwa` page now sets an app badge.
  These WASM-only demos show their source via `CodeSample` like every other example — `EmbeddedSource` now
  resolves embedded demo sources across registered assemblies, so demos that must live in the WASM app
  assembly (they reference `Rask.Wasm.Browser`) are covered too.
- **Local notifications (`INotifications`, `Rask.Wasm.Browser`)** — show a notification from the running
  page (no server/push): `IsSupportedAsync` / `PermissionAsync` / `RequestPermissionAsync` /
  `ShowAsync(title, NotificationOptions?)`. WASM-only (`requestPermission` needs a live user gesture). For
  notifications while the app is closed, use `IWebPush` (delivered via the service worker).
- **Typed Web App Manifest (`WebAppManifest`)** — configure the PWA manifest in C# via
  `WasmHostBuilder.UseManifest(new WebAppManifest { … })`; the framework injects the
  `<link rel="manifest">` (a `data:` URL with sub-path-correct absolute URLs) and
  `<meta name="theme-color">` at boot — no `manifest.webmanifest` to hand-write. `ToJson()` is
  exposed for hosts that prefer to serve a physical file. The `--pwa` templates and the WASM showcase
  now use it.
- **📱 PWA / mobile support (WASM)** — build installable, offline mobile apps in C#:
  - **`IWebPush`** (`Rask.Wasm.Browser`): Web Push — `IsSupported`/`RequestPermission`/
    `RegisterServiceWorker`/`Subscribe`/`GetSubscription`/`Unsubscribe`, returning a typed
    `PushSubscription` to hand to your backend. (Server-side *sending* — VAPID/RFC 8291 — is out of scope.)
  - **Default service worker `rask-sw.js`** shipped by `Rask.Wasm`: offline app-shell runtime cache
    (navigations fall back to the cached shell) **plus** push display / notification-click handling.
  - **`--pwa` option** on the `rask-wasm` and `rask-wasm-hosted` templates: scaffolds a web app
    manifest + icon and registers the service worker, so `dotnet new rask-wasm --pwa` is installable
    and offline out of the box.
  - The **WASM showcase** (`samples/Rask.Example.Wasm`, deployed to GitHub Pages) is now an
    installable, offline PWA.
  - New [Mobile & PWA guide](docs/pwa.md).
- **More typed browser APIs** (building on the typed browser-API foundation), all shared across Server
  and WASM and injected through the constructor: `ICookies` (`document.cookie` with typed
  `CookieOptions`), `IPermissions` (`QueryAsync` → `PermissionState`), `IVibration`
  (`navigator.vibrate`), and `IPageVisibility` (`document.visibilityState`). The showcase's browser-API
  examples are now their own **Browser APIs** section — one page per wrapper (`/browser/storage`,
  `/browser/cookies`, `/browser/clipboard`, `/browser/geolocation`, `/browser/permissions`,
  `/browser/vibration`, `/browser/visibility`, `/browser/navigator-info`).
- **WASM-only typed browser APIs now live in `Rask.Wasm.Browser`** (shared APIs stay in
  `Rask.Core.Browser`). First entry: `IShare` (Web Share — `ShareAsync`/`CanShareAsync`), which needs
  transient user activation and so can't work across the Server WebSocket round-trip. This namespace is
  the home for upcoming PWA-only APIs.

### Changed
- **HTML tag components now mirror the DOM interface hierarchy.** Tags that share a DOM interface derive
  from a shared abstract base instead of each redeclaring the same attributes: `HtmlMediaElement`
  (`Audio`/`Video`), `HtmlTableCellElement` (`Td`/`Th`), `HtmlModElement` (`Ins`/`Del`),
  `HtmlTableColElement` (`Col`/`Colgroup`), `HtmlQuoteElement` (`Q`/`Blockquote`), plus the structural
  `HtmlHeadingElement` (`H1`–`H6`) and `HtmlTableSectionElement` (`Thead`/`Tbody`/`Tfoot`). The factory
  call shape is unchanged for every tag except that **`Video` and `Th` positional factory argument order
  changed** (inherited attributes now sort after the tag's own) and `Video`'s tag-specific attributes emit
  after the shared media block — use named arguments (`Video(Src: …, Poster: …)`). No runtime behavior
  change for any other tag.
- **`Rask.Core.Browser` now holds only transport-shared browser APIs.** APIs that can't function on the
  Server transport (currently `IShare`) moved to `Rask.Wasm.Browser`, registered by the WASM host only.
- **Typed browser-API foundation** — strongly-typed, DI-injected C# wrappers over the Web APIs that
  previously needed raw `IJSRuntime` string identifiers, identical on Server and WASM: `IBrowserStorage`
  (`localStorage`/`sessionStorage`), `IClipboard`, `IGeolocation` (`GetCurrentPositionAsync` →
  `GeolocationPosition`), and `INavigatorInfo` (`OnLine`/`Language`/`UserAgent`). Inject the interface
  through a component constructor and await typed methods. Shared framework JS interop helpers
  (`__raskEl`, `__raskApi`) are now a single source of truth in `Rask.Core/Resources/rask-api.js`,
  spliced into both client runtimes at build time so the transports never drift. New `/browser`
  showcase page. First step toward PWA support (WASM-only service-worker/cache/manifest APIs follow on
  the same pattern).

### Fixed
- **Controlled `Select`/`Input`/`Textarea` `OnChange` now re-renders the consumer.** A controlled-mode
  form control (`OnChange`/`OnChangeAsync` with parent-owned state, no `Bind`) wraps the typed callback
  in its own DOM handler to parse the raw string → `T`. That handler's target is the control, so the
  post-dispatch dirty-mark landed on the control instead of the component whose state `OnChange` mutates —
  the consumer's view (e.g. the showcase "Select — onChange" picked-value text) never updated. The shared
  `ControlledChangeHandler` bridge now notifies the callback's owning consumer after invoking it.
- **Live diff now updates adjacent text nodes correctly.** Two bare strings rendered side by side
  (e.g. `Button()[ "Toggle ?tab=", value ]`) were emitted as two render frames, but the browser
  coalesces adjacent text into a single DOM node — so the diff's per-frame slot walk drifted past the
  real child nodes and the `UpdateText` patch was silently dropped (the text never changed; surfaced on
  the showcase "Switch user" toggle button after a `Navigator.SetQuery`). Adjacent text frames are now
  coalesced at emission to match the DOM, including across transparent `Fragment`/`Context` boundaries.
- **Empty text children no longer drift sibling positions.** An empty/`null` string child
  (`Div()[Span(), "", Span()]`) produces no HTML and no DOM node, but still emitted a text frame — so
  every following sibling's diff path was off by one. Empty text frames are now skipped.
- **`Raw` adjacent to other siblings falls back to a full morph.** A `Raw`'s verbatim markup can parse
  into zero, one, or many DOM nodes, so a sibling rendered after a non-sole-child `Raw` could be patched
  at the wrong index by an ungated `UpdateText`/`SetAttribute`. When a changed sibling level mixes a
  `Raw` with other nodes, the render now routes to the full-HTML morph (which reparses the markup
  correctly) instead of shipping a mis-targeted positional op. A solitary `Raw` is unaffected.

### Changed
- **`CheckboxGroup`/`RadioGroup` rewritten as Components** (the `MultiSelect<TItem>` template) instead of
  static `Fragment` factories. Each now supports **bound** (`() => model.X` with a per-field `Validate`
  rule + `AfterBind` hooks) and **controlled** (`Value` + `OnChange`/`OnChangeAsync`) modes, and their
  factories are generator-emitted (Bind-first validator fan-out for bound mode). **Note:** as Components
  they are their own re-render boundary, so host-side derived UI updates via the auto-wrapped controlled
  `OnChange` (the showcase group demos moved to controlled mode) rather than the free host re-render the
  old `Fragment` form gave.
- **Generator-driven validator fan-out for bound form controls.** `[GenerateForwarderFactory]` gained a
  `Validator` parameter: naming a `Delegate?` parameter makes the generator emit the none/sync/async
  `Validate<T>`/`ValidateAsync<T>` overloads around a single hand-written core. `Input`/`Select`/`Textarea`
  now declare one `Bound` core each instead of three near-identical overloads + a private `BoundCore` — the
  generator emits the cast-free `Validate` overloads. The generated `Input(…)`/`Select(…)`/`Textarea(…)`
  factory surface is unchanged (no consumer impact). The forwarder fan-out also supports **generic
  components**: it carries the component's type parameters and derives the validator type `T` from the
  `Expression<Func<T>>` Bind parameter, so the sample `MultiSelect<TItem>` declares one `Bound` core (its
  `Validate` over `ICollection<TItem>`) and the hand-written `MultiSelectBoundFactory` is removed.

### Changed
- **`Input`'s `Type` is now a strict `InputType` enum instead of a free string.** `InputType` (in
  `Rask.Core`) is the closed set of HTML input types (`Text`/`Search`/`Tel`/`Url`/`Email`/`Password`/
  `Number`/`Checkbox`/`Radio`/`File`/`Date`/`DatetimeLocal`/`Time`/…), so the type is validated at compile
  time; `Input<T>` still derives the type from `T` when `Type` is unset. New analyzer **RASK025** warns when
  a string-only `InputType` (text family) is set on a non-`string` `Input<T>` (the type would never
  round-trip). **Breaking:** `Type:` takes an `InputType` — `Input<string>(InputType.Text, …)`,
  `Input(() => m.Email, Type: InputType.Email)` — instead of a string literal.
- **Built-in `Input`/`Select`/`Textarea` are now generic `Input<T>`/`Select<T>`/`Textarea<T>` implementing
  `IFormControl<T>`.** Bound usage infers `T` from the expression (`Input(() => model.Age)` → `Input<int>` →
  `<input type="number">` — the HTML input `type` derives from `T`: `bool`→checkbox, numeric→number,
  `DateOnly`→date, etc.); binding is resolved at render time rather than in a `Bound` factory. **Breaking:**
  plain (non-bound) usage now needs the explicit type argument — `Input<string>("text", …)`,
  `Select<string>(…)`, `Textarea<string>(…)` — and bound `Select` takes its options via the `[...]` indexer
  rather than a `Children:` argument. `IFormControl<T>` also gained default-method helpers that remove
  per-control boilerplate (`Validator`, `RegisterValidator`, `InvokeAfterBindAsync`, `InvokeOnChangeAsync`,
  `ControlledChangeHandler` — the string↔`T` bridge for controlled mode), adopted by the built-ins and the
  sample `MultiSelect`/`CheckboxGroup`/`RadioGroup`.

### Added
- **`IFormControl<T>` — a declarative contract for building custom form controls.** A generic component
  implementing `IFormControl<T>` (in `Rask.Core.Forms`) gets both of its factories synthesized by the
  generator: a **controlled** factory (`Value` + `OnChange`/`OnChangeAsync`) and a **bound** factory
  (`() => model.Field`, Bind-first, with the per-field validator fanned into none/sync/async overloads).
  The interface carries the typed `Bind`/`Validate`/`ValidateAsync`/`AfterBind`/`AfterBindAsync` (bound) and
  `Value`/`OnChange`/`OnChangeAsync` (controlled) members over a single value type `T`; the generator
  excludes the bound members from the controlled factory automatically (no `[SkipFactory]` needed). The
  sample `MultiSelect<TItem>`/`CheckboxGroup<TItem>`/`RadioGroup<TValue>` now implement it and drop their
  hand-written `Bound` methods. (Controlled `OnChange` for the collection controls now delivers
  `ICollection<TItem>` rather than `IReadOnlyCollection<TItem>`, matching `Value`.)
- **`Callback` / `Callback<T>` / `CallbackAsync` / `CallbackAsync<T>` delegate types.** Named delegate
  types in `Rask.Core` are now the shape of every built-in component's event callbacks (the `X` / `XAsync`
  pairs: `OnClick`/`OnClickAsync`, `OnInput`/`OnChange`/`…Async`, `OnKeyDown`/`OnKeyUp`, `OnDrag*`,
  `OnScroll`, `OnSubmit`, `OnFiles`, and `Form`'s `OnValidSubmit`/`OnInvalidSubmit`), replacing the inline
  `Action`/`Func<…,Task>` spellings — paired with the `Validate`/`ValidateAsync` convention. `AutoCallback`
  and the live dispatcher (`Component.TryInvokeHandlerAsync`) carry typed overloads/cases for them, so the
  re-render and DOM-dispatch hot paths stay reflection-free. **Standard `Action`/`Func` handlers remain
  supported** for consumer code — these are additive. (`CallbackAsync`, not `AsyncCallback`, to avoid the
  `System.AsyncCallback` clash.) **Minor breaking:** code passing a pre-typed `Action`/`Func` *variable* to
  a built-in handler must switch the variable to the matching `Callback`/`CallbackAsync` type (inline
  lambdas and method groups are unaffected).
- **`Validate<T>` / `ValidateAsync<T>` delegate types.** Named, shared validator delegate types in
  `Rask.Core.Forms` replace the verbose inline `Func<T, IEnumerable<string>>` /
  `Func<T, CancellationToken, ValueTask<IEnumerable<string>>>` as the `Validate` parameter shape on every
  form control (`Input`/`Select`/`Textarea`/`Form`/`MultiSelect`). **Changed (minor breaking):** the
  `Validate` parameter type changes accordingly — lambda call sites are unaffected; a caller passing a
  pre-typed `Func<…>` variable must adjust.
- **Public binding API for custom form controls.** `ExpressionAccessor` (+ its `Accessor` record) and
  `BindingHelpers` (`ResolveBindingContext`, `FormatValue`) in `Rask.Core.Forms` are now public — the
  same machinery the sample `RadioGroup`/`CheckboxGroup`/`MultiSelect` use, so consumers can build their
  own controls that bind to a model property and drive the ambient `EditContext`. `docs/forms.md` §9 is a
  "building form components" guide. See also `EditContext.RegisterFieldValidator` for per-field rules.
- **`BindingHelpers.SetCollectionMembership<T>` and `NotifyAndValidateFieldAsync`.** Two more public
  building blocks for custom bound controls: the first adds/removes an item in a bound `ICollection<T>`
  by a comparer (returns whether it changed); the second commits a field change (marks it changed +
  touched and re-validates, no-op without a context). The sample `MultiSelect`/`CheckboxGroup`/`RadioGroup`
  now share these instead of each hand-rolling the add/remove + notify/validate logic.
- **Multi-select showcase.** A reusable generic `MultiSelect<TItem>` example component
  (`samples/Rask.Example.Shared/Shared/MultiSelect.cs`) — a custom Bootstrap dropdown with removable
  chips, open/close + Esc / click-outside close driven entirely by the server live diff (no client JS).
  Supports two shapes: **bound** (`() => model.Items` with `AfterBind`/`AfterBindAsync` post-bind hooks
  and a per-field `Validate` rule, sync or async) and **controlled** (`Value` + `OnChange`/`OnChangeAsync`,
  no `Bind`). Surfaced on the `/multiselect` page with both bound and controlled demos.
- **Floating-label form controls showcase (samples only).** Reusable `FloatingInput<TProp>`,
  `FloatingSelect<TProp>`, and `FloatingTextarea<TProp>` example components
  (`samples/Rask.Example.Shared/Shared/`) wrap Rask's `Input`/`Select`/`Textarea` + `Label` +
  `ValidationMessage` in Bootstrap 5.3's `.form-floating` markup — one line per field, with the id
  derived from the bound property, the label read from its `[Display(Name)]` attribute, and the
  input type inferred from `TProp`. They own no validation state and need no extra CSS (errors show
  via Bootstrap's `.invalid-feedback .d-block`). Surfaced on a new `/floating-labels` page under the
  Forms nav group.

### Changed
- **Moved `CheckboxGroup<TItem>` and `RadioGroup<TValue>` out of `Rask.Core` into the samples**
  (`samples/Rask.Example.Shared/Shared/`), where they join `MultiSelect<TItem>` as copyable example
  controls built on the public binding API — keeping `Rask.Core` minimal (the framework ships the
  binding API, not a control library). They now render Bootstrap 5.3
  [check markup](https://getbootstrap.com/docs/5.3/forms/checks-radios/) (`form-check` wrapper +
  `form-check-input`/`form-check-label` with `id`/`for`); `ItemClass` now adds extra wrapper classes
  (e.g. `form-check-inline`). **Breaking:** they are no longer part of `Rask.Core` — copy the sample
  files into your project, or build equivalents on `ExpressionAccessor`/`BindingHelpers` (see
  `docs/forms.md` §9).

### Removed
- **Dropped the `TableModel<T>` headless-table primitive and the `Rask.Core.Tables` namespace**
  (`ColumnDef<T>`, `ColumnSort`, `SortDirection`, `HeaderCell`, `TableRow<T>`, `TableModelContext<T>`)
  from the framework, keeping `Rask.Core` minimal. The `/table` and `/master-detail` showcase pages
  now render a plain `Table` directly — they already owned all the sort/page/expand state, so the
  controlled-projection layer added no capability over the standard HTML table components. **Breaking:**
  consumers using `TableModel<T>` should render `Table`/`Thead`/`Tbody`/`Tr`/`Td` themselves (see
  `samples/Rask.Example.Shared/Features/Table/TablePage.cs` for the pattern).

### Security
- **`CSS.escape` the ElementRef reviver selector (both runtimes).** The client reviver that
  resolves an `{"__raskRef__":"id"}` placeholder to a live DOM element now escapes the id via
  `CSS.escape(...)` before building the `[data-rask-ref="…"]` selector, in `rask.js` (Server) and
  `rask.wasm.js` (WASM). Defense-in-depth: ids are framework-minted, but escaping closes any path
  by which a value carrying a quote/bracket could break out of the attribute selector. The reconnect
  overlay is now built with `document.createElement`/`textContent` instead of `innerHTML`, removing
  the only `innerHTML` write on a non-`<template>` path (cleaner under a strict CSP).

### Changed
- **Modernized the client runtime JavaScript.** `rask.js` (Server), the shared diff/morph codec
  (`rask-dom.js`/`rask-morph.js`), the WASM runtime (`rask.wasm.js`) and `main.js` were brought to
  modern JS (`const`/`let`, arrow callbacks, `for…of`, template literals, optional chaining, rest
  params). Behavior is unchanged; the shared spliced helpers keep hoisted `function` declarations
  and emit no `export`/`import` so they remain valid in both the Server's classic-`<script>` IIFE
  and the WASM ES module. Release-build minification stays correct (single-line template literals).
- **Beginner-friendly getting-started rewrite ([docs/getting-started.md](docs/getting-started.md)).**
  Restructured for a developer new to Rask into a "run → understand → extend" path: promoted
  prerequisites, a recommended default template, a *"what you should see"* checkpoint, a new tour of the
  scaffolded files, and a troubleshooting section. Security/edge-case detail (string encoding, URL
  sanitization, the `RenderResult` shapes) moved into clearly-labelled asides instead of interrupting
  the main flow. Surfaced prerequisites up front in `README.md` and `NUGET.md`, and pointed the
  `docs/README.md` index at the guide as the starting point.

### Added
- **Best practices guide ([docs/best-practices.md](docs/best-practices.md)).** A new hub that
  consolidates the patterns and common pitfalls previously scattered across the subsystem docs and
  the RASK diagnostics — component design, rendering/keys, state & callbacks, context/DI, forms, data
  access, security, accessibility, performance, and testing — with each rule linking to its deep
  dive. Indexed from `docs/README.md`, `README.md`, and `llms.txt`.

## [0.10.0] - 2026-06-23

### Added
- **Cooperative handler timeout (`RaskServerOptions.HandlerTimeout`).** `Component.CancellationToken`
  now does double duty: it still cancels on unmount, and *while an event handler is running* it **also**
  cancels when the host cancels that dispatch — the server's new `HandlerTimeout` elapsing, or the socket
  closing. Thread it into the cancellable async work a handler starts (`HttpClient`, `Task.Delay`) and a
  slow handler unwinds cleanly instead of pinning the session's render pipeline; the timeout is logged
  and counted on `rask.handlers.timedout`. Handlers that already pass `CancellationToken` to their async
  calls gain this for free once an operator sets the timeout. It is cooperative — a handler that ignores
  the token can't be force-aborted (a .NET reality; the backpressure and idle-socket caps remain the
  backstop). Opt-in: `HandlerTimeout` defaults to `TimeSpan.Zero` (off), validated at startup like the
  other server limits. See the [configuration](docs/configuration.md) and
  [composition](docs/composition.md) guides.
- **Resource-exhaustion hardening for the server host (all opt-in, default off).** Three new bounds,
  configurable via `RaskServerOptions` / `RaskUploadOptions`: (1) **`IdleSocketTimeout`** closes a
  connected WebSocket that sends no inbound frame for the window — reclaiming silently-idle sockets
  that would otherwise hold a receive loop open indefinitely; the session survives for reconnect under
  the grace period. (2) **`MaxPendingHandlerBytes`** bounds the *aggregate bytes* of queued handler
  payloads (the memory companion to `MaxPendingHandlers`, which bounds only the queue length), so a
  client can't fill the count-bounded queue with large frames and pin gigabytes of cloned payloads;
  tracked on the live session and tripped before cloning. (3) **`RaskUploadOptions.MaxBytesPerSession`**
  caps the cumulative staged-upload bytes a single session may hold at once — an authenticated client
  can no longer accumulate unbounded temp-file storage across requests (a request over the quota is
  rejected with `413`; staged bytes are freed when the session ends). Each new limit is validated at
  startup and defaults to off, so upgrading is a no-op. See the [configuration guide](docs/configuration.md).
- **The WebSocket and session-lifecycle safety limits are now configurable** through a new
  server-host-only options object, `RaskServerOptions`, set via a second `AddRask` callback
  (`configureServer`): the inbound frame-size cap (`MaxInboundFrameBytes`), the handler-backpressure
  cap (`MaxPendingHandlers`), the per-connection inbound rate cap (`MaxInboundFramesPerSecond`), and the
  reconnect / unconnected session grace periods (`SessionGracePeriod` / `UnconnectedSessionGracePeriod`).
  These are server-only (the WASM runtime has no socket server), so they live in `Rask.Server` rather
  than the shared `RaskLiveOptions`. They were previously hardcoded; **all defaults are unchanged**, so
  upgrading is a no-op until you set one. Bind from configuration with
  `AddRask(configureServer: o => builder.Configuration.GetSection("Rask").Bind(o))`; `AddRask`
  validates the values and throws `ArgumentOutOfRangeException` at startup on an out-of-range one (a
  negative grace period, a non-positive frame-size cap), so a misconfiguration fails the boot rather
  than misbehaving at runtime. See the new [configuration guide](docs/configuration.md).
- **Production observability for the server host (OpenTelemetry-aligned).** The Rask server is now
  instrumented with standard .NET primitives — no extra packages, exportable to OpenTelemetry out of
  the box. (1) **Structured logging:** `UseRask<TApp>()` bridges the framework's internal diagnostics
  seam to your `ILogger` pipeline, so every framework fault (a lifecycle hook that threw with no
  ancestor `ErrorBoundary`, a duplicate sibling `Key`, a malformed WS frame, a handler that threw) is
  logged under a stable category (`Rask.Live`, `Rask.Diff`, `Rask.Lifecycle`, …) at the matching level
  with the original exception — replacing the previous scattered `Console.Error` writes. (2)
  **Metrics:** a `Meter` named `Rask.Server` (`RaskTelemetry.MeterName`) exposes session counters
  (`rask.sessions.created` / `.rejected` / `.evicted`), an active-sessions gauge, handler counters
  (`rask.handlers.dispatched` / `.faulted`) and duration histogram, and a `rask.ws.frames.rejected`
  counter tagged by `reason` (`size` / `rate` / `backlog`) — the headline DoS-visibility signal. Read
  with `dotnet-counters --counters Rask.Server` or `AddMeter(RaskTelemetry.MeterName)`. (3) **Tracing:**
  an `ActivitySource` named `Rask.Server` spans each handler dispatch (`rask.handler.dispatch`),
  zero-cost when no listener is attached. (4) **Health checks:** `services.AddHealthChecks()`
  `.AddRaskLiveSessions()` reports live-session capacity — `Healthy` / `Degraded` (≥80% of
  `MaxSessions`) / `Unhealthy` (at the cap). All of it is on by default with no configuration; you opt
  in only to *exporting* it. The render/diff/serialization hot path is untouched; the per-dispatch
  instrumentation (a counter increment, a `Stopwatch` timing pair, a histogram record, and a tracing
  `Activity` that is `null` when no tracer is attached) is allocation-free. See the new
  [observability guide](docs/observability.md).
- **Keyboard events on every element.** `Element` now exposes the `OnKeyDown` / `OnKeyDownAsync` and
  `OnKeyUp` / `OnKeyUpAsync` pairs, the focus-scoped counterpart to `OnClick`, wired by both client
  runtimes (Server WS + WASM) via `data-rask-on-keydown` / `data-rask-on-keyup`. A handler takes
  `Action<KeyboardEventArgs>` (or the async sibling `Func<KeyboardEventArgs, Task>`); the new
  `KeyboardEventArgs` record carries `Key`, `Code`, the `Shift`/`Ctrl`/`Alt`/`Meta` modifiers, and
  `Repeat`. The runtime never `preventDefault`s a key event, so handlers compose with normal typing,
  and the handler storage is hoisted into the lazy `LiveState` so an element with no key handler
  keeps its zero per-instance footprint. The `/todos` sample now closes its dialog on **Escape**
  (focusing the `<dialog>` on open via an `ElementRef`). See the [composition guide](docs/composition.md).
- **A typed async variant for every DOM event handler.** Every event handler now follows the
  `OnClick` / `OnClickAsync` convention — a synchronous `OnXxx` plus an asynchronous `OnXxxAsync`
  (`Func<…, Task>`). This adds `OnScrollAsync` (`Div`) and `OnDragStartAsync` / `OnDragOverAsync` /
  `OnDropAsync` / `OnDragEndAsync` (every `Element`), and gives the keyboard pairs their async
  siblings, replacing the previous untyped single `Delegate?` slots on the drag/keyboard/scroll
  events with discoverable, type-checked pairs. The sync and async siblings coalesce over a single
  backing slot per event (distinguished by delegate type), so the richer surface adds **no
  per-element instance footprint**. The `DragDropContext.Drop(...)` helper is now async-shaped and
  wires to `OnDropAsync`.
- **Accessibility primitives on every element.** A new `Aria` parameter — a `string → string?`
  dictionary modelled on the existing `Data` (`data-*`) bag — renders each entry as
  `aria-{key}="{value}"`, so the full WAI-ARIA vocabulary is reachable without a typed property per
  attribute (`Button(Aria: new() { ["label"] = "Close" })` → `aria-label="Close"`). `Role` (`string?`)
  and `TabIndex` (`int?`) are typed parameters for the two non-`aria-*` affordances. The universal
  attribute order is now **id, class, style, data-\*, role, tabindex, aria-\*, then tag-specific**. A
  new **RASK023** analyzer warns when an `Img` is created without `Alt` (pass `Alt: ""` for decorative
  images). Demonstrated on the `/props` showcase page. See the new [accessibility guide](docs/accessibility.md).
- **`TableModel<T>` — a headless, fully *controlled* table primitive** (in the spirit of TanStack
  Table). It renders no markup of its own and owns no state: the host sorts, filters, and pages its
  own data and hands the model the final `Rows` plus the current view state (`Sort`, `PageIndex`,
  `SelectedKeys`, …). The model projects sort-aware `Headers` and selection-aware `Rows` into the
  `Render` delegate and raises `OnSort` / `OnPage` / `OnSelect` **intents**; the host applies them to
  its own state and re-renders. Supporting types live in the new `Rask.Core.Tables` namespace
  (`ColumnDef<T>`, `ColumnSort`, `SortDirection`, `HeaderCell`, `TableRow<T>`, `TableModelContext<T>`).
  The `/table` showcase page now drives its sorting and paging through `TableModel<T>` from the URL
  query string.
- **Master-detail showcase page (`/master-detail`)** — a datagrid with collapsible rows whose expanded
  panel hosts a second, independently sortable `TableModel<T>`. Expand and both grids' sort are held in
  plain component fields; each open row inserts a keyed detail `<tr>`, so the live diff reconciles
  expand/collapse as an in-place keyed insert/remove and sibling open rows keep their own inner sort.
- **Duplicate-`Key` warning in the live diff.** Two sibling elements sharing the same `Key:`
  (`data-rask-key`) silently disabled keyed reconciliation and fell back to a positional diff, which
  can graft a row's DOM state (focus, input value, scroll position) onto the wrong sibling when the
  list reorders — a hard-to-spot correctness bug. The diff codec now emits a one-time warning naming
  the offending key to standard error when it detects a duplicate (deduplicated, capped, and only ever
  reached on the already-broken path, so a correctly-keyed render pays nothing). See the
  [composition guide](docs/composition.md).

### Changed
- **`Navigator.Navigate(...)` renamed to `Navigator.NavigateTo(...)`.** All three overloads
  (`RouteUrl`, `string` path, and `string` path + query) now read as `nav.NavigateTo(...)` at the
  call site, matching the `NavigateTo` convention. This is a breaking API change with no compatibility
  shim — update call sites accordingly. The `SetQuery`/`RemoveQuery`/`ClearQuery`/`Download` members
  are unchanged.
- **RASK002 no longer fires when a parameterless constructor is available.** The "`required`
  property is incompatible with a DI constructor" warning now only triggers when the component's
  *only* constructor takes dependency-injected parameters (no parameterless ctor). With a
  parameterless ctor present, the generated factory uses the object-initializer path — which *does*
  honour `required` — so the previous warning was a false positive in that case. The diagnostic
  message and `docs/diagnostics.md` now spell out the parameterless-ctor escape hatch, and the
  `Sparkline`/`LiveTicker` samples mark their non-nullable factory parameters `required` (dropping a
  `CS8618` suppression each) to demonstrate it.
- **The `/drag-drop` showcase now splits its two demos into separate `CodeSample` cards.** The
  sortable list and the Kanban board were extracted from a single 178-line `DragDropDemo` into
  `DragDropSortableDemo` and `DragDropKanbanDemo` (each with its own scoped CSS), so the page shows
  one focused source panel per use case — matching the multi-demo layout already used by
  `/virtualize` and `/binding`. Same route, same behaviour.
- **The live-session render pipeline is now shared from `Rask.Core`.** A new `LiveSessionBase` owns
  the render→diff-vs-full→payload build both hosts ran near-identically (`RenderTreeToHtml`,
  `ConsumeDownload`, `WritePayload`) plus the common state (render cache, diff ops, write buffer,
  dedup baseline, the IJSRuntime queue) and the `IRenderHandle`/`ILiveJsHost` plumbing. Server's
  `LiveSession` and WASM's `WasmLiveSession` now extend it, keeping only what genuinely differs —
  their transport (WebSocket push vs in-process `ApplyRender`), locking, reconnect/dispatch
  lifecycle, and send-dedup strategy. Internal only — no API or behaviour change (both hosts'
  full E2E journeys pass unchanged); ~340 lines of duplicate pipeline collapse into one base.
- **The host JS-interop plumbing is now shared from `Rask.Core`.** The pending IJSRuntime-invoke
  queue (`LiveJsInvokeQueue`), the `BeginInvokeJS` deferral + `[JSInvokable]` result envelope
  (`RaskJSRuntimeBase`), and the after-`applyDiff` dispatch loop (`applyFrameInvokes`, in the shared
  `rask-dom.js`) were duplicated between Server (`RaskJSRuntime` / `LiveSession` / `rask.js`) and WASM
  (`WasmJSRuntime` / `WasmLiveSession` / `rask.wasm.js`); both hosts now compose the Core pieces
  through a shared `ILiveJsHost` seam. Internal only — no API change beyond the WASM ordering fix
  noted below.
- **The live-diff codec is now a single shared client source.** `applyDiff` and its helpers
  (`resolvePath`, `relevantChild`/`relevantChildSkipping`, `moveChildBefore`, `syncFormProperty`)
  were duplicated between the Server (`rask.js`) and WASM (`rask.wasm.js`) clients; they now live
  once in `src/Rask.Core/Resources/rask-dom.js`, spliced into both at build time at a new `RASK_DOM`
  marker — the same mechanism the full-HTML morph (`rask-morph.js`) already used. Internal only: no
  API or behaviour change beyond the fix noted below. Both runtimes now decode the C# `FrameDiffer`
  opcodes from one source and cannot drift.
- **Every showcase example page now shows its own source via `CodeSample`.** The five remaining
  pages without a runnable-source panel were brought into the convention: `/asset-loading` (each
  scoped-asset section is now a `CodeSample` with its component source + live result), `/jsruntime`
  (the `sessionStorage` round-trip was extracted into a `JsRuntimeDemo` shown beside its source),
  and `/master-detail`, `/table`, `/todos` (which append a `CodeSample` of the page's own source
  beneath the live demo). The `[NotFound]` page and the routing-demo sub-page are intentionally
  excluded.

### Fixed
- **Reconnect catch-up frame could be dropped on weak memory models.** The Server `LiveSession`
  socket handoff sets two flags (`_forceResend`, `_renderRequestedWhileDetached`) before publishing
  the new socket, relying on "publish the socket last" so a concurrent background render sees the
  flags before the socket. Only `_forceResend` was `volatile`; `_socket` and
  `_renderRequestedWhileDetached` were plain fields, so the intended store ordering held on x86 (TSO)
  but not on weaker models such as ARM64, where a render could observe the fresh socket yet miss the
  resend flags and skip the reconnect recovery frame. Both are now `volatile`, giving the publish its
  release/acquire semantics across all architectures.
- **A fault while failing pending JS invokes no longer masks the real send error.** When a WebSocket
  send throws with queued `IJSRuntime` invokes in flight, `LiveSession` faults those awaiting tasks
  locally before rethrowing. That cleanup is now wrapped so a throw inside it (e.g. an unavailable
  runtime) is logged rather than propagated in place of the meaningful send exception — matching the
  method's documented best-effort contract and ensuring the original error reaches the caller.
- **RASK002 now catches the two cases where a parameterless ctor does *not* rescue a `required`
  property.** The previous narrowing (only warn when no parameterless ctor exists) had two gaps.
  First, a `required` property that *also* carries a member initializer is excluded from the factory
  parameters, so the object-initializer path never assigns it — the consumer build failed with a
  cryptic `CS9035` inside generated code and no diagnostic; RASK002 now fires for this combination.
  Second, the diagnostic message, `docs/diagnostics.md`, and the `/components` showcase all
  recommended "add a parameterless constructor" as a fix — but doing so while keeping the DI
  constructor makes the factory build the component with `new C()` and silently skip the DI ctor,
  leaving injected services `null` at render time (a runtime `NullReferenceException` in place of the
  former compile-time nudge). The guidance now warns against that trap and points to dropping the DI
  constructor or moving the value to a constructor parameter instead.
- **A single malformed WebSocket frame no longer tears down the live session.** The Server receive
  loop parsed each inbound frame with an unguarded `JsonDocument.Parse`, and a valid-JSON-but-non-object
  root (a bare array/number/string) reached `TryGetProperty`, which throws on non-objects — either way
  the exception escaped the loop's `OperationCanceledException` / `WebSocketException` catches, detached
  the socket and scheduled the session for removal. One buggy or adversarial frame could drop a whole
  session. Such frames are now dropped (logged once) and the loop keeps serving; the existing 8 MB
  inbound-frame cap still bounds memory.
- **Component teardown is now strictly one-shot.** A tree mutation inside an `OnUnmount` hook (clearing
  persisted children, re-parenting) could route a node through a second dispose pass, firing `OnUnmount`
  and the user's `Dispose()` twice. `DisposeComponentTree`/`DisposeComponentTreeAsync` now guard on a
  per-component flag so the unmount → cancel → dispose sequence runs exactly once. (The lifetime
  `CancellationTokenSource` was already idempotent.)
- **The deferred-session-removal task can no longer surface an unobserved `ObjectDisposedException`.**
  A reconnect or a reschedule that disposed the pending-removal `CancellationTokenSource` while the
  delayed task was about to read `cts.Token` produced an exception the `OperationCanceledException`
  catch missed. The token is now captured before the task starts and `ObjectDisposedException` is
  handled, so an obsolete removal exits cleanly without orphaning the session.
- **WASM now runs `IJSRuntime` calls issued *during* a render after the DOM is patched, matching
  Server.** Interop from a lifecycle hook — e.g. `OnRenderedAsync` focusing a dialog as it opens —
  used to dispatch immediately on WASM, *before* that render's DOM patch, so a `focus()` could hit a
  still-`display:none` element (a `<dialog>` whose `open` attribute the diff hadn't added yet) and
  silently no-op. Such mid-render calls now ride the render frame's `jsInvokes` and the client
  dispatches them after `applyDiff`, against the committed DOM — the post-commit ordering the Server
  already had. Interop from event handlers is unchanged (WASM still dispatches it immediately, the
  path awaited handler interop relies on). The `/todos` dialog now auto-focuses on open on every
  host, so Escape-to-close works without a prior click.
- **The `/todos` showcase dialog now opens centered over a dim backdrop.** A declaratively-open
  `<dialog open>` is non-modal, so the browser left it `position:absolute` at its in-flow spot (low
  on the page, partly off-screen) with no `::backdrop`. The `TodoFormDialog` scoped CSS now pins the
  open dialog to the viewport centre (`position:fixed; inset:0; margin:auto`) above a clickable
  `.todo-backdrop` overlay — clicking the backdrop cancels, mirroring the nav-drawer pattern. No JS;
  the URL-driven open/close (New todo, Cancel, browser Back, deep links) is unchanged.
- **Server mode now executes scripts inserted through a keyed diff.** A scoped
  `<script src="/_rask/a/{hash}.js">` (or a user `Head` `<script>`) delivered via a keyed
  `InsertSubtree` diff was parsed by the Server client but never ran — `innerHTML`-parsed scripts
  carry the "already started" flag — so its `window.Rask.{Type}` global never appeared. The shared
  codec now revives inserted scripts via `reviveScript`, matching the full-HTML morph path (the WASM
  client already did this). Only the Server diff path is affected.
- **The `/virtualize` showcase table header no longer disappears while scrolling.** The off-screen
  rows were reserved by two spacer `<div>`s *outside* the table, so the table's own box was relaid
  out on every scroll frame and the sticky `<thead>` unstuck — the header vanished mid-scroll and
  snapped back when scrolling stopped. The spacers are now two keyed spacer **rows inside the
  `<tbody>`**, making the single table the scroller's only child with a constant outer height, so
  the header's containing block never resizes. Stickiness also moved onto the `<th>` cells (opaque
  background + inset `box-shadow` divider, `border-collapse:separate`). Verified in a headless
  browser: the header stays pinned to the scroller top across a 40-step rapid scroll burst. The
  page also now presents both demos through the `CodeSample` component so the runnable source is
  shown beside the live result.
- **Clean parallel builds of WASM-hosted apps no longer race the static-web-assets pipeline.** A full
  rebuild (`dotnet build --no-incremental`, or Rider's *Rebuild Solution*) could fail with `MSB4018`
  from `UpdateExternallyDefinedStaticWebAssets` — the ASP.NET host project resolved the WASM
  project's fingerprinted `dotnet.native.*` assets before that project had emitted them. A Rask WASM
  host serves the published bundle from the publish directory at runtime (`app.UseRask()`) and owns no
  static web assets of its own, so the hosts (and the `rask-wasm-hosted` template) now set
  `StaticWebAssetsEnabled=false`, which skips the racy cross-project resolution while preserving build
  ordering. Downstream hosts that serve only a Rask WASM bundle should do the same.

### Changed
- **Renamed the headless `Virtualize<T>` primitive to `VirtualizeModel<T>`** (component + factory) so
  the headless "view-model" primitives read as one family with `TableModel<T>`. **Breaking:** update
  call sites from `Virtualize<T>(…)` to `VirtualizeModel<T>(…)`; behaviour is unchanged. The
  `Rask.Core.Virtualization` support types keep their names.

### Security
- **Inbound WebSocket frame-rate cap.** The Server receive loop now closes a connection that sends
  more than 1000 frames/second (sliding one-second window). This complements the existing per-frame
  size cap (8 MB) and handler-backlog breaker (512 queued dispatches), which don't bound a flood of
  small non-handler frames (`jsResult` / `navigate` / malformed) — each of which still costs a JSON
  parse, so a high-rate stream was a CPU-DoS gap. On a trip the socket closes with a policy-violation
  status and the client reconnects against the intact session. The cap is a fixed internal default
  (far above any legitimate interaction burst); operators should still front the app with a
  reverse-proxy / WAF rate limit for connection-count and cross-connection floods. See the
  [hardening reference](docs/authentication.md#hardening-reference).
- **Suppressed the unactionable `SQLitePCLRaw.lib.e_sqlite3` audit advisory (`GHSA-2m69-gcr7-jv3q`).**
  The native SQLite package arrives transitively through the latest `Microsoft.EntityFrameworkCore.Sqlite`
  (used only by the `Rask.Example.EfCore` sample); the whole SQLitePCLRaw family tops out at `2.1.11`
  and the advisory reports no patched version, so the solution-wide NuGet audit (run as
  warnings-as-errors) failed `restore` for every job. A scoped `NuGetAuditSuppress` for this single
  advisory unblocks the build while leaving audit active for everything else; it is annotated to be
  removed once a patched SQLitePCLRaw family ships.
- **New `RASK024` analyzer — `UseAuthentication()` must precede `UseRask()`.** A compile-time warning
  when `app.UseRask<App>()` is wired before `app.UseAuthentication()`. Rask seeds the live session from
  `HttpContext.User` during the initial GET render and the WebSocket upgrade; if authentication runs
  after `UseRask`, the principal is empty at that point and every `[Authorize]` page challenges — a
  silent, easy-to-miss misconfiguration the docs previously only warned about in prose. Fires only when
  both calls are present and `UseAuthentication` is positioned after `UseRask`; an app with no
  authentication middleware is left alone. Documented in [diagnostics](docs/diagnostics.md#rask024).
- **CI now scans for vulnerable and deprecated dependencies.** The `unit` job runs
  `dotnet list package --vulnerable --include-transitive` and `--deprecated` and fails on findings —
  defence-in-depth beyond restore-time `NuGetAudit` (which doesn't cover transitive or deprecated
  packages). The accepted-and-suppressed SQLitePCLRaw advisory is excluded, and "Legacy" (superseded
  but functional) deprecations are reported as informational rather than failing the build.
- **The scaffolded WASM JWT token store now warns when it holds a plaintext token.** The
  `dotnet new rask-wasm --auth` `TokenStore` keeps the bearer JWT in `localStorage` (a development
  floor — readable by any script via XSS); it now logs a one-time `console.warn` steering to an
  HttpOnly cookie or `ProtectedTokenStore` so a scaffold shipped to production unchanged surfaces the
  risk. New [reverse-proxy `ForwardedHeaders` guidance](docs/authentication.md) documents that the
  host-only anti-CSWSH / redeem-CSRF checks need forwarded headers behind a TLS-terminating proxy, or
  legitimate same-origin WebSocket handshakes are rejected with `403`.
- **File downloads now send `X-Content-Type-Options: nosniff`.** The one-shot download endpoint serves
  its content-type from whoever staged the entry — often echoed verbatim from a client upload — so a
  mislabelled file could be MIME-sniffed by the browser. The response now sets `nosniff` alongside the
  existing `Content-Disposition: attachment` and same-origin / same-session-owner guards, matching the
  asset endpoints. The `Raw` component also gained an XML-doc XSS warning making explicit that it is the
  framework's only un-encoded output path and must never carry untrusted input.
- **Upload filenames are sanitized before they are stored and echoed.** A client-supplied upload filename
  is attacker-controlled and is returned in the upload response (`name`) for hosts to display. Staged
  files were always written to a server-generated token path (never the name), so there was no
  server-side traversal — but the echoed name could carry directory components (`../../etc/passwd`) or
  control characters. The upload endpoint now reduces it to a safe leaf (drops `/` and `\` directory
  components whatever the host OS, strips control/NUL characters, caps the length at 255, and falls back
  to `file` for empty / path-dot inputs) as defence in depth. The returned `name` is still
  attacker-controlled and must be HTML-encoded by hosts before display — never bound into `Raw`.
- **WASM sign-out now guards against open redirects.** `WasmAuthSignIn.SignOutAsync` SPA-navigated to
  its `returnUrl` argument verbatim — commonly a `?returnUrl=` query value an attacker can shape — so a
  crafted value (`//evil.com`, `https://evil.com`, a `\`-prefixed variant) could redirect the user
  off-origin after logout. It now passes `returnUrl` through the shared `LocalUrl.Sanitize` rule (the
  same open-redirect guard the server sign-in path already applies at dispatch), collapsing anything
  non-local to `/` before navigating.
- **Content Security Policy guidance.** The [authentication guide](docs/authentication.md#content-security-policy)
  now documents how to run Rask under a strict CSP. Rask's runtime is built for it — the runtime script
  and scoped assets are external (`<script src>` / `<link>`, no inline JS), and events bind via
  `data-rask-on-*` + `addEventListener` — so `script-src 'self'` suffices (add `'wasm-unsafe-eval'` on
  the WASM host); only `style-src` needs `'unsafe-inline'` for the inline `style=""` the `Style:`
  parameter emits, and the same-origin live WebSocket is covered by `connect-src 'self'`. Includes a
  copy-paste middleware baseline and a new security-checklist item.

### Performance
- **Lower render allocations for elements carrying `Data` / `Aria` / `TabIndex`.**
  `Element.WriteAttributes` iterated the `Data` and `Aria` bags through the
  `IReadOnlyDictionary<,>` interface, boxing an enumerator on every render of an attribute-bearing
  element, and formatted `TabIndex` with `int.ToString()`, allocating a string per element. The bags
  now take a `Dictionary<,>` struct-enumerator fast path (the common `new() { … }` literal), and
  `TabIndex` formats straight into the pooled `StringBuilder` via a new integer `AppendAttr` overload
  (zero allocation on the no-frame-sink path). Measured on the render benchmarks (M-class, ShortRun):
  a 1,000-row data-rich tree dropped **661 KB → 552 KB** allocated (−16.5%), and a 1,000-row
  a11y-rich tree (`Aria` + `Role` + `TabIndex`) **808 KB → 677 KB** (−16%); small/medium trees −25–30%.
  No change to rendered output or the documented attribute order. New `AccessibilityAttributesBenchmarks`
  locks in the a11y path.
- **Halve the diff-codec allocation when a keyed list grows.** `FrameDiffer` sliced each inserted
  subtree's HTML out of the rendered document with a `Substring` while diffing — one short-lived string
  per `InsertSubtree` op. Each op now carries the fragment's `[HtmlStart..HtmlEnd)` char range instead,
  and `LivePayload.BuildPayloadUtf8Diff` slices it straight into the UTF-8 wire buffer at write time
  (`Utf8JsonWriter.WriteStringValue(ReadOnlySpan<char>)`), so no intermediate string is materialised.
  Directly-constructed ops still ship a verbatim `Value`, so the wire format is byte-identical. Measured
  on the new `FrameDifferBenchmarks.InsertRows` (M-class, ShortRun): a 100-row list gaining 50 rows
  dropped **11.32 KB → 5.11 KB** allocated (−55%), and a 1,000-row list gaining 500 rows
  **113.27 KB → 50.81 KB** (−55%). Reorder/no-change/text paths are unchanged.
- **Cheaper attribute-name symbol table in the diff payload.** `LivePayload.BuildPayloadUtf8Diff` built
  an attribute-name count map (and, in a burst, an index map + names list) on every diff to intern names
  appearing 3+ times. A diff with fewer than 3 ops can never reach that break-even, so the whole pass is
  now skipped for the common small update; larger diffs reuse per-thread scratch collections instead of
  reallocating the count map each frame. Measured on the new `AttributeDiffPayloadBenchmarks` (M-class,
  ShortRun): a 2-attribute update dropped **424 B → 208 B** allocated (−51%) and a 100-op
  single-name burst **728 B → 208 B** (−71%) — the 208 B floor is the `Utf8JsonWriter`'s own state.
  Wire output and the interning threshold are unchanged.

## [0.9.0] - 2026-06-16

### Added
- **Eager prefetch of scoped CSS/JS eliminates navigation FOUC.** The page `<head>` now also
  emits a low-priority `<link rel="prefetch">` for *every* registered scoped asset — not just the
  components on the current route — so when a component first mounts later (client-side navigation,
  a conditionally rendered section) its stylesheet/script is already in the browser cache and the
  scoped-JS namespace is ready on first interaction. (Cache-warming alone does not remove the
  flash — the live runtime also holds the body paint until the sheet has *applied*; see the
  matching **Fixed** entry below.) `prefetch` (rather than `preload`) keeps these hints at the lowest priority and
  avoids a *"resource preloaded but not used"* console warning for off-route assets. The markup is
  render-independent and cached (rebuilt only when the asset set changes), so it costs a single
  append per render. On by default; opt out with `AddRask(o => o.PreloadScopedAssets = false)` (or
  the equivalent WASM host-builder option) to fetch each scoped asset only when its component first
  mounts.

### Fixed
- **Scoped-asset flicker on client-side navigation and lazy mount is gone — the live runtime now
  holds the body paint until a newly mounted component's scoped stylesheet has *applied*.** The
  eager `<link rel="prefetch">` warms the HTTP cache, but cached bytes are not an applied
  stylesheet: the client previously swapped in the styled body in the same morph that inserted the
  `<link>`, so it painted unstyled for a frame while the (cached) CSS parsed. The render-application
  path now gates on the `<link>`'s `.sheet` property — non-null only once the CSSOM stylesheet is
  parsed and applied — rather than on Resource Timing `responseEnd` (which a prefetch satisfies the
  moment the bytes land, before the rule applies). A full reply preloads each new scoped stylesheet
  and awaits its `.sheet` before the morph paints the body; a diff that morphs a `<head>` fragment
  waits the same way. Both bound the wait by a 500 ms cap, so a genuinely slow/failed sheet shows a
  briefly unstyled page rather than stalling navigation. The scoped-JS invoke gate is hardened the
  same way on WASM: a prefetched scoped `<script>` now waits for its real `load` event before a
  first-render `Rask.*` invoke dispatches, so the call can't race ahead of the script's execution.
- **Switching a syntax-highlighted code-sample tab no longer renders the new pane's markup as
  literal text.** A live re-render that replaced one `Raw` value with another (e.g. switching a
  `CodeSample` tab from highlighted C# to highlighted CSS) shipped an in-place `UpdateText` diff op,
  which the client applied via `textContent` — escaping the token `<span>` markup into visible
  `<span class="cssSelector">…` text and only touching the first of the `Raw`'s several DOM nodes.
  A `Raw` value change is now treated as a structural replace that routes through the full-HTML
  morph, so the browser reparses the new markup into real token spans. Unchanged `Raw` values still
  diff to zero ops; `Text` nodes are unaffected.

### Changed
- **Showcase & auth samples reorganized into feature folders.** `samples/Rask.Example.Shared`
  moved from flat `Pages/` + `Demos/` + `Layout/` to per-feature folders under `Features/` with
  cross-cutting infrastructure (`CodeSample`, `EmbeddedSource`, `PageHeader`, `ShowcaseLayout`, …)
  in `Shared/`; the four `Rask.Example.Auth*` apps likewise moved to `Features/` + `Shared/`,
  matching `Rask.Example.EfCore`. No public framework API changed and all routes are unchanged.
- **Every showcase code sample now displays its real, full component class verbatim.** Each
  `CodeSample` reads its source through `EmbeddedSource.Read(...)` instead of a hand-written string
  literal, so the shown code is the actual compiled class and can never drift from the live result.
  Inline demos were promoted to dedicated, self-contained component classes co-located with their
  feature. A build-time check now fails if two embedded source files share a leaf name.
- **Showcase code samples now show one tab per source file, labelled with its file name.**
  `CodeSample` takes an ordered list of embedded file names and renders a tab per file (the
  highlight language is inferred from the extension), replacing the previous generic `C#` / `JS` /
  `CSS` strip that concatenated sibling files of the same language into a single pane. Multi-file
  demos (e.g. Scoped CSS, Context, Background service) now expose each file on its own tab.

## [0.8.0] - 2026-06-15

### Added
- **Background-service showcase (`/background`).** A new example page demonstrating an app-wide
  background process driving the UI: a DI **singleton** `IMetricsFeed` runs its own loop and raises
  an event each tick, and two independent components (`MetricsGauge`, `MetricsChart`) subscribe via
  `feed.Updated += StateHasChanged` in `OnMount` / `-=` in `OnUnmount`. Unlike the existing Live
  ticker — whose poll loop lives inside one component — this producer is decoupled from the
  component tree (it keeps ticking across navigations and, on the Server, across sessions). State is
  published as a single immutable snapshot swapped by reference so cross-thread reads are consistent.
  Runs in both the Server and WASM sample apps. `Sparkline` gains an optional `ValueFormat` so its
  axis labels can render as percentages (default unchanged).
- **Slow-connection affordances on both transports.** WASM boot now renders a determinate
  download-progress bar on the splash (driven by the runtime's `onDownloadResourceProgress`
  resource count, not bytes — so Brotli/gzip-precompressed assets don't skew it), replacing the
  indefinite spinner so a slow link shows movement instead of an apparent hang; it falls back to
  the spinner when the total is unknown. Server mode adds a subtle top-of-viewport **pending-action
  bar** that appears when a handler round-trip outlives a ~300ms latency threshold and clears when
  the reply lands — so a high-latency user sees their click registered. It is backed by an opt-in
  WebSocket ack: a client tags handler events with a monotonic `seq` and the server replies
  `{type:"ack",seq}` once the dispatch completes, **even when the render dedupes and ships no
  frame** (otherwise a no-op click would wedge the bar). Seq-less clients are unaffected — the prior
  frame contract is byte-for-byte unchanged.

### Changed
- **`Authorize`'s `Authorized` slot now receives the current user** — its type changed from `Child` to
  `Func<ClaimsPrincipal, Child>`, so authorized markup can greet the signed-in user
  (`Authorized: user => H1()[$"Hi {user.Identity!.Name}"]`, the headless analogue of Blazor's
  `AuthorizeView` `@context.User`) without injecting `IUserProvider` or subscribing to
  `IUserProvider.Changed` — the delegate re-runs with the fresh principal whenever the gate
  re-renders. **Breaking:** static authorized content moves to the children-indexer shorthand —
  `Authorize(Authorized: Panel())` becomes `Authorize()[ Panel() ]`. `NotAuthorized`/`Authorizing`
  are unchanged.
- **WASM build migrated to `Microsoft.NET.Sdk.WebAssembly`** so framework assets are
  content-fingerprinted (`dotnet.<hash>.js`, `App.<hash>.wasm`) and `index.html` carries an
  SDK-generated import map with integrity hashes. This fixes the GitHub Pages (and any static
  host) failure where a redeploy paired a stale, browser-cached integrity manifest with freshly
  served assemblies and tripped a subresource-integrity error on every `_framework/*.wasm` until
  the cache expired — fingerprinted URLs change per release, so a stale asset can never collide
  with a new manifest. **Breaking for downstream WASM consumers:** set
  `<Project Sdk="Microsoft.NET.Sdk.WebAssembly">`, replace `<WasmGenerateAppBundle>true` with
  `<RaskWasm>true</RaskWasm>` + `<OverrideHtmlAssetPlaceholders>true</OverrideHtmlAssetPlaceholders>`,
  and add a `wwwroot/index.html` shell (the `dotnet new rask-wasm*` templates already include
  one). Published output moves from `bin/<cfg>/net10.0-browser/browser-wasm/AppBundle/` to
  `bin/<cfg>/net10.0-browser/publish/wwwroot/`; `Rask.Wasm.Hosting`'s `UseRask()` follows it
  automatically. Sub-path deploys keep `/p:RaskPathBase=/<repo>` (now a post-publish `<base href>`
  rewrite; every other asset URL is document-relative).
- **Showcase code samples are now copy-pasteable and multi-language.** Every `CodeSample` card in
  the example apps gains a copy-to-clipboard button (scoped JS, with an `execCommand` fallback for
  non-secure/headless contexts), and samples that have a JavaScript or CSS side render a **C# / JS /
  CSS tab strip** — tab state is component state switched through the live diff, and each pane is
  highlighted server-side by ColorCode. The Element-refs and Scoped-CSS pages now show the **real,
  verbatim source** of their demo components (`ElementRefDemo.cs` + `.js`, `ScopedRed/Blue.cs` +
  `.css`), embedded from the actual files via a new `EmbeddedSource` reader so the snippet always
  compiles and matches the live result. Samples-only — no framework API change.

### Removed
- **`:global(...)` scoped-CSS escape hatch removed.** A scoped `{Component}.css` no longer has a
  per-selector opt-out. Global styles — brand palettes, `:root` variables, shell tags (`body`/`html`),
  and framework classes like Bootstrap's — now belong in a plain `wwwroot/global.css` linked from your
  App component's `<Head>` (`Link(Rel: "stylesheet", Href: LiveOptions.PathBase + "/global.css")`),
  exactly like any other static stylesheet; user `<Head>` contributions already load before the
  framework's scoped links. This drops the special-case selector rewriting and removes a source of
  confusion about scoping semantics. **Breaking:** move `:global(...)` rules out of `{Component}.css`
  files into a `wwwroot` stylesheet — all sample apps have been migrated as the worked example. See
  [scoped CSS](docs/js-interop.md).

### Documentation
- EF Core data-access guide: clarified that an awaited event handler re-renders automatically on
  completion (like an async lifecycle hook), and removed the misleading explicit `StateHasChanged()`
  from the delete example — it's only needed for out-of-band changes (timers, fire-and-forget,
  external event subscriptions). The `Rask.Example.EfCore` sample's `DeleteAsync` drops the call.

### Fixed
- **Scoped JS now supports `export async function`.** The module wrapper that exposes a sibling
  `{Component}.js` on `window.Rask["{Type}"]` only recognised `export function` / `export const`,
  so an `export async function name(...)` kept its `export` keyword inside the (non-module) IIFE —
  a `SyntaxError` that silently prevented the *entire* file from loading, leaving `Rask.{Type}`
  undefined and every invoke into it failing with "Could not find … on target". The export-strip
  and export-collection now accept the optional `async` modifier (sync and async exports may be
  mixed in one file). Affects both the Server runtime and the WASM publish bake (both go through
  the same `ScopedAssetRegistry` wrapping).
- **Navigation now scrolls to the top of the new page.** Forward client-side navigation (a `NavLink`
  click or `Navigator.Navigate`) previously left the window at the previous page's scroll position, so
  users could land mid-page on a new route. It now resets the scroll to the top on a history *push*,
  matching a server-rendered page load. Back/Forward and in-place URL changes (`SetQuery`, auth
  redirects — history *replace*) are left to the browser's native scroll restoration. When a
  `NavLink`'s `Href` carries a `#fragment` that matches an element on the destination page, the runtime
  scrolls to that element (and preserves the fragment in the address bar) instead of jumping to the
  top. Fixed in the shared client runtime, so both the Server (WebSocket) and WASM transports get it.
- **Showcase side navigation no longer overflows the page.** On desktop the long sidebar link list
  was an unconstrained `position-sticky` block, so when it exceeded the viewport its bottom entries
  fell below the fold and were only reachable by scrolling the whole page. The sidebar is now pinned
  under the navbar, capped at the available viewport height, and scrolls on its own. The navbar
  height — previously a `56px` literal duplicated across the sidebar `min-height`, the mobile-drawer
  offset, and the sticky `top` — is centralised in a single `--nav-h` custom property (samples only).
- **`Rask.Wasm.Hosting` now serves precompressed static assets with their real MIME type.** When a
  request for a `wwwroot` file (e.g. `global.css`) was satisfied from its publish-time `.br`/`.gz`
  sibling, `UseStaticFiles` keyed the content type off the `.br`/`.gz` extension and fell back to
  `application/octet-stream` — which browsers refuse to apply as a stylesheet (or execute as a
  module), so a linked `global.css` silently had no effect on a hosted WASM app. The host now
  re-derives the type from the underlying asset name for any known extension (`.css`/`.json`/`.svg`/…),
  alongside the existing `.wasm`/`.js` handling; genuinely unknown extensions still serve as
  `application/octet-stream`.
- **Keyed reorders now preserve the survivors' focus, text selection, and caret position**, not
  just their uncommitted input *value*. Applying a trusted structural diff (`MoveSubtree` /
  `PermutationBatch`) — and the keyed branch of the untrusted morph — relocated each surviving
  node with `removeChild` + `insertBefore`. Detaching a node blurs any focused descendant, so the
  typed text travelled with its row (the same DOM node is reused) but focus, selection, and caret
  silently did not — contradicting the "survivors keep their DOM state" contract the Keyed lists
  demo advertises. The runtime now relocates with the Atomic Move API (`Node.moveBefore`,
  Chromium 133+), which moves a node without disconnecting it, and falls back to `insertBefore`
  where it is unavailable. Affects `Rask.Server` (`rask.js`), `Rask.Wasm` (`rask.wasm.js`), and
  the shared morph runtime (`rask-morph.js`); covered by the Keyed lists step of every host's E2E
  journey, which now reverses a list with a focused, mid-string caret and asserts it survives.
- GitHub Pages demo showed `v1.0.0` instead of the released version in the navbar badge. The
  `pages` workflow checked out a shallow clone, so MinVer couldn't read the git tags and the
  assembly kept the .NET default informational version. The workflow now fetches full history
  (`fetch-depth: 0`), matching the CI/release/nightly workflows, so `RaskVersion.Current`
  reflects the real tag.
- `LiveSessionStore` no longer throws `ObjectDisposedException` while retiring pending session
  removals under contention. `ScheduleRemoval` disposed the prior `CancellationTokenSource` from
  inside an `AddOrUpdate` factory — i.e. while it was still reachable through `_pendingRemovals` —
  so a concurrent `CancelPendingRemoval` / `CancelAllPending` (e.g. two sockets detaching at host
  shutdown) could `TryRemove` the same instance in that window and call `Cancel()` on an
  already-disposed source. The install now swaps atomically (`TryUpdate`/`TryAdd`) and retires the
  prior source only after the CAS has made it unreachable, so exactly one thread owns its disposal.
  Fixes flaky `Reconnect_WhileExistingSocketAttached_NewSocketBecomesAuthoritative`.
- `LiveSession` disposal no longer throws `InvalidOperationException: Collection was modified`
  while tearing down the component tree. `Dispose`/`DisposeAsync` walked the tree (enumerating
  each component's persisted children) without holding `_renderLock`, so a render still draining on
  a thread-pool thread at host shutdown could rebuild those same child dictionaries (the swap+clear
  in `BuildRenderTree`) mid-enumeration. Teardown now takes `_renderLock` around the walk — the
  same gate renders already use to keep concurrent tree mutations mutually exclusive — and a
  `_disposed` guard stops a `StateHasChanged` from an unmount/dispose callback re-entering the
  render path. Fixes flaky `HandlerOrderingTests.TwoHandlers_AcrossMultipleRounds_NeverReorder`.
- Showcase samples no longer 404 on `bootstrap.min.css.map`: the vendored `bootstrap.min.css`
  carried a `sourceMappingURL` comment pointing at a map file that isn't shipped, so browsers
  (and the GitHub Pages demo) logged a console 404. Dropped the dangling comment.
- `LiveRenderContext.CurrentSync` no longer returns a disposed context. The thread-static sync
  mirror could linger on a pooled thread after an async render released it at an `await`; a later
  synchronous render reusing that thread observed the stale context (wrong handler attribution).
  Reading through the `IsActive` guard restores the documented "null outside an active render"
  contract. Allocation-neutral (113.9 KB render unchanged). Fixes flaky
  `*_OutsideLiveContext_OmitsHandlerAttribute` tests.

### Added
- New `samples/Rask.Example.EfCore` sample: an EF Core + SQLite CRUD app (Server host) showing data
  persistence in Rask. It uses `IDbContextFactory` (right for long-lived live sessions), organises
  code as vertical slices (List/Create/Edit), models the catalogue with a DDD aggregate + value
  objects whose validation rules are reused by the inline form validators, and stores money as
  integer minor units to sidestep SQLite's lack of a decimal type. Covered by unit + EF/SQLite
  integration tests (`Rask.Example.EfCore.Tests`) and a Playwright CRUD smoke test, and documented in
  `docs/data-access.md`.
- `RaskVersion.Current` exposes the running framework version (from the assembly's MinVer
  `InformationalVersion`). The server (`UseRask`) and WASM host log it on startup, and the
  showcase samples display it as a version badge.
- AI-assistant onboarding: an `AGENTS.md` ships in every `dotnet new rask-*` template (app-author
  conventions), plus a root `AGENTS.md`, `llms.txt`, and `docs/ai-agents.md`.
- Community health files: issue forms, PR template, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
  `CODEOWNERS`, and `docs/repo-administration.md` — contributions open, maintainer merges.

### Removed
- **Breaking:** removed the `Component.User` convenience property. Components that need the current
  principal now inject `IUserProvider` via the constructor and read `.Current` (a never-null
  `ClaimsPrincipal`) — explicit, testable dependencies instead of a base-class service locator. The
  built-in `Authorize` component, `[Authorize]` route gating, and the auth samples/templates are
  unchanged in behaviour.

### Changed
- Build is now **warnings-as-errors** with .NET analyzers and code-style enforced in-build
  (`Directory.Build.props`); see `docs/code-analysis.md`.
- NuGet packages ship a concise, gallery-friendly `NUGET.md` README (absolute URLs) instead of
  the full repo README.

### CI
- `nightly.yml` publishes a prerelease to nuget.org + GitHub Packages on every push to `main`,
  now gated on the **full** test suite — the prerelease only publishes after both the `unit` job
  and the complete sharded `e2e` matrix pass (previously `unit` only).
- `commitlint.yml` enforces Conventional Commits; `dependabot.yml` keeps NuGet/Actions current.

### Documentation
- New guides: `docs/development-workflow.md`, `docs/code-analysis.md`, `docs/ai-agents.md`,
  `docs/repo-administration.md`. CLAUDE.md compacted to a map pointing at the new
  `.claude/skills/` playbooks.

### Security
- URL-bearing attributes (`href`, `src`, `cite`, `formaction`, object `data`, `poster`,
  SVG `href`) are now **scheme-sanitized by default**: `javascript:`/`vbscript:` and
  `data:` outside media tags are neutralized to `about:blank`, closing a DOM-XSS hole that
  HTML-encoding alone left open. Detection defeats whitespace/tab/NUL obfuscation. Opt out
  per call with `RaskUrl.Trusted(...)` for URLs you control; media tags still allow inline
  `data:image/*`, `data:video/*`, `data:audio/*`. See the [getting-started guide](docs/getting-started.md#url-attributes-are-scheme-sanitized).

### Performance
- Scoped-asset registry reads (run per component, per render) are now lock-free
  (`ConcurrentDictionary`), so concurrent sessions no longer serialize on a process-wide lock.
- Removed `AsyncLocal` reads from the per-element attribute path via a thread-local
  render-context mirror, and cache `Component.Key` stringification (no per-render `ToString`
  allocation on keyed lists).
- `<head>` splice avoids a second whole-body scan, pools its builders, and appends keys
  without per-asset string allocation.

### Memory
- The per-render head-asset collector and mounted-type set are hoisted onto the root and
  reused (cleared per render) instead of allocating fresh collections every frame.
- A session minted by the GET shell but never connected over WebSocket is now evicted on a
  short grace (vs. the 30s reconnect grace), and `MaxSessions` is enforced as a hard atomic
  reservation — a concurrent GET burst can no longer exceed the cap.
- The WebSocket handler-dispatch chain is bounded: when handlers back up behind a hung or
  flooding client, the socket is closed instead of retaining queued payloads without limit.

### Changed
- **CI is now parallel.** The single build-then-test job is split into a fast `unit` gate
  (every non-browser test, built without the WASM AppBundle) and an `e2e` job that **shards
  one browser host per runner** so all fixtures boot concurrently instead of in serial
  batches. PR feedback no longer waits on the full E2E suite.
- **E2E suite consolidated to one journey per hosting project** (8 facts, down from ~192). Each
  host now runs a single comprehensive journey: the showcase trio (Server, Wasm.Host,
  StandaloneWasm) walks every page and exercises every browser-observable feature plus unusual
  activity (in-session + deep-link NotFound, back/forward, deep-link refresh, slow-3G throttling,
  offline→WebSocket reconnect, bounded-heap memory loop, CSS-loaded / JS-loaded / global
  error-handling checks); the sub-path host verifies the full `/sub` prefix contract; each auth
  host (cookie/JWT × server/WASM) runs one admin round trip + non-admin check with the token
  at-rest assertions intact. The fine-grained framework/component logic those per-feature facts
  asserted is covered in-process by the unit suites (unit-first).

---

## [0.7.0] - 2026-06-10

### Fixed
- `SessionUploadStore` no longer blocks a thread-pool thread with sync-over-async
  (`.GetAwaiter().GetResult()`) while staging an upload — the copy is now awaited.

### Documentation
- New [Composition](docs/composition.md) guide: children & fragments, callbacks, context,
  `Virtualize`, drag-and-drop.
- New [JS interop](docs/js-interop.md) guide: scoped CSS/JS, `IJSRuntime`, element refs,
  asset delivery.
- XML `<summary>` docs on the public host entry points (`AddRask`, `UseRask`,
  `WasmHostBuilder` / `RunAsync`).
- Added `CONTRIBUTING.md` and this changelog.
- Per-sample and per-template `README.md` files; clearer `--auth` template descriptions.

### Changed
- Showcase sample: the home page is now a grouped feature index, with light UI polish.

---

## Earlier history

Condensed from the commit log; see GitHub PRs for detail.

### Authentication & security
- Production authentication: `Authorize` component, route guards, cookie & JWT for Server
  and WASM, runnable samples, templates and the [authentication guide](docs/authentication.md) (#33).
- Hardened the live session: `returnUrl` handling, WebSocket origin checks, reconnect race
  fix, and a concurrent-session cap (#34).

### Components & DX
- Replaced the `Callback` type with automatic generator-managed parent re-render (#31),
  building on Context, element refs, form groups, and user-gating (#30).
- Added a headless drag-and-drop primitive (#26) and typed SVG components with a showcase (#16).
- Made non-nullable value-type factory parameters required (#25).
- First-class `Component.Key` with auto-forward and the RASK022 missing-key warning (#7).
- Emit `[DebuggerStepThrough]` + a `<see cref>` breadcrumb on generated factories (#35).

### Live runtime & diff codec
- `PermutationBatch` diff op to close the keyed-reorder byte soft spot (#20).
- Pooled the keyed-diff scratch so `FrameDiffer` is allocation-free per session (#17).
- Ship a `<head>` fragment / history-only diff on head- or query-only navigations.

### Infrastructure
- Restructured to `src` / `tests` / `samples` / `benchmarks` with `.slnx` and Central
  Package Management (#14).
- Consolidated shared test helpers into `Rask.TestSupport` (#4).

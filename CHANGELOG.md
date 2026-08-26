# Changelog

All notable changes to Rask are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versions are stamped at pack
time (`$(PackageVersion)`); this log groups changes by the pull request that introduced
them until tagged releases begin.

## [Unreleased]

### Added

- **Non-overlapping range constraints, declared on the model.** A booking, a lease, a price valid for a
  period — the rule is always "two rows may not cover the same point", and SQLite has no way to say it:
  there is no `EXCLUDE … WITH &&`, and a `UNIQUE` index only stops *identical* rows, so `100–200` and
  `150–250` both pass. Every application ended up hand-writing the same triggers per table, or checking in
  application code where the check races the insert.

  `modelBuilder.Entity<Booking>().HasNonOverlappingRange(x => x.StartsAt, x => x.EndsAt, partitionBy:
  x => x.RoomId)` is now the whole API. The rule is carried as provider-agnostic model metadata in
  `Rask.Data`, and `UseRaskSqlite(...)` emits the index and the `BEFORE INSERT`/`BEFORE UPDATE` trigger
  pair that enforces it, so `dotnet ef migrations add` picks it up with no hand-written SQL. A violating
  save throws `RangeOverlapException` naming the table instead of a `SQLITE_CONSTRAINT_TRIGGER` buried two
  levels inside a `DbUpdateException`.

  Ranges are **half-open** — `[lo, hi)` — so `100–200` and `200–300` are neighbours rather than a conflict.
  `partitionBy` scopes the rule to a room, a SKU, a tenant; a soft-deleted row frees its slot automatically
  for `ISoftDeletable` entities. Enforcement lives in the database, so raw SQL and other processes are bound
  by it too.

  **The constraint survives table rebuilds.** SQLite cannot `ALTER` most things in place, so EF's provider
  rebuilds the table (create `ef_temp_*` → copy → `DROP TABLE` → rename) — which drops the triggers with the
  original table and would silently retire the constraint. The DDL is therefore re-emitted at the end of
  every migration that touches the table, drop-then-create so it stays idempotent. A guard test runs a second
  rebuilding migration and asserts the rule still bites.

  It composes with `strictTables: true` from the same release: EF Core resolves exactly one
  `IMigrationsSqlGenerator`, so registering a strict generator and a range-exclusion generator separately
  would keep only whichever was replaced last and drop the other feature with nothing failing. The two are
  a single choice instead, and a test asserts a table can be both `STRICT` and range-constrained.

- **STRICT tables — `UseRaskSqlite(connectionString, strictTables: true)`.** SQLite is dynamically
  typed: a column's declared type is an *affinity*, not a rule, so the text `"lots"` stores happily in
  an `INTEGER` column and comes back later as a cast error, a mis-ordered index or a silently wrong
  result. EF Core's model keeps C# honest, but nothing stops a direct `INSERT`, an admin tool or a
  legacy row. [STRICT tables](https://sqlite.org/stricttables.html) reject the write at the source, and
  EF Core has no support for them — so Rask ships `RaskSqliteStrictMigrationsSqlGenerator`, which emits
  `CREATE TABLE … ) STRICT`. Table rebuilds (SQLite's route for most `ALTER`s) go through the same
  operation, so a rebuilt table keeps its strictness.

  Every column must declare one of `INT`, `INTEGER`, `REAL`, `TEXT`, `BLOB` or `ANY`. EF Core's default
  SQLite types all qualify, so a normal model needs no changes; an explicit `HasColumnType(...)` outside
  that set is rejected **naming the table and column at fault**, rather than leaving you with SQLite's
  own message, which names only the type. Verified against the full `Rask.Example.Shop` schema —
  Products, Orders, Outbox, Jobs, Mail and Cache all create cleanly as STRICT.

  Off by default, because strictness is decided when a table is created: turning it on needs no
  migration and affects tables created from then on, while converting an existing table means
  rebuilding it. **`rask new --data` scaffolds it on**, where it is free.

- **Three hardening pragmas, on by default.** `trusted_schema=OFF` — a schema can carry function calls
  in views, triggers, index expressions and `CHECK` constraints, and `OFF` is the setting SQLite
  recommends for any app that opens a file it did not create, since a malicious schema is otherwise a
  code-execution surface. `cell_size_check=ON` — turns a corrupt b-tree page into an immediate,
  localised error instead of letting the damage reach query results. `analysis_limit=400` — bounds the
  cost of the next item. Each is `null`-able to fall back to SQLite's own default.

- **`PRAGMA optimize` on connection close.** SQLite's planner chooses between indexes using
  `sqlite_stat1`, and nothing updates that table on its own — so a table that was small when it was last
  analysed keeps handing the planner stale numbers, which is the usual reason a query that was instant
  in development crawls in production. The EF Core interceptor now runs `PRAGMA optimize` on
  `ConnectionClosing` (for a pooled connection, every return to the pool), bounded by `analysis_limit`
  and best-effort so a connection being torn down never fails because of it. Raw ADO.NET users can call
  `SqlitePragmas.Optimize(connection)` directly.

### Fixed

- **`decimal` no longer mis-sorts — or kills the process — on a non-English locale.** SQLite has no
  decimal type, so EF Core stores one as culture-invariant `TEXT` and sorts it with a collating sequence,
  emitting `ORDER BY "Price" COLLATE EF_DECIMAL`. EF registers that sequence as
  `decimal.Compare(decimal.Parse(x), decimal.Parse(y))` — **with no `IFormatProvider`** — so it parses the
  invariant text under the machine's `CurrentCulture`. Where `.` is the *group* separator (`de-DE`,
  `fr-FR`) `"19.95"` reads as `1995` and rows come back **silently mis-ordered**; where `.` is neither
  separator (`en-HU`, `hu-HU`) the parse **throws inside a native SQLite comparison callback**, which a
  managed exception cannot be unwound across — so it does not surface as a query error, it **terminates
  the process**. The same crash happens on any locale once a non-numeric value reaches the column, which
  SQLite's dynamic typing permits.

  `UseRaskSqlite` (and the raw-ADO `IRaskSqliteConnectionFactory`) now re-register `EF_DECIMAL` on every
  connection open with an invariant, total, non-throwing comparison — the new `SqliteCollations.Apply`.
  EF's own generated SQL picks it up, so `ORDER BY`, `GROUP BY` and `DISTINCT` on a `decimal` are correct
  on every locale, and text that cannot be parsed sorts after the numbers instead of crashing the app.

  **Nothing in the database file changes** — no column type, no collation in the DDL, no migration, and
  every other tool still reads the file exactly as before. Re-registration happens on each open because
  Microsoft.Data.Sqlite's pool runs `Deactivate()` on return, which un-registers collations.

  Correct is not free, and the cost is now measured rather than guessed: each comparison is a managed
  callback, so ordering 100k decimals takes ~156 ms and allocates 125 MB, against ~4.5 ms and 768 B for
  an indexed `INTEGER` column (`SqliteDecimalOrderingBenchmarks`). It is avoidable — declaring
  `UseCollation("EF_DECIMAL")` on the property lets an index serve the ordering with no comparisons at
  query time, at the cost of a DDL that only a connection registering the collation can query. Both are
  documented, and the documented snippet is compiled and run by a test.

  Arithmetic, comparisons and `Sum`/`Average`/`Min`/`Max` were never affected — those translate through
  EF's `ef_add`/`ef_compare`/`ef_sum`/… helpers, which take typed `decimal` parameters. `docs/sqlite.md`
  and `docs/data-access.md` claimed EF "falls back to REAL for `ORDER BY` and aggregates" and that "EF
  Core warns"; neither was true — there is no REAL fallback and `SqliteEventId.DecimalTypeDefaultWarning`
  no longer exists in EF Core 10. Both are corrected, and modelling money as integer minor units is now
  presented as an indexing/throughput choice rather than a correctness workaround.

### Removed

- **The native hosting model is gone. Rask is a web framework.** `Rask.Native` and `Rask.Chrome` are
  deleted, along with the `native` CLI template (and its `--host` / `--platform` options), both native
  samples, the WebView spike, the Appium suite, the macOS CI/pack jobs, and the native guides. This is a
  direction change, not a verdict on the code: shipping one component model to a browser is the whole
  product now, and a second presentation shell was taking design decisions hostage across Core, the
  serializer, the CLI and the docs.

  **What this takes with it.** `Screen`, `AppBar`, `TabStrip`, `TabItem`, `BarButton` and `BarIcon` are
  gone — they existed to be a portable bar vocabulary a native head could project, and rendered landmark
  HTML on the web only so one class could serve both. A page is a plain `Component` again. The
  host-awareness axes collapse to one: `RenderEngine` (`Server` | `Wasm`) with `HostEngine` / `IsServer` /
  `IsWasm`. `RenderShell`, `RenderPlatform`, `HostShell`, `HostPlatform`, `IsNative`, `IsIOS`, `IsAndroid`
  and `IScreenChrome` no longer exist, and `RenderEngine.InProcess` is removed.

  **Diagnostics RASK032, RASK048, RASK049 and RASK050 are retired** — every one of them policed a native
  composition rule. Their IDs are not reused.

  The render walk got simpler on the way out: the serializer no longer reports every user component to the
  session, the render cache no longer refuses to cache while chrome is being collected, and
  `IRenderHandle` loses four members. That path is web-only now and pays for nothing else.

- **`rask new --template native` still existed, and it scaffolded an ASP.NET server app.** Removing the
  native hosting model took the packages, the samples and the generator's native arm, but left the entry in
  `TemplateCatalog` — and that entry is the whole contract: it is what `--template` validates against, what
  the wizard lists, and what shell completion offers. With no native arm left in `rask new`'s switch, the
  request fell through to the default one, so `rask new Field --template native` announced *"Creating Rask
  native mobile app (iOS + Android)"*, wrote a Server project, and signed off with *"Created Field (Rask
  server app)"*. Nothing threw, and the catalog's own tests asserted the entry was **present**, so the suite
  stayed green over it. `native` is no longer an accepted value — it is a usage error (exit 2) naming the
  three real templates — and a test now asserts the absence.

  Everything else the sweep left behind goes with it: `rask dev`'s dead native-refusal path
  (`DevTemplateKind.Native`, `RefuseNative`, `NativeRunCommands`, `NativeHotReloadGuidance`) and its tests;
  the unreachable `window.__raskNative` share bridge in the client JS, which nothing has injected since the
  hosts were deleted; the `Appium.WebDriver` package pin; and the dangling `../native.md` links in
  `IBattery` / `ISpeechRecognition`, which shipped to consumers in the packed XML docs.

  **The docs had drifted furthest, and in the worst way** — `docs/cli.md` still documented `--template
  native` with its `--host` and `--platform` options as if they worked. Five pages had also been cut
  mid-sentence by the sweep and read as truncated prose: `docs/sqlite.md`'s "SQLite on mobile" section
  opened on a fragment, and `docs/cli.md`, `docs/browser-capabilities.md`, `docs/browser-apis-sharing.md`,
  `docs/js-interop-runtime.md` and `docs/development-workflow.md` each had a clause that stopped dead. Those
  are repaired, and `samples/README.md` no longer lists two deleted sample projects.

  **RASK032, RASK048, RASK049 and RASK050 are now recorded as retired in `docs/diagnostics.md`.** They had
  simply vanished from the table, which is how an id gets quietly reused — the one failure mode that file's
  own descriptor tests exist to prevent.

- **The last of the native model is out of the prose, including the parts that shipped.** Two deletions had
  taken the code and left the text describing it, in ~60 files. The half that reached users mattered most:
  `src/Rask.Cli/NUGET.md` — the CLI's README on nuget.org — still advertised `--template server|wasm|
  wasm-hosted|native`, and XML doc comments on `IShare`, `IPermissions`, `IMediaStreams`, `ShareData`,
  `Shareable`, `RaskHostContracts`, `RaskBrowserApis` and `Build` documented a Native host, a native
  backend and deleted types like `NativeAppHost` and `NativeBarItem` — all of which ship to consumers in
  the packed `.xml`. `Navigator.Download`'s **runtime exception message** named `NativeAppHost` as a host
  that registers an `IDownloadSink`. `Rask.Cqrs.Client`'s NuGet `<Title>` sold "Browser and Native Clients".

- **Every native backend is reachable from every model** (#778, the four-models epic). `NativeCapabilities`
  advertised a hardcoded `["share"]` and its dispatcher took a single `IShare`, so fourteen of the fifteen
  native backends the platform modules register were unreachable from a remote shell — a page running as a
  *server* app silently got the WebView's JS instead. All fifteen cross the bridge now, in every model.

  **The docs had drifted in a shape the last sweep hid.** `docs/browser-capabilities.md` was cleaned, but
  the ~19 `docs/apis/*.md` pages it links to were not: each had the Native column stripped from its
  **Availability** line while its **Home** line and prose note still described a native backend, so
  `docs/apis/permissions.md` remained, in effect, a native-permissions guide — a `## On Native` section
  with an iOS/Android table. `README.md` still promised the component "runs three ways … or as a real
  iOS/Android app" and a `net10.0-ios;net10.0-android` target; `docs/roadmap.md` marked "UI across three
  hosts" shipped; `docs/deployment.md` explained why the `native` template emits no Dockerfile; and
  `AGENTS.md` told agents to pin macOS jobs for an Xcode SDK no workflow has used since the removal.
  `CONTRIBUTING.md` claimed `ci.yml` had "exactly two jobs — the benchmark byte-gates and the native
  compile gate"; it has one, and `docs/development-workflow.md` already said so, so the two contradicted
  each other. In the showcase, `ShareDemo` **rendered** "Ship real iOS/Android apps from the same C#
  component code." on screen.

  **Four sentences had been cut in half by the sweep and were left ungrammatical**, on top of the five the
  last pass repaired: `NUGET.md`'s prerequisites stopped mid-clause at "or `ios android`",
  `docs/apis/battery.md` welded an unclosed paren onto a native fragment,
  `docs/browser-apis-reference.md` read "In the / the native app-icon badge)", and
  `BuilderRenderPathTests.cs` ended a comment at "…Header/Footer, and".

  **Two pieces of genuinely dead code go with it.** `window.__raskFiles.readChunkBase64` in
  `rask-files.js` existed only for the Native host's `IJSRuntime`, which is JSON and could not marshal a
  `Uint8Array`; nothing has called it since, exactly like the `window.__raskNative` share bridge the last
  sweep removed. And `SharedSmokeTests`' `ConfigurePageAsync`/`TeardownAsync` hooks existed so the deleted
  `NativeExampleTests` could start a host in-process — zero overrides remain.

  **A test now covers the shape the bug actually took.** The template's removal was pinned only by
  `TemplateCatalogTests` asserting the catalog entry is absent; nothing asserted that `rask new Field
  --template native` *fails*, which is what a user would have hit — the original bug scaffolded a Server
  project and exited 0. `NewCommandTests` now asserts exit 2 and that nothing is written.

  **The unreleased notes no longer ship and delete the same feature.** ~30 `[Unreleased]` entries from
  #775–#818 added the native model — `NativeCapabilities`, pure-native screens, `Screen`, `-p:RaskNativeHeads=ios`,
  a BREAKING marker for `--host native`, and an entry "fixing" `docs/getting-started.md` to say there are
  four templates. None of it was ever released, so it is gone rather than annotated; the removal record
  above stays, and released sections are untouched.

### Changed
- **The capability envelope now carries an `op`.** It was `{ component, data }`, which could only ever name
  one operation per backend; it is `{ id, component, op, data }` now. The old two-field form only ever
  routed `share`, and both clients ship in this repo, so nothing outside it can have depended on the shape.

- **The remote heads take the app's services, not a lone `IShare`.** `RaskServerViewController` and
  `RaskServerWebView` had their own private notion of what a capability was, which is why they could only
  ever forward one. They share the in-process dispatcher and its reply channel now, so the two models
  cannot drift apart — the same envelope, the same backends, the same answers.

### Removed
- **The generated `Generated.X(...)` factory is gone; the chain is the only way to write markup.**
  `Div.Class("card")[Span["hi"]]` was already the documented surface — the factory was the one it
  replaced, kept alongside it so a migration could land project by project. Every call site in the repo
  is converted (the deterministic rewriter in `tools/RaskBuilderRewrite` did all but six of them, and
  trial-compiled each rewrite before accepting it), so what goes now is the second way to say the same
  thing.

  Gone with it: the per-namespace `Generated` class, the `global using static …Generated;` lines that
  made it reachable, `[assembly: RaskFactoryNamespace]` (which existed only to carry a satellite factory
  family across an assembly boundary), and the `RaskBuilderSurface` / `RaskGlobalUsings` /
  `RaskFactoryNavigation` MSBuild switches. There is no switch for the chain: turning it off would leave
  a project unable to build a component at all.

  **`RASK030` is retired.** It asked you to name a factory call's arguments once three or more were
  positional, because the generated parameter order could shift under an unrelated edit and silently
  rebind them. A chain has no positional arguments — every step names its property — so there is nothing
  left to misbind. The id is not reused.

  **`RASK043` stays**, and keeps its diagnosis: a bare component name in a type that is not a markup host
  is still `CS0119`, and the analyzer still says so in terms that name Rask. What it lost is the third
  fix it used to offer ("or add `using static …Generated;`"). Derive from `Rask.Core.RaskMarkup`, or
  mark the type `[RaskMarkup]`.

  **`Route<T>(...)` is now `Route.To<T>(...)`.** A route table is not markup, so those helpers were only
  living in the `Generated` class to ride the factory's static import. On the record itself they need no
  import at all.

  Two rules the chain has that the factory did not, surfaced while migrating the tests and now pinned in
  `ChainPropertySelectionTests`: an **init-only property gets no chain step** (a step assigns after the
  component exists, and `init` is callable only from an object initializer — the factory could set one
  because it constructed with `new T { … }`), and the name **`Children` is reserved whatever its type**
  (the factory excluded it on name *and* type; the indexer owns the word).

### Fixed
- **The bound-control numbers were measured against the wrong model, and a bound control is now
  benchmarked at all.** Adding the benchmark #802 asked for turned up the answer to #793.

  Every probe in `BuilderEntryAllocationPinTests` bound `BoundForm`, a test fixture that derives
  `RaskMarkup`. Constructing an `Expression<Func<T>>` resolves a member token on the terminal
  property's **declaring type**, and that cost scales with how many members the type has — measured at
  312 B for a one-property class, 1912 B for a 200-property one, and 2312 B for a `RaskMarkup`
  subclass, which carries the whole chain surface as members. So the probes were paying ~1970 B/render
  for a shape no guide recommends.

  That is what #793 was looking at. It recorded 3555 B/render on 2026-08-08 against 5163 B when next
  measured and concluded Rask's bind path had regressed 45%. It had not: the representative probe costs
  **3041 B today, below the number it was compared against**, and the difference was the fixture. The
  claim that ~46% of the cost was unavoidable compiler work went the same way — against a plain model
  the expression tree is 320 B, about 10%. Rask's own share, 1505 B, was measured correctly and is
  unchanged; it remains the part worth attacking.

  | | B/render |
  | --- | --- |
  | `Div[Input.Value(…)]` — controlled | 1216 |
  | `Div[Input.Bind(hoisted)]` — bound, expression built once | 2721 |
  | `Div[Input.Bind(() => …)]` — bound | 3041 |
  | …the same, against a model deriving `RaskMarkup` | 5011 |

  The probes now bind a plain model, the aggregate ceiling drops from 5600 B to **3300 B**, and the
  expensive shape gets a pin of its own instead of being averaged into the representative one — it is
  not contrived, since `Component : RaskMarkup` means `Input.Bind(() => Draft)` against a component's
  own property lands there.

  `BoundControlRenderBenchmarks` closes the coverage gap that surfaced all this: nothing in
  `benchmarks/` rendered a bound control. `ExpressionAccessorBenchmarks` covers `Parse` alone with a
  hoisted expression, and `LiveDiffPayload_InputTypingBurstBenchmarks` reads like form coverage but its
  inputs are `Input.Value(…)`, i.e. controlled. The new benchmark runs the three arms the pins now pin,
  so the suite and the gate describe the same decomposition: controlled 6.77 KB, bound-hoisted 9.39 KB,
  bound 10.38 KB per three-field form.

- **The WASM-hosting tests raced each other over two process-wide statics.** `UseRask` sets
  `ScopedAssetBundle.BakedDirectory` and `LiveOptions.PathBase`, which are **per process**, not per
  server. Three classes in `Rask.Wasm.Hosting.Tests` stand hosts up and xUnit runs classes in
  parallel, so a second host took the first one's state away from it — and disposal made it worse
  rather than better, since it resets the statics, letting a host that merely *finished* break one
  still serving. That is the
  `RegistryMiss_BakedBundleFile_NegotiatesPrecompressedSibling — Expected: OK, Actual: NotFound`
  the local gate reported on a diff that touched none of it (#789).

  The assembly now disables test parallelisation, as `Rask.Dashboard.Tests` and `Rask.SQLite.Tests`
  already do for their own process-wide state — assembly-wide rather than a per-class collection
  precisely because a new class cannot forget to join it, which is how this arose. The suite still
  runs in 2 s. `ProcessWideHostStateTests` demonstrates the overlap directly, standing two hosts up
  and watching the second take the first's bundle directory and path base, so the reason for the
  serialisation is verifiable instead of merely asserted.

  The statics themselves are not a bug: a real deployment has one host per process, which is why
  `UseRask` can set them at all. Only a test process holds two, so the fix belongs to the tests.
- **A bound form control allocated less, and the pin that guards it now says which layer moved.**
  `ExpressionAccessor.Accessor` carried a `Func<object?> Getter` and an `Action<object?> Setter` that
  closed over the same `Target` and `Property` the record already held. They were rebuilt on every
  `Parse`, which is every render of every bound control (`Input` / `Select` / `Textarea` / `Bs*`), for
  a display class and two delegates that told nobody anything. They are methods now; every call site
  still reads `acc.Getter()`. The bound probe went **5195 → 5034 B/render**.

  The bigger result is the measurement. #793 recorded 5163 B/render against 3555 B on 2026-08-08 and
  could not say where the difference went, because one aggregate ceiling cannot. Decomposed:

  | | B/render |
  | --- | --- |
  | bare `Div` | 1088 |
  | `Div[Input.Value(…)]` — controlled | 1216 |
  | `Div[Input.Bind(hoisted)]` — bound, expression built once | 2723 |
  | `Div[Input.Bind(() => …)]` — bound | 5034 |

  > **Corrected below (see "the bound-control numbers were measured against the wrong model").** This
  > entry originally read that 2311 B — 46% of the total — was the C# compiler building an
  > `Expression<Func<T>>`, and that the probe could therefore never go below ~3500 B. Both were
  > artefacts of the probe binding a model that derives `RaskMarkup`. Against a representative model
  > the tree is 320 B and the probe costs 3041 B. Rask's own share, 1505 B, was right.

  Those layers are now **absolute** pins rather than one aggregate. A relative pin is what let the
  original regression hide, so they decompose the cost without reintroducing that: a regression in the
  shared element path trips the controlled pin, one in the bind path trips the hoisted pin, and either
  trips the aggregate.

- **Four more test suites shared one `DbContext` across classes xUnit ran in parallel.** The shape that
  made `Rask.Outbox.Tests` fail the gate in #769 was still present in `Rask.Jobs.Tests`,
  `Rask.Cache.Tests`, `Rask.Mail.Tests` and `Rask.Data.Tests`: EF Core's model cache is **per process,
  keyed on the context type** — not per `ServiceProvider` — so classes that each build their own
  provider over their own SQLite file still share one `IModelSource` and one `IModel`, and the first
  test in each racing to first-touch it drives one piece of EF-internal state from two threads. The
  DB-touching classes in each suite now sit in one `[CollectionDefinition]`, as the Outbox ones do. It
  costs nothing measurable — each suite still runs in 1–3 s, because these tests are bounded by their
  own waits rather than by CPU.

  `Rask.Dashboard.Tests` was on the same list and needed no change: it already disables parallelisation
  assembly-wide. `OutboxSerializerRegistryReplaceTests` was flagged as "same family" and is not — the
  registry rebuilds under a lock and installs its lookup in a single volatile store, so a reader
  observes either the whole old map or the whole new one. Concurrent use is its design.

  A shared `DbCollectionGuard` (linked into all five suites, Outbox included) now fails if a test class
  is neither in the collection nor named as one that never builds a context. It asks the question the
  safe way round on purpose: these suites build their contexts in a **local**, which no reflection over
  fields and signatures can see, so a guard that tried to detect "does this class use the context?"
  would pass every class for the wrong reason. Defaulting to *collected* means a new test class cannot
  join the suite silently, and every exception is a name somebody wrote down with a reason.

- **An `internal` component's chain entry did not cross an `InternalsVisibleTo` boundary.** A friend
  assembly could see the component and could see its entry, and was told about neither: the scan that
  reads a referenced assembly's `RaskEntries{Assembly}` class took **public** members only, and an
  internal component publishes its entry `internal static`. The friend assembly's only remaining
  spelling was the fully-qualified entry host — `global::RaskEntriesRask_Example_Shared.PageHeader
  .Title(…)` — because with the factory gone there is no second way to reach the component at all.

  The scan now asks `IAssemblySymbol.GivesAccessTo(compilation.Assembly)`, which is the same question
  the compiler asks of `InternalsVisibleTo`, and admits internal entries when the answer is yes. They
  are injected as `private static` forwarders like every other one, so an internal type never leaks
  past the host that receives it. `PageHeaderTests` now reads `PageHeader.Title("Greetings")`
  unqualified, and a cross-assembly generator test pins both halves — the grant, and a negative
  control with the grant naming somebody else, where the public entry still arrives and the internal
  one does not.

### Fixed
- **`Rask.Wasm` now declares `Microsoft.Extensions.ObjectPool`, which its bundled `Rask.Core` needs at
  runtime.** `Rask.Core.dll` is packed into `lib/` with `PrivateAssets="all"`, which is exactly what keeps
  Core out of the nuspec — and takes Core's own package dependencies with it. The package already
  re-declared `Microsoft.JSInterop` and `Microsoft.AspNetCore.Authorization` for that reason, but not
  ObjectPool, which `RaskStringBuilderPool.Shared` uses on the render path. `Rask.Server` never noticed
  because `Microsoft.AspNetCore.App` carries ObjectPool; the WASM track has no framework reference to hide
  behind, so a consumer restoring the published package got a `FileNotFoundException` on the first render.

  Confirmed against the shipped artifact, not only the source: restoring the published
  `Rask.Wasm 0.20.1-alpha.0.77` into a fresh browser-WASM app resolves **no** ObjectPool on either target
  framework, while the same restore of the fixed package delivers
  `lib/net10.0/Microsoft.Extensions.ObjectPool.dll`.

  `PackageDependencyTests` gained the guard that would have caught it and will catch the next one: for
  every host that packs another project's DLL into its own `lib/` (via `TfmSpecificPackageFile` or
  `BuildOutputInPackage`), each of that project's package references must be declared by the host,
  reachable from what it does declare, or covered by a `FrameworkReference`. Reachability is read from the
  host's real restore graph rather than a hand-kept list — `Microsoft.Extensions.Primitives` sits in the
  same position and is fine only because `Logging -> Options -> Primitives` brings it in, so the guard has
  to fail if that edge ever disappears. Closes #742.
- **A test helper that could not report the thing it existed to detect.** `WaitFor.True` threw on timeout
  only when a caller passed the optional `reason`, and most callers do not — so a wait that gave up returned
  exactly like a wait that succeeded, handing the test a half-settled world. The failure then surfaced later
  as a confusing assertion ("expected the body text, got a spinner") instead of at the wait. It now always
  throws, with `reason` only enriching the message.

  Turning it on immediately found a test that had never worked: `LiveTickerTests.OnPropsChanged_LogsSymbolSwitch`
  waited for `OnPropsChangedAsync` *before* changing any prop, so it could not fire — the wait burned its full
  10-second budget on every run and moved on as if it had succeeded (10 s → 722 ms once corrected to wait for
  the mount, as its sibling unmount test always did). Separately, the three `HttpPageTests` waited for "the
  request was issued" and then slept a fixed 50 ms before asserting on "the response rendered", which is the
  race that failed a gate run on an unrelated branch; they now wait on the rendered result itself.

- **A diagnostics capture could be silently unhooked by another test class disposing its own.**
  `CapturingDiagnostics` (Rask.Testing) saved the previous sink on install and put it back on dispose, and
  the sink is process-global. xUnit runs test classes in parallel, so two captures were routinely live at
  once: whichever disposed first restored the sink from before *it* installed, leaving the other one
  installed but wired to nothing. That capture then recorded no diagnostics at all and its test failed on
  `Assert.Contains` against an empty collection — on a full-solution run, on a diff that touched none of
  it, while passing on its own. Installs are now tracked as a set: every live capture receives every
  diagnostic, and the outer sink is restored only when the last one is disposed, in any order. Reproduced
  4 runs out of 6 of the loaded gate before the fix, in both directions (each of the two installing
  classes stealing from the other), and pinned by a test that stages the out-of-order dispose directly.

- **Three unit tests that waited a fixed duration for a thread-pool continuation.** A `Task.Delay` budget
  is not a synchronisation primitive: on a machine busy enough — which the gate itself makes it, running
  ~40 test hosts at once — the continuation had not run when the assertion read the result.
  `AsyncLifecycleErrorBoundaryTests` drained its async-fault path with 50 ms of `Task.Delay` and now waits
  for the outcome (the boundary's error, the reported fault, and a `TaskCompletionSource` the recording
  render handle signals). Waiting for the result is *faster* when the pool is idle — the class went from
  five fixed sleeps per test to 27 ms for all four — and patient when it is not, which is what lets the
  budget be generous rather than tuned.

  `WaitFor.True` moved from `Rask.Example.Shared.Tests` into `Rask.TestSupport` so every suite polls the
  same way instead of each growing its own timing helper; the Example projects that shared it by linking
  the source file now reference it.

- **The Outbox test classes that share a `DbContext` type no longer run in parallel.** EF Core's model
  cache is per *process* and keyed on the context type, not per `ServiceProvider`: two Outbox test classes
  building their own provider over their own SQLite file still share one `IModelSource` and one `IModel`
  instance (verified by reference identity). Their first touch of the model was therefore two threads
  driving one piece of EF-internal state, and the gate caught it — `SaveChangesAsync` threw *"The model
  must be finalized and its runtime dependencies must be initialized before 'GetRelationalModel' can be
  used"* from a test whose diff was a `.svg` file. The five classes that build a context are now one xUnit
  collection, which removes the concurrency the failure needs; the suite still runs in 3 s.

- **A WASM-hosting test class was outside the collection that keeps hosts off each other.** Two pieces of
  state a host owns are process-wide statics — `ScopedAssetBundle.BakedDirectory`, which `UseRask` points
  at the bundle it just resolved, and `LiveOptions.PathBase`. One app serves one bundle, so a single value
  is right in production; it is the tests that stand up a host per case. Three of the four classes that do
  were already grouped in the `ScopedAssets` collection for exactly this reason and `UseRaskTests` was not,
  so it overlapped them: a host starting there re-pointed the bundle directory out from under a request
  another class was midway through, and the `AssetEndpointParityTests` case covering precompressed-sibling
  negotiation 404'd on a file it had written itself. Resetting the statics on dispose — which the
  harness already did — cannot help when the tests overlap rather than follow one another. The property now
  says so where it is declared.

- **The unit gate collects crash evidence.** When a test host dies below the managed layer the run says
  only "Test host process crashed", with no exception, no stack, and not even the name of the test in
  flight — and because the solution runs its assemblies concurrently, the console lines nearest the crash
  belong to whichever *other* assembly was writing at the time. That is how #769's third report attributed
  a crash to `Rask.Server.Tests`, which has fewer tests than the crashed host had already passed. The gate
  now runs with `--blame-crash`, so the next occurrence names the test and leaves a dump under
  `artifacts/test-blame/` instead of being merely observed. The crash itself did not reproduce here (6
  full loaded gate runs, plus 5 dedicated `Rask.Server.Tests` runs under load, all green).

- **The hero animation no longer hangs a character off the end of an untyped line.** `spacingAndGlyphs`
  stopped the last glyph of a line from spilling *well* past `textLength`, but not from reaching the very
  edge of its advance box — the `>` of `=>` does — and a cover rectangle that stopped exactly at
  `textLength` still left that glyph's antialiased edge showing from the first frame, before its line was
  typed. The cover now runs `Bleed` px past the text and scales about the **text's** right edge rather
  than its own, so the overhang is still there at the last step and is exactly zero at `scaleX(0)`. That
  second half is what makes it safe: a cover with a fixed pad would leave a residue over the segment that
  follows it on the same line (`Button.` and the rest of line 9), and this one collapses to nothing.

  Regression evidence is a pixel scan of rendered frames rather than a unit test — the artifact is a
  rasterisation detail no assertion on the markup can see.

- **`ChromeScreen` declares its route the way every other page now does.** It landed with a `Route`
  override in the same window that removed the `Page` base class, so `main` briefly did not compile —
  each change was green on its own branch and only their combination was broken.

### Changed
- **BREAKING — routes are declared by `[Route]` again; the `Page` base class is gone.** A routable
  component is any `Component` carrying `[Route("/x")]`, with `[ParentRoute(typeof(Layout))]` for nesting
  and `[NotFound]` for the catch-all. `Page` and its `Route`/`Parent` overrides are removed.

  This is what makes **one page answer several URLs**: `[Route]` is `AllowMultiple`, so stacking it is the
  whole feature. The first template declared is canonical — it is what `X.Url(...)` and `Routes.X(...)`
  format — and the rest are alternates the router matches but nothing generates, so a generated link can
  never drift onto a path kept only for old bookmarks. A single `Route` property could express exactly one
  URL, which is what motivated the change.

  Migration is mechanical: delete the `Route` override and put its template in a `[Route]` above the class,
  delete the `Parent` override in favour of `[ParentRoute(typeof(...))]`, and change the base from `Page`
  to `Component` (`Screen` subclasses keep `: Screen`).

- **The hero animation types the new declaration.** `ChainAnimation` (and the `assets/rask-chain.svg`
  baked from it) opens with `[Route("/counter")]` above a plain `Component`, retimed so the attribute and
  the class line type at the same rate. Two new colour roles came with it: an operator role, so `=>` reads
  as code rather than as dim punctuation, and an interpolation role, so the `{_count}` hole inside
  `$"Current count: {_count}"` is coloured instead of disappearing into the string literal.

  Each typed run also measures with `lengthAdjust="spacingAndGlyphs"` rather than `spacing`. `spacing`
  adjusts only the gaps *between* glyphs, so the last glyph kept its natural advance and its ink could
  spill past `textLength` — past the cover rectangle that hides the untyped remainder, leaving the tail of
  a line (the `=>` ending the `Render` line) hanging on screen from the first frame, before the line was
  typed.

- **The hero animation's completion list is bigger, and the whole loop is slower.** The member list is
  the picture's argument — "press `.` and the chain tells you the rest" — and at 380px wide with 15px
  names it was the smallest thing on screen. It is now 460px wide on 32px rows, with the names, the types
  and the doc comment each a size up, and the loop runs 20s instead of 16s so a reader can follow the
  typing without it feeling like a progress bar. Every keyframe offset is a percentage of the loop, so
  the duration is one constant; the canvas grew to 880×620 to seat the taller popup, which the landing
  page's `aspect-ratio` follows.

- **RASK047 is retired.** It reported a `Page.Route` override that was not a compile-time constant. A
  `[Route]` argument is an attribute argument and therefore constant by construction, so the failure it
  guarded cannot be written. The id is retired, not reused.

### Added

- **Remote CQRS — `Rask.Cqrs.Client` and `Rask.Cqrs.Server`.** A WASM-hosted app now reaches
  its server through the same `IDispatcher` call it already uses in-process, with no `HttpClient` at the
  call site and no hand-written `/api/*` endpoints. One package and one line per project: the client
  calls `AddRaskCqrsClient()`, the server calls `AddRaskCqrsServer()` + `MapRaskCqrs()`, and neither
  references the other half — so a browser bundle cannot compile the endpoint code and the server never
  carries the browser transport.
  - **Nothing marks a message as remote.** You write a record and a handler, exactly as for in-process
    CQRS; where the project sits decides where it runs. A client is a *pure* client — every message it
    dispatches goes to the server, so a stray client-side handler can never quietly intercept one.
    Notifications are the deliberate exception: they fan out, so a client's own handlers still run and
    the notification also travels.
  - **The wire codec is source-generated**, not reflected: `Utf8JsonWriter`/`Utf8JsonReader` code emitted
    per contract, so remote dispatch publishes clean under the WASM/AOT trimmer. **RASK053** reports a
    shape that has no wire encoding. This reaches no existing code — codecs are generated only for a
    compilation that references one of the two transport packages, so an app using `Rask.Cqrs`
    in-process is unconstrained.
  - **`[LocalOnly]`** keeps a message off the wire entirely, and on an *interface* covers a whole family:
    `IJob` and `IOutboxEvent` both derive from `ICommand`, so without it every job payload and outbox
    event would become an internet-reachable endpoint. It is also how a **client** keeps a message
    in-process — "a pure client" is literal, so a handler sitting in the client project is otherwise
    bypassed and the server answers 404 for a name it has no handler for.
  - **Two endpoints, not one per message** — `GET` and `POST` on `/_rask/cqrs/request/{name}`. The verb
    carries what `IQuery` and `ICommand` already declare, so a command is 405 on GET and cannot be
    triggered by a URL, a prefetch or a link scanner. A query too long for a URL falls back to POST with
    an identical result.
  - **Fails closed.** Authenticated-required by default with `[AllowAnonymous]` as the only way past;
    `[Authorize]` on the handler supplies a policy *and* roles (both enforced — ignoring `Roles` would
    leave an author believing it was checked). An anonymous caller gets the same answer for a real
    message name as for a typo, so the endpoint cannot be used to enumerate an app's messages. Both
    verbs require the `X-Rask-Cqrs` header, which no cross-site markup can set. Handler exceptions become
    RFC 9457 `problem+json` with no exception text in production.
  - **Files are just `RaskFile`.** A message declares `RaskFile` — the same type a file input hands a
    component — so the file a user picked is passed straight to the handler with nothing to convert:
    `await dispatcher.DispatchAsync(new AttachReceipt(id, picked))`, identical on a server-rendered app,
    and a WASM-hosted one. In-process the handler gets the picked file; over the wire the
    generated codec carries the bytes and hands the handler a `RaskFile` over what arrived. Neither
    direction buffers — every host reads a `RaskFile` in bounded slices (the browser ones through
    `Blob.slice`) — and a query returning `FileDownload` streams back with an `attachment` disposition, a
    filename reduced to a safe leaf, and `nosniff`.
  - **`rask new --template wasm-hosted --cqrs`** scaffolds the whole arrangement: the messages in
    `Shared`, the handlers in `Server`, `AddRaskCqrsClient()` in the browser, `AddRaskCqrsServer()` +
    `MapRaskCqrs()` in the host, and a page that dispatches a query and a command. Each half takes only its
    own transport package. Without `--auth` the template sets `RequireAuthenticatedUser = false` and says
    in a comment why — there is no authentication to require, and left on, every message would answer 401;
    with `--auth` the secure default stands. Both variants are compiled by the CLI build gate.
  - **The generator no longer ships in three packages at once.** `Rask.Cqrs.Client` and `Rask.Cqrs.Server`
    each packed their own copy of the codec generator alongside the one in `Rask.Cqrs`, so a project
    referencing a transport *and* the message library loaded it twice and every generated codec collided
    with itself (CS0101/CS0111) — remote CQRS could not be consumed as packages at all. Invisible in-repo,
    because a `ProjectReference` does not flow analyzers the way a package dependency does.
  - **A notification publishes once.** A client's own notification handlers still run and the
    notification still travels — but the invokers are installed once per *process*, not once per
    `ServiceCollection`. The registry they go into is static, so a second registration (a test, a rebuilt
    container, a host composing two collections) used to wrap the composed invoker in itself and turn one
    publish into two sends, then three.
  - **Upload parts are paired by the index they name**, not by sorting the part names as text — which
    mispaired every file after the tenth ("10" sorts before "2") and handed the handler somebody else's
    file without failing. A duplicate, missing or non-numeric part is now a 400 rather than a silent
    shift. `MaxUploadBytes` is applied to the request *before* the body is read, so an oversized upload is
    aborted mid-stream instead of being spooled to disk and reported afterwards.
### Added
- **`BulkInsertAsync` — loading many rows is no longer everyone's hand-rolled loop.** EF Core covers the bulk
  *update* and *delete* shapes with `ExecuteUpdate`/`ExecuteDelete`, and its own plan puts bulk **inserts**
  out of scope, so every app that seeds, imports or migrates data writes the same loop — usually the one that
  keeps every entity tracked to the end and commits nothing as a unit.

  ```csharp
  await db.BulkInsertAsync(products);
  await db.Products.BulkInsertAsync(products, o => o.BatchSize = 10_000);
  ```

  It runs through the context, so `Rask.Data`'s guarantees survive: `AuditingInterceptor` stamps
  `CreatedAt`/`UpdatedAt` and `DomainEventInterceptor` publishes each entity's events, for every batched row.
  What changes is the shape — batched adds (5,000 by default), change detection off, and the change tracker
  **cleared between batches**.

  The tracker clearing is what makes a large load flat rather than quadratic. Over 100,000 rows on SQLite:

  | approach | time | allocated |
  |---|---:|---:|
  | `SaveChanges` per row | 5.48 s | 2,472 MB |
  | `AddRange` + one `SaveChanges` | 1.22 s | 1,307 MB |
  | `BulkInsertAsync` | 976 ms | 1,105 MB |
  | `BulkInsertAsync`, `SkipChangeTracking` | **406 ms** | **141 MB** |

  `o.SkipChangeTracking = true` is the fast path: one prepared `INSERT` with the parameters rebound per row,
  no entity entry ever materialised — 2.4x the speed of the batched path and an eighth of its allocation, and
  13x/17x against the naive loop. It is opt-in because **no `ISaveChangesInterceptor` runs**, not Rask's and
  not yours. The writer stamps the audit columns itself, from the same `TimeProvider` the interceptor
  resolves, so a frozen test clock agrees across both paths; everything else it cannot honour it refuses by
  name rather than writing wrong rows — entities carrying domain events, store-assigned integer keys,
  store-computed columns, shadow properties, navigations, inheritance hierarchies, and a value-generated key
  left unset. A client-assigned `Guid` key — the `Entity<Guid>` shape — is fine, which needed care: EF marks
  those `ValueGenerated.OnAdd` by convention, so the guard has to test for store-supplied values rather than
  for `OnAdd`.

  `samples/Rask.Example.Sqlite` gains a third card that imports 10,000 rows each way and reports the
  elapsed time, so the difference is visible rather than only measured. Its `Reading` row derives from
  `Entity<Guid>`, which is also the shape that matters: the key is assigned on the client, where the
  sample's existing `WriteLog` has an int key SQLite assigns as the rowid — a shape `SkipChangeTracking`
  refuses on purpose.

  The obvious alternative is a trap, and the benchmark records it: a multi-row `INSERT … VALUES (…),(…)`
  loses at every packing, because each distinct row count is a new statement for SQLite to parse and
  Microsoft.Data.Sqlite binds parameters by name. Packed to SQLite's 32,766-parameter statement limit it is
  quadratic in its own parameter count — 192 ms / 7.2 s / 2.07 min for 1k / 10k / 100k rows.

  **Each batch commits on its own**, and that is the deliberate default: SQLite has one write lock, so
  wrapping a long import in a single transaction makes every other writer wait for the whole load while the
  WAL holds every uncommitted page. `o.SingleTransaction = true` asks for all-or-nothing when a load needs it.

  That mode — and any ambient transaction — **rejects entities carrying domain events**, which is a real trap
  found while building this rather than a theoretical one: `DomainEventInterceptor` publishes in
  `SavedChanges`, which inside a transaction runs *before* the commit, so a load that failed at batch 7 had
  already announced batches 1–6. Rather than ship that, the combination throws and points at `Rask.Outbox`,
  whose messages are written in the same transaction and drained after it commits.

  Two more consequences are deliberate and enforced: the context must have **no pending changes** (the load
  clears the tracker, so it refuses rather than silently discard unsaved work), and an **ambient transaction
  still owns the commit**, so the load composes inside one you opened. Under a retrying execution strategy a
  `SingleTransaction` load is one retryable unit with the sequence buffered for re-enumeration, while the
  per-batch default lets EF retry each batch on its own with no replay.
- **`Rask.SQLite.Litestream` can now prove the backup is *restorable*, not just that the replicator is
  running.** Every field on `LitestreamStatus.Current` describes the local child process. A replica
  silently writing to the wrong prefix, a bucket whose credentials were rotated to read-only, a `-config`
  file naming a database nobody writes to any more — all of them keep `IsReplicating` true and
  `RestartCount` flat, and all of them are discovered at the one moment that matters, which is the restore.

  ```csharp
  o.Verification.Enabled = true;                      // off by default — a pass costs a real restore
  o.Verification.Interval = TimeSpan.FromHours(24);   // a daily audit, not a health poll
  ```

  Each pass upserts a sentinel row into the live database (through Rask.SQLite's non-blocking
  busy-retry, so it waits out a busy writer without holding a thread), waits for replication to carry it,
  restores **to a temp path** that is deleted on every path, and checks the sentinel came back — then
  publishes `LitestreamStatus.Verification`: `Outcome`, `LastVerifiedAt`, `LastAttemptedAt`,
  `ReplicationLag`, `LastError`.

  **Three outcomes, not two.** `Inconclusive` means the sentinel had not shipped yet — replication lag, not
  a broken backup — and stays distinct from `Failed`, because a job that pages someone every time it races
  the sync interval is a job that gets turned off. Alert on a `LastVerifiedAt` that stops moving.
  `ISqliteBackupVerifier` is registered whether or not the schedule is on, for a pass on demand.

  `LitestreamCommand.Restore` gained an output path and now emits `-o` in **`-config` mode too**, where it
  previously emitted none — a verification restore there would have overwritten the live database with a
  copy of itself. The verification restore also deliberately omits `-if-replica-exists`, which turns "there
  is no replica at all" into a silent success.

  Verified end to end against a real object store, not just a fake: `scripts/verify-litestream-minio.sh`
  runs MinIO in Docker, replicates to it, verifies the round trip, and then destroys the replica to
  demonstrate `IsReplicating` staying `true` while verification reports `Failed`. `Rask.Dashboard`'s Backup
  card shows the same split — a "Last verified restore" tile beside the replication one, amber for
  unproven and red only for a genuinely broken restore (`IDashboardBackupProbe.VerificationAsync`, a
  default interface member, so existing probes keep compiling). Closes #751.

- **The operator dashboard runs on the `wasm-hosted` template.** `rask new --template wasm-hosted --ops`
  (and `--all-batteries`) now scaffolds the database and every DB-backed battery into the `.Server`
  project and mounts the dashboard there, server-rendered at `/_rask`, while the WASM client keeps every
  other route. Previously all of that was server-template-only: the `.Server` host had no database and
  served nothing but static files, so an app whose UI ran in the browser had no way to see its own queues,
  dead letters or logs.

  The batteries are the server template's, emitted from one shared source rather than a second copy, so
  the wiring order that matters — the outbox registered before the `DbContext` factory, so its interceptor
  joins the `SaveChanges` pipeline — cannot drift between the two templates. `--push` is the one battery
  this template does not take: its subscribe endpoints and the service worker that posts to them live in
  two different projects, which is a feature rather than a wiring gap, so it is left out rather than
  half-scaffolded.

  `Rask.Dashboard` ships `RaskDashboardShell` for it — a root component that renders the router and
  contributes the two document-level head tags the dashboard's layout cannot. A host serving a WASM bundle
  runs no components of its own, so `UseRaskServer<TApp>` had nothing to name; every such app would
  otherwise hand-roll the same four lines.
- **`TestFileBackend` + `TestServiceProvider` — an `OnFiles` handler can finally be unit-tested.** `Rask.Testing`
  shipped a `TestDownloadSink` but no file backend, and the gap was worse than a missing helper: a handler
  test could not fail. `FileListReader` resolves `IBrowserFileBackend` from the container and hands the
  handler an **empty list** when there is none, so a test that rendered a file input and raised its event ran
  the handler with nothing in it and passed on whatever it did. That is the same silent-empty failure the
  framework's quietest failure mode (#736), reproduced in every test.

  ```csharp
  var files = new TestFileBackend();
  var picked = files.Add("notes.txt", "hello world", "text/plain");

  var page = RaskTest.Render(new UploadPage(), TestServiceProvider.With<IBrowserFileBackend>(files));
  await page.On("#picker").FilesAsync(picked);
  ```

  The handler gets real files: `OpenReadStream()` returns the staged bytes and `maxAllowedSize` is enforced
  exactly as the real backends enforce it, so a component that forgot to raise the limit for a large upload
  fails in a unit test rather than on a real file. `.FormPayload(field, …)` covers a file inside a submitted
  form (the `FormData.Files` shape), `.Staged` lists what was added, and `.Released` records the framework's
  release call — the browser hosts drop their client-side references there and the server frees its upload
  slot, so a component holding a `RaskFile` past the handler is holding something already gone. An unstaged
  ref throws with the reason instead of quietly yielding an empty file.

  `TestServiceProvider` is the provider that makes it a one-liner: `RaskTest.Render` takes an `IServiceProvider` and
  `Rask.Testing` depends on no DI container, so every test needing one service had to pull in
  `Microsoft.Extensions.DependencyInjection` or hand-roll one. Registrations are by exact type with no
  lifetimes or scopes; pass a real container's provider when a test needs more.

  `RenderedComponent` gained `FilesAsync` on both interaction surfaces (`page.On(selector).FilesAsync(...)`
  and the first-handler shortcut), which builds the metadata payload a real client would send. Closes #737.

### Security
- **`rask deploy`'s firewall now actually covers Docker's published ports.** Setup enabled `ufw` and
  reported "deny everything else inbound", which was not true of anything a container publishes: Docker
  writes its own iptables rules, filtered through `FORWARD`, where ufw's `INPUT` rules never see them. The
  gap was worse than an unclosed port, because it was invisible — `ufw status` called a port denied while
  the internet could reach it. Anyone who followed the obvious instinct on a Rask-provisioned box and ran,
  say, a database with `-p 5432:5432` behind "a firewall" had published it.

  Setup now writes a fenced block into `/etc/ufw/after.rules` that hooks `DOCKER-USER` — the chain Docker
  consults before its own rules and never writes to itself — jumps to ufw's forward rules, and default-denies
  anything else being forwarded into a Docker bridge, allowing only the container port this deploy publishes.
  A box that already runs ufw gets the same treatment without its own allow list being touched, since that
  box is precisely the one whose owner believes the deny already covers Docker.

  Three details are load-bearing. The rules live in `after.rules` rather than a live `iptables` call because
  raw chains do not survive a reboot, and ufw reloads that file at boot. The allow is the port *inside* the
  container, not the published one — DNAT has already rewritten the destination by the time any filter rule
  runs, so allowing the host's port would deny every packet and take the app offline. And the deny matches
  the interface traffic is leaving on rather than an RFC1918 destination, so a box that also forwards for
  something else (a VPN, a router) is unaffected; containers still reach out and reach each other.

  Opening another container port is plain ufw: `sudo ufw route allow proto tcp from any to any port 5432`.
  Opt out with `--no-firewall`, which now also opts out of this. The block carries a signature of its own
  rules and ports that the host probe reads back, so a deploy that changes `--port` rewrites it and an
  unchanged box stays a no-op.

### Changed
- **BREAKING: the operator dashboard moved from `/_ops` to `/_rask`.** Rask already reserved `/_rask` for
  itself — scoped assets are served from `/_rask/a/{hash}.{ext}`, and the live runtime owns
  `/_rask/auth/redeem`, `/_rask/upload/{sessionId}` and `/_rask/download/{sessionId}/{token}` — so `/_ops`
  was a second framework-owned prefix carved out of an app's URL space for no reason beyond history. One
  prefix is now one prefix. Update any bookmark, reverse-proxy rule or IP allow-list that named `/_ops`;
  nothing else changes, and the pages, policy and panels are identical.

  The dashboard's own routes (`/_rask`, `/_rask/queues/{queue}`, `/_rask/cache`, `/_rask/logs`,
  `/_rask/system`) resolve through the router's catch-all, and the framework's endpoints are literal
  routes, so the two coexist by ordinary routing precedence. A page whose first segment collided with one
  of `a`, `auth`, `upload` or `download` would be shadowed and silently 404 — pinned by a test rather than
  left to a comment.

- **`Rask.Wasm.Hosting` and `Rask.Server` gained host-specific names for `AddRask`/`UseRask`.**
  `AddRaskWasmHost()` / `UseRaskWasmHost()` and `AddRaskServer()` / `UseRaskServer<TApp>()` behave exactly
  like the calls they forward to. They exist because an app referencing **both** hosts — which the
  wasm-hosted `--ops` scaffold now is — cannot say `AddRask()` and mean anything definite: both packages
  declare one on `IServiceCollection`, and C# does **not** report an ambiguity. The WASM host's overload
  takes no optional parameters and the server's takes two, so the "fewer defaulted arguments" tie-break
  silently selects the WASM one; the app then compiles, starts with no live runtime registered, and fails
  on its first request with a missing-service error naming a type the author never used.
  `UseRask<TApp>` resolves the other way, so the two collide in opposite directions in one file. The
  original names are unchanged and remain correct for an app with a single host.

- **The "no `IBrowserFileBackend`" diagnostic now names the fix.** It reported the silent-empty case (added
  in #736) but could only say "register a backend", because none shipped. It now points at
  `Rask.Testing`'s `TestFileBackend`.

### Fixed
- **The browser E2E gate's Gantt step could fail on a click that was never delivered.** The bar has to be
  clicked with `Force` — the bar's own `<text class="bar-label">` covers the `<rect class="bar">`, so
  Playwright's "receives pointer events" check never passes and an unforced click times out every time,
  even though a real click works (the label is inside the same `.bar-wrapper` the library binds to). But
  `Force` skips that check for *every* overlay, including the showcase's `sticky-top` `.app-navbar`: when
  the pre-click scroll parked the bar underneath it, the click landed on the navbar, the chart never saw
  it, and the assertion waited out its timeout for a log line that could not arrive — with nothing in the
  failure naming the navbar, so it read as a flake and cost a push.

  Plain retries do not fix that (measured: 6/6 still lost) — the bar *is* in the viewport, merely covered,
  so neither Playwright nor `scrollIntoView` finds anything to correct and every retry repeats the same
  dead click. The page itself has to be moved, by a hit test that says the bar's centre really belongs to
  the bar.

  But aiming alone is not enough either, which the gate then demonstrated: a run with a clean hit test
  still lost the click, and the chart's own `popup-wrapper` was left **empty** — proof the library was
  never sent the event. A hit test only describes the instant it ran, and this guide is thousands of
  pixels tall and still settling, so the layout moves between the aim and the click. So the step now aims,
  clicks, checks that the chart logged it, and re-aims against the current layout if it did not — and
  fails naming what is over the bar, or that the click keeps arriving nowhere, rather than leaving the
  next assertion to time out. Re-clicking is safe by construction: a click that landed never reaches the
  retry. Verified 6/6 on the host that failed, with the network fault that destabilised the layout still
  present.

- **Referencing two Rask host packages made the build fail in generated code.** `Rask.Server`, `Rask.Wasm`
  each pack their own copy of `analyzers/dotnet/cs/Rask.Generators.dll`, so referencing
  any one of them is enough to get the generator. Reference **two** and NuGet hands csc both copies at
  different package paths, which Roslyn reads as two distinct generators: both run, both emit
  `RaskBuilderSetters.g.cs`, and the build dies with `CS0101 ... already contains a definition for
  RaskBuilderSetters<Assembly>` pointing at a file the author never wrote. The shared core targets now
  deduplicate the analyzer payload by file name (the paths are what differ; the copies are byte-identical)
  before the compiler reads `@(Analyzer)`.

  Nothing hit this before because no supported composition referenced two host packages. The wasm-hosted
  `--ops` scaffold is the first, pulling in `Rask.Wasm.Hosting` (and with it `Rask.Wasm`) alongside
  `Rask.Server`.

- **Two hosts in one app fought over the scoped-asset endpoint.** `Rask.Server` and `Rask.Wasm.Hosting`
  both map `/_rask/a/{hash}.css` and `.js`. Two endpoints with an identical route template and identical
  precedence are accepted at startup and then throw `AmbiguousMatchException` on the first request for a
  scoped stylesheet — an app that boots clean and serves an unstyled 500. It is now mapped at most once
  per app, and the two handlers were made interchangeable first: both resolve a registry miss through the
  published bundle's baked files, so whichever host maps it serves the same bytes and the order of the two
  `UseRask` calls stops being load-bearing.

- **The SQLite docs said EF Core's `SaveChanges` transaction was `DEFERRED`. It never was.**
  `docs/sqlite.md` told you to wrap read-then-write EF work in `BeginImmediate` (nested inside
  `IExecutionStrategy.ExecuteAsync`) to avoid an unretryable lock-upgrade dead-lock. That ceremony
  guarded against nothing: Microsoft.Data.Sqlite composes its begin as
  `IsolationLevel == Serializable && !deferred ? "BEGIN IMMEDIATE;" : "BEGIN;"`, ADO.NET's default
  isolation is `Serializable`, and EF Core's `Unspecified` normalises to it — so every transaction EF
  opens on SQLite, implicit or explicit, already takes the write lock up front. The `IMMEDIATE` default
  Rails 8 had to add is something the .NET driver has always done.

  The docs, `llms.txt` and `BeginImmediate`'s own summary now say so, and redirect the read-then-write
  paragraph to the hazard EF users actually have: the read usually happens *outside* any transaction, so
  the risk is a **lost update**, whose fix is a concurrency token rather than a transaction mode.
  `connection.BeginImmediate()` is documented for what it is — the driver default spelled out at the call
  site, so it cannot quietly become deferred if an isolation level is passed later.

  New `RaskSqliteTransactionModeTests` pins the behaviour rather than restating it: a second connection
  with SQLite's busy handler off asks for the write lock, so a held lock reports instantly. It covers the
  implicit `SaveChanges` transaction (probed from a `DbCommandInterceptor` in the one window where the two
  modes differ — after `BEGIN`, before the first statement), the sync and async explicit transactions, and
  `ReadUncommitted` as the deferred negative control that proves the probe can tell them apart.
- **Everything in `Rask.Core` now works on every host, and a test says so.** Core is the shared component
  surface, so a component written once is supposed to run on Server and WASM alike. One of its contracts
  did not: the WASM host registered a `WasmAuthSignIn` that needed an `HttpClient` nobody registered. It
  failed only on that host, only at runtime, with nothing at compile time to warn you — a shared
  `LoginPage(IAuthSignIn auth, ...)`, the shape `rask new` scaffolds, failed DI outright.

  - **`HttpClient` on WASM.** `WasmHostBuilder` registers a lazy default (page origin as base address) with
    `TryAdd`, so `IAuthSignIn` resolves out of the box and an app that registers its own still wins.

  **The gate.** `RaskHostContracts` names the contracts Core promises everywhere, and each host's test
  project asserts its own bootstrap *resolves* every one — resolution rather than registration, because a
  descriptor whose dependencies are missing looks registered and still throws at the injection site, which
  is exactly how the WASM `HttpClient` hole shipped. A completeness test in `Rask.Core.Tests` partitions the
  whole `Rask.Core.Browser` namespace against the list, so adding a wrapper forces an explicit decision
  instead of quietly landing on one host.

### Changed
- **`Navigator.Download` and file-input errors name every host.** `FileListReader` returned an empty list
  when no `IBrowserFileBackend` was registered and said nothing at all — the framework's quietest failure.
  It now reports through `RaskDiagnostics`.

### Added
- **Background Sync — `IBackgroundSync`** *(WASM)*. Ask the browser to wake the app when connectivity
  returns, or on a recurring schedule, so an edit made offline is flushed without the user coming back to
  the tab and waiting. Wraps both `SyncManager` and `PeriodicSyncManager`; it rides the service worker a
  `--pwa` app already has, so there is nothing extra to wire.

  **The boundary is the design, so it is stated rather than glossed.** The browser fires the sync even
  with the tab closed — but the .NET runtime lives in the *page*, not in the service worker, so your C#
  runs only while a client is open. Rask's worker forwards the woken-up tag to every open client; with
  none open the registration is consumed unseen. Two things follow, and both are in the docs: re-request
  your tags at boot rather than treating a registration as durable queue state, and expect the real win
  on a *backgrounded* tab — still a client, so it wakes and drains the moment the network is back — which
  is the case most offline-first apps actually hit. Keep the work itself in `IIndexedDb` or OPFS and let
  the sync be the nudge to drain it, never the store.

  The service-worker handler deliberately stays **out** of the shared `rask-sw-shared.js`: a Server app
  has no client-side runtime to wake into, so shipping it there would advertise a capability that cannot
  fire. Registration goes through `getRegistration()` rather than `navigator.serviceWorker.ready`, which
  never settles when no worker is registered and would otherwise hang every call in an app that skipped
  the service worker. A sync that lands while the page is still booting is held for the first subscriber
  instead of being dropped — that is precisely the event an offline-first app most wants to see. Periodic
  sync has no request API at all (the browser grants it on its own terms), so `GetPeriodicPermissionAsync`
  is a check, not an ask. Closes #695.

- **Web Animations — `IWebAnimations`.** Run and control an animation on an element from C#, with no
  stylesheet and no animation library. Keyframes take the API's *object* form
  (`["opacity"] = ["0", "1"]`), which is what `Element.animate()` accepts natively and which serializes
  as the `Dictionary<string, string[]>` already in the source-generated JSON context — so nothing new
  has to be kept trim-safe.

  `StartAsync` hands back an `AnimationId` because an `Animation` object cannot cross interop, the same
  shape `MediaStreamId` uses for a `MediaStream`. Three choices worth knowing: on a browser without the
  API the handle is **invalid rather than an error**, so you can animate without feature-testing first;
  `WaitAsync` returns **`false` on cancel instead of rejecting**, because a cancelled animation is an
  ordinary outcome and awaiting it should not need a `try`/`catch`; and the handle is dropped once the
  animation ends, so a page that animates on every render does not grow the map forever.

  Unlike `IViewTransitions`, **reduced motion is the app's call here.** These are the app's own
  animations and only the app knows what each is for — refusing to run a loading affordance and refusing
  to run decoration are not the same decision. `IMediaQuery` already reads the preference. Part of #695.

- **View Transitions — the browser animates between the old and new DOM.** `IViewTransitions`, and it is
  the one Web API on this surface an app genuinely could not add for itself: a same-document transition
  has to **wrap** the DOM mutation, and in Rask the mutation is the framework's morph. There is no point
  in an app's code that sits around it.

  Enabling routes the live runtime's own commit — the diff apply and the full-document apply, on **both**
  the Server and WASM hosts — through `document.startViewTransition`. Style it with the standard
  `::view-transition-*` pseudo-elements; a stable `view-transition-name` makes the browser morph an
  element between routes rather than cross-fade it, which is what carries a shared header across a
  navigation.

  **Off by default, and off is byte-for-byte the previous behaviour** — the commit runs synchronously, as
  it always has. Deferring every app's DOM commit into a transition callback is a timing change, and the
  render queue chains on that commit, so nobody gets one they did not ask for.

  `prefers-reduced-motion` is honoured in the runtime rather than left to your CSS: what this drives is
  the browser's own default cross-fade, so there is no stylesheet of ours for the preference to switch
  off. `IsActiveAsync()` is deliberately separate from what you set — a settings toggle can be on while
  nothing animates, because the browser lacks the API or the reader asked for less motion, and a UI that
  conflates the two tells the user their preference was ignored. Part of #695.

### Changed
- **Pruned four `PackageReference`s that no code was compiling against.** The audit was a full sweep —
  every `PackageVersion` in `Directory.Packages.props` still has a referencing project, and every other
  reference either has API usage behind it or a stated reason to exist. Only these four had neither:

  `Rask.Html` carried `Microsoft.Extensions.Primitives`, `Microsoft.AspNetCore.Authorization` and
  `Microsoft.JSInterop` because the element family had them when it lived in `Rask.Core`. The types
  behind all three stayed in Core when the family moved out (#710), so the references have been dead
  since. Nothing needs re-declaring at a package boundary either: the project is `IsPackable=false` and
  every host references it with `PrivateAssets="all"`, so its dependencies never reached a nuspec —
  `Rask.Wasm` already surfaces Core's runtime deps itself, which is why *its*
  seemingly-unused copies of the same packages stay put.

  `Rask.Example.Auth.WasmCookie` referenced `Microsoft.Extensions.Logging` without logging anything.

- **`Rask.Example.Auth.Jwt` depends on the JWT token library instead of the bearer handler.** The
  sample issues and validates its own tokens (`JwtSecurityTokenHandler`) and authenticates through
  Rask's session pipeline — it never calls `AddJwtBearer`, and was reaching
  `System.IdentityModel.Tokens.Jwt` transitively through
  `Microsoft.AspNetCore.Authentication.JwtBearer`. It now references that package directly, pinned to
  the same 8.19.2 the handler resolves, so the two JWT samples share one IdentityModel graph.
  `Rask.Example.Auth.WasmJwt.Host` does wire the handler and keeps its `JwtBearer` reference.
- **`VirtualizeModel<T>(…)` is now `Virtualize.Items<T>(…)`.** It is the one hand-written generic entry
  point on the surface — a chain infers its type argument from the step that opens it, and `T` here
  comes from the *render delegate* — and it was reachable by simple name only because it sat inside the
  globally-imported `Generated` factory class.

  Moving it out under its own name did not work: `VirtualizeModel` also names the component's chain
  entry, an inherited member beats a `using static` import in simple-name lookup, and every call site
  failed with CS1744 as overload resolution landed on the entry instead. Renaming the method removes
  the collision, so the facade can live in its own class and be imported without the factory class —
  which is what unblocks dropping the `Generated` static imports.

  `Virtualize` is a global **alias** for that class rather than a static import, deliberately: a static
  import would put a bare `Items` in scope for every markup host, which is far too general a simple
  name to hand out, and importing the namespace is what the existing props notes rule out (a type beats
  a same-named builder entry, CS0119). An alias puts exactly one type in scope. Closes #684.
### Fixed
- **`docs/pwa.md` no longer implies WASM had background sync.** The "What you don't get on Server" note
  listed it beside genuinely WASM-only features when it existed on neither host; both that line and the
  Server/WASM summary at the top now say plainly that it is WASM-only, which is what shipped. Part of #695.

- **The local unit gate went red on changes that touch no server code.**
  `ShutdownDrainTests.Readiness_goes_unhealthy_while_draining_but_capacity_still_reports_capacity`
  asserted that an empty session store reports `Healthy` — but `RaskLiveHealthCheck` judges **memory
  load first** and lets it outrank the session count, deliberately, because what a session costs is a
  property of the page rather than of the number of them. Memory load is a property of the *machine*,
  so a full-suite run (parallel test hosts, WASM native relinks) pushed the process past
  `DegradedMemoryLoad` and an empty store reported `Degraded`.

  It passed standalone and failed in the full run, which is the wrong way round — the honest signal
  was buried under a red gate, on a diff with no `src/Rask.Server` changes in it. Encountered three
  times in one session, on three unrelated changes.

  The memory reading is now a seam (`MemoryLoadReader`, defaulting to the real one), pinned by the
  tests that are about the session branch. That also makes `DegradedMemoryLoad` and
  `UnhealthyMemoryLoad` testable **at all** — both are load-bearing in production and had no coverage,
  because the only input was whatever the host happened to be doing. Four tests added for them.
  Closes #732.

### Added
- **`Button` gained `command`/`commandfor`, and four elements gained the loading-priority attributes.**
  `command`/`commandfor` generalise what `popovertarget` does for popovers: the button names the element
  it acts on and the action to invoke — `show-modal`, `close`, `request-close`, `toggle-popover`,
  `show-popover`, `hide-popover`, or a custom `--name` that dispatches a `CommandEvent` — so a
  `<dialog>` opens and closes with no script on either side. Declared beside `PopoverTarget` and
  **appended** after it, because factory parameters are ordered by declaration span.

  `FetchPriority` lands on `Img`, `Link`, `Script` and `Iframe`; `Blocking` on `Link` and `Script`; and
  `ImageSrcset`/`ImageSizes` on `Link`, for `rel="preload" as="image"`. The one with a measurable story
  is `fetchpriority="high"` on the LCP image — the browser discovers it at the same moment either way,
  this moves it ahead in the queue. `blocking="render"` is the odd one out among loading knobs: an
  opt-**in** to blocking rather than the usual opt-out. And a responsive-image preload without
  `imagesrcset` fetches the wrong candidate, so the page pays for two downloads — the opposite of what
  the preload was for.

  `interestfor` is deliberately excluded: still experimental and Chromium-only, the same bar #694 used
  to leave out `<fencedframe>` and `<selectedcontent>`. Closes #729.
- **Every HTML global attribute is now reachable, and there is an escape hatch for the rest.** `Element`
  exposed `Id`/`Class`/`Style`/`Title`/`Data`/`Role`/`TabIndex`/`Aria`/`Ref`/`Draggable` and nothing else,
  so the remainder of MDN's global attributes were not verbose — they were **impossible**.

  The sharpest case was accessibility: `lang` existed on `<html>` only, so a page's language worked and a
  phrase inside it did not. Marking a run of text in another language is WCAG 3.1.2 *Language of Parts*,
  and without it a screen reader reads a French quotation with English phonetics. Now `Span.Lang("fr")`.

  Added as typed properties: `Lang`, `Dir`, `Hidden`, `Inert`, `Popover`, `ContentEditable`, `Spellcheck`,
  `Translate`. Plus **`Attributes`**, a verbatim dictionary shaped exactly like `Data`/`Aria` but with no
  prefix, which reaches microdata, `nonce`, `part`/`exportparts`, `accesskey`, `slot`, `inputmode` and
  whatever HTML adds next.

  **The cost is one reference per node, and nothing at all on the static path.** `Hidden` and `Inert` are
  two bits each of the flags byte every component already carries (as `Draggable` is). The other six
  share a **single** reference on the lazy live state — a side object allocated only by an element that
  actually names one of them — rather than a typed field each, because that state is allocated per node
  on a mounted page.

  Measured against the previous commit: a static render (`RenderAndBuildPayload`) is unchanged at
  35.31 KB, since a plain element keeps its live state null. A live render grows by 655 B–819 B
  depending on node count — exactly 8 B per node, the one added reference. Six typed fields would have
  been roughly six times that. An element naming no global renders byte-for-byte as it did before.
  Closes #693.
- **`Button` gained the attributes the spec defines**: the six form-override attributes (`form`,
  `formaction`, `formenctype`, `formmethod`, `formnovalidate`, `formtarget`), plus `popovertarget`,
  `popovertargetaction` and `autofocus`. `Input` has had all six form-* since it was written, so until now
  a submit button could override the form's action spelled as `<input type="submit">` but not as
  `<button>` — an inconsistency rather than a decision. `popovertarget` is the other half of the new
  `Popover` global. `Video` gained `controlslist`, `disablepictureinpicture`, `disableremoteplayback` and
  `loading`; `Form` gained `rel`. Closes #694.

### Changed
- **Attribute render order now has two more groups**: `id`, `class`, `style`, `title`, the plain globals
  (`lang`, `dir`, `hidden`, `inert`, `popover`, `contenteditable`, `spellcheck`, `translate`), `data-*`,
  `role`, `tabindex`, `aria-*`, `Attributes`, then tag-specific. The escape hatch renders last of the
  universal block precisely because its names are arbitrary — putting it earlier would make the documented
  order depend on what a caller passed.

  Two attributes moved as a result. `Bdo.Dir` and `Input.Spellcheck` were per-tag properties; they are now
  the inherited globals, so they render with the plain globals rather than among the tag-specific run
  (`Html.Lang`/`Html.Dir` likewise). The properties behave identically — same names, same types — but the
  emitted attribute ORDER changed for those three tags.
- **`BuilderRuntime.OwnPendingBit` raised 16 → 32.** The shared surface reached 19 folding properties and
  RASK041 fired, which is exactly what that diagnostic is for. **This is a rebuild-required change**: a
  component compiled against the old value numbered its own pending bits from 16, which the shared surface
  now occupies. The constant and the generator's copy of it must move together.
### Removed
- **Seven packages are withdrawn: `Rask.SQLite.Crdt`, `Rask.SQLite.Crdt.Sync`, `Rask.ObjectStore`,
  `Rask.Sync`, `Rask.Sync.Client`, `Rask.Postgres` and `Rask.SqlServer`.** Rask wires **SQLite and nothing
  else**. There is no offline-first sync story, no CRDT replication, no object-storage client, and no
  alternative database provider.

  **BREAKING for `Rask.Postgres` and `Rask.SqlServer`**, which shipped in `v0.20.0` and exist on
  nuget.org — an app referencing either keeps working on the version it already resolved, but there will
  be no further release of them. Point EF Core at your own provider instead: the [`Rask.Data`](docs/data.md)
  aggregates, [`Rask.Cqrs`](docs/cqrs.md) handlers and generated slices are provider-agnostic, so what you
  give up is the file-shaped machinery (Litestream, snapshots, `rask db backup`, the deploy volume) rather
  than your code. The other five were added after `v0.20.0` and were only ever published as nightly
  prereleases.

  **`rask new` no longer has a `--database` option.** SQLite is unconditional, so the flag, the wizard's
  "Database engine" question and the whole `DatabaseProvider`/`DatabaseCatalog` model are gone rather than
  left as a one-valued choice — along with the branches they gated: `rask db backup`/`restore` no longer
  refuses a client-server database, `rask deploy` no longer refuses a deploy with no
  `ConnectionStrings__App`, and the data volume plus its connection string are now always injected.
  `rask doctor` drops its `database` row, which could only ever say one thing.

  The `Rask.Example.Crdt` sample and its browser journey go with them, as do
  `tests/Rask.Providers.Tests` and `scripts/run-providers-local.sh` — a Docker-based gate racing 20
  instances against real PostgreSQL and SQL Server, which proved a claim about servers Rask no longer
  ships against.

  **The lease documentation survives the move.** `docs/databases.md` is deleted, but its "Running more
  than one instance" section is about jobs/mail/outbox leasing, not about the provider, and three
  processors name it in the message they log at 3am. It now lives in
  [`docs/scaling.md`](docs/scaling.md#running-more-than-one-instance) under the same anchor, so every
  existing link and log line still resolves. The sentence citing the deleted provider gate as evidence is
  gone with it — the mechanism is unchanged, but it is no longer tested against a real multi-writer
  server, and saying otherwise would be a claim with nothing behind it.

### Fixed
- **The pre-push gates blamed your branch for your machine.** When the `wasm-tools` workload is momentarily
  unresolvable every browser-targeting project fails at *evaluation* with `NETSDK1147`, and the CLI gate
  reported that as *"the code the CLI writes doesn't compile"* — measured at 0 `error CS` against 24
  `error NETSDK1147`. It sent two people hunting a scaffolder bug that did not exist.

  All four gate arms (browser E2E, CLI build, watch hot-reload, deploy) now run through one `run_gate`
  helper that keeps the full output and **reads the verdict off the log** instead of asserting one. The
  reading itself lives in `scripts/lib/build-failure.sh`, shared by the hook and by the gate scripts a
  developer runs directly, and sorts a log into four kinds: `code` (`error CS` present — the branch really
  is broken, and the gate's own message stands), `workload` (`NETSDK1147` and no compiler error — names
  the usual cause, a concurrent `dotnet workload install` from any session or worktree bumping the shared
  mono/emscripten manifests for the whole SDK band before the packs are restored, and prints the two
  commands that confirm it), `sdk` (any other `NETSDK` — still not your branch), and `unknown` (neither,
  so the gate did not fail at compiling at all: a failing journey, an assertion, a timeout). `CS` wins
  when both appear, because a real compiler error is the actionable one; the two machine kinds are the
  only ones that suppress the gate's own "what to do now", since pointing at your diff there is the whole
  defect. Closes #718.

  Four predicates over two counts decide whether a red gate sends you to your own diff or to `ps aux`, so
  it is table tested (`scripts/tests/build-failure-kind.test.sh`, run first by `run-unit-local.sh`) rather
  than left to the two cases someone happened to try — the same reasoning as
  `BakeScopedAssetsTask.IsNodeReuseBakeFailure`'s table test in #690. Both halves were verified by being
  made to fail: reordering the classifier to check `workload` before `cs` reddens the "CS wins" row, and
  letting the `workload` message carry the gate's branch-blame reddens the message assertions. `scripts/`
  and `.githooks/` also join the pre-commit path filter — a change to the gate logic was otherwise the one
  change that skipped the gate.
- **The CLI build gate deleted packages out of the machine-global NuGet cache.** `EvictFromGlobalCache`
  removes all 22 packed `Rask.*` packages at the version under test, which is load-bearing — without it a
  restore reuses a previously-cached nupkg of the same version and the gate silently tests stale bits
  (#534). But MinVer stamps the *same* version for the same commit in every worktree, so one gate run
  could delete what another worktree's build was restoring at that moment.

  The gate now restores into a private cache under `artifacts/`, scoped to the test invocation so the
  repo's own build still uses the normal one. Verified directly: 20 package directories used to land in
  `~/.nuget/packages` at the packed version and now none do, with the gate still passing 27/27. A test may
  not reach outside its sandbox to delete shared state.

  This is the prime suspect for #721, but **it is not a reproduction** — the gate ran green 8/8
  consecutively while investigating. That is consistent with the reported failure having occurred while
  nine sessions were saturating the machine, and it is why this is justified by the hazard being real in
  the code rather than by a repro. #721 stays open pending a sighting under load.

### Added
- **The per-element attribute gaps MDN turned up (closes #694).** `<button>` gains the six form-override
  attributes `Input` already had — `Form`, `FormAction`, `FormEnctype`, `FormMethod`, `FormNovalidate`,
  `FormTarget` — so a submit button can override the form's action written as `<button>` and not only as
  `<input type="submit">`, plus `Autofocus`, `PopoverTarget` and `PopoverTargetAction`. `<video>` gains
  `ControlsList`, `DisablePictureInPicture`, `DisableRemotePlayback` and `Loading`. `<form>` gains `Rel`.
  `FormAction` is sanitised like every other URL-valued attribute — it is a navigation target, so a
  `javascript:` value there would be script execution on submit.
- **`<var>`** — the one element MDN lists that Rask had no component for (part of #694). A variable in a
  mathematical or programming context; not emphasis (`em`) and not literal code (`code`).
- **`Element.Attributes` — the escape hatch for HTML's global attributes (part of #693).** Everything
  `Element` does not name is now reachable: `lang`, `dir`, `hidden`, `inert`, `popover`,
  `contenteditable`, `inputmode`, microdata, and anything vendor or experimental —
  `.Attributes(new() { ["lang"] = "fr" })`, HTML-encoded, `null` emitting a bare attribute like `Data`.
  `lang`/`dir` were the pointed case: WCAG 3.1.2 (Language of Parts) needs the element that *changes*
  language marked, and that could previously be written on `<html>` and nowhere else.
- **`tests/Rask.Example.Site.Tests`** — the landing app's first unit coverage. It bakes the new
  `ChainAnimation` component to `assets/rask-chain.svg` and compares byte-for-byte, so the README's asset
  cannot drift from the component (`RASK_BAKE_CHAIN_SVG=1 dotnet test tests/Rask.Example.Site.Tests`
  re-bakes). It also pins the three properties the standalone file depends on: the SVG namespace, a
  literal fallback behind every `var(--token)`, and the `rc-` prefix on every class and keyframe — an SVG
  `<style>` inline in HTML is document-scoped, so an unprefixed rule would restyle the page around it.
- The README's `Counter` example is now compiled by the snippet gate (`ReadmeChainSnippetTests` →
  `ChainSnippetTests`), which previously mirrored three README blocks that have since moved to
  [`docs/building-components.md`](docs/building-components.md).

### Fixed
- **The `add-html-tag` skill sent a new tag's test to a project that no longer holds any.** After the
  HTML/SVG family moved into `Rask.Html`, the skill's component path was updated but its TEST path still
  read `tests/Rask.Core.Tests/Components/{Tag}Tests.cs`, where the tag tests no longer live — they are in
  `tests/Rask.Html.Tests/Components/`. Its "attributes" reference also pointed at
  `src/Rask.Html/Components/Button.cs`, which does not exist: `Button` is one of the handful of tags
  `Rask.Core` retained for its own shell and error pages. Both corrected, with a note about the split so
  the retained tags are not taken as the pattern to copy, and the same path fixed in `CLAUDE.md`.
### Changed
- **The README opens on the chain instead of on a wall of badges.** It led with 31 NuGet version badges
  before showing a line of C#; it now opens with the logo, three named links (Site · Docs · Playground),
  an animated hero that types the `Counter` component out a character at a time — with the IDE's hints
  arriving mid-flight, the indexer tooltip as `H1[` is written and the member list plus its doc comment
  when the caret stops after `Button.` — and then the `Counter` source itself. 391 lines → 185.
  - The versions moved into a grouped **Packages** table (Package · Version · What it's for), so the same
    31 badges each carry a description instead of forming an unlabelled grid.
  - The batteries table, the "Why the One Person Framework" prose and the Rask-vs-Blazor table left the
    front page. Nothing was deleted: [`docs/one-person-framework.md`](docs/one-person-framework.md)
    already carried a richer version of the first two (it gains the missing `Rask.Dashboard` row), and the
    perf numbers live in the CI-enforced
    [vs-blazor baselines](benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor.md).
  - The Pages site is three apps — the landing page at `/`, the live showcase at `/docs/`, the playground
    at `/playground/` — and calling `/docs/` "the live demo" left the docs themselves unnamed. Both the
    README and the landing page's hero now name all three.
- **The landing page (`samples/Rask.Example.Site`) opens with the same animation.** `ChainAnimation` is
  built from the typed SVG family with CSS keyframes in an `SvgStyle` — `Rask.Html` ships no `<animate>`,
  so SMIL is not available and CSS is also what survives being loaded through an `<img>`. Rendered inline
  it inherits the site's theme tokens and follows the theme toggle; every colour is written
  `var(--token, #literal)`, so the same markup baked to a standalone file is self-contained for GitHub.
  Its base state is its final frame, so `prefers-reduced-motion: reduce` lands on the finished picture
  rather than a blank one.
- **The HTML/SVG element family moved out of `Rask.Core` into a new `Rask.Html` assembly.** ~155 tag
  components (`Div`, `Span`, `Table`, `Input`, the 41 `<svg>` elements, `Doctype`) now live in
  `Rask.Html.Components`; `Rask.Core` keeps only what its own engine constructs — `Text`, `Raw`,
  `Fragment`, `ErrorBoundary`, `Context`, `Mount`, the default error/not-found pages, and the shell tags
  those build from (`Html`, `Body`, `Head`, `A`, `Div`, `H1`, `P`, `Pre`, `Code`, `Details`, `Summary`,
  `Meta`, `Title`, `Button`). The dependency is one-way and now checkable: Core can no longer reach for a
  tag component by accident.
  - **Nothing changes at the call site.** `Rask.Html` is `IsPackable=false` and bundled into the
    `Rask.Server` / `Rask.Wasm` package `lib/` folders next to `Rask.Core.dll`, exactly
    like `Rask.Client`. `[assembly: RaskFactoryNamespace("Rask.Html.Components")]` — the same opt-in
    each host uses — makes a consuming app's generator surface the factories, so `Div.Class("card")`
    and `Generated.Img(…)` resolve with no per-file `using`.
  - **The factory namespace had to change** (`Rask.Core.Components` → `Rask.Html.Components`): the
    generator emits one `public static partial class Generated` per compilation, and Core still declares
    components, so sharing a namespace would put two `Rask.Core.Components.Generated` types in the
    reference graph (CS0433). Code naming a tag *type* explicitly needs `using Rask.Html.Components;`.
  - **A type that ENCLOSES a component must now be `partial`** (RASK036 reports it by name). Entries
    reached a nested component only by inheritance from `RaskMarkup`, where nesting is irrelevant; a
    referenced library's can only be injected, and the generator skipped nested hosts silently. It now
    re-opens the enclosing types as `partial`s around the host rather than dropping the chain.
  - **`Doctype` is an HTML component** and moved with the family. `HtmlSerializer` matches the
    `DoctypeComponent` base Core keeps, so it still emits the declaration without depending on `Rask.Html`.

### Fixed
- **The per-host builder-entry collision filter had nothing to filter against.** `EntryHostDecls` built
  every host declaration from a `Candidate` without its member names, so an injected entry could collide
  with a member the host declares or inherits (`Style`, `Data`, `Label`, `Cite`, `ClipPath`). Harmless
  while the tag entries arrived by inheritance — a member merely shadows one — and CS0102/CS0108 the
  moment any of them is injected.
- **`ComponentDocumentationTests` walked only `src/Rask.Core/Components`**, so moving the element family
  would have silently stopped checking ~150 tags. It spans both projects now.

### Added
- **`RaskBuilderEntryInjection`** (MSBuild, opt-out) splits the consumer half of the builder surface: the
  assembly still publishes its `RaskEntries{Assembly}` class for referencing compilations, but its own
  entries are not injected back into its own hosts, and it does not re-emit the universal setter surface.
  A component *library* that IS the entry set needs that — injecting each of ~155 tags into every other is
  O(n²) generated members colliding with what those hosts inherit from `Element`, and a second copy of the
  generic `Key`/`Class`/`Id` extensions makes them ambiguous to infer (CS0411).

### Added
- **A routable component is a `Page`, and it declares its route as a property rather than an attribute.**
  `[Route("/products/{id:int}")]` becomes `protected override string Route => "/products/{id:int}";`, and
  `[ParentRoute(typeof(Layout))]` becomes `protected override Type? Parent => typeof(Layout);`. The value is
  still read at compile time — constant expressions and `const`s work, and a computed override is
  **RASK047** rather than a page that silently never registers. Deriving from `Page` is now also what makes
  a class a valid `[RouteParam]`/`[QueryParam]` target, so RASK009/RASK010 say so.

  Reading the template out of the override rather than off the attribute also drops the runtime reflection
  `RouteTemplateResolver` used for the no-template `Route<T>()` overload.

- **Every page gets a generated `SomePage.Url(...)` and `SomePage.Go(...)`.** `Url` builds the typed
  `RouteUrl` — the same parameter list as the `Routes.SomePage(...)` factory it forwards to — and `Go`
  navigates to it, with an extra `replace` flag for the history entry:

  ```csharp
  A.Href(ProductPage.Url(42, sort: "asc"))["Open"]      // build the URL
  Button.OnClick(() => ProductPage.Go(42))["Open"]      // navigate to it
  ```

  `Url` returns a `RouteUrl` rather than a string deliberately: a string binds `NavigateTo`'s path-only
  overload, which **clears the query string**. The string is one implicit conversion away when you want it.

  These are C# 14 static extension members, so they need `LangVersion` 14 or later (the .NET 10 default) and
  the page's namespace imported — a fully-qualified `My.Ns.HomePage.Go()` with nothing `using`-ed does not
  resolve. Below C# 14 they are not emitted at all and `Routes.SomePage(...)` carries the app, because an
  extension block on an older compiler fails as a parse-error cascade inside generated code.

  **One caveat worth knowing before you reach for it:** inside a markup host the bare page name is the
  chain's `Build<TPage>` builder entry, not the type, so `HomePage.Go()` written in another page's `Render()`
  or handler does not compile. Qualify the receiver, or use `Routes.HomePage()` — which never collides. The
  short spelling is at its best outside markup hosts.

- **`Navigator.Current`** — the navigator of the handler currently running, published from the handler scope
  every host already enters around a dispatch. It is what lets a static `SomePage.Go()` reach the right
  session's navigator with no receiver to inject through; outside a handler it is `null`, and
  `Navigator.RequireCurrent()` throws the same actionable message the instance methods do.

### Fixed
- **Two analyzer assemblies could number a diagnostic the same, and nothing failed.** Roslyn's `RS1019`
  catches a duplicate id within ONE compilation, so the common case was already covered — but it is
  per-compilation, and Rask declares descriptors in three analyzer assemblies (`Rask.Generators`,
  `Rask.Cqrs.Generators`, `Rask.Jobs.Generators`). Declaring `RASK022` a second time from a different
  assembly builds clean, warnings-as-errors and all: the family then ships two meanings under one number,
  with one help link pointing at whichever doc section was written second.

  `DiagnosticDescriptorTests` could not have caught it either — its `AllDescriptors()` keys a dictionary
  on the id, so two colliding descriptors silently collapsed into one entry and every invariant it asserts
  passed. The new check enumerates without collapsing, deduplicating by the descriptor's own equality
  instead, so one descriptor reachable both through `SupportedDiagnostics` and through the static field
  behind it still counts once while two genuinely different ones stay two. Verified by injecting a
  cross-assembly duplicate: the solution built clean and the test went red.

  Prompted by RASK047 being claimed by two open branches on the same afternoon — the third id collision in
  a day, after RASK044/045 and RASK046.

### Fixed
- **Two analyzers had stopped firing altogether on chain-built markup — including an accessibility
  check.** `RASK022` (a list item without a `Key`) and `RASK023` (an `Img` without `Alt`) each identified
  their subject as *"a static method on the class named `Generated`"* — the factory. A chain has no such
  method: `Li[…]` is a property reference plus a children indexer, and `Img.Src("/x")` ends at the `Src`
  **setter**. So neither analyzer failed on the chain; neither ever ran. Every chain-written `<img>` in
  every consuming app was going unchecked for `alt`, and every keyless chain-built list unwarned — on the
  only spelling the docs teach.

  Their own tests could not have caught it: those compile without running the builder generator, so they
  cannot express a chain at all. `AnalyzersOnTheChainSurfaceTests` runs the generator first, and its two
  keyless/alt-less cases fail on the previous behaviour.

  Both now read the chain down to the entry that opened it and ask whether the step was named, via a
  shared `BuilderEntry.TryReadChain`. The factory path is untouched while it still exists, and every
  existing factory test still passes. What it deliberately does **not** do is flag any expression merely
  *typed* as a component: a static markup helper (`Ui.Badge(x)`) yields one and cannot be keyed, so only
  a factory call or a chain — the two things that can carry the step — are considered.

  Re-enabled, the pair found five real sites in this repo. Closes #704.

  This is the same failure mode `BuilderEntry.ChainedComponent` already records for RASK025/038/044, and
  it is worth stating as a rule: **a green build proves nothing about an analyzer still firing.** Only its
  tests do, and only if they use the surface people actually write.

- **A keyed row's own state followed its POSITION, not its item.** A child's identity inside its parent
  was its ordinal among entry-built siblings, and `Key` took no part — the parent's child map never read
  it. `Key` was only ever the diff codec's identity (`data-rask-key`), one layer above. So inserting an
  item at the top of a keyed list handed every later row the *next* row's instance: private fields, an
  edit buffer, an open/closed toggle, a subscription taken in `OnMount` — all of it moved with the slot
  rather than with the item. That is exactly the bug `Key` exists to prevent, happening one layer below
  where `Key` was being consulted. Older than the chain surface; `main` behaved identically.

  A keyed child is now identified by its key. `GetOrCreateChild` stops recycling by ordinal for any type
  its parent has keyed — it has to, because a key that is new this frame must get a FRESH instance rather
  than whichever item used to sit at that position — and the `Key` step claims the instance the key owns.

  **`Key` must now open a component's chain**, and that is enforced: **RASK046**. Claiming an instance
  discards the one the entry built, so a step written before `Key` lands on the discarded one and is
  silently lost. To make that expressible, `Key` is available before the required steps too — a component
  with required properties can settle its identity first (`BsToast.Key(id).Id(…).Message(…)`), which the
  chain previously had no way to say.

  **Elements are exempt**, and not as a carve-out: an element is re-specified in full every render — what
  its chain does not name, the deferred reset puts back — so its instance carries nothing and is never
  claimed. `Div.Class("row").Key(i)` is unchanged, which is the spelling used in its hundreds.

  RASK046 found **nine** live instances of the trap while this was being written — in `Rask.Bootstrap`,
  `Rask.Dashboard`, the samples and the benchmarks. `BsToaster` and the toast demos are the clearest:
  a toast list that changed shape rendered the *previous* frame's message and title.

  **What it costs, measured** (`LiveRenderRoundTripBenchmarks`, 100 keyed components reshuffled every
  iteration):

  ```
  | RenderKeyedList100_ShuffledEachIteration   Mean       Allocated
  | position identity (and losing state)       85.64 us    92.21 KB
  | key identity                               83.82 us    96.20 KB
  |                                            within noise  +4.0 KB (+4.3%)
  ```

  The allocation is inherent rather than incidental, and worth naming: a type its parent has keyed can no
  longer be recycled by ordinal, so each keyed row is constructed and then discarded when the key claims
  an earlier instance — about 40 bytes a row. Wall clock is unchanged within the error bars, so no claim
  is made about it. The ELEMENT path is untouched and stays at exact allocation parity with the factory
  (`BuilderSurfaceBenchmarks`: 19.7 KB on both arms).

  The obvious way to reclaim it is to defer construction until the key has been seen, which needs
  `Build<T>` to carry an unmaterialised child — a wider change to the struct that every generated setter
  takes. Left for later rather than folded in here.

### Changed
- **A form control's two modes are now mutually exclusive, on both surfaces.** A control's value comes
  from exactly one place — an expression it binds (`Bind`) or a value its parent owns (`Value`) — and the
  step you open the chain with now decides what the rest of it may say. Bound mode adds `Validate` and
  the `AfterBind` hooks; controlled mode adds `Value`, `Checked` and the `OnInput`/`OnChange` callbacks;
  everything else (`Placeholder`, `Type`, `Required`, `OnFiles`, the whole element surface) belongs to
  neither and stays reachable from both.
  - **These were accepted and then silently dropped.** Bound mode derives the rendered value and a
    checkbox's `checked` from the model and installs its own `oninput`/`onchange` write-back, so it never
    read `OnInput` or `Checked` — `Input.Bind(() => m.Name).OnInput(v => …)` compiled and did nothing.
    The mirror hole was smaller only because `AfterBind` was already off the controlled factory.
  - **Enforced by the type, not by an analyzer.** A form control's chain is now a
    `Build<TControl, Bound>` or a `Build<TControl, Controlled>` — the entry step fixes the mode, shared
    steps stay generic over it, and each mode's own steps are declared only on their mode. A step from
    the other mode is not offered in completion and does not compile. The generated factories carry the
    same split, so `Input(() => m.Name, OnInput: …)` has no such parameter either.
  - **BREAKING — a form control always opens on its mode.** A non-generic control pinned nothing, so it
    had no chain in front of it and `Bind` and `Value` were both plain setters one chain could take
    both of. It gets a seed now because it is a form control: write `BsCheck.Bind(() => m.Done)` or
    `BsCheck.Value(false)` where a bare `BsCheck` used to do.
  - Nothing outside form controls changes: an ordinary component's chain is the same `Build<T>`.
  - **RASK046 sees form controls again, and no longer misfires on them.** `KeyOpensChainAnalyzer` read
    the built type by *shape* — an arity-1 generic return — so a mode-carrying chain answered "nothing"
    and the rule went quiet for exactly the components it was added for (the `Bs` controls derive from
    `Component`, not `Element`). It reads both arities now. It also no longer reports the chain's
    *opening*: a seed step is what constructs the component rather than a setter that can be lost, and it
    necessarily precedes `Key` — `Check.Value(true).Key(1)` has no legal reordering, since the seed
    exposes no `Key` of its own.

### Added
- **Every public component is documented, and the documentation now reaches the call site.** A factory
  call is what you *write*, so that is where the docs have to be: hovering `Video(` says what `<video>`
  is and links its MDN page, and each parameter carries its own description.
  - **The generator forwards the docs onto the factory.** A component's `<summary>` becomes its
    factory's, and every documented property becomes a `<param>` on the matching parameter — so the
    ~490 property descriptions below are visible at the call site rather than only on the type. Without
    this the tooltip stayed blank however well the component was documented.
  - **The whole HTML and SVG element surface, referenced to MDN.** All 141 element components and the
    41 SVG ones now carry a summary and a link to their MDN reference page, plus per-attribute
    descriptions — including the ones that are easy to get wrong (`Meter`'s `Low`/`High`/`Optimum`,
    `Track.Kind`, `Iframe.Sandbox`, `Input`'s twenty-two types).
  - **The Bootstrap surface too.** Every `Bs*` component, enum and utility group is documented, with
    the accessibility caveats stated where they bite — a spinner needs a label, a close button needs an
    accessible name, colour must never be the only signal.
  - **Guarded against rot.** Tests pin that a documented component's summary and params reach its
    factory, that the MDN link on an element matches its tag, and that partial documentation stays
    warning-clean — `CS1573` fires per undocumented parameter once *any* parameter is documented, which
    would otherwise have broken every consumer's warnings-as-errors build.
  - **The two form validators say how to use them.** `DataAnnotationsValidator` and
    `FluentValidationValidator` render no markup, so a blank tooltip left nothing to go on: both now
    show the `Form` they belong inside, and `FluentValidationValidator` states that validators are
    de-duplicated by type — the first `IValidator` registered wins for the life of the form.
  - **A bundled DLL must ship its XML docs.** `Rask.Core` is unpackable and rides inside
    `Rask.Server`/`Rask.Wasm`'s `lib/`, so its `.xml` has to be packed by hand next to it. Dropping that
    line breaks nothing visible — the build is green and every consumer tooltip is silently blank — so a
    test now pins it for every DLL packed into `lib/`.
  - **The `CS1573` guard covered one generated file out of nine.** The pragma sat in the factory emitter
    alone, so the moment the chain setters gained `<param>` tags, `Rask.Core` itself stopped compiling.
    Every generated file now goes through one header emitter, including the files that document nothing
    today — adding a doc comment to one of those must not be able to break a consumer's build.
  - **The chain is documented end to end — all 1319 setters, up from 1138.** The chain is the syntax, so
    a blank tooltip there is the one that costs most, and the gaps were in the most-written places of
    all: `.Class`, `.Id`, `.Style`, `.Data`, `.Aria`, `.Role`, `.TabIndex`, `.Ref` and `.Draggable` live
    on `Element` rather than on any tag, so documenting all 141 element components missed every one of
    them. Now documented, with MDN's global-attribute and ARIA references.
  - **All 88 DOM events and 24 media events.** `.OnClick`, `.OnInput`, `.OnBlur` and their siblings are
    declared once and inherited everywhere; each now says what it fires on, and states the trap where
    there is one — `dragover` must cancel on *every* event or the drop never happens, `mouseover`
    bubbles where `mouseenter` does not, a positive `tabindex` reorders the whole page. Every MDN link
    was resolved against the live site, so `drag` points at `HTMLElement` and `reset` at
    `HTMLFormElement` rather than at a guessed interface.
  - **`Key`, and the form-control contract.** `Key` explains what it buys and warns off the loop index;
    `IFormControl<T>`'s `Bind`/`Value`/`Validate`/`AfterBind`/`OnChange` are documented once and reach
    every control that implements them.
  - **`<inheritdoc/>` now resolves — it never did.** Roslyn hands the generator the literal
    `<inheritdoc/>` element, so every async twin written as `<inheritdoc cref="OnValidSubmit"/>` emitted
    a setter with no documentation at all, beside a fully documented sibling. Nothing about the source
    looked wrong, which is why it survived. The generator now follows a `cref`, an override, and an
    implemented interface member — the last one covering the commoner case of an implementing member
    with no comment at all, which is how every form control declares `Validate` and `OnChange`.
  - **The attribute-bag overloads.** `.Aria("label", "Close")` and `.Data("test-id", "submit")` are how
    those attributes are actually written, and they were the undocumented overload beside a documented
    one. Each now names the prefix it renders under, so nobody writes it twice.
  - **The validation and routing API you call directly.** `EditContext` — which a page reaches for to ask
    whether a form is valid, or to attach a server-side error to a field — is documented across its whole
    surface, including the two rules that surprise people: `Validate()` *throws* rather than guess when
    any validator is async, and `AddValidationMessage` is idempotent per field and message. Alongside it
    `FieldIdentifier` (identity is by model *reference*, so a replaced model invalidates old ones),
    `ValidationEntry`, and the `[Route]`/`[RouteParam]` attributes that go on every page.
  - **`Rask.Server` and `Rask.WebPush` are documented to zero gaps.** Both packages now have no
    undocumented public member at all. The security-shaped ones say what they are and what they are not:
    `RaskUploadOptions` bounds what the server accepts and proves nothing about a file being *safe*,
    `SessionUserProvider` answers "who is this" and never on its own "may they", `VapidKeys.PrivateKey`
    is a signing key while rotating the pair silently unsubscribes every user, and a `PushSubscription`
    is a credential for reaching someone's device — dropped, not retried, when a send reports
    `ShouldDelete`.
  - **The Bootstrap chain is documented end to end — all 555 setters, up from 407.** The gap was the same
    shape as Core's: `Id`, `Class`, `Style` and `Aria` are *redeclared* on `Bs*` components (which extend
    `Component`, not `Element`), so `.Class(…)` on a `BsButton` was blank while the same call on a `Div`
    was documented. `BsBlock` is the base for every Bootstrap component, so documenting it there closed
    112 of them at once. `Class` now also states the thing worth knowing: extra classes are added
    *alongside* the component's own Bootstrap classes, never instead of them.
  - **The chain ENTRY is documented — the first thing anyone types.** `Div`, `Span`, `BsButton`: the
    identifier that opens a markup expression carried no documentation at all, while the factory and
    every setter after it were fully covered. Hovering `Div` said nothing; hovering `.Class(…)` one
    keystroke later explained itself. All three entry emitters now forward the component's summary and a
    `<seealso>` — Core 177/177, Bootstrap 62/62 — and the injected forwarders point at the canonical
    entry with `<inheritdoc>`, which the IDE resolves across assemblies where the generator cannot.
    `ErrorBoundary`, `Router`, `Outlet` and `Fragment` had no class summary to forward and now do.
  - **`Rask.Wasm`, `Rask.Wasm.Hosting`, `Rask.Sync` and `Rask.Wasm.Tasks` reach zero** undocumented
    public members, joining `Rask.Server` and `Rask.WebPush`. `WasmAuthSignIn.SignInAsync` now says why
    it throws rather than leaving it a runtime surprise: a WASM app cannot mint its own principal, so
    credentials go to a server endpoint and the server issues the identity.
  - **The generated `Routes` helpers are documented, with the template they build.** `Routes.UserPage(42)`
    is how a link is meant to be written instead of `"/users/42"`, and the helper now says so along with
    its own route template — so the reason to prefer it is visible at the point of use rather than only in
    the guides. The `Generated` factory class, which appears in every consumer assembly, is documented too.
  - **The guides reference MDN too.** Each of the 50 browser-API guides names the MDN page it wraps,
    and the element catalog in [`elements.md`](docs/elements.md) links all 104 tags. The paths are the
    post-move ones (`Web/HTML/Reference/Elements/{tag}`, `Web/SVG/Reference/Element/{tag}`) — the older
    shape now redirects, and a test keeps it from creeping back.

### Fixed
- **A mail-retry test waited on the wrong signal.**
  `Failing_send_is_retried_with_backoff_then_delivered` waited for the mail to be *sent*, then asserted
  the row's `ProcessedAt` — which the processor writes just after handing the mail to the sender. The wait
  could return inside that window, leaving the assertion to fail on a loaded machine while passing in
  isolation. It now waits for the row to be marked processed, which is what the assertions are about.
- **A flaky test that raced the clock rather than testing the behaviour.**
  `ValidatingIndicator_AfterPendingDropsToZero_StaysRenderedForStickyWindow` left `ValidatingStickyMs` at
  its 200 ms default, so everything between completing the validator and reading the flag had to finish
  inside that window — on a loaded machine it did not, failing twice (797 ms and 255 ms). It now sets the
  window explicitly, as the two sibling tests either side of it already did. Confirmed it still fails when
  the sticky behaviour is removed, so the race went and the coverage stayed.
- **The README advertised `rask generate`, removed in #672.** Two places in the README and one in the
  roadmap still listed it in the CLI's command set, while `docs/cli.md` says plainly that it does not
  exist.
- **`pwa.md` implied WASM had Background Sync.** It listed "no background sync" among the things a
  Server app gives up, alongside genuinely WASM-only features — but Background Sync is wrapped on
  neither host. Stated plainly in both places, with the gap tracked in #695.
- **Eight shipping packages were missing from the README.** `Rask.Signaling`, `Rask.ObjectStore`,
  `Rask.Postgres`, `Rask.SqlServer`, `Rask.Sync`, `Rask.Sync.Client`, `Rask.SQLite.Crdt` and
  `Rask.SQLite.Crdt.Sync` had no badge and no row in the package → project-type → entry-point table.
  `Rask.Signaling` appeared nowhere but `NUGET.md`, so the WebRTC epic's server half was effectively
  undiscoverable; `llms.txt` now covers the realtime surface as well.
### Fixed
- **A WebRTC signaling relay — `ISignaling` (client) and the new `Rask.Signaling` package (server).**
  `IWebRtc` deliberately doesn't pick a signaling channel; this is the channel for apps that don't already
  have one. `AddRaskSignaling()` + `MapRaskSignaling()` host rooms; `ISignaling.JoinAsync` joins one and
  relays opaque payloads to a named peer.
  - **It's a relay between untrusted peers, so it behaves like one.** Peer ids are minted by the server and
    never taken from the client; a message reaches only a peer in the *sender's own room*, checked at
    delivery rather than trusted from the message; nothing is ever echoed back to its sender; payload size,
    message rate, room size and room count are capped. A refused join says the same thing whether the room
    was full or the caller wasn't allowed in, so nobody can probe which rooms exist.
  - **Authentication is required by default.** A relay anyone can join is a way to reach other people's
    browsers, so opening it is a decision (`RequireAuthorization = false`), not an accident.
    `AuthorizeRoom` is the per-room hook for the question the framework can't answer — is *this* user a
    member of *this* conversation.
  - **The payload is an opaque string end to end.** The relay never parses an SDP or an ICE candidate;
    only the two browsers need to understand it, and parsing attacker-controlled SDP server-side would be
    surface for no benefit.
  - **Its own package, not part of `Rask.Server`.** It needs only ASP.NET routing and WebSockets, so any
    host can map it — including a static-file host serving a published WASM bundle.
  - **Rooms are in memory, per process.** Signaling is short-lived so there's nothing worth persisting, but
    a multi-instance deployment needs sticky routing on the signaling path; documented rather than implied.
- **Media over WebRTC, and a captured stream a Server app can actually keep — `IMediaStreams`.** A
  `MediaStream` can't cross interop, so the framework holds it under a `MediaStreamId`; `IMediaStreams`
  attaches one to a `<video>` or stops it. Neither needs a user gesture, so it works on every host.
  - **`MediaCaptureTrigger.OnStream` closes a real hole.** The `media.start` capability used to resolve the
    literal `"granted"`, so the stream it started was unreachable from C# — a Server-hosted app could not
    stop the camera it had just opened, re-attach it, or do anything else with it. The capability now
    resolves the stream's id and the trigger hands it to `OnStream`. `OnResult` keeps its
    `"granted"` / `"denied"` vocabulary unchanged, and a trigger with no callbacks stays fire-and-forget.
    The gesture-bridge demo grew the **Stop camera** button it could not have had before.
  - **`IPeerConnection.AddStreamAsync` / `RemoveStreamAsync` and `RtcHandlers.OnTrack`** send a camera,
    microphone or screen to a peer and receive theirs. `OnTrack` fires once per *stream*, not per track, and
    delivers a `MediaStreamId` that attaches like any other. A stream you add stays yours — disposing the
    connection stops sending it but leaves it running; a remote stream belongs to the connection and is
    stopped with it. Adding or removing renegotiates, so exchange a fresh offer/answer.
  - `IMediaStreamHandle.Id` (WASM) exposes the same id, so all three sources of a stream — `IMediaDevices`,
    the capture trigger, and a peer — speak one currency.
- **WebRTC — `IWebRtc`, the 48th typed browser API.** Peer connections and data channels, so two browsers
  can exchange data directly instead of through your server. It lives in `Rask.Core.Browser` and needs no
  user activation, so it works on **every host** — Server and WASM — from one injected service.
  - **You supply the signaling.** `RtcDescription` and `RtcIceCandidate` are plain serializable records, so
    the offer/answer/ICE exchange rides whatever channel you already have — a WebSocket, an HTTP endpoint,
    or `IBroadcastChannel` between two tabs. The demo runs both peers in one page, so signaling is a method
    call and everything else is real.
  - **Incoming messages and ICE candidates arrive in batches**, not one callback each. On the Server host
    every push from the browser costs an inbound WebSocket frame, and the host closes a socket past
    `RaskServerLimits.MaxInboundFramesPerSecond` (1000 by default) — a busy data channel delivered one
    message per push would end the session in under a second. The client buffers on a short timer instead,
    which bounds the push rate no matter how fast the peer sends, and WASM gets the same shape so the two
    hosts stay identical. Past a cap the oldest messages are dropped and counted rather than growing the
    buffer without bound; the count is reported through `RaskDiagnostics`. ICE candidates are never dropped.
  - **A channel buffers from the moment it exists and delivers once you call `ListenAsync`**, so a channel
    the remote peer opened loses nothing between arriving at `OnDataChannel` and being listened to.
  - **ICE server URLs are checked** — `stun:`, `turn:` or `turns:` only — and `IceTransportPolicy = "relay"`
    is documented as the way to stop a peer learning your local network addresses.
- **An attribute bag names its pair: `Div.Data("test-id", "primary")`.** The dictionary form still
  works, and is still the right thing for a genuinely large bag:

  ```csharp
  Div.Data("rask-no-restore")                    // bare: data-rask-no-restore
  Div.Data("test-id", "primary")
  Div.Data(("test-id", "primary"), ("state", "idle"))
  Span.Aria("label", "Close")
  ```

  The name-only form is the BARE attribute, which is how the framework's own opt-out flags are
  written — `.Data("flag")` renders `data-flag`, `.Data("flag", "")` renders `data-flag=""`, and those
  are different attributes.

  It is not only shorter. A `Dictionary` for one attribute is **three** allocations — the dictionary,
  its bucket array and its entry array — and a chain step re-assigns its property on every render, so
  that was a per-render cost on every element carrying a single `data-*`. The pair form allocates one
  object with two fields (`Rask.Core.AttrBag`), which `Element` writes straight from those fields:
  without that branch it would have traded three allocations for a boxed enumerator, since only
  `Dictionary<,>` has a struct enumerator to borrow.

  Measured over 100 elements each carrying one `data-*` (`AttrBagBenchmarks`): **80.7 KB → 63.52 KB,
  Alloc Ratio 0.79**, mean 11.32 μs → 10.09 μs. With three attributes each it is still ahead —
  87.15 KB → 75.43 KB. So the ergonomic spelling is also the cheap one, at both sizes.

  The steps are emitted for any property typed `IReadOnlyDictionary<string, string?>` rather than for
  a list of names, so `Data`, `Aria` and `FieldAria` all get them and so does anything added later.
  Lookup on the bag is a linear scan, which is the right structure at this size — it is written once
  and read once per render.

- **BREAKING — callbacks are plain delegates. `Handler`, `HandlerAsync`, `Handler<T>`,
  `HandlerAsync<T>`, `Carrier<TDelegate>` and the four `Callback*` delegate types are deleted.** A
  component property says the delegate it means:

  ```csharp
  public Action? OnPick { get; set; }
  public Func<Task>? OnSaveAsync { get; set; }
  public Action<int>? OnRate { get; set; }
  public Func<Product, Component>? Template { get; set; }
  ```

  Every one of those was a wrapper a moment ago — `Handler?`, `HandlerAsync?`, `Handler<int>?`,
  `Carrier<Func<Product, Component>>?` — and the wrappers existed for exactly one reason: a delegate-typed
  property on the chain's receiver swallowed its own setter. The `Build<TComponent>` receiver removes that
  collision at its source, so ~180 properties across `Rask.Core` and `Rask.Bootstrap` drop
  back to `Action` / `Func<…>`, and reading one back is `OnPick?.Invoke()` rather than `.Fn` / `.Invoke`
  / `.InvokeAsync`.

  Everything that rode on the wrappers goes with them: `AutoCallback.Wrap`'s duplicate named-delegate
  overloads, `Component.TryInvokeHandlerAsync`'s duplicate typed dispatch arms, `ElementEvents`'
  carrier views and their null-preservation dance, and the generator's `CarrierDelegate` / `AssignExpr` /
  `ParamType` mapping layer. `Validate<T>` and `ValidateAsync<T>` stay — they are domain delegates, not
  wrappers, and spelled out they would put `Func<T, CancellationToken, ValueTask<IEnumerable<string>>>`
  on the form surface.

  **A callback setter now keeps the property's name in every case.** The old rule dropped a leading `On`
  where it could (`OnRate` → `.Rate(…)`), so a handful of call sites move: `.Rate(…)` → `.OnRate(…)`,
  `.TaskClick(…)` → `.OnTaskClick(…)`, `.Save(…)` → `.OnSave(…)`.

- **RASK042 is retired**, not renamed. It reported a delegate-typed property whose setter could never be
  reached; there is no such property any more. The ID is not reused.

- **RASK026 recognises a callback by NAME rather than by type.** It used to key on the `Callback` /
  `CallbackAsync` delegate types, which said "framework event callback" on sight; a BCL delegate says
  nothing, so the signal is now the property — `On…`, plus `AfterBind`/`AfterBindAsync`. Narrower than
  what it replaces, not wider (a `Func<T, Component>` template is not an event and never was reported) —
  and it immediately found two live instances in `GestureBridgeDemo` that the type test had missed.

- **BREAKING — a chain's receiver is `Build<TComponent>`, so a callback property can be an ordinary
  delegate.** The entry hands back a chain rather than the component, and every step takes and returns
  one:

  ```csharp
  Div.Class("card")[Span["hi"]]                       // reads exactly as before
  BsButton.Color(BsColor.Primary).OnClick(Save)["Save"]
  ```

  The reason is a C# rule rather than a preference. Resolving `x.OnClick(handler)` looks `OnClick` up on
  the receiver's type; if that lookup finds a property of DELEGATE type it stops there and reads the call
  as an invocation (CS1593), and extension methods are never considered. While the receiver was the
  component itself, every callback property therefore needed a non-invocable wrapper around its delegate
  to stay out of the lookup's way. One step off the component, the lookup finds nothing and the setter
  binds — whatever the property's type.

  `Build<T>` converts implicitly to `T`, and a user-defined conversion may be followed by a standard one,
  so a chain reaches `Component` through it as well: markup, `Render()` returns, `Component` parameters,
  strongly-typed children collections and properties typed as a particular component all take a chain with
  no cast.

  It is a `readonly struct` over one reference, and on ALLOCATION that costs exactly nothing: a 50-row
  re-render (one entry plus three setter calls per row, 150 setter calls a frame) allocates **19.7 KB
  on both surfaces — Alloc Ratio 1.00**.

  **On wall-clock it is 43% AHEAD** — but only after a regression the surface introduced was found
  and fixed, so both numbers are worth keeping. Each row is its own run, so read the ratio rather than
  comparing the two `Factory` means (they differ by run-to-run drift):

  ```
  before   Factory 22.73 us   Entry 27.74 us   ratio 1.22   19.7 KB both, Alloc Ratio 1.00
  after    Factory 24.11 us   Entry 13.62 us   ratio 0.57   19.7 KB both, Alloc Ratio 1.00
  ```

  What it was: the eager reset runs at the entry, before the chain's setters, and puts every
  non-folding prop back so a callback named LAST render cannot survive into one that does not name it.
  On `Element` that is ~88 delegate fields, written unconditionally on every entry-built element on
  every render — and almost no element carries a callback, so nearly every one of those writes was
  assigning null over null. A single bit on `Component` (`FlagCallbackAssigned`, in the existing
  `_flags` byte, so it costs no memory) now records whether there is anything to put back, and the
  block is skipped when there is not.

  Worth recording what it was NOT, because the benchmark's own comment had predicted a different cause
  and it was wrong: the unconditional reset of the five body-setter props (`Draggable`, `Role`,
  `TabIndex`, `Aria`, `Ref`). Measured during the original investigation: narrowing that path to
  `Router.Routes` alone — the one property that genuinely derives state — moved the ratio 1.18 → 1.17,
  and removing the *whole* pending reset moved it to 1.11. Neither was the cost; the eager block was,
  and it is not the one the comment named.

  The ratio is recorded because dropping the generated factory removes the arm that produces it, so
  this comparison cannot be reproduced afterwards.

  Two consequences worth stating, because neither is reachable by reading the happy path:
  - **A component's static members are no longer reachable by simple name inside a markup host.** C#'s
    "Color Color" rule merges a type and a same-named property only when the property's type IS that type,
    which a chain's is not — so `MyComponent.Helper()` resolves to the entry and needs qualifying.
  - **`cond ? SomeChain : null` needs a target type**, because a struct and `null` share none. Assigning
    to a `Component?` local (rather than `var`) is the fix, and target-typed conditionals do the rest.

- **`Of<T>()` — the way into a generic component that has nothing to infer from.** Every other opening
  pins a type argument from an argument (`Input.Bind(() => m.Name)`), but a generic component is not
  obliged to be used generically: Rask.Bootstrap drives a bare `<input type=checkbox>` through
  `Input<string>` with no bind and no value. The generic factory had a no-argument overload for exactly
  that; a seed of pins alone dropped it, which is what left those sites on the factory. Required
  properties are not waived — only the type is settled.

- **BREAKING — `Form` is genuinely generic: `Form<TModel>`.** Its typing used to be a factory-only
  fiction — the component held `object? Model` and three `Delegate?` properties, and a `[FactoryGeneric]`
  overload narrowed them per call. That works only while a factory exists; a chain has no overloads to
  narrow through, so the generics moved onto the component.

  ```csharp
  Form.Model(_m)
      .Validate(CheckoutRules.Check)
      .OnValidSubmit(Save)
      .Class("vstack gap-3")[ … ]
  ```

  The submit handlers now receive the model itself instead of a `Delegate` the component had to
  `DynamicInvoke`, and the cross-field validator is a `Validate<TModel>` rather than something checked at
  runtime. The three-way validator fan-out is gone with them: it existed so a sync and an async validator
  could each be a required, correctly-typed PARAMETER, and as two setters (`Validate`/`ValidateAsync`)
  they simply coexist.

  **`Model` is required**, so a form with nothing to bind to no longer compiles — the guard that used to
  throw at first render is unreachable from the surface, and the ~14 plain-`<form>` call sites each bind
  the fields they were already posting.

- **`[AutoCallback]` — opt-in wrapping for a callback an element-derived component invokes itself.** An
  `Element`'s delegate props go straight to the DOM, where handler-owner resolution already repaints, so
  wrapping them would add a closure on the render hot path for nothing. `Form`'s submit handlers are not
  dispatched that way — its own bridge invokes them after validation — so without a wrap the component
  that supplied the handler never re-rendered. `[FactoryGeneric]`'s `TypedDelegateProperties` used to say
  this by hand for the one component that needed it; the attribute says it where the property is declared.
  Caught by `FormSubmitWrapTests`, which exists precisely to ask this question.

- **A property's summary now rides onto the step and the setter that write it.** A chain shows the
  SETTER in a tooltip, not the property — so hovering `.Placeholder(…)` said nothing at all while the
  property behind it was fully documented. The generator copies each property's `<summary>` onto every
  member it emits for it, taken from the compiler's own XML rather than the trivia, so an inherited
  `<inheritdoc/>` arrives already resolved. A property with no summary gets no doc comment: an empty one
  is worse than none, because it suppresses the fallback the IDE would otherwise show.
  `BsSelect`'s own properties are documented as the first pass, which is what the surface's most-used
  chain shows. The rest of the framework's properties carry `//` line comments rather than `///`, and
  converting them is per-property work that this only makes worth doing.

- **RASK044 — a builder chain that sets the same property twice.** The second call wins and the first is
  dead: it compiles, renders, and uses the last value, so the mistake survives review and shows up later
  as markup nobody can account for. Two writes to one property are always a merge artefact or a copied
  line that was not adjusted; if the value is conditional, compute it once and pass it. Reported once per
  chain, naming the property. Two *separate* chains that each name it once are ordinary markup and are
  not reported.
  An analyzer rather than a property of the type, and the boundary is worth stating: the chain already
  makes a required property impossible to omit and `Bind`/`Value` impossible to mix, because each step
  returns a type offering only what is still legal. Extending that to every setter would need one state
  per subset of the surface — 2^n over ~90 properties, where the required-property machinery pays 2^k
  over the few that are required.
- **RASK014 speaks about the chain now**, not the factory: `new` skips the first step's `GetOrCreate`, so
  the runtime cannot match the instance across renders and it re-mounts every frame. It also skips what
  the chain enforces — a component whose required properties are steps cannot be incomplete, and `new`
  can make one that is.

- **BREAKING — a builder chain is a state machine, and the type at each point says what is legal next.**
  A component with anything to settle first no longer hands back the component: it hands back a *seed*,
  and each step returns a state offering only what is still outstanding. The component — with its
  optional setters and its `[…]` children indexer — appears once nothing is.

  ```csharp
  BsSelect.Bind(() => _m.TeamId).Options(Teams).OptionValue(t => t.Id)
  BsSelect.Bind(() => _m.Plan).Options(Plans)     // the option IS the value
  BsRadioGroup.Options(AllPlans).Value(_plan)
  BsToast.Id(7).Message("Saved")                   // or .Message("Saved").Id(7)
  Div.Class("card")[…]                             // nothing required — unchanged
  ```

  Three things stop being possible to write, rather than being reported after the fact:
  - **A required property cannot be omitted.** It is a step, so the component does not exist until it is
    supplied. RASK038 has not gone, but within one compilation the type now says it; the analyzer's
    remaining job is a *referenced* library's component, whose RASK001-requiredness metadata destroys and
    the owning assembly republishes.
  - **Bound and controlled cannot be mixed.** `Bind` and `Value` are the two openings, so taking either
    leaves the other unreachable. A control bound to an expression *and* handed a value had two sources
    of truth and nothing decided which won; it used to compile.
  - **A type argument is never written by hand.** The step that opens the chain infers it — no
    `Input<string>()`, no empty `()`.

  Order is free where it can be: any outstanding required property may come next, which costs one state
  struct per reachable subset (at most two per component here). Type pins are the exception, and that is
  the language rather than the design — `OptionValue` is a `Func<TItem, TValue>`, so it cannot precede
  the `Options` that fixes `TItem`.

  The steps are **instance** methods on the seed, and that is load-bearing rather than stylistic: a state
  fixes its own type arguments, so a step on it introduces none, which lets `Options(IEnumerable<TValue>)`
  beat `Options<TItem>(IEnumerable<TItem>)` when the option *is* the value and fill in the identity
  projection itself. As extension methods both would have had to declare the state's type parameters,
  leaving them equally generic and the call ambiguous (CS0111) — which is what made this design look
  impossible when it was first tried.

  Seeds and states are `[EditorBrowsable(Never)]`, so they stay out of completion lists; they are named
  `RaskSeed_`/`RaskStage_`/`RaskPending_` and are never written by hand.

  **Costs nothing measurable.** `BuilderSurfaceBenchmarks` over a 50-row steady-state re-render:
  **19.7 KB/op on both surfaces, allocation ratio 1.00**, the chain 5% faster in time. A state is a
  struct holding the component it is building, so a chain allocates exactly what the factory did.

- **BREAKING — `BsSelect<TItem>` is retired; `BsSelect<TValue, TItem>` is the only arity.** Two arities
  cannot both hang off one seed: the step that fixes `TValue` would have to yield the finished component
  for one and a stage for the other from the same receiver and the same argument, and the two
  continuations are ambiguous precisely when the option type equals the value type — which is the whole
  of what the second arity was for. The common case costs nothing at the call site, because
  `.Options(items)` recognises it and supplies `x => x` itself.

- **BREAKING — eighteen raw-delegate properties are carriers now**, so their setters keep the property's
  own name: twelve `Template` props (the `GestureTrigger` family, `Shareable`, `ToastOutlet`,
  `ValidatingIndicator`, `ValidationMessage` ×3, `ValidationSummary`), `BsSelect.OptionValue`, and five
  sample `Log` probes. A raw delegate property is invocable, so a same-named setter can never be reached
  (RASK042) — and those seven components had no builder entry at all as a result. Renaming the setter
  instead was tried and reverted: it reached the property at the price of spelling the surface's most-used
  chains `.SetTemplate(…)` and `.SetOptionValue(…)`. Read one back through `.Fn` (or `.Invoke(…)` for a
  `Handler`).
- **Measured whether the cache and the job queue should get their own SQLite files.** They share the
  app's file today, and SQLite's write lock is per file — so the purge sweep and the job-claim batch
  take the lock a request needs. A new `split` workload in `benchmarks/Rask.Benchmarks.Sqlite` runs
  identical app writers under identical battery churn against one file and against three, with the
  batteries at their shipped defaults as the control arm. **The split does not pay for itself:** every
  difference sits at or below the control pair's own noise floor. The result that does hold is the one
  both arms share — the churn costs ~35% throughput at 8 VUs *whether or not the file is shared*, so the
  cost is the writes themselves rather than lock contention. Splitting the file does not split the disk.
  Written up in `docs/sqlite.md` under *One database file, or several?*, including the two limits on what
  the harness can show. The remaining arguments for splitting are size, per-file pragmas and blast
  radius — not latency; the outbox can never move, since it commits with the business change by design.

### Fixed
- **The quick-fixes for the newly chain-aware analyzers could damage code, found by reviewing the diff
  rather than trusting that green tests meant done.**
  - **RASK027's lightbulb deleted whatever enclosed the chain.** Making the analyzer fire on chains
    without touching its fix left the provider looking *upward* for an argument to remove, so
    `Wrap(Content: Button.OnClick(…).OnClickAsync(…)["x"], Label: "hi")` became `Wrap(Label: "hi")` —
    the component silently gone, and the result still compiling. The diagnostic is now anchored on the
    step's name rather than the whole chain, and the fix splices that one step out and never walks past
    the node it was given.
  - **RASK023's lightbulb could land the `Alt` on the wrong call.** `Wrap(Img)` became
    `Wrap(Img).Alt("")` — uncompilable, and the image still had no text alternative. It now acts only on
    the call the flagged node is the *callee* of.
  - **RASK014's lightbulb could trade its error for a worse one.** The bare entry it now writes only
    binds inside a markup host; in a service or a plain class `Widget` names the type and the rewrite is
    `CS0119`. The fix is withheld there — the error stands with its message instead. The old test proved
    nothing on this point: it asserted on text alone, inside a plain class.
  - A false positive of my own making in RASK021: the bare-identifier arm matched shell names on the
    identifier alone, so a local called `Body` in a root's `Render()` raised RASK021 on ordinary code —
    build-breaking under `-warnaserror`. It is now restricted to `Doctype`, the one part of the shell a
    chain writes bare; the rest are caught as element accesses.
  - The four analyzers' copies of a `Build<T>` unwrapper collapse into one `BuilderEntry.BuildOf`, which
    compares the resolved symbol instead of calling `ToDisplayString()` on every arity-1 generic. These
    run on `OperationKind.PropertyReference` — every property read in the compilation — so the old
    version allocated a string per `List<T>`/`Task<T>` read, per keystroke, in the IDE.
- **Two more analyzers were blind to the chain, found by auditing the rest rather than stopping at the
  ones that announced themselves.** These do not key on `Generated` at all — they test a TYPE, and a chain
  hands back `Build<T>` rather than `T`.
  - **RASK019 (`<head>` is a framework-managed slot) never fired on `Head[…]`.** Children passed there are
    dropped, silently. The analyzer also had **no tests at all**; it has them now, for both spellings.
  - **RASK021 (a root that renders the page shell) never fired on a chain.** It scans the root's `Render()`
    for *invocations* named `Doctype`/`Html`/`Head`/`Body`, and a chain invokes nothing — `Html[…]` is an
    element access and `Doctype` a bare identifier. It now recognises all three spellings, and the
    standalone-identifier arm is deliberately narrow so a local called `Body` cannot trip it.
- **Three analyzers were silently dead on the chain — the syntax the framework teaches.** Each identified
  its subject as "a static method on a class named `Generated`", which a chain never is: a chain's steps are
  extension methods on `Build<T>`, and its shortest spelling is a bare entry that is not an invocation at
  all. The build stayed green throughout, because a diagnostic that never fires breaks nothing.
  - **RASK023 (`Img` without `Alt`) never fired on `Img.Src("/logo.png")`.** An accessibility guard
    (WCAG 1.1.1) that was absent from every chain ever written.
  - **RASK022 (list item missing a `Key`) never fired on a keyless chain row.** `_items.Select(i => Li[…])`
    — the exact shape the guides teach — went unreported, so those lists reconcile by position and lose
    focus and input state on insert/remove/reorder.
  - **RASK027 (both the sync and async handler set) never fired on a chain.**
    `Button.OnClick(…).OnClickAsync(…)` silently dropped the async handler, which is the whole reason the
    diagnostic is an **Error** on a factory call.
  - **RASK034 (a `BsDataGrid` column with no `Field`) never fired on a chain either.** The column
    chooser addresses a column by the token read off `Field`, so a column without one can never be
    shown, hidden or reordered — it sits pinned with no menu row, silently, which is exactly the
    failure this diagnostic exists to catch. Verified against the real `Rask.Bootstrap` types rather
    than the test's stand-ins: removing a `Field` from a sample's grid makes it fire on the offending
    column and nothing else.
  - The chain branch is deliberately additive: the factory branch still owns `Generated.X(…)`, so nothing
    reports twice. Each new branch requires the operation's type to genuinely be `Build<T>` — without that
    the children indexer qualifies too (it is also a property reference) and every keyless row reported
    twice.
- **Both shipped quick-fixes wrote factory syntax.** RASK014's lightbulb was titled "Use the generated
  factory" and rewrote `new Widget()` into `Widget()`; it now produces the bare entry `Widget`, which is
  what RASK014's own message has been telling the reader to write. RASK023's inserted `Alt: ""`, a named
  argument — on a chain that is not merely stale but broken, so it now appends a `.Alt("")` step (and still
  inserts the named argument on a factory call).

### Changed
- **The chain is now what the docs, the README, the site and the playground actually teach.** #681 made
  markup a chain but left the teaching surfaces describing the generated factory, so a newcomer's first
  Rask code came from a page that taught the older spelling.
  - **The playground taught the factory end to end, and could not have taught anything else.** Its three
    gallery snippets and all eight guided-tutorial chapters are raw strings compiled only in the browser,
    so no build ever saw them — every one wrote `Div(Class: …)` / `Button(OnClick: …)`. They are chains
    now, `partial` like a real project's components, and the tutorial's bullets teach steps-versus-setters
    and `.Key(…)` instead of factory parameters. Chapter 5's `rask generate feature` reference is gone
    (the command was removed in #672).
  - **The playground's Roslyn driver never switched the builder surface on.** It builds a
    `CSharpGeneratorDriver` by hand and passed no `AnalyzerConfigOptionsProvider`, so
    `RaskBuilderSurface` read as absent — which is *false* — and the generator emitted no entries for the
    visitor's **own** components. `Div`/`Span` kept working (their entries are compiled into the
    referenced `Rask.Core`), which is exactly why nothing caught it. A chain over a component you wrote in
    the editor failed with `CS1955`; it works now, on the Run path and on the IntelliSense path alike.
  - **The README teaches the chain rather than only using it**, with the bound-versus-controlled rule and
    the step-versus-setter rule, and links `docs/building-components.md` from the guide map. The landing
    page's hero sample — the most-read Rask code there is — was still factory-shaped beside a live tile
    that was already a chain.
  - **RASK014's documentation had drifted from the message it ships.** The analyzer says "Components must
    be built through a chain"; `docs/diagnostics.md` still said "created via factory methods" and told the
    reader to call `Div(...)`. RASK022's own message said "set `Key:`" and now says "chain `.Key(…)`".
  - Two defects the doc snippets only gave up once they were compiled rather than read: the `[…]` indexer
    is an argument list, so the **trailing comma** in `docs/building-components.md`'s opening example was a
    syntax error, and its controlled-input example called `.Change(…)`, a step that has never existed
    (the property is `OnChange`). The README's and that page's Rask.Core examples are now compiled by a
    test, so the next such slip fails the build instead of a reader.

### Fixed
- **A dependency bump could fail the build of a project that has no scoped assets at all.** The
  scoped-asset bake refuses to write an empty bundle silently (#650) — but the check that decides whether
  "zero files" is a failure asked only whether *some* assembly had been skipped, on the reasoning that
  "zero written plus a skip is never a legitimate no-scoped-assets project". That reasoning was wrong, and
  the skip need not be an assembly that could ever hold a scoped asset.

  Bumping the `Microsoft.Extensions` family was enough to prove it: the app then carries a
  `Microsoft.Extensions.DependencyModel` newer than the one MSBuild already has loaded, `Assembly.LoadFrom`
  throws on identity, and `Rask.Example.Wasm.Jobs` — which genuinely has no scoped assets — failed a build
  that was entirely correct, with an error blaming node reuse.

  The check now also requires that the registry was **never read**, which is what actually distinguishes
  the two: if `Rask.Core` loaded and the registry was read, "zero files" is an answer rather than a
  failure, and `FailOnEmpty` still speaks for the projects that assert they should have produced some. The
  decision is extracted as `BakeScopedAssetsTask.IsNodeReuseBakeFailure` and pinned by a table test — it
  is three booleans that had already been wrong once in a way no build caught.

- **`rask new` scaffolded projects that could not compile, and every in-repo gate said they were fine.**
  `RaskBuilderSurface` — the switch that emits the chain entries — defaulted to **false**, and the repo
  turned it on only in its own `Directory.Build.props`. So the solution build, the 5,956-test unit gate,
  the warnings-as-errors analyzer build and the 64/64 browser E2E all passed, while the code the CLI
  writes did not build at all: with no entries emitted, `BsCard[…]` binds to the `Generated.BsCard(…)`
  factory **method** and a reader gets `CS0119` / `CS0021` / `CS0428` out of markup the framework itself
  generated.

  The default now lives in the shipped `src/Rask.Core/build/Rask.Core.targets` and is **true** — the
  chain is the surface the docs, the guides and the scaffolder are written in, so it cannot be opt-in.
  Set `<RaskBuilderSurface>false</RaskBuilderSurface>` to get the factories alone.

  Worth stating as a rule rather than an anecdote: **the only gate that crosses the package boundary is
  the CLI build gate** (`scripts/run-cli-build-e2e.sh`, run by the pre-push hook). The in-repo build
  *references* the projects instead of restoring them, so it never imports the packaged targets and
  anything about packaging is invisible to it. All 26 `rask new` cases were red while everything else
  was green.

- **The docs taught component classes that do not compile.** 59 component declarations across 28 doc
  pages — the whole tutorial included — were written without `partial`. On the chain surface a
  generator has to inject each non-framework component's entry into every type that might name one, and
  it can only inject into a `partial`; that is RASK036, and the tutorial gate builds with
  `-warnaserror`. Every one now carries the modifier, except the deliberate counter-example in
  RASK036's own section.

- **A whole page became "Something went wrong" whenever `Router` was served from the render cache.**
  Twelve browser journeys died on `Outlet() and Router rendering require an active route context`, and
  the symptom pointed at the wrong thing entirely — every one of them timed out waiting for a sidebar
  locator, which reads as "the sidebar did not render" when in fact *nothing* had: the whole document
  was an error boundary.

  `Router.Render()` assigns `ctx.Route`, and that is per-FRAME state — it exists only for the walk that
  set it, and nothing else in the framework produces it. `Router` had no cache opt-out, so a frame in
  which its props and state were both clean skipped `Render()` and left the frame with no route context
  at all. Any `Outlet` that *did* render in that frame — a freshly created one, say — then reached
  `RouteChainRenderer` with a null route and threw.

  `Outlet` had the same defect for a different reason and is fixed alongside: its `Render()` advances
  `RouteRenderState.Cursor`, a frame-global positional counter, so a cached `Outlet` fails to advance it
  and the next one to render pulls the wrong link of the chain — its own parent, nested inside itself.
  The invariant both now satisfy: **every participant in the route walk must run on every frame**, which
  is what `BypassRenderCache` is for. The page components the chain resolves to are still cached
  normally, so the expensive half is untouched.

  Not new to the chain surface, but only *reachable* there. The generated factory re-applied every
  property on every render, so `PropsDirty` was set unconditionally and nothing was ever really
  render-cached; `Router` re-executed every frame by accident. Writing only what the call site names is
  what makes the cache real, and this is what it exposed first.

  **It is not free, and the number is here rather than left to be rediscovered.** A new
  `RoutedRenderBenchmarks` renders the shape every routed app renders — a two-level chain, 20 rows,
  steady-state re-render as a live root — and prices the opt-out:

  ```
  | RoutedFrame          Mean       Allocated
  | Router/Outlet cached  3.410 us   3.86 KB   (and throwing — see above)
  | Router/Outlet bypass  3.649 us   4.18 KB
  |                      +7.0%      +8.3%
  ```

  Read that comparison carefully: the cheaper arm is not doing the same work faster, it is doing
  *less* work — a cached `Router` skips the route match altogether, which is exactly why it left the
  frame without a route context. The cost is one `RouteMatcher.TryMatch` (which allocates the chain
  list and the values dictionary) plus one chain entry per `Outlet`, per frame. The page components
  the chain resolves to are still cached normally.

  The obvious way to claw most of it back — memoise the match on `RouteState.Path`, since it is a pure
  function of the flattened leaves and the path — is deliberately NOT in this PR: it changes routing
  cache semantics, and doing that after the gate has gone green is how a late unverified change becomes
  a regression. Filed as #688.

- **The disposal demos' log stayed on "Empty — mount and unmount the probe." forever.** `DisposalDemoLog`
  renders a `List<string>` that the demo above it APPENDS to in place. The reference never changes, so the
  props fold (`EqualityComparer<T>.Default` — reference equality for a `List`) reported no change and the
  render cache replayed the stale subtree; the `<ol>` the E2E looks for was never emitted at all.

  This is the invariant `ExternalStateInvalidationTests` already pins, met from the other side: a
  component deriving its UI from state it does not own must either subscribe to a change source or opt
  out of the cache, and a bare `List` has no event to subscribe to. Same root cause as the `Router` entry
  above — the cache only started being real when the chain stopped re-assigning every property.

- **A type that declares a nested component can be a markup host now.** Opting one in produced **CS0102 in
  generated source** — `DependencyInjectionTests.GreetingComponent` the injected entry against
  `DependencyInjectionTests.GreetingComponent` the nested class — out of a one-line opt-in, with no
  modifier that fixes it. It cost 190 test classes their builder surface.
  Two mistakes, one on top of the other. The list of names an injected entry must leave alone was gathered
  **only for the injected delivery**, on the reasoning that an inherited entry a member shadows is merely
  hidden and `new` says so. That is true of the *framework* entries, which are the only half that arrives
  by inheritance — a consuming assembly's own components and a referenced library's are injected as
  **members into every host whatever its delivery**, so they need the list too. And the list was built from
  `INamedTypeSymbol.MemberNames`, which does not carry **nested type** names — which is precisely the
  collision, since an entry is named after its component. Both halves are fixed: the names are collected
  for every delivery, and from `GetTypeMembers()` as well as `MemberNames`.
  A nested component named after a tag still hides the *inherited* tag entry (CS0108) and still owes a
  `new` — `OutletTests.Section` is the example, and that one no modifier can avoid.
- **An auth gate built by a builder entry rendered as if nobody were signed in.** `Authorize[content]` and
  `Authorize.Authorized(user => …)` produced an empty page on a server-rendered first paint — the shape at
  the top of every gated route, failing silently and open-ended: no exception, no diagnostic, just missing
  content.
  The chain is the trigger and the commit is the cause. A chain writes only what it names, and only a
  *folding* prop goes through `BuilderRuntime.Track` — a chain that names nothing, or names only `Children`
  or a carrier-typed callback, never marks the child prop-changed. Marking is what allocated the child's
  `LiveState`, and `CommitEntry` read a missing `LiveState` as *"this child never reached `GetOrCreate`, so
  there is nothing to notify"*. So the deferred `NotifyParameters` that stands in for the factory's inline
  one never fired, `OnMount` never ran, and `Authorize` — which resolves its `IUserProvider` in `OnMount` —
  saw an anonymous principal.
  That guard's premise only holds when the render carries a **handle**: a live session gives every
  `GetOrCreate`'d child one, and setting a handle allocates the state. A handle-less render does not, and
  the two that matter are `ToHtml()` and the server-rendered first paint. The factory has no such hole
  because it calls `NotifyParameters` unconditionally.
  The fix is a cheaper signal rather than the obvious repair. Letting a null `LiveState` through would make
  **every element in an entry-built tree** allocate one at commit, which is the per-node memory the builder
  surface exists to save. Instead the generator now computes, per component, whether it overrides any of
  `Component`'s own `On*` hooks — read off the `Component` symbol itself, not a hard-coded list, so adding a
  hook to the framework cannot silently leave a component uncommitted — and hands the answer to
  `Entry<T>` / `EntryDi<T>` / `EntryRequired<T>`. A component that has a lifecycle claims its `LiveState`
  when the entry builds it; one that does not is left exactly as it was. **160 of Rask.Core's 166 entries
  opt out**, and `Element`-derived types are not exempt as a class — `NavLink` is an `Element` and overrides
  `OnMount`.
  The parameter defaults to `true`, so generated code from an older version is *correct* rather than fast.
  Measured: `BuilderSurfaceBenchmarks` is unchanged at **19.7 KB/op on both surfaces** (alloc ratio 1.00,
  the entry 14% faster in time), the absolute and relative allocation pins pass untouched, and a new pin
  covers the shape the fix actually acts on — ten lifecycle-bearing children, entry versus factory — so
  "it costs what the factory costs" is asserted rather than argued.

### Added
- **The migration does not have to hoist anything — and here is the test that says so.** A factory
  evaluates its arguments *before* it builds the component; a setter chain builds the receiver first and
  evaluates the argument after. `GetOrCreateChild` hands out identity positionally — one counter per
  parent, keyed `(Type, position)` — so `SlotHost(Payload: Leaf(...))` and `SlotHost.Payload(Leaf...)`
  give the same two children **different positions**. The open question was whether the rewriter had to
  hoist every component-valued argument into a local to preserve the old numbering, which would have
  meant converting expression-bodied `Render()` members to block bodies across the repo.
  It does not. `BuilderHoistTests` pins why: positional identity only has to be **stable render to
  render**, never equal to the numbering some other spelling of the same tree would have produced. A
  chain keeps every child instance across renders; the two orders are equivalent in HTML *and* in
  lifecycle; and a half-rewritten tree — a factory whose argument is already a chain, or a chain whose
  argument is still a factory — is stable in both nesting directions, which is what lets the rewrite land
  one project at a time.
  What it also pins is the one shape that *does* break, because that is the rewriter's rule: a `Render()`
  that emits the same subtree through the factory on one render and through a chain on the next
  renumbers its children, so the leaf is not found in the previous frame and mounts a second time — with
  **identical markup**, so nothing downstream would notice. No fixed source can do that; a `Render()`
  whose branches were converted unevenly can. The rewriter therefore converts a whole call site, and
  reverts a whole one, never half of a conditional.
- **Stage E, first pass: every call site that is already inside a markup host is on the builder surface.**
  Twenty-two projects, one commit each — the samples, `Rask.Bootstrap`, `Rask.Dashboard`, `Rask.Core`'s own
  markup, the component probes in the test projects, and the benchmark trees. `dotnet format` is clean,
  the solution builds warnings-as-errors clean, and the 5,969 unit tests pass.
  The evidence that matters is not the compile. `Rask.Example.Shop` was left **untouched** — it is the
  committed output of `rask new` and `ShopProvenanceTests` pins it to the CLI's templates, so it cannot
  move until the CLI and the `RaskBuilderSurface` default move with it — which makes its golden
  transcript an *independent* instrument: the whole document, byte for byte, across sixteen render paths,
  over a Bootstrap library that moved 252 sites and a Core that moved 23. It never changed.
  What is left, and why, because a named gap is worth more than a silent conversion:
  - **~2,350 sites in test classes and static helpers**, which are not markup hosts. Their own pass.
  - **~190 `Form` sites**, excluded outright: `Form` is becoming generic and every one of them moves again.
  - **~100 generic-component sites** whose entry is a *method* with a required argument (the
    `ValidationMessage<T>` / `BsDataGrid<T>` forwarders). A method entry displaces its own same-named
    factory, so those sites move with the entry rather than before it. Generic components that have a
    parameterless entry overload did convert — `Input<string>().Id(…)`, with the fully-qualified
    `Rask.Core.Components.Generated.Input(…)` the displacement used to force gone with them.
  - **~20 properties with no reachable setter at all** — every one a raw delegate whose name the
    generator's setter rule leaves unchanged (`Template` on the gesture-bridge triggers, on
    `ValidationSummary` and `ToastOutlet`; `Log` on the lifecycle probes). RASK042's shape, and a real
    gap in the surface rather than a limit of the rewriter.
  - **~15 sites where the entry name binds to something else** — a component's own `Label` property over
    the `<label>` entry, `Component.Head` over the `<head>` entry. Reverted by the tool, not by hand.
  Nine files opt out by marker (`rask-rewrite: keep the factory`): they hold both surfaces on purpose and
  assert the two agree, and converting the factory half would leave a test comparing a chain to itself —
  still green, proving nothing. That was found the hard way, when the first run over
  `tests/Rask.Core.Tests` turned the deliberately-mixed host in `BuilderHoistTests` into two identical
  branches and the test that pins the renumbering went red.
- **Stage E, second pass: the types that were not markup hosts are, and their call sites moved with them.**
  Entries are inherited members, so a test class and a static markup helper reached none of them — which
  is what put a third of the repo out of the first pass's reach. 260 types now take the surface directly
  (`: RaskMarkup` where the base slot is free, `[RaskMarkup]` where it is not), and ~1,400 further call
  sites moved. Format clean, solution clean, all 40 test assemblies green, Shop's golden transcript
  unchanged throughout.
  Four things that were not true before this pass ran:
  - **A name collision does not argue for `[RaskMarkup]` over `: RaskMarkup`.** When an attributed type's
    base slot is free the generator writes `: RaskMarkup` into its own generated partial, so the entry is
    *inherited* either way and a member named after a tag still hides it. `BsDataGridColumnsTests` took
    the attribute and hid `Thead` anyway. The base slot is the only thing that chooses between the two
    forms, and `new` on the colliding member is owed by both — six members across the repo now carry it.
  - **Becoming a host displaces every factory whose component has a *method* entry** — the bound controls
    and the generic ones — so the project deliberately does not compile between opting in and rewriting.
    The rewriter now finds a displaced factory by name and argument names instead of by binding, reads
    its constructed type arguments from the call site's own syntax, and keeps a bound site's `Bind`
    argument as the entry's own argument (`BsInput(() => m.Name).Label("Name")`).
  - **A type that declares a nested component cannot become a host at all.** Consumer entries are injected
    into the host's partial, one member per reachable component, named after the component — so
    `DependencyInjectionTests.GreetingComponent` the entry collides with the nested class of the same
    name. CS0102, in generated source, out of a one-line opt-in. Injection already skips a name the host
    already *reaches*; it should also skip a name the host *declares*. 190 types in `Rask.Core.Tests` are
    waiting on that.
  - **An entry-built component whose chain sets no folding prop never runs `OnMount`.** `Authorize[…]` and
    `Authorize.Authorized(user => …)` render as if nobody were signed in. A chain that sets only `Children`
    or a carrier-typed delegate never calls `BuilderRuntime.Track`, so the child never allocates its
    `LiveState`, and `CommitEntry` reads a null `_live` as "this child never reached `GetOrCreate`" and
    returns — so the deferred `NotifyParameters` that stands in for the factory's inline one never fires.
    The guard's premise only holds when the render carries a handle; a handle-less render (`ToHtml`,
    `RenderAsLiveRoot`, a server-rendered first paint) allocates no `LiveState` on its own. The factory
    has no such hole because it notifies unconditionally. **Not fixed here** — the obvious repair makes
    every element in an entry-built tree allocate a `LiveState` at commit, which is the memory work this
    design exists to protect, so it needs a cheaper "this came from an entry" signal than the one it has.
- **The seed surface's arity-2 pin is designed and works, and it needs one public name that does not exist
  yet.** `BsSelect<TValue, TItem>` cannot be reached by a single `.Bind(…)` — C# has no partial inference,
  so one call cannot pin both parameters. A two-stage chain does it:
  `BsSelect.BindOn(() => m.PersonId).Options(people)` pins `TValue` at stage 1 and `TItem` at stage 2, and
  it is **order-independent** — shared props accumulated before the first pin, between the two pins, or
  after both replay identically. Arity-1 keeps its single pin. Verified end to end in a scratch probe.
  The emission is mechanical. The stage-1 seed is generic over the kind, so the second copy of the 93
  shared setters is **+93 methods once, not per component**; only own props double, and only for arity-2
  components. The one new capability the generator needs is generalising "the inference property" to
  "which property pins which type parameter, and in what order".
  What blocks it is a name, and the language picked the fight: the arity-1 pin and the arity-2 stage-1 pin
  take the **same receiver and the same parameter** and differ only in return type —
  `error CS0111: already defines a member called 'Bind' with the same parameter types`. Making the pins
  kind-specific does not dissolve it. That fixes the collision between *different components* (`Input` and
  `BsSelect` can both spell it `Bind`, confirmed), but both **arities of `BsSelect` share a type name**, so
  they share the one entry member, so they share its seed type, so they share its kind. Same receiver.
  Two ways out, both public-API decisions: a second name for the arity-2 stage-1 pin, or retiring
  `BsSelect<TItem>` so one arity remains — which costs the common case an explicit `.OptionValue(x => x)`,
  since `OptionValue` is `required`. This is load-bearing rather than a nicety: the ~6 arity-2 call sites
  have **no builder entry at all today**, so introducing the `BsSelect` seed property displaces the factory
  they currently use and leaves them with nothing.
- **The rewriter will not collapse a two-surface comparison any more.** It converted the factory arm of its
  own parity test into a second copy of the entry arm — a test that still passed and proved nothing, caught
  by reading the diff rather than by anything failing. Ten files were relying on a marker somebody has to
  remember to add.
  The refusal is per **component**, not per file: a file may hold `Div` chains and a leftover `Form(…)`
  factory without either being a comparison, and that still converts. Only a component spelled **both ways
  in one file** is held back, and it is reported rather than skipped in silence. Across the four largest
  projects it fires once, on a true positive nobody had noticed —
  `LiveRenderRoundTripBenchmarks.DeepNode` builds itself through the entry at one site and through the
  factory at another, and converting the second would have changed what that benchmark measures. The
  `rask-rewrite: keep the factory` marker stays as the explicit, whole-file override.
- **A committed Stage E rewriter** (`tools/RaskBuilderRewrite`), because ~6,600 call sites is past
  hand-editing and the migration has to be re-runnable rather than remembered. It resolves each site
  against the **real generated factory signature** — a purely syntactic pass cannot; positional arguments
  have no names in the source — by rebuilding the project with `EmitCompilerGeneratedFiles` and reading
  the generator's own output back as ordinary source, which also gives it the entry and setter surfaces
  to check against. Deliberately not MSBuildWorkspace: the compilation must contain generator output, and
  reading what the compiler read is the more honest way to get it.
  Its safety net is a verification loop rather than a rule set. Every rewritten site carries a syntax
  annotation, the rewritten tree goes back into the compilation, and each resulting error is walked up to
  the site that caused it and reverted — so a shadowed entry, a non-`partial` host or a factory call
  standing where a statement has to be all come back as a *named* gap. An error it cannot attribute to a
  site abandons the whole file. It leaves `Form` alone entirely (`Form<TModel>` is pending, and every one
  of those sites moves again when it lands), leaves generic components alone (their entry is a *method*,
  which displaces its own factory inside a markup host — those sites move with the entry, not before it),
  and leaves anything outside a markup host alone.
- **BREAKING — the builder surface has a base a test class can derive from, and the entries moved onto
  it.** Entries are *inherited* members, which is the design (a static-imported property loses to a
  same-named type — CS0119 — while a member of the enclosing type wins), and its consequence was that
  the surface was reachable only from **inside a component**. A quarter of this repo's call sites are
  not in one: 1,399 in test classes, plus the static markup helpers. The framework entries now land on
  **`Rask.Core.RaskMarkup`**, and `Component` derives from *it* — the same 166 members (163 distinct names), one extra link
  in the chain, and a type that is not a component reaches them by deriving from the half of `Component`
  that is only the markup. `RaskMarkup` has no members of its own: no `Render()`, no lifecycle, no
  positional identity, no render cache. Emitting the surface a *second* time onto a separate base was
  the alternative, and two emissions of one surface are two things free to drift.
  A consuming assembly's own components still cannot ride there — a generator cannot add members to a
  type it does not declare — so those are injected into a markup host's own `partial`, exactly as they
  are into a component's. Measured on `Rask.Core.Tests`: a markup host costs **69 forwarders (9.9 KB)**
  of generated source, *the same as a component host*, because the 166 framework entries arrive by
  inheritance and not by injection. Injecting them instead costs **234 forwarders (26.9 KB)** — see the
  attribute below, which is when that happens and how rarely.
  A markup host is one that names `RaskMarkup` **directly**. Not transitively, and that is not a
  detail: making a shared test base derive from it turned all fourteen of its subclasses into hosts and
  demanded `partial` of every one — an error, under warnings-as-errors, in files that name no markup at
  all, out of a one-line edit to something else.
- **`[RaskMarkup]` — the markup surface for a type that cannot spend its base slot.** Deriving from
  `RaskMarkup` is the cheap delivery and stays the default, but it costs the one base slot C# gives a
  type, which is impossible when the base belongs to someone else (a fixture base from a test library,
  a `TheoryData<…>`) and impossible outright for a `static class`. The attribute says the same thing
  without one: put it on a `partial` type and it becomes a host.
  It **composes with the base-class form rather than replacing it**, and the generator picks:
  when the attributed type's base slot is still free, its generated `partial` declares
  `: Rask.Core.RaskMarkup` — a partial declaration may name the base class as long as only one does —
  so the entries arrive by the *same* inheritance and cost the same. Only when the slot is genuinely
  spent, or the type is `static`, are the 166 framework entries injected as members, forwarding to a new
  public `RaskEntriesRaskCore` class that Rask.Core publishes exactly as every other assembly already
  publishes its own. The author never chooses, and the expensive form is never paid unnecessarily.
  Measured, per host, on three real projects — the injected surface is a **fixed +165 forwarders /
  +16.9 KB**, so the *multiplier* is `1 + 166/E` and depends entirely on how many non-framework entries
  the project already injects:

  | project | hosts | inheriting | injected | ratio |
  |---|---|---|---|---|
  | `Rask.Core.Tests` | 68 | 69 fwd / 9.9 KB | 234 fwd / 26.9 KB | **2.70×** |
  | `Rask.Example.EfCore` | 8 | 68 fwd / 8.6 KB | 233 fwd / 25.5 KB | **2.96×** |
  | `Rask.Example.Shared` | 223 | 277 fwd / 39.7 KB | 442 fwd / 56.7 KB | **1.43×** |

  So it is a fallback by cost, not a default — but a bounded one, and it is per *host*, not per project.
  **Direct, not transitive**, and here by construction rather than by policy: `GetAttributes()` reports
  what was written on a type's own declarations and never what a base carries, so a subclass of an
  attributed host cannot become one.
- **`static class` can join the surface after all, and the two that had left it are static again.**
  `DemoRegistry` (~320 markup sites in lambdas) and `FieldErrors.Template` (a render-fragment
  *delegate*, which a component cannot be) had become sealed classes with a private constructor to reach
  the surface at all. With `[RaskMarkup]` they are `static partial class` again, which is what they
  always were and the whole of what they wanted; the private-constructor ceremony is gone.
  Neither needs `new` any more — and neither *could* have used it. An **inherited** entry whose name a
  member of yours shares is merely hidden, which `new` says out loud and the compiler accepts; an
  **injected** one is a second member of the same type (CS0102, which no modifier fixes), or, against a
  base you do not own, a silent hide (CS0108, an error under warnings-as-errors). So injection now
  leaves alone every name the host already reaches — its own members and its whole base chain's — and
  `DemoRegistry.Map` and `FieldErrors.Template` simply keep their names, with `<map>` and `<template>`
  not injected there. A static class **nested inside** a markup host still works too, and still needs
  nothing: C# simple-name lookup walks out through enclosing types.
- **RASK036 speaks about a host, not a component**, and covers both kinds of markup host; its message
  now says *what* a non-`partial` host loses, which differs — an inheriting host still has the framework
  tags and loses only the injected half, while an attributed one loses the whole surface, because the
  generated `partial` is where its base would have come from. **RASK043** names deriving from
  `RaskMarkup`, or `[RaskMarkup]` when the base is taken, as the fix, ahead of the `using static` that
  disappears when the factory does. `docs/diagnostics.md` updated for both.
  Across the repo, **15 of 211** markup-building test files declare at least one member whose name a tag
  entry occupies — the standing cost of putting 163 names into a type's scope, paid with `new` on the
  inheriting form and with nothing on the injected one.

- **Experimental — a builder surface that needs no `using` at all (spike).** `Div[...]` /
  `H1.Class("t")["x"]` alongside today's `Div()` factories, as a compiling proof of the design
  rather than a shipped feature. Entry points are `protected static` members whose name *is* their
  component type, so the type stays usable (C#'s "Color Color" rule) — and they are *inherited*
  rather than imported, because a static-imported property loses to a same-named type in scope
  (CS0119) while a member of the enclosing type wins. That is what removes the global usings.
  Setters are extension methods, which may share a property's name where a method declared in the
  type could not (CS0102). Both surfaces render byte-identical HTML and compose in one tree, so any
  migration can be incremental. Three constraints the spike pinned down and encoded: entries must
  route through `GetOrCreate` or they silently defeat the render cache; a callback prop needs the
  non-delegate `Handler?` carrier for the setter to share its name (a delegate property is invocable
  and wins), and the carrier must be nullable or it becomes a *required* factory parameter; element
  handlers must NOT be `AutoCallback`-wrapped, matching the generator's existing rule. The entries are
  emitted by `ComponentFactoryGenerator` (opt-in via `RaskBuilderSurface`, and only in the assembly
  declaring `Component`, since a generator cannot add members to a referenced type); setters are still
  hand-written. Generic, DI-constructed and `required`-member components are skipped — none has a valid
  no-argument entry. Emitting the full tag set surfaced the collision cost: 86 files need `new` where a
  component member, a private helper, a nested type or a `using` alias shares a tag's name.
  Since then the generator also emits the setters (shared `Element`/`Component` props once as constrained
  generic extensions rather than per tag) and injects entries for a project's own components into each
  consuming component's `partial` — a generator cannot add members to `Rask.Core.Component` from a
  consumer's compilation, and `using static` loses to a same-named type. A component that is not
  `partial` gets RASK036. DI-constructed components build through `ActivatorUtilities` inside
  `GetOrCreate`, and an `internal` component's entry is `private protected` (CS0053).
  **Bound form controls now collapse to one entry plus setters.** A property cannot be generic, so
  `Input<T>` / `Select<T>` / `Textarea<T>` get a static generic *method* entry whose single argument —
  the bind expression — infers `T`, plus a no-argument overload for plain/controlled use
  (`Input<string>()`). The generated factory needed three overloads per `IFormControl<T>` control for
  one reason only: `Validate` had to be a required, correctly-typed parameter, and a sync `Validate<T>`
  cannot share an optional parameter with an async `ValidateAsync<T>` without losing inference. On the
  builder surface the validator and the post-bind hooks are ordinary setters, so the fan-out disappears:
  `Input(() => _form.Name).Validate(ProductName.Validate).Id("name")`.
  To make that read naturally, `IFormControl<T>`'s four bound delegates now ride in the new
  `Carrier<TDelegate>` (`Rask.Core`) — a delegate-typed property *is* invocable, so `.Validate(rule)`
  would otherwise bind to the property instead of the setter (CS1593). The carrier's implicit conversion
  keeps plain assignment and every generated `Validate:` / `AfterBind:` factory parameter unchanged;
  read the delegate back through `.Fn`. Custom controls implementing `IFormControl<T>` must update their
  four bound property declarations (see `docs/building-form-controls.md`). Validators and post-bind hooks
  are never `AutoCallback`-wrapped; other setters now wrap on exactly the same rule as the factory.
  Unlike a property entry, a **method** entry hides the same-named factory inside a component body
  (C# stops at the first declaration space containing the name), so the `Input`/`Select`/`Textarea` call
  sites in `samples`, `tests` and `src/Rask.Cli`'s scaffolding moved to the builder chain; the
  plain/controlled calls that have no chain equivalent yet are qualified as
  `Rask.Core.Components.Generated.Input<…>(…)`. RASK025 and RASK026 now recognise the builder chain
  (`.Type(…)`, `.AfterBind(…)`) as well as the factory arguments.
  **Entries now fire the lifecycle the factory fires, and dirty the render cache the way it does.**
  A generated factory does three things — `GetOrCreate`, assign the props, `NotifyParameters` — because
  it knows where the assignments end. An entry could only do the first: `Div.Class("a").Id("b")` might
  take another setter or the `[…]` indexer, so a setter chain has no natural end and there was nowhere
  to notify from. The consequence was invisible while every migrated call site was a tag (an element is
  never reached through the render cache at all) and would have bitten the moment a *stateful* component
  was built through an entry: no `OnMount`, no `OnPropsChanged`, and — because `Live.PropsDirty` was
  never set — a child served from last frame's cached render after its props changed. Entries now defer
  that half to the first point at which the chain is provably finished: the moment the parent's
  `Render()` returns, which is also still before the child is walked, so this is the factory's ordering
  rather than an approximation. Each folding setter accumulates its own `EqualityComparer` delta in
  place of the factory's one-shot fold, with the same exclusions — `Key` is a reconciliation identity,
  and auto-wrapped callbacks, raw delegates and carrier props are a fresh closure every render, so
  folding them would report a change every frame and defeat the cache outright. Costs nothing measurable:
  the two flags land in `LiveState`'s existing padding (retained bytes per live session unchanged to the
  byte across the 0/5/200/1,000-row sweep), and the factory path only gains one bool test per component
  render (`LiveRenderRoundTripBenchmarks` allocation identical on all four shapes).
  **And a prop the chain stops naming now leaves the output, the way it does with a factory.** A
  generated factory assigns *every* parameter each render, so `Div(Id: "x")` on one render and `Div()`
  on the next puts `Id` back to null; a setter chain writes only what it names and the entry hands back
  the same instance, so `Div.Id("x")` → `Div` still rendered `id="x"`. That is silently wrong HTML at
  every conditional call site, not a missed callback, and it was reachable from any `cond ? A : B`.
  Entries now restore the state the factory would have left — but in two halves, because the reset and
  the `propsChanged` fold want opposite moments. Non-folding props (raw delegates, carriers, `Key`)
  are defaulted when the entry is created; they never call `Track`, so nothing can be disturbed. Folding
  props cannot be: blanking `Class` before `.Class("card")` runs would make the fold compare against the
  *default* instead of last render's value, so every constant prop would report a change every frame and
  no entry-built component would ever hit the render cache. Those are instead marked *pending*, each
  setter clears its own bit as it writes, and whatever is still pending when the parent's `Render()`
  returns is reset then — with the previous value still in place, so the fold stays exactly the
  factory's. What is *not* reset matches the factory too: a prop with a non-constant initializer
  (`= new List<>()`) is not a factory parameter at all, a constant initializer is restored to that value
  rather than to null, and a required parameter has no default for either surface to put back. The
  pending bits are split — the shared `Element`/`Component` surface owns the low 16, each component's
  own props the rest — so a component compiled against one Rask.Core cannot collide with a shared prop
  added in a later one. Free at rest: the pending slots live on a per-thread stack reused across renders
  rather than on the component, so retained bytes per live session are unchanged across the
  0/5/200/1,000-row sweep, `LiveRenderRoundTripBenchmarks` allocation is identical on all four shapes,
  and a pinned test holds an entry-built render at or below the equivalent factory-built one (a bound
  control is ~1.1 KB/render *cheaper* — one entry where the factory has a three-overload fan-out).
  **An event setter is now called what the property is called.** `Div.OnClick(Save)`, not
  `Div.Click(Save)` — the setter used to drop the `On` because a delegate-typed property *is* invocable,
  so the property beat the same-named extension (CS1593). `Element`'s whole GlobalEventHandlers surface
  (~88 sync/async pairs, plus `HtmlMediaElement`'s media events) therefore moved to the carriers, which
  now cover the argument-taking shapes too: `Handler` / `HandlerAsync` gain `Handler<TArgs>` /
  `HandlerAsync<TArgs>` over `Callback<TArgs>` / `CallbackAsync<TArgs>`. Those properties are computed
  views over the shared DOM-event slot rather than storage, which is what makes the swap free: the
  dictionary keeps holding the raw delegate, so handler registration, dispatch and emit order are
  untouched, and the carrier is a readonly struct wrapped and unwrapped around a reference that is
  already there. Assignment (`OnClick = Save`) and every generated `OnClick:` factory argument keep
  working through the implicit conversion — no call site in `src`, `samples` or `tests` needed a change —
  but code that *reads* a handler back off an element now calls it back — `el.OnClick?.Invoke()`. Element handlers are
  still never `AutoCallback`-wrapped: they go straight to the DOM, where handler-owner resolution
  already re-renders the owner, and a wrapper would be a closure per handler per render (pinned by an
  entry-vs-factory allocation test on a handler-bearing tree, not just a plain one).
  **And a control finally gets setters for what it inherits from its own base.** Only Rask.Core's
  `Element`/`Component` chain is emitted once as constrained generic extensions; everything else a
  component inherited — `HtmlMediaElement`'s `Src`/`Controls`/media events, `BsBlock`'s `Id`/`Class`,
  `BsFormControl<T>`'s `Label`/`Disabled`/`Size`/… — was skipped as "part of the shared surface" and got
  no setter anywhere, so a Bootstrap control could not be built through a chain at all. Those props are
  now emitted per component with the CONCRETE component as the receiver (a `BsFormControl<T>`-typed
  extension would return the base and end the chain), and they take part in the omitted-prop reset on
  the same rules as a component's own.
  **The same rename for every framework component, not just elements.** The remaining 81 prefix-dropped
  setters — `BsButton.OnClick`, `BsDataGrid`'s fourteen, `BsFormControl<T>.OnChange` (and therefore every
  Bs control that inherits it), `Input`/`Select`/`Textarea`/`Form`, `DragDrop.OnDrop`, the gesture
  triggers' `OnResult`/`OnColor`/`OnOutcome`, … — moved to the carriers too,
  so `.OnClick(Save)` is now the shape everywhere and `.Click(Save)` is gone. `IFormControl<T>`'s
  controlled pair changes with it (`Handler<T>?` / `HandlerAsync<T>?`), so a custom control must update
  those two declarations alongside its four bound ones. Assignment and every generated `OnClick:`
  argument keep working through the implicit conversion — no call site in `src`, `samples` or `tests`
  changed — but calling a callback back off a component is now `OnClick?.Invoke(…)`.
  Unlike an element's, these callbacks **stay `AutoCallback`-wrapped**: a component callback has no DOM
  handler-owner resolution behind it, so dropping the wrapper would leave the handler running while
  nothing re-rendered, with byte-identical markup. Pinned on both surfaces, against the element case, and
  in the entry-vs-factory allocation test (a wrapped component callback costs 1464 B/render on both).
  One trap the carrier brings is now closed at the source rather than per call site: its implicit
  conversion accepts a *null* delegate, so an omitted `OnClose:` would have arrived as a non-null carrier
  wrapping null and every `OnClose is not null` a component asks about its own callback (BsToast's
  auto-hide timer, BsDataGrid's controlled-mode gates) would have answered true for a handler nobody
  wired. Each carrier gains a null-preserving `From`, and every generated assignment goes through it.
  **And the deferred commit now holds up where a chain leaves the happy path.** A factory finishes its
  component before it returns; an entry is finished by the parent when the parent's `Render()` returns,
  which turns three ordinary situations into three separate promises. *A `Render()` that throws* —
  a supported path, since an `ErrorBoundary` catches it and renders a fallback — used to strand the
  entries it had already built on the per-thread slot stack, which is only ever popped by the render
  that pushed onto it. That both pinned a live subtree on a pooled thread shared across sessions and
  silently corrupted the *next* successful render of the same component: it pushes a second slot for the
  same instance, the stale one drains first, and its stale pending mask blanks a prop the new chain just
  set (`Div.Id("x")` rendering as `<div></div>`). The reset half now runs as the exception unwinds; the
  lifecycle half deliberately does not, so a hook cannot throw over the original fault. *An entry inside
  a `Head` override* was owned by the enclosing component, because the serializer collected head
  contributions from outside the component's own render scope — so an omitted head prop took an extra
  frame to disappear, and on a shell that renders once the slot never drained at all. A component's
  `Head` is now produced by its own render (`RenderForLive`) and read back by the walk, which also means
  it is evaluated exactly once per render rather than twice, and a `Context` read inside a `Head` now
  marks the right component as ambient-reading. *A lifecycle hook that builds something* — `OnMount`
  calling a factory or an entry — threw `Collection was modified` outright: the commit was enumerating
  the parent's child map, and building anything writes to it. The commit now runs over a snapshot and
  repeats until no new entry appears, so a hook's own entries are reset and notified like any other.
  All three ride the existing per-thread buffers, so allocation per render is unchanged (entry vs
  factory: 1528/1576/2072/1464 B on the plain, head-bearing, handler-bearing and component-callback
  trees; 3555 vs 4709 on a bound control).
  **And four ways the surface could disagree with itself are now closed.** *An entry is keyed by simple
  name* — factories are not, they live in a per-namespace `Generated` class — so
  `Features.Products.Card` and `Features.Orders.Card` cannot both be `Card`. The loser used to be
  dropped in silence; both are now reported (**RASK040**) and neither gets an entry, because which one
  the name should mean is the author's call, not the generator's. Worse than the silence was the
  disagreement it hid: the entry pass and the per-component *reset* pass applied different eligibility
  rules to the same simple name, so an entry could be handed the reset generated for the OTHER type —
  whose first statement is `var __c = (Features.Products.Card)__c0;`. An `InvalidCastException` at
  render time, out of source that compiled clean. Resets are named and deduplicated by fully qualified
  name now, and one predicate decides eligibility for both passes (which also stops a `partial`
  component whose declarations each carry a base list from emitting its setters twice — CS0111).
  *A prop the reset cannot restore no longer gets a setter*: a non-constant initializer (`= new()`)
  excludes a prop from the factory's parameters entirely, so the factory can neither set it nor put it
  back, while the builder could set it once and have it survive every later render — the same staleness
  bug the deferred reset exists to prevent, pointed the other way. *A component with a required factory
  parameter* (non-nullable, no initializer — RASK001) briefly kept its factory instead of getting an
  entry, exactly as a `required` member does: an entry has no argument to carry the value and nothing
  was resetting it, so `Widget.Title("x")` followed by a bare `Widget` silently kept the title and the
  first render left it `null!`. That restriction is lifted again below, once RASK038 could enforce the
  value and the reset could put it back. *And the `On`-prefix rule left a gap*: a delegate property whose name
  does not start with `On` got a setter of its own name, which C#'s invocable-member rule can never
  bind to — the property wins and the setter is unreachable dead code. `BsDataGrid`'s
  `RowKey`/`RowClass`/`ExpandedContent`, the `OptionLabel`/`OptionDisabled`/`OptionGroup`/`Filter` of
  the four option controls, `BsDatePicker`/`BsDateTimePicker.Disable`, `Authorize.Authorized`,
  `ErrorBoundary.Fallback` and `DragDrop`/`VirtualizeModel.Body` ride `Carrier<TDelegate>` now (reading
  them back is `.Fn`; assignment and every generated argument are unchanged), and anything left is
  reported as **RASK042**. The bound `Validate`/`AfterBind` setters were spelling those members as bare
  delegates, which ran the carrier's implicit conversion instead of `From` and reopened the null trap
  one layer up; they go through the carrier now. Finally the shared pending-bit budget has a guard
  (**RASK041**): 16 bits handed out in ordinal name order means adding one folding prop to `Element`
  silently pushes an alphabetically-later one (`Title`, `TabIndex`) onto the always-dirty eager path,
  with no compile error and no failing test. Allocation per render is unchanged on every pinned shape,
  and a carrier-borne render fragment costs the same through a chain as through the factory
  (1208 B/render either way).
  **BREAKING — a carrier hands you the CALL, not the delegate.** `Handler`, `HandlerAsync` and their
  argument-taking siblings stopped being positional records, so the delegate they carry is no longer a
  public `Fn` property: the public surface is `Invoke` — `button.OnClick?.Invoke()`,
  `await form.OnSubmitAsync?.InvokeAsync(data)`. It reads as what it does, and it is null-safe by
  construction on both halves of the problem: an unset carrier (`?.` never reaches it) and a carrier that
  wraps a null delegate, which the implicit conversion makes constructible and which a hand-held `.Fn()`
  would have thrown on. That is the same trap `From` closes at the assignment end, structurally rather
  than one call site at a time. `Carrier<TDelegate>` keeps its `Fn` public and is the deliberate
  exception: it names its delegate only by a type parameter, so it knows neither the arity nor the return
  type an `Invoke` would need, and a component declaring a value-returning callback prop
  (`Carrier<Func<T, string?>>? RowClass`) has to reach the delegate to use it. Costs nothing per render —
  an instance method on a readonly struct reached through `Nullable<T>` is a stack copy and a call
  (1208 B/render through a chain and through the factory alike, on a component that invokes both its
  callbacks every render).
  **A referenced library's components are reachable through the builder surface now, and the injection
  that carries them stopped being quadratic in bytes.** Entries were emitted only for the compilation's
  *own* components, so Rask.Bootstrap's `Bs*` — and anything from any third-party component library —
  reached the builder surface not at all: they were neither Rask.Core's (whose entries ride on
  `Component` itself, inherited by everything) nor the consumer's. Every assembly now publishes one
  canonical entry per component in a public `RaskEntries{Assembly}` class, and a referencing compilation
  reads that class straight off the assembly. Reading the emitted *members* rather than re-deriving
  entries from the referenced components is the point: whether a component can have an entry at all
  depends on its constructors, its `required` members and its RASK001 props, and the compilation that
  owns it already answered that — with the diagnostics reported. `[assembly: RaskFactoryNamespace]` is
  deliberately not the hook; it names a namespace so the `using static` emission can surface a satellite
  factory family, which is the mechanism the builder surface exists to remove, and Rask.Bootstrap never
  declared it. A name Component already carries, one the consumer's own components claim, and the same
  name from two libraries (RASK040) are the three cases that withhold a forwarder.
  That same class is what makes the per-component injection affordable. Entries are injected into every
  component's `partial`, so N components produce N×(N+M) members — 43,183 of them in the showcase, each
  carrying its own reset triple and its own pair of cached delegates. Each is now a one-line forwarder
  onto the canonical entry, which cut generated source for `Rask.Example.Shared` from 14.4 MB to 8.6 MB
  and its IL from 7.12 MB to 5.24 MB *while adding* 57 Bootstrap entries per class (199 → 256), and
  `Rask.Bootstrap`'s from 2.01 MB / 1.41 MB to 1.18 MB / 0.90 MB. Behaviour is unchanged by
  construction: the forwarder's body is the canonical entry, so every entry that existed still exists,
  with the same reset routines, the same pending mask and the same `GetOrCreate` identity.
  Injecting Bootstrap's bound controls does what a **method** entry always does — it hides the
  same-named factory inside a component body — so the `BsInput` / `BsDatePicker` / `BsTimePicker` /
  `BsDateTimePicker` call sites in `samples` and in `src/Rask.Cli`'s scaffolding moved to the builder
  chain, exactly as `Input`/`Select`/`Textarea` did. `BsCheck`, `BsSelect` and `BsMultiSelect` have a
  required factory parameter, so they have no entry and keep their factory untouched.
  **And each assembly now publishes which of its components' properties a chain must set**, as
  `[assembly: RaskRequiredProperties("Rask.Bootstrap.BsIcon", "Name")]`. This is the one fact about a
  component that an assembly boundary destroys: a member initializer compiles into the constructor and
  leaves no symbol-level trace, and a metadata symbol has no syntax to fall back on, so from a
  referencing compilation `string Title` and `string Title = ""` are the same symbol — RASK038 could
  police only the properties carrying the language's `required` modifier, which is the one kind metadata
  preserves and the one kind that was never the problem. It is not a rough edge that a better analyzer
  closes; the information is gone. So the compilation that owns the component publishes it — the same
  rule it already applies to decide whether the component may have a builder entry at all — and a
  consumer reads it back instead of guessing. Publish, don't re-derive, exactly as the entry host does:
  a second derivation could only be a divergent copy. The property carries a *name* rather than a
  `typeof` on purpose — a `System.Type` in an attribute blob is an assembly-qualified name the trimmer
  resolves and marks, which would root every component of a referenced component library in every
  trimmed app.
  **With that in place, a RASK001-required property no longer withholds the entry.** `BsIcon`,
  `BsProgress` and `BsCheck` can be built through a chain now, and are no longer components that would
  simply cease to exist when the factory is deleted. The restriction was covering two different
  problems with one veto, and they now have one answer each: *nothing enforced the property at the call
  site* — a factory makes it a required parameter and the language reports an omitted one, a chain just
  doesn't call that setter — which is RASK038's job, cross-assembly included; and *nothing put it back*
  — the factory re-assigns every parameter each render, the entry hands back the same instance, so
  `BsIcon.Name(Star)` on one render and a bare `BsIcon` on the next still rendered the star. The reset
  now covers required props, writing `default!`. That second half is the one no call-site analyzer can
  reach — RASK038 says the value is *absent*, the reset says last render's must not survive in its
  place — and it is why the two had to land together. The `required` **modifier** still withholds an
  entry, and this does not change that: `BuilderRuntime.Entry<T>` is constrained
  `where T : Component, new()` and a type with a required member does not satisfy `new()` at all
  (CS9040), so `BsToast`, `BsStat`, `BsSelect`, `BsMultiSelect`, `BsRadioGroup` and `BsCheckboxGroup`
  need a construction path that is not `new T()` before they can follow — a decision for those
  components' own API. No call site changed: all three get a *property* entry, and a property is not
  invocable, so the same-named factory still wins an invocation (only a method entry hides one).
- **`BsCheck.Value` defaults to `false` instead of being required.** It was the one property where
  RASK038 was wrong rather than strict: `Value` is required on the control's *controlled* factory and
  excluded from its *bound* one, and RASK038 reads a single entry, so `BsCheck.Bind(() => model.Done)`
  was reported as never setting `Value` even though `Render` only reads it when `Bind` is `null`. The
  fix is on the Bootstrap side rather than in the analyzer, because the property was mis-declared:
  every other control's `Value` is nullable and therefore optional, and `BsCheck`'s cannot be (the
  interface's `T?` collapses to `bool` for `T = bool`), so it needs the `= false` initializer its own
  source comment already described to reach the same place. An unchecked box is what the control
  renders for an unset `Value` anyway, so nothing renders differently. Source-compatible on the factory
  too — `Value` moves from a required parameter to one defaulting to `false`, and every call site names
  its arguments. Teaching the analyzer the controlled/bound split instead was the alternative, and it
  would have been a special case in a general rule for a single property that should not have been
  required in the first place. `BsCheck` is the only control affected: `Value` on `BsSelect`,
  `BsMultiSelect`, `BsRadioGroup`, `BsCheckboxGroup`, `BsFormControl<T>`, `Input<T>`, `Select<T>` and
  `Textarea<T>` is nullable already, so their bound chains were never asked for it.
- **The builder-surface migration was piloted on `Rask.Example.Shop` — three blockers, one of them
  silent and now fixed.** The RASK038/039 survey had come back "fires at zero sites", but every
  call site in the repo was still a factory call, so no chain existed for either analyzer to walk;
  converting one app by hand is what makes the exposure real. All 211 call sites in the sample were
  converted, the result was compared against a new whole-document transcript of every one of its render
  paths, and then reverted. What it found:
  - **`Router` rendered an empty page, and nothing reported it — now fixed.** `App.Render() => Router` —
    the shape at the root of every Rask app — produced an empty `<body>`. `Router.Routes` is a property
    whose *setter manufactures the default*: assigning `null` resolves `RouteRegistry.BuildTree()` and
    flattens the route leaves, and the factory gets there by passing `Routes: null` on every render. A
    chain writes only what it names, and the deferred reset that stands in for the factory's
    re-assignment was guarded by "is this already the default" — which a never-assigned `Routes`
    trivially is — so the setter never ran, no route matched, and the page disappeared. `Routes` is
    nullable, so it is not a required property and RASK038 had nothing to say.
    **The shape, not the component, was the bug**, so the fix is on the shape: for a property whose
    `set` accessor has a *body*, the reset now assigns unconditionally, because for that prop "reads as
    the default" and "the setter has run" are not the same statement. The fold keeps its meaning by
    comparing across the assignment — `before` against `after` rather than `before` against the literal —
    which is the same question for an ordinary auto-property and the right one here. Auto-properties keep
    the cheaper guarded form, so the ~90-prop shared `Element` surface pays nothing extra per element per
    render; the five props that do take the new form (`Draggable`, `Role`, `TabIndex`, `Aria`, `Ref`,
    all thin forwarders onto the lazy `LiveState`) are no-ops when they write a null. `Form.Model` and
    `Form.Context` — the same shape, both registering with the ambient `EditContext`, and `Model`'s own
    comment says it depends on the factory re-applying it every render — are covered by the same rule
    rather than by happening to be named.
    Measured, since the reset is the one part of this surface that costs per *render* rather than per
    call site: a new `BuilderSurfaceBenchmarks` renders the same 50-row tree through both surfaces as a
    live root, steady-state. **Allocation is identical between the two surfaces and unchanged by this
    fix — 19.7 KB either way** — and the entry stays slightly ahead on time (Entry/Factory 0.97 → 0.95,
    both inside the noise). That is also the first half of the entry-vs-factory parity number the design
    had never measured.
  - **A submit handler set through a chain never repainted the component that owned it — now fixed.**
    `Form` folds a typed submit callback into one untyped `Delegate?` property, and its *generic*
    factory — the overload every real call site uses — wraps whatever it is handed in `AutoCallback` on
    the way in. A builder setter is generated from the *property*, so it saw only the `Delegate?` and did
    neither half. A method group reaches it through its C# natural type, so
    `Form.Model(m).OnValidSubmit(SaveAsync)` compiled, read correctly, and silently skipped the wrap.
    The reasoning that this was harmless — a submit already arrives through a DOM handler whose owner
    resolution re-renders — is wrong, and the test that proves it is the shape where the two owners
    differ: a `Form` rendered by a *child*, with the handler belonging to an *ancestor* whose own markup
    is what changes. The typed factory repaints it; the chain left it stale.
    `AutoCallback.Wrap` gained an untyped `Delegate?` overload for the folded properties (sync stays
    sync, async stays async, and it costs the one `DynamicInvoke` the call site was already making), and
    the setters for a `[FactoryGeneric]` component's `TypedDelegateProperties` now wrap. They also stop
    folding into `propsChanged` — a wrapped callback is a fresh closure on every render, so folding one
    would report a change every frame and defeat the render cache for the whole subtree, which is the
    rule the auto-wrapped callbacks have always followed and which the bare `Delegate` shape had slipped
    past. As a side effect the pilot's workaround collapses:
    `.OnValidSubmit(AutoCallback.Wrap((CallbackAsync<T>)SubmitAsync))` is now just
    `.OnValidSubmit(SubmitAsync)`, for a sync or an async handler alike.
    What is still open in A4 is narrower than the pilot claimed: the *name* (there is no
    `OnValidSubmitAsync` setter, because one untyped setter takes both), and the compile-time tie between
    the model type and the handler type, which a fluent chain over a non-generic `Form` cannot carry. A
    generic setter cannot recover it — C# will not infer a type parameter that appears in a delegate's
    *parameter* position from a method group (CS0411), confirmed against a negative control.
  - **The sample cannot move before the CLI does, and the CLI cannot move yet.** `Rask.Example.Shop` is
    the committed output of `rask new`, and `ShopProvenanceTests` re-runs the real generators and
    compares — 14 of the 16 files touched are CLI-owned, so migrating the sample alone turns the README's
    provenance claim into a lie. Migrating `src/Rask.Cli`'s templates with it is not available either:
    `RaskBuilderSurface` defaults to **false** outside this repo, so a scaffolded app has no entries and
    builder-surface output would not compile for anyone. Sample, CLI and the default all have to move in
    one step.

  What the pilot cleared: the other 204 call sites converted mechanically and rendered byte-identically,
  and neither RASK038, RASK039, RASK037 nor the `CS0108` hiding fix fired once. One structural change to
  know before the rewriter is written — a factory evaluates its arguments *before* the component is
  created, a chain evaluates them after, so `Authorize(NotAuthorized: P()[…])[…]` builds the `P` first and
  `Authorize.NotAuthorized(P[…])[…]` builds it second. The markup is identical; the positional identity
  `GetOrCreate` hands out is not.
- **A generic component's entry can infer its type argument from any property, not only a form control's
  `Bind`.** A property cannot be generic, so a generic component's entry is a *method*, and its single
  argument is what pins the type argument. That was reachable only through `IFormControl<T>`:
  `CanHaveEntry`'s generic branch demanded one, and the runtime helper behind it could assign nothing but
  `Bind`. The consequence is not hypothetical — it is the next thing on the roadmap. Making `Form`
  generic (`Form<TModel>`, so a chain can carry the model type to its submit handler) would have given it
  **no entry at all**, silently removing `Form` from the builder surface. Verified rather than reasoned
  about: with `Form` made generic, the eight elements declaring `public new string? Form` immediately
  reported CS0109 — *"does not hide an accessible member"* — because the entry they were hiding had
  vanished.
  The rule is now "the property that pins the type argument", and a form control's `Bind` is one way of
  naming it rather than the only one; anything else generic uses the first factory-parameter property
  whose type *is* one of the component's type parameters. One emission serves both, and the runtime's
  `EntryBound` helper is gone with them — it could only ever assign `Bind`, so a second shape would have
  meant a second helper and a second eligibility rule, which is precisely how the bound path drifted from
  the general one before. The entry now assigns its own inference property inline, folding the change and
  clearing the pending bit exactly as that property's own setter does, so the entry and a later
  `.Prop(x)` cannot disagree.
  **Nothing moves today, deliberately.** The rule matches no component that did not already have an
  entry: `BsDataGrid<T>`'s properties are `IEnumerable<T>` and `List<BsColumn<T>>`, neither of which *is*
  `T`. It is also deliberately not general enough to match an `Expression<Func<T>>` property on a
  component that is not a form control — `FloatingInput<TProp>` and its two siblings in `samples/` have
  exactly that shape, and matching them would hand them a method entry and displace their factory call
  sites. Widening it is a migration to schedule, not a rule to relax quietly.
- **A `required` member no longer withholds a builder entry.** `BuilderRuntime.Entry<T>` is constrained
  `where T : Component, new()`, and a type with a required member does not satisfy `new()` (CS9040) — so
  `BsToast`, `BsStat` and `FluentValidationValidator` had no builder surface at all and would have ceased
  to exist the day the factory is deleted. That is a *construction* problem with a construction answer:
  requiredness is a compile-time check with no runtime enforcement, so `EntryRequired<T>` builds through
  `Activator.CreateInstance<T>()` and drops the constraint. It is a separate helper rather than the
  default precisely so every other component keeps the cheap `new T()`. What enforces the value
  afterwards is RASK038 on the chain, the same trade the surface already makes for a RASK001-required
  property — and the two are pinned together, since removing a setter from the probe now fails the build.
  Construction happens once per (parent, position), not per render, so nothing on the render path
  changes; the WASM Release publish stays at zero IL warnings, with the trimmer annotation on the type
  parameter that flows into the reflective construction.
  **Two things this does *not* unblock, both worth knowing before E3 counts on it.**
  - **A required *delegate* member still blocks, and cannot be unblocked here.** A raw delegate property
    is invocable, so `x.Template(fn)` binds to the property and a same-named setter can never be reached
    (the RASK042 rule). An optional property of that shape moves to a carrier; a required one cannot,
    because a carrier built from a null delegate is a non-null carrier wrapping null — exactly the state
    `required` exists to forbid. So `ValidationMessage`, `ValidatingIndicator`, `ValidationSummary`,
    `ToastOutlet`, `Shareable`, the `GestureTrigger` family and `BsSelect<TValue, TItem>` still have no
    entry. An entry for them would be constructible and never completable, which is worse than none.
  - **`BsSelect`, `BsMultiSelect`, `BsRadioGroup` and `BsCheckboxGroup` are held back deliberately**, for
    a reason that turns out to have nothing to do with required members: they are *generic*, a generic
    component's entry is a **method**, and a method entry hides its same-named factory inside a component
    body. Handing them one breaks ~20 multi-argument factory call sites in `samples/` on the spot
    (CS1501/CS1739). That is not an addition, it is a migration — and it contradicts the premise that
    both surfaces compile side by side, which is what E1 is resting on. `Input`/`Select`/`Textarea`/
    `BsInput` already paid that cost when the bound entries landed, which is why `BsCheck` reaches the
    controlled `Input` factory fully qualified.
  Also fixed on the way: `CanHaveBoundEntry` carried its own copy of the eligibility rule, which is the
  exact divergence its own comment warns about — both halves consult one predicate now.
- **Three diagnostics for the builder surface, where the compiler stops being able to speak for us.**
  Entries are members named after their component type, so they interact with name lookup in ways the
  factory never did — and the two failures that produces both surface as compiler errors that name
  neither the entry nor the fix.
  - **A quick-fix for `CS0108`** that inserts `new` on a member hiding an entry: a component property
    (`BsModal.Footer`), a private helper named after a tag (`Section(…)`), a nested type (`record Line`
    vs the SVG `<line>` entry), a field. Offered **only** inside a component — hiding in your own class
    hierarchy is your decision — and it puts `new` where `csharp_preferred_modifier_order` wants it, so
    the edit survives the next `dotnet format`. Deliberately a code fix rather than a
    `DiagnosticSuppressor`: a suppressor satisfies the compiler, but `dotnet format` ignores suppressors
    and applies the underlying fix anyway, so the format gate would never settle.
  - **RASK037** — a `using` alias hidden by an entry. `using B = …` loses to the `<b>` tag inside any
    component body and fails as **CS1061** at the *use*, naming a `B` nobody wrote; no code fix can
    reach it, because by then the alias has already lost the lookup. The analyzer says it at the alias,
    where the rename goes, and only when an entry actually claims the name.
  - **RASK038 / RASK039** — the builder half of RASK001. A required property is a required *parameter*
    on the generated factory, so the language reports an omitted one; in a chain it is just a setter
    that isn't there, and the component renders holding a `null`. RASK038 walks the chain and names what
    it never set. RASK039 covers the case the walk cannot answer — a chain stored in a local or a field
    can be continued anywhere — and reports the gap in the analysis rather than a wrong answer. RASK001
    stays: both surfaces exist side by side during the migration.

  RASK038 and RASK039 bound to nothing at first: the generator withheld an entry from exactly the
  components that have a required property, so a chain over a *generated* entry had nothing for them to
  find. They are what made lifting that restriction safe, and they are live now — `BsIcon`, `BsProgress`
  and `BsCheck` have entries, and their properties are enforced at the chain.
  All three, plus the `CS0108` fix, are now pinned against the emission itself — the real
  `protected static` entries in `Rask.Core`, and the `private static` ones the generator injects into a
  consumer's `partial` — rather than only against hand-written stand-ins for them.
- **An abstract component base can reach the builder surface now.** The entries are *inherited members*,
  which is the whole design — a static-imported property loses to a same-named type (CS0119) while a
  member of the enclosing type wins — and the consequence is that the surface is only reachable from
  inside a component. Injection targeted concrete components only, so every abstract base that composes
  other components (`BsBlock`, `BsFormControl<T>`, `BsSelectBase<TValue, TItem>`, `BsPickerBase<T>`,
  `PollingPanel`) could name no entry at all. Nothing said so: the calls bound to the
  factory instead, and `CS0119` with no RASK diagnostic is what an author would have seen the day the
  factory went away. An abstract class is collected as an injection **host** now — deliberately not as a
  candidate, since nothing can construct it, so it still publishes no entry of its own. The forwarders
  stay `private static`, which is what makes a base and its subclasses both carrying them legal:
  `CS0108` fires only for an inherited member the derived type can *see*, and by the same rule the base
  cannot stand in for its subclasses — each class needs its own copy, exactly as before. A non-`partial`
  abstract base is held to the same **RASK036** as any other component.
- **The markup-building static helpers were audited, and the ones that were really components became
  components.** A static class cannot reach the builder surface — entries are inherited members — so
  every helper that builds markup is either a component that was never declared as one, or genuinely a
  helper that keeps the factory. Converted, because each returns markup and nothing else: the showcase's
  `PageHeader`, `RaskLogo`, `GuideCards` and `DisposalDemoLog`, and the dashboard's `Loading` / `Empty` /
  `Error` / `Parked` panel states (now `DashboardLoading`, `DashboardEmpty`, `DashboardError`,
  `DashboardParked`). `DashboardParts` keeps only its two string formatters, which build no markup at all
  and so need nothing from either surface. Rendered HTML is unchanged — a component's `Render()` returns
  the same factory calls the static method returned — but each part now has a positional `GetOrCreate`
  identity of its own, which is what lets the render cache serve it.
  Left on the factory deliberately: `DemoRegistry` (a `Dictionary<string, Func<Component>>` — a lookup
  table, not markup), `FieldErrors.Template` (a render-fragment *delegate* handed to
  `ValidationMessage(Template:)`; a component cannot be a `Func<…, Component>`), `TierStaticHelper` (it
  *is* the sample that documents the tier-0 static helper), `Rask.Bootstrap`'s `PickerParts` (half of it
  is culture-driven date math, and its markup half takes ten parameters including predicates and
  callbacks — its natural home is `BsPickerBase<T>`, which after this release is an injection host, not a
  set of new components), and `Generated.VirtualizeModel<T>` (it *is* the factory).
  Two things this turned up. `PageHeader.Title` is a **CS0108** hiding the `<title>` tag's inherited
  entry — the collision the surface creates, resolved with `new` exactly as its quick-fix does. And
  inside `src/`, a *new* component is reachable **only** through the chain: framework projects opt out of
  `RaskGlobalUsings`, so the generated factory is not in scope there, and the entry property is — which
  makes `DashboardLoading()` a **CS1955** ("non-invocable member … cannot be used like a method"). The
  dashboard's new parts are therefore written as `DashboardEmpty.Heading(…).Detail(…)`: the first
  production code on the builder surface.
- **RASK043 — the factory is not imported here.** The discovery migration's largest failure mode
  produced roughly 3,000 compiler errors and **not one Rask diagnostic**: 1,700 × CS0119, 694 × CS0120,
  656 × CS0021, none of which names Rask, the factory, or the one line that fixes it. The cause is the
  design working as intended — entries are *inherited members*, so the builder surface is reachable only
  from **inside** a component, and a quarter of the repo's call sites (test classes, static markup
  helpers, fixtures) are not in one. Those keep the **factory**: a factory is a *method*, so C#'s
  invocable-member rule lets it share its component's name where an entry property cannot, which is
  exactly why it works in these positions. Leave the `using static …Generated;` out and the simple name
  binds to the component TYPE instead — CS0119. RASK043 says so at the call, names the enclosing type
  that has no entries, and prints the `using static` to add. It stays quiet inside a component (where
  the entry wins the lookup outright and the answer would be the chain, not an import) and on a
  qualified call. Pinned against the real `Rask.Core.Components.Div`, not a stand-in.
- **RASK036 and RASK040–042 are documented.** The builder surface's own four diagnostics (a component
  must be `partial`; two components share a simple name; the shared pending-bit budget is exhausted; a
  delegate-typed property has no reachable setter) shipped with a `helpLinkUri` pointing at an anchor
  that did not exist. They now have their sections in `docs/diagnostics.md`, report under the single
  `Rask` category with the rest of the family, and carry the expanded IDE tooltip every other RASK
  diagnostic carries.

### Changed
- **BREAKING — the app renders into `<body>`; Rask composes the document around it.** A root component
  used to have to produce the whole page itself — `[Doctype(), Html("en")[Head(), Body()[Router()]]]` —
  and the framework checked, at runtime, by scanning every root render's finished HTML for four tokens
  and throwing when one was missing. That is a contract the app can only get wrong: a missing `<body>`
  left the auto-injected runtime `<script>` nowhere to land, so the page loaded and then did nothing.
  Now the root returns the body's content and nothing else:
  ```csharp
  protected override Component? Render() => Router();
  ```
  and the doctype, `<html>`, `<head>` and `<body>` are the framework's. The two attributes an app
  actually sets on them get named hooks — `HtmlLang` (default `"en"`, `null` omits it) and `BodyClass`,
  the one that carries a theme — and anything beyond that gets `Shell(head, body)`:
  ```csharp
  protected override Component Shell(Component head, Component body) =>
      Html("en", Dir: "rtl")[head, Body(Class: "dark")[body]];
  ```
  The pieces arrive as **parameters** deliberately: an override never has to name the `Head()` tag, so
  the `<head>` element and the `Component.Head` virtual — which every component already uses to
  contribute *into* that element — cannot be confused for each other. `Doctype`/`Html`/`Head`/`Body`
  remain ordinary tag components for hand-built documents (`ToHtml()`, email bodies); they have only
  left the app-authoring path. **Migrating:** delete the shell from your root's `Render()`, keep what
  was inside `Body()`, and move `<html lang>` to `HtmlLang`, `<body class>` to `BodyClass`. Head
  contributions were already in the `Head` override and do not move.
  - **The runtime shell check is gone**, along with one string scan of the whole page per root render.
  - **RASK021 is inverted rather than retired**: it now warns when a root *does* render the shell. It
    has to, because this is the one mistake with no symptom — the parser unwraps a second document
    nested inside `<body>`, so the page renders on and quietly drops the nested tags' attributes.
  - **A fault no longer replaces the document.** The root error boundary sits inside `<body>`, so the
    error page keeps the `<html>` attributes, the body class and the head that were already there. It
    contributes its own charset, viewport and title through `Head`, because an App whose `Render()`
    threw contributed none. A `Shell` override that throws is held to the same promise as a `Render()`
    that does: the error page is shown, inside the framework's default shell, instead of the exception
    escaping to the host as a 500.
  - **`RaskTest.RenderDocument(app, services)`** is the new way to assert on the page — the `<head>`,
    the `<html lang>`, the `<body class>` — since `RaskTest.Render` deliberately adds no markup of its
    own and now therefore produces no document. Every sample, the `rask new` templates and the docs
    move with the change.
  - **What it costs.** The composition is 624 B per root render — pinned as a delta against the same
    body rendered bare, and pinned again as *not* scaling with page size — against one whole-page scan
    per render removed. Per live session it is +144…+424 B unconnected on an empty and a 5-row page,
    and 0.01–0.02% on a 200- and a 1000-row one: the shell elements moving onto the boundary, plus its
    child map growing. The grouping wrapper is a collection expression rather than the `Fragment`
    factory, because the factory would retain it for the session's lifetime while this is the same
    transient every `[Doctype(), Html(...)]` used to build. End-to-end render cost is unchanged
    (`LiveRenderRoundTrip` allocates byte-identically on all four cases).
  - **Two things it does not change**, since the shape of the page is the same as before: `<head>` is
    still filled by splicing into a sentinel after the body walk rather than by concatenation — the
    diff codec's op paths resolve from `document` and its frame offsets are captured against the page
    being serialized, so the head cannot be appended late — and a full frame still carries the whole
    document, so the client's `document.documentElement` morph still strips `<html>` attributes it did
    not render. An app that stamps a theme attribute pre-boot still re-applies it from
    `window.raskAfterMorph`.

### Fixed
- **The browser-API counts in the docs were a release behind.** Six places said "46 wrappers" and the
  capability matrix said "34 transport-agnostic" while the repo already shipped 47 and 35 — the
  `IOriginPrivateFileSystem` change never bumped them. Corrected to 48 and 36 alongside this one.
- **Tutorial chapter 7 contradicted chapter 3.** It told the reader to write `Create(string customer,
  decimal total)` on an `Order` that chapter 3 gave `Total`/`ProductId`/`Placed` and no `Customer` —
  the snippets came from a generator run with different fields, which the old `--force` regeneration
  papered over. Chapter 7 now shows the revised `Order.cs` whole, with chapter 3's fields intact. The
  build gate walks chapters 2 → 3 → 4 → 5 → 7.
- **The CLI build gate's intermittent wasm-hosted failures were MSBuild node reuse
  ([#650](https://github.com/pal-tamas/rask/issues/650)), not flakiness.** A worker node kept alive
  from an earlier run had already loaded `Rask.Wasm.Tasks.dll` from that run's temp directory, so the
  next run's publish silently baked nothing. `Directory.Build.rsp` sets `-nodeReuse:false` for in-repo
  builds but these projects are generated outside it; the harness now sets `MSBUILDDISABLENODEREUSE=1`,
  which the nested `dotnet publish` inherits. Three consecutive gate runs are now 27/27.

### Fixed
- **Tutorial chapter 4 didn't compile either.** The job handler used `IDbContextFactory<AppDbContext>`
  with no `using` and no namespace, so the file the chapter names failed with two `CS0246`s as soon as
  a reader filled the handler in. Chapter 5's email component gained the namespace every other file in
  the tutorial declares. The build gate now walks chapters 2 → 3 → 4 cumulatively — they depend on each
  other (chapter 4's handler reads the `Orders` set chapter 3 adds), so isolation would have missed it.

### Fixed
- **Tutorial chapter 2 didn't compile if you typed it in.** Four defects, none visible to the snippet
  parser: the `AppDbContext` step gave the `DbSet<Product>` line but not the `using` the slice needs
  (the resulting error names `Product`, not the missing import); the list page linked to
  `UpdateProduct` and `DeleteProduct`, which the chapter never provided; and the form used
  `DataAnnotationsValidator` without saying it comes from its own package. The chapter now provides
  both missing files and names the package, and chapter 3 gained the matching `using` for Orders.
  `TutorialChapterBuildE2ETests` now scaffolds a project, types the chapter in, and builds it with
  `-warnaserror` — reading the chapter itself, so an edited snippet is compiled rather than a copy.

### Changed
- **`rask deploy logs -f` works.** `--follow` had no short name because `-f` was `--fields` CLI-wide,
  and the comment explaining that named the cost: `docker logs -f` muscle memory. `--fields` went with
  `rask generate`, so the letter is free and the tail of a log now reads the way every other tool
  spells it. A test already enforces one meaning per short name across the CLI, which is what makes
  reclaiming a freed letter safe rather than a guess.

### Removed
- **Dead code the `rask generate` removal left behind.** Eight properties on `ScaffoldResult` (the
  DbContext splice points, the `Program.cs` registrations, the sibling test-project wiring), the
  `TestProjectWiring` record, `Identifiers.Capitalize`, and `Scaffold.IsInside` with its private
  path-comparison helper. Nothing warns about an unused property on an internal record, so they would
  have sat there looking like part of the design.
- **Stale references to `rask generate` in the CLI itself.** The scaffolded welcome page told every new
  project to run three commands that no longer exist, `rask new`'s next steps printed a fourth, and the
  generated `Program.cs` and `AppDbContext` carried comments describing a generator. They now point at
  the tutorial.

### Added
- **The tutorial's C# snippets are now checked by a test.** Since the code moved out of `rask generate`
  and into the guides, nothing compiled it — a snippet could rot into something that never builds and
  the first person to find out would be a reader typing it. `TutorialSnippetTests` parses every fenced
  C# block in `docs/tutorial/` and verifies that every `override` inside a `Component` names a member
  that actually exists on `Component`. That second check is the one that matters: it catches a snippet
  calling a lifecycle hook from a different framework, which parses perfectly and never compiles.

### Added
- **`rask` on its own opens a new-project wizard.** Typing `rask` with nothing else used to print the
  command list — a reasonable answer to a question nobody asked, since someone typing it has no project
  yet. On a terminal it now walks the wizard: project name, an arrow-key **project type** picker,
  **styling** (Rask.Bootstrap or plain elements), a **Dockerfile** question, and a checklist of
  **batteries** toggled with space instead of thirteen consecutive yes/no prompts. A database picker
  follows when something you ticked needs one. Piped or scripted, bare `rask` still prints the command
  list, so `rask | head` and CI are unchanged.
- **The wizard fills gaps instead of re-asking.** Anything already given on the command line is kept
  verbatim and its question skipped, so `rask new --template wasm --auth` asks only for the name.
  Questions that cannot apply are skipped too — no database question without a battery that needs one,
  no snapshots question for a database that isn't a file.
- **Every scaffolded project gets a `.gitignore`, an `.editorconfig`, and a `.slnx` solution, and is
  initialized as a git repository with one commit.** These are the things whose absence is paid for
  later and by someone else: a committed `bin/` or `app.db`, a formatting-only diff, a solution nobody
  can open. `--no-git` skips the repository, and it is skipped automatically when the target is already
  inside one. The `wasm-hosted` template's hand-written `.sln` — three projects, six GUIDs and a
  configuration matrix — is now a nine-line `.slnx`.
- **`rask new --no-bootstrap`.** Renders the generated pages with plain elements against a small
  stylesheet carried in the app shell, and drops the `Rask.Bootstrap` reference, for projects bringing
  their own CSS. The wizard asks for it as a styling choice. Covered by the CLI build gate, which packs
  this commit's packages and runs a real `-warnaserror` build over the result — the one flag where the
  generated *code* differs rather than the wiring is the one a string assertion proves least about.
### Removed
- **`rask generate` is gone — the CLI no longer scaffolds code inside a project.** All six artifacts
  (`page`, `component`, `feature`, `job`, `email`, `cache`) and the ~3,400 lines of generators behind
  them are deleted, along with `.rask/generate.json` and the feature flag surface (`--fields`,
  `--bs`, `--modal`, `--soft-delete`, `--concurrency`, `--events`, `--outbox`, `--tests`, `--id`,
  `--plural`, `--validation`, `--save-defaults`). `rask` is now `new`, `dev`, `db`, `deploy`, `info`,
  `doctor`, `completion`.

  **The code did not go away — it moved into the guides.** Every artifact the scaffolder used to emit is
  now written out as copyable code: [tutorial ch.2](docs/tutorial/02-first-feature.md) builds a full CRUD
  slice (entity, form model, EF configuration, CQRS commands/queries, list/create/edit pages), and
  ch.4–7 do the same for jobs, email, cache and outbox events. The finished app remains committed as
  `samples/Rask.Example.Shop`, so a snippet always has somewhere to be read in full.

  Scaffolded code is read far more often than it is written, and a generator's output has to be
  understood line by line the first time you meet it anyway. Teaching it in the guides means there is one
  version of that code — the one you can read, adapt and keep — instead of a generated one plus a
  document describing it, drifting apart.

### Changed
- **The CLI's terminal output is rendered by [Spectre.Console](https://spectreconsole.net).** The help
  pages, `rask deploy status`, `rask doctor`, the deploy and host-setup spinners and every prompt were
  ~270 lines of hand-rolled ANSI escapes, `\r` overwrites and `PadRight` columns that had never learned
  terminal width, word wrap or arrow-key selection. Long option descriptions now wrap under themselves
  instead of running off the edge, lists are navigated with the arrow keys, and the tool wears its own
  purple. Two behaviours are held exactly as they were, and tested: piped output carries **no escape
  codes, no logo and no reflowing** — a line you grep for stays on one line — and progress spinners
  write **nothing at all** off a terminal. `NO_COLOR` now removes only color, where before it also
  disabled the cursor control the spinner and the list prompts need.
- Emoji and the block-glyph logo are drawn only on a terminal that reports Unicode support, so a console
  on a legacy code page gets plain text rather than mojibake.

### Fixed
- **A second build in the same session no longer fails on the scoped-asset bake.** MSBuild keeps its
  worker nodes alive between invocations, and the bake is not safe across them: it loads bundle
  assemblies with `Assembly.LoadFrom`, so a node that already loaded one of that simple name from
  *another* project's output throws and the bake produces an empty bundle
  ([#650](https://github.com/pal-tamas/rask/issues/650)). Since the guard added in #652 that is a build
  error rather than a silently 404-ing app — correct, but it means publishing two different WASM samples
  in a row **breaks the second one**, and an ordinary `dotnet build` after a publish can break too. A
  repository-level `Directory.Build.rsp` now passes `-nodeReuse:false`, so every build starts from fresh
  workers and the collision cannot arise. Measured cost on this repo: a no-op incremental build of
  `Rask.Core` goes 0.68s → 0.89s.
  Explicitly a **mitigation, not the fix** — consumers building their own apps are unaffected by the
  response file. The fix needs a load context that intercepts *dependency* resolution rather than only
  top-level loads; `Assembly.Load(byte[])` and a bare `AssemblyLoadContext` both look like they work and
  do not, because `AssemblyResolve` is last-chance and the pre-loaded copy wins, splitting the registry
  the bake reads from the one the registrations write to. Diagnosis recorded on #650.
- **`rask generate job` in the Server half of a `wasm-hosted` solution treated it as a browser app.**
  Browser detection matched `Rask.Wasm` as a substring, and the Server project references
  **`Rask.Wasm.Hosting`** — so the one project a background job actually belongs in got the browser
  next-steps (`AddRaskBrowserSqlite`, "`rask db` does not apply") and had `Rask.SQLite.Browser` added to
  it. That package doesn't resolve for a server project, so the command finished with *"the files were
  written, but the wiring above didn't complete"* and exit 1 — broken, not merely misleading.
  All three signals are now matched precisely rather than as substrings: `-browser` on the
  `TargetFramework(s)` element rather than anywhere in the file, `<RaskWasm>true</RaskWasm>` rather than
  `<RaskWasm>` (which also matched an explicit `false`), and a reference to `Rask.Wasm` itself as either
  a package or a project — anchored so `Rask.Wasm.Hosting` can't satisfy it. A genuine browser app,
  including the Client half of the same solution, is unaffected.
- **The battery demo's live subscription no longer overwrites what you just read.** `BatteryDemo` showed
  one "Status" line written by two independent sources: the `WatchAsync` subscription stamped `live` on
  every push from the device, and the *Read now* button stamped `read`. Whichever wrote last won — so a
  push arriving after a click replaced the answer with `live` and **never put it back**, making the
  button look like it had done nothing. The two are now separate lines, `Watch:` and `Status:`, each with
  exactly one writer; the level and charging figures stay shared, since both sources describe the same
  battery and the freshest value is the right one whichever produced it.
  This also removes an intermittent red from the **longest test in the E2E gate** — the shared journey
  asserted that one label after clicking Read, so a chance push failed the whole journey on unrelated
  branches ([#661](https://github.com/pal-tamas/rask/issues/661)).

### Added
- **Compaction, so a shared bucket doesn't grow forever.** A replica folds its own objects into a single
  one holding its whole current contribution and removes the rest — automatically once its prefix passes
  `CompactAfterObjects` (default 50), or on demand via `CompactAsync()`. What makes it cheap is a
  property of cr-sqlite's feed worth knowing on its own: **it is current state, not history** — one entry
  per (row, column) with the value that won, so editing a field forty times leaves *one* entry and a
  deleted row collapses to a single tombstone. Republishing everything therefore costs the size of the
  database rather than the number of edits ever made. No coordination is needed, because a replica only
  ever rewrites its own prefix — the same rule that removes write conflicts makes compaction a local
  decision. Three things keep it safe, each pinned by a test: the replacement is keyed so it sorts
  **after** everything it replaces (a replacement that sorted earlier would silently stop reaching peers
  that had already synced); re-reading state a peer already holds is harmless because applying twice does
  nothing; and a peer reading an object as it is removed skips it and finds the replacement. The payoff
  is a new device's first sync — one object per peer instead of replaying every sync those peers have
  ever done, verified against the real extension including that a **tombstone survives compaction**, so a
  deleted row does not come back from the dead.
- **A working sample: three devices sharing one database with no server.**
  `samples/Rask.Example.Crdt` runs three replicas — Phone, Laptop, Tablet — each with its own SQLite
  file and its own replica identity, sharing a bucket and nothing else. Each device can be taken
  offline independently, so the demo exercises the real offline path rather than a special case: edit
  **different fields of the same todo** on two offline devices, bring both back, sync, and both edits
  survive. That is the claim per-column merging makes, and it is asserted by an E2E that drives it
  through a browser. cr-sqlite's native binary is per-platform and not redistributed, so without
  `RASK_CRSQLITE_PATH` the page explains what to download instead of failing at the first query with
  "no such function" — and *that* state is asserted too, so one of the two always runs.
- **`FolderObjectStore` — a bucket backed by a directory.** The same `IObjectStore` over a folder, which
  is what lets the sample run with no cloud credentials. It also covers a single-machine deployment
  with no reason to pay for object storage, and — the interesting case — **a folder something else
  already replicates**: pointed at a Syncthing share, devices converge with no central server at all.
  Objects are written beside their key and moved into place, so a concurrent reader sees either nothing
  or the whole object, and keys that would escape the root are refused rather than normalised, because
  a key can come from a listing of a folder other people also write to.
- **`Rask.SQLite.Crdt.Sync` — share those replicas through a bucket, with no server between them.**
  Ships the change feed over `Rask.ObjectStore`: `new CrdtSyncEngine(objectStore, feed)` then
  `SyncAsync()`, with a status a UI can render (published / received / peers / offline). The design
  rests on one rule — **each device writes only under its own prefix** (`crdt/{site-id}/changes/`) and
  never touches another's — so no two devices ever write the same key and there is nothing to lock,
  nothing to retry on conflict, and no lease to leak when a device dies mid-write. Keys carry the
  publisher's own `db_version` range in fixed-width hex, so they sort in the order changes were made
  and a remembered key resumes where the last sync stopped; peers are found with a grouped listing, so
  discovery costs one response naming the *devices*. Only `ReadLocalChangesAsync()` is published, or
  every device would re-upload every other device's history, and uploads batch because object storage
  charges per request.
  **A peer watermark is a key, not a version** — a `db_version` is assigned by whichever database reads
  it, so "everything peer X has after N" is unanswerable from versions and building on them would
  silently skip changes. The watermark advances only after changes commit locally, so an interrupted
  pull is retried rather than skipped. **Offline is the normal case**: the database *is* the queue, so
  an unreachable bucket loses nothing and `CrdtSyncPhase.Offline` is deliberately not a failure state.
  There is no conflict count, on purpose — merging is per column and automatic, so nothing was silently
  discarded. `ICrdtSyncStore` is a cache rather than a record: losing it costs re-uploading and
  re-reading, never data, and a fresh state is answered *from the bucket* so a reinstalled device does
  not republish its history. The wire format is hand-written (no reflection, trim-safe) and tags each
  value with its SQLite storage class, because a value written back as the wrong class is a different
  value rather than a formatting difference. Documented in
  [docs/sqlite-crdt-sync.md](docs/sqlite-crdt-sync.md); verified with two real replicas syncing through
  a bucket, not only against a fake feed.
- **`Rask.SQLite.Crdt` — several replicas of one database, written independently and merged without
  conflicts, through ordinary EF Core.** Wires the cr-sqlite extension into a `DbContext` so application
  code stays LINQ, change tracking and `SaveChanges`, and merging happens per **column** rather than per
  row: two devices editing different fields of the same record both keep their work, and last-writer-wins
  applies only where two devices wrote the same field. `CrdtChangeFeed` exposes the change log with no
  transport attached — `ReadChangesAsync` from a watermark, `ApplyChangesAsync` back — so the same log
  works over a bucket, a socket, or nothing at all; pair it with `Rask.Sync.Client` for the bucket case.
  Applying a change twice is a no-op, which is what makes re-sending safe after an upload whose outcome
  is unknown. Two properties a transport has to build around, both verified against the real extension:
  a replica's feed carries **every change it ever accepted**, still stamped with the originating
  `site_id`, so `ReadLocalChangesAsync()` is what to publish or every device re-uploads every other
  device's history; and a `db_version` belongs to the database it was read from — applying a peer's
  change stamps it with *this* replica's next version — so a version orders your own publishing but can
  never mean "everything peer X has after N". A batch applies in **one transaction**, so a peer's work
  lands atomically and costs the receiver one version rather than one per column.
  The package exists for the three requirements that otherwise fail *quietly*: the extension is per
  connection rather than per process, so loading once at startup works until the pool recycles and then
  silently stops (it is now loaded on every open and finalized before every close); cr-sqlite refuses a
  `NOT NULL` column without a default, which is the exact shape EF emits for every required property, so
  `ApplyCrdtConventions()` supplies them; and loading the extension seeds bookkeeping tables that make
  `EnsureCreated` treat the database as already provisioned, so the schema must be created on a context
  *without* the extension — otherwise nothing is created at all and the first symptom is the promotion
  complaining about a missing primary key. Configuring a non-SQLite provider is reported rather than
  skipped, because silently not replicating surfaces later as data loss. The native binary is supplied by
  the app via `ExtensionPath`, since cr-sqlite ships one per platform. Documented in
  [docs/sqlite-crdt.md](docs/sqlite-crdt.md); the merge behaviour is covered against the real extension
  (`RASK_CRSQLITE_PATH`), and everything reachable without it always runs.
- **A waiting tab now finds out when the database becomes free.** `BrowserSqliteOwnership.Available`
  completes in a non-owner tab once the owning tab closes, so an app can turn "close the other tab" into
  "your data is ready — reload" instead of leaving the user to guess when the condition was met.
  **Reloading is what takes ownership**, deliberately: a waiting tab already opened its own empty database
  at boot, so the file cannot be swapped under its live connections, and a tab that started persisting its
  empty database would overwrite the previous owner's good snapshot with nothing. The watcher polls with
  `TryRequestAsync`, which acquires and releases within the call, rather than waiting on `RequestAsync` —
  waiting would mean *holding* the lock the moment it frees, which would both make this tab an owner it
  must not be and block a tab that could actually use it. The signal is therefore advisory: another tab
  may win between the poll and the reload, and the reloaded page runs the normal election to find out.
  Tunable via `TakeoverPollInterval` (2s default); `samples/Rask.Example.Wasm.Jobs` shows it, covered by
  an E2E that opens two real tabs and closes the owner.
- **A browser database now asks not to be evicted.** `Rask.SQLite.Browser` keeps its snapshots in
  IndexedDB, and IndexedDB is evictable: under storage pressure a browser may discard them, and the
  database comes back empty on the next load with nothing to indicate why. The owning tab now calls
  `navigator.storage.persist()` at startup (via `IStorageEstimator`, added in #645), checking
  `IsPersistedAsync()` first so an already-exempt origin is never asked twice. A refusal is logged and
  changes nothing else — the app runs exactly as before, the risk is just no longer silent. Only the
  owning tab asks, since the others persist nothing. Chromium decides from engagement heuristics without
  prompting; **Firefox prompts**, and this is asked during boot rather than from a click, so an app that
  would rather choose its moment sets `o.RequestPersistentStorage = false` and calls
  `IStorageEstimator.RequestPersistAsync()` from a user-gesture handler instead.
- **`BrowserSqliteOwnership` — let a second tab explain itself.** Only one tab may own a browser SQLite
  database, so the others run against their own empty, unpersisted one. That is correct, and until now it
  was also indistinguishable from the user's data having been deleted: the package logged a warning to the
  console and the app had no way to ask. Inject `BrowserSqliteOwnership`, `await ownership.Resolved`, and
  say so. `IsOwner` is `null` while the election is in flight rather than `false`, so "still deciding" and
  "another tab has it" stay distinguishable and a normal boot never flashes a warning banner.
  `samples/Rask.Example.Wasm.Jobs` shows one, covered by an E2E that opens two real tabs.

### Fixed
- **A WASM publish that drops its scoped assets now fails the build instead of shipping.** The bake stages
  scoped CSS/JS into `obj/…/rask-scoped` and registers them as computed static web assets in the build
  pass, trusting them to flow into the publish manifest. When that link breaks — most reliably by building
  one project in both `WasmBuildNative` modes through a single `obj/` — the published bundle simply has no
  `/_rask/a/` and **nothing says so**: the app builds, publishes, boots and renders, with only its scoped
  CSS/JS absent. Every scoped URL 404s, and for an app whose scoped JS owns something load-bearing that
  presents as a hung page, sending you to debug the app rather than the build. The publish now compares
  what was staged against what shipped and errors with the cause and the fix.

  It also catches a second shape — **the bake not running at all**. With the staging directory absent,
  "this project has no scoped assets" and "the bake was skipped" are the same observation, so the
  comparison above has nothing to compare and stays quiet. The bake now records that it ran, and a publish
  that never baked *and* shipped no scoped assets fails. A project that genuinely has none bakes zero,
  records the run, and is unaffected — verified against `samples/Rask.Example.Wasm.Jobs`, which has no
  scoped assets; an incremental publish that skips the build pass while its assets already sit in
  `wwwroot` stays quiet too.

  **Neither covers what #650 actually is**, so that issue stays open. Two sessions have now reproduced it
  and the build log is unambiguous: after a no-native solution build, the native publish's bake *runs* and
  writes **zero** files for an app that plainly has scoped assets. Nothing outside the bake can see the
  difference between that and a project with none, so no publish-time check can catch it —
  `FailOnEmpty` can't either, since its `registryResolved` is true whenever `Rask.Core` merely loads and
  would fail every WASM app without scoped assets. The fix has to be inside the bake, once the binlog
  shows what its inputs looked like on the failing run.

  `BakeScopedAssetsTask.FailOnEmpty` could not have caught this and — despite its own documentation
  claiming otherwise — has never been wired to anything: it fires only when the bake *runs* and writes
  zero, whereas here the bake runs perfectly well and the break is downstream of it. Its docs now say so.
- **62 in-page anchors across the docs sent readers to the top of the page instead of the section they
  named.** The docs under `docs/` are authored and reviewed on GitHub, and their `#anchor` links are
  written against GitHub's heading slugs — but the guides site rendered Markdig's, which differ wherever
  a heading holds punctuation. GitHub *deletes* the character and keeps the spaces either side of it, so
  `## Rask db — migrations` becomes `#rask-db--migrations`, while Markdig collapses the run to
  `#rask-db-migrations`; leading numbers survive on GitHub (`#1-two-way-binding`) and are stripped by
  Markdig. Every one of those links still navigated, which is why none of them looked broken.
  Heading ids are now stamped GitHub-style, so the same anchor resolves in both places a doc is read.
  Markdig's own `AutoIdentifierOptions.GitHub` does **not** close this gap — it produces identical output
  to the default, verified — so the slug is ours. The on-this-page rail reads ids from the same pipeline
  and follows automatically.
  Five anchors were genuinely dead rather than mis-slugged (a heading reworded, one that never existed)
  and are repointed at the sections they meant.

### Added
- **The docs suite now checks that links *inside* a doc resolve, not just that every doc is reachable.**
  `DocsIndexTests` guarded reachability from `docs/README.md` and `GuidesTests` guarded catalogue parity;
  neither looked at whether a link written in a doc pointed at anything, so both misses above were found
  by a person reading rather than by a test. `DocsLinkTests` adds three checks over every `docs/**/*.md`:
  a relative `*.md` link resolves to a file that exists; it resolves to a doc the app can actually serve
  (the renderer rewrites `dir/x.md` to the SPA route `/guides/x` by bare leaf, so a doc present on disk
  but not embedded under that slug is a link a reader can follow to a 404); and an `#anchor` — in the
  same doc or another — names a real heading. Anchors are checked against the ids Markdig actually
  stamps, by parsing with the renderer's own pipeline rather than a second slugifier that could disagree
  with the first. External `http(s)` links are left alone: checking them needs the network and would buy
  flakiness for no benefit.

### Fixed
- **Playground: picking a chapter or an example before the editor had mounted silently kept the starter
  code.** Run and Reset waited for the editor; the controls that *load* code did not — they only guarded
  against a compile being in flight. Loading a chapter is a round-trip to `setEditorValue`, and before the
  editor exists that call is a no-op, so the editor then came up holding the starter instead. The reader
  was left with the brief and the chapter highlight showing one chapter while the editor held another —
  and Run compiled the wrong code and ticked the chapter off as done. On a cold load the window is seconds
  wide, which is exactly when a first-time reader clicks "Tutorial". Every control now shares one gate
  (`CanInteract`), since the bug was two copies of the condition disagreeing. Closes #647.
- **Playground: a bundle whose scoped assets are missing now says so, instead of looking hung.** Mounting
  the editor is an interop call into `PlaygroundView.js`; if that module never loaded, the call never
  *settles* — which is not the same as failing, and the textarea fallback never gets a chance. Every
  control then sat disabled forever with no explanation, which reads as "the playground is broken" and
  sends you to debug Roslyn or Monaco rather than the build that dropped the assets. The mount now has a
  deadline (generous, so a slow connection fetching Monaco can't trip it) and reports the module as
  missing. See #650 for the build-side glitch that produces such a bundle.
- **The pre-commit gate now covers `samples/` and `docs/`.** Its change filter listed `src/`, `tests/`,
  `benchmarks/` and the build files, so a commit touching only samples or only docs reported "no code
  changes staged" and skipped both formatting and the unit suite. That is not merely a missed format run:
  `Rask.Example.Shared.Tests` compiles `samples/Rask.Example.Shared` and owns a committed markup golden,
  and `DocsIndexTests` / `GuidesTests` read `docs/**/*.md` off disk for reachability and catalog parity —
  so a samples-only or docs-only commit could break a golden or a docs invariant with nothing objecting
  until somebody else's push. An entire feature could land ungated; the playground tutorial was largely a
  `samples/` + `docs/` change.
- **`rask generate job` no longer prints server-only next steps inside a browser app**
  ([#646](https://github.com/pal-tamas/rask/issues/646)). `rask g j` is not gated on project kind, so it
  runs anywhere a `.csproj` is found — and it told everyone the same thing: point a `DbContextFactory` at
  `Data Source=app.db`, then run `rask db add && rask db update`. In a WASM app the migration step is not
  something the reader can do at all (there is no design-time database in a browser bundle), and the
  registration omits the one call that makes the queue durable. The failure was silent rather than loud:
  the app built, ran, and quietly lost every queued job on reload. `ProjectContext` now detects a browser
  project the same way it already detects the database provider — by reading the project file — and the
  notes tell a WASM app to register `AddRaskBrowserSqlite`, create its schema at boot, and avoid the two
  build settings (`-p:WasmBuildNative=false`, `PublishTrimmed=true`) that each break it without an error.
- **`INotifications` no longer reports `Granted` on Android for an app whose notifications the user
  switched off.** The check asked only whether the app *held* `POST_NOTIFICATIONS`, and short-circuited
  to `Granted` outright below API 33 where no such permission exists. But the per-app notification
  toggle in Settings is independent of the permission, exists on every supported version, and turning
  it off makes `NotificationManager.Notify` a **silent** no-op — so `PermissionAsync()` said `Granted`,
  `ShowAsync` returned without throwing, and nothing ever appeared. The one call that sees that toggle,
  `AreNotificationsEnabled`, is now consulted first; it exists from API 24, the android head's own
  minimum, so it needs no version guard.
  A muted app reports **`Denied`** rather than `Default`, because the way back is the Settings screen
  and not a prompt — which is what `Denied` means in the web contract this backend mirrors.
  `RequestPermissionAsync()` returns it too instead of claiming a grant no prompt could produce (it was
  answering `Granted` unconditionally below API 33). **Behaviour change:** `ShowAsync` on a muted app
  now throws `InvalidOperationException` like any other ungranted permission, where it previously
  returned and quietly showed nothing.

### Added
- **`Mount` — give a component you built yourself the lifecycle it was missing.** A component normally
  enters the tree through its generated factory, and that factory is what registers the instance with its
  parent. One built another way — because its type isn't known until runtime: a plugin, a component chosen
  by name, one compiled in the browser — arrives as a plain object. It rendered correctly but was invisible
  to the alive-set walk: **no `OnMount`, no `OnMountAsync`, no `OnRendered`, no `OnUnmount`**, and no handle
  to re-render through when an async hook completed. Anything loading its data in `OnMountAsync` sat on its
  placeholder forever, with nothing reported — the failure looked exactly like code that doesn't work.
  `Div()[Mount(Child: instance)]` adopts and notifies it, and adds no markup of its own; wrapping a
  factory-built child is a harmless no-op. See [composition.md](docs/composition.md#hosting-a-component-you-built-yourself).
  (Found because the playground mounts every compiled component this way — so until now *no* playground
  snippet could load anything in `OnMountAsync`.)
- **A guided tutorial in the playground, with real EF Core + SQLite running in the browser.** The
  playground's left pane gains a **Tutorial** tab beside the example gallery: eight chapters that start at
  "what is a component" and end at a database, each with its goal and notes above the editor, prev/next
  navigation, and a tick once the chapter compiles (your edits included — the tick means it built, not
  that you clicked it).

  Chapters 5–8 are the point: they run **actual EF Core against actual SQLite inside the tab** — not a
  mock, not the in-memory provider. `e_sqlite3` is linked into the published WebAssembly runtime, so
  `SaveChangesAsync` writes rows a later `Where(...)` reads back through real SQL. They teach the same
  [`Rask.Data`](docs/data.md) conventions `rask generate feature` scaffolds — `Entity<Guid>`,
  `ApplyRaskConventions()`, and the auditing / soft-delete interceptors — so what a reader learns in the
  browser is what they will write on their machine. A reader can now go from the front page to "I inserted
  a row and queried it back" with nothing installed.

  Notes: each chapter owns its own database file (chapters evolve the schema, and `EnsureCreated()` does
  nothing to a database that already has tables), those files live in the runtime's in-memory filesystem
  and are lost on reload — which is the intent for a sandbox, and still [not how to build an
  app](docs/sqlite.md#sqlite-in-the-browser-wasm). Linking `e_sqlite3` means the playground now publishes
  with a **native relink** (the `wasm-tools` workload); a build made with `-p:WasmBuildNative=false` ships
  without the EF Core reference set and marks those chapters read-only rather than pretending they work,
  so the fast unit-gate build is unaffected.
- **`samples/Rask.Example.Wasm.Jobs` — `Rask.Jobs` running in the browser, verified end to end.** Queue a
  job, a `BackgroundService` picks it up and writes a row, reload the page and the row is still there —
  with no server behind any of it. Every registration below the first line is what you would write on a
  server, and `GreetJob` plus its `ICommandHandler<GreetJob>` would compile and run there unchanged. The
  new E2E test is the only evidence for a chain no unit test can reach: the WASM host starting a
  registered `IHostedService`, EF Core opening a natively-linked SQLite database in the browser,
  `JobProcessor` claiming a row with its lease, and the database surviving a reload from an IndexedDB
  snapshot. It is its own sample rather than a page in the showcase because EF Core cannot be trimmed,
  and it is the one sample that must **not** be published with `-p:WasmBuildNative=false` — SQLite is a
  native library, and skipping the relink produces a bundle that boots and then fails on every database
  call. `scripts/run-e2e-local.sh` publishes it accordingly, and the fixture checks the output and says
  so if it was built the wrong way.
- **`Rask.SQLite.Browser` — a real SQLite database inside a browser WASM app, persisted across reloads.**
  `docs/sqlite.md` said this was not worth doing; it is, and it now works. The native `e_sqlite3` links
  into a `browser-wasm` publish on its own (the patched SQLitePCLRaw 3.x bundle already pinned for
  CVE-2025-6965 resolves to a native package that ships the browser asset and wires the
  `NativeFileReference` itself), so `Microsoft.Data.Sqlite` — and Entity Framework Core on top of it,
  including `ExecuteUpdateAsync` — runs in the browser unchanged. What was missing was durability and a
  single writer, which is what this package adds: the database is restored from IndexedDB during a hosted
  service's `StartAsync` (so anything registered after it opens a populated file), written back on an
  interval and on page-hide through SQLite's Online Backup API rather than an unsafe file copy, and owned
  by exactly one tab via a Web Lock — because every tab has its own in-memory filesystem, and two owners
  would mean two divergent databases with the last snapshot silently winning.
  ```csharp
  builder.Services.AddRaskBrowserSqlite("app");
  builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(BrowserSqlite.ConnectionString("app")));
  builder.Services.AddRaskJobs<AppDbContext>();   // unchanged from the server
  ```
  Three limits worth stating plainly: an app using EF Core in the browser must publish with
  `PublishTrimmed=false` (EF Core does not survive the trimmer there; `Microsoft.Data.Sqlite` alone does);
  the durability window is the snapshot interval, not the page-hide flush, because the browser does not
  wait for a `pagehide` handler; and non-owner tabs get their own unpersisted database rather than a view
  of the owner's — promotion and write-proxying are not implemented.
- **`IIndexedDb` stores raw bytes, not just strings.** `IKeyValueStore` gains
  `SetBytesAsync(key, byte[])` / `GetBytesAsync(key)` for content that is binary rather than text — an
  image, a compressed blob, a database file. The value lands in IndexedDB as a real `Uint8Array`, so a
  megabyte of bytes costs a megabyte of quota; base64 is used only in transit, being the one encoding
  that marshals identically across the interop boundary on every host. Both methods have default
  interface implementations that fall back to the string API, so a custom `IKeyValueStore` written
  before this still compiles and behaves correctly — it just pays the ~33% inflation in storage too.
  The two pairs are not interchangeable: read a key with the same kind of accessor you wrote it with.
- **`AddHostedService` now works on the browser host.** It always compiled there —
  `Microsoft.Extensions.Hosting.Abstractions` is pure abstractions — but nothing ever *started* what
  it registered, because a WASM app has no generic host. So a `BackgroundService` that ran on the
  server registered fine, resolved fine, and silently never ran. Rask now starts registered hosted
  services itself, in registration order, at the end of boot — late enough that a service can call
  `StateHasChanged()` against a mounted tree, and late enough that a slow `StartAsync` cannot hold up
  the rest of the boot or anything after `await RunAsync<App>()`. Differences from the server, all
  documented in [docs/lifecycle.md](docs/lifecycle.md#hosted-services): a service that throws from
  `StartAsync` is logged and skipped rather than blanking the app, since a browser tab has no
  orchestrator to restart it (a throwing *constructor* takes the whole set down, because the
  container builds them in one call — reported plainly); a `BackgroundService` whose loop faults
  after starting is observed and logged, so a crashed loop cannot masquerade as one that never ran;
  and shutdown is drained from `pagehide` — in reverse start order, and not for a back/forward-cache
  suspend where the page can be restored still running — which the browser does not wait for, so it
  is an optimisation rather than a guarantee.
### Changed
- **An event handler's id now belongs to the component that renders it, so one component gaining a
  handler no longer renumbers the rest of the page.** Ids came from a single counter reset to zero on
  every root render and handed out `h0, h1, …` in walk order, so a conditional button appearing
  anywhere pushed every later handler on the page up by one. Two things fell out of that. The diff
  rewrote `data-rask-on-*` on elements whose markup had not changed — on a 100-row list of buttons with
  one conditional action above it, an update shipped **3,205 bytes across 101 edit ops; it now ships 94
  bytes in 1** (new gated `HandlerShiftAboveList100` scenario in `payload-bytes`; the five existing
  scenarios are byte-identical). And the clean-subtree cache had to refuse any snapshot whose baked-in
  ids the shifted counter had invalidated, so an interactive list re-walked itself whenever anything
  above it changed shape: on the `session-churn` handler-shift pass that is **−60% allocation per
  update at 200 rows (252,097 → 100,124 B) and −64% at 1,000 (1,220,549 → 438,170 B)**, which brings
  the cost of an update that moves the handler count down to within ~2% of one that does not.
  Ids are now assigned per (component, local slot) and held for that component's lifetime, anchored to
  the component whose subtree the element serializes in rather than to the delegate's target — so a
  callback passed down into a composite wrapper cannot shift the wrapper's own ids either. Renumbering
  is *bounded to one component*, not eliminated: an element passed into a wrapper as children takes a
  slot on that wrapper, so a conditional sibling there still shifts the ones after it — within that
  wrapper, rather than to the end of the page. A number is never handed to a **second component**: when
  one unmounts its ids simply stop being registered, so a click a user sent a moment before a row
  vanished resolves to nothing and no-ops instead of being redirected onto whichever component
  inherited a recycled number. (Within a single component a slot is still reused when its handler set
  shrinks — unchanged from the counter this replaces, and still narrowed by the frame-shape guard.)
  The number space
  therefore tracks cumulative rather than concurrent handler slots — but each id string is minted once
  and cached on its slot, so steady-state re-render allocation is **unchanged to the byte**
  (`LiveRenderRoundTrip`'s marginal cost per re-render is 70.31 KB before and after). The costs, both
  one-off: a component's first render allocates one small state object (a 1,000-row interactive grid
  retains **+1.5%**, 7,381,092 → 7,493,300 B/session), and a single component that registers many
  handlers in its own render allocates one array for slots 1.. (`Register1000`, the pathological
  one-component-owns-1,000-handlers shape, 128.82 → 145.1 KB on the render that builds it).
  Nothing changes on the wire or in any API: a first render still emits `h0, h1, …` in document order,
  and ids stay opaque to the client, which echoes them back verbatim.
- **BREAKING — a short flag now means the same option on every `rask` command.** The same two
  keystrokes used to do different things depending on where you were, and the two worst cases failed
  *silently* rather than erroring, which is what made this worth breaking for:
  - **`-o` is `--output` everywhere.** It was `--output` on `new`, `generate` and `db` but a boolean
    `--open` on `dev` — so `rask dev -o ./somewhere` parsed happily and dropped the path into the
    positionals. `rask dev --open` keeps its long form.
  - **`-p` is `--project` everywhere.** It was `--project` on `dev`, `db` and `deploy` but `--plural`
    on `generate`, so `rask g f -p X` set something different from the same keystrokes elsewhere.
    `rask generate --plural` keeps its long form.
  - **`-f` is `--fields`.** `rask deploy logs --follow` loses its short name: `generate` is the command
    people run most and `--fields` is its primary input, where `--follow` is occasional and four more
    characters. `--feature` stays `-F` — the only uppercase short in the CLI, and now for a stated
    reason rather than an accident.
  - **`rask db --force` is now `--yes` (`-y`).** On `new`/`generate` that word means "overwrite files";
    on `db` it meant "skip the confirmation" — one spelling for two unrelated powers, and the one that
    destroys a database was reachable by muscle memory from the one that overwrites a file. `--force`
    on `db` is now rejected with a suggestion rather than silently ignored.
  - A test enforces the convention: a short name must map to one long option CLI-wide, and must not be
    a flag on one command and a value on another. It fails with the offending pair named.
- **`rask doctor` — one command that checks the environment before another command hits it.** Every
  probe it runs already existed, each reachable only from the one command that needed it: the EF tool
  from `rask db`, Docker and SSH from `rask deploy`, project/template/provider detection from whichever
  command was about to use them. So the way to find out whether a machine could run something was to run
  it and watch where it stopped — halfway through, having already done part of the work. It is read-only
  by design: it reports, it never installs or fixes. Warnings don't fail it (Docker missing is fatal to
  `rask deploy` and irrelevant to everything else), so the exit code means "something here will stop a
  command from starting", and `--json` carries the same verdict for CI.
- **A corrupt `.rask/deploy.json` or `.rask/generate.json` no longer disappears in silence.** Both
  loaders catch `JsonException` and fall back to defaults — which is right, a hand-edited file shouldn't
  wedge a deploy — but they did it without a word, so a typo'd config looked exactly like no config and
  the remembered host or the team's generate flags simply stopped applying. They now say which file, why
  it didn't parse, and that it's being ignored until fixed; `rask doctor` reports it as a failure.
- **`--dry-run` behaves the same on every command that has it, and is on two more.** It was on three
  commands in three shapes: `new` printed indented names, `generate` printed an unindented path followed
  by the *entire file*, and `deploy` printed docker commands under its own heading. All of them now emit
  one `[dry-run] would …` line per action.
  - **`rask generate --dry-run` lists files; `--verbose` adds their contents.** A feature scaffolds a
    dozen files, so dumping every one buried the list of what was about to happen in thousands of lines
    of C# — which is the question `--dry-run` is asked.
  - **`rask db --dry-run`** covers `drop`, `update`, `backup` and `restore` — the destructive and the
    slow — printing the exact `dotnet ef` command and the directory it would run in. It is checked
    *before* the confirmation prompt: a dry run changes nothing, so requiring consent for it (or
    refusing it outright for want of a terminal, which is what happened) made the one safe way to
    inspect a destructive command the hardest to reach.
  - **`rask dev --dry-run`** prints the `dotnet watch` command line *and the environment overlay*. The
    overlay is not incidental — the MSBuild property that stops a rude edit blocking on an interactive
    prompt travels through it, so showing only the command line would hide the half people come asking
    about.
- **`--json` on the three commands worth scripting: `rask info`, `rask deploy status`, `rask db list`.**
  A `--json` run prints the document and nothing else, so it pipes into `jq` without filtering banners
  out; errors stay on stderr and the exit code still separates a mistyped command (`2`) from failed work
  (`1`). Fields with no value are absent rather than carrying a human placeholder — no SDK means no
  `dotnetSdk` key, where the report prints `not found`.
  - `rask deploy status --json` keeps the fields the human table folds together for width: `domain` and
    `ports` stay separate instead of collapsing into one URL column with `(not published)` standing in
    for both, and an empty list is an empty array rather than prose.
  - `rask db list --json` asks `dotnet ef` for JSON rather than parsing its human listing. EF prints a
    build preamble before the document and does not delimit it, so the payload is located rather than
    assumed to start at byte zero — verified against real `dotnet ef migrations list --json` output, and
    pinned by a test using a captured copy of it.
  - `rask info --json` used to "succeed" while printing the plain report, because that command ignored
    its arguments entirely. It now parses them like every other command.
- **`rask generate` has a `--project` (`-p`), which it was the only project-scoped command to lack.**
  Project resolution stops whenever a directory holds more than one `.csproj`, and until now the error
  had no escape hatch to suggest. It accepts a `.csproj` or a directory, and the resolution starts from
  there rather than the working directory.
- **`rask generate feature` now maps its entities with the app's own `DbContext` instead of writing one per
  feature.** An unflagged run used to emit `<Plural>DbContext` — so a `rask new --data` app that already had
  `Features/Shared/AppDbContext.cs` ended up with a second context after its first feature, and a third after
  its second. That was never just duplication: every context in an assembly calls
  `ApplyConfigurationsFromAssembly`, so each one mapped *every* entity, and the app carried two
  `AddDbContextFactory` registrations and two migration histories over the same tables. `--context` existed to
  avoid it, which made the correct command the one you had to know to type.
  - The run now scans the project first. **One context** (the usual case) → it attaches: the `DbSet` and its
    `using` are spliced into that context, the slice imports its namespace, and no factory is registered
    because the app already registers one. **No context** → it writes one, and the one it writes is the same
    app-wide `Features/Shared/AppDbContext` that `rask new --data` scaffolds, so the next feature attaches to
    it. **Several contexts** → it stops and names them, because which database a feature belongs in is your
    call and guessing it would silently attach the feature to the wrong one; `--context <Name>` picks, and
    still wins whenever you pass it. Copies under `obj/`/`bin/` are ignored, so a built project doesn't read
    as having two.
  - `--outbox` on an attached context now splices `modelBuilder.AddRaskOutbox();` (and its `using`) into that
    context's `OnModelCreating`. Only the generated context baked it in before, so `--context` + `--outbox`
    left the `OutboxMessage` table unmapped — delivery stopped being durable while every handler still ran.
  - `--tests` keeps emitting the round-trip persistence test when it attaches: the gate is now "can this
    context be constructed from `DbContextOptions<T>`" (read off its source — every scaffolded context can)
    rather than "did we write it", so the test doesn't vanish from the common path.
  - Docs, the tutorial (chapters 2, 3, 7), the `Rask.Example.Shop` command list and `rask new`'s printed next
    steps all drop the `--context AppDbContext` that is now the default behaviour.
- **No E2E fixture picks its own port any more, so a straggler host can't block the push gate.** #612 fixed
  the in-process static hosts; the eleven fixtures that launch a real `dotnet` process kept their constants,
  maintained against a comment listing their siblings' numbers. That list was only ever unique *within* one
  run of one checkout — and `5099` is the port `.githooks/pre-push` gates on, so a single leftover host
  blocked pushing from every worktree on the machine. All eleven now reserve a loopback port from the OS
  (`LoopbackPort.Reserve`, probing the family `localhost` actually resolves to), and a test fails the build
  if a literal port comes back.
  - **A bind clash and a broken app are no longer the same error.** An out-of-process host has to be *told*
    where to listen, so the number is decided before anything binds and a clash stays possible in principle.
    The fixture now reads the child's output: Kestrel's "address already in use" retries the whole launch on
    a fresh port, anything else still fails immediately — a genuinely broken sample must not be retried into
    a five-times-longer timeout. Previously a clash surfaced as "exited before becoming ready", sending the
    reader after a bug in the sample; worse, a *stale host* on the fixed port answered the readiness poll
    and every assertion silently ran against the wrong process.
  - `WasmWatchAppFixture`'s hand-rolled `EnsurePortFree` is gone with the constant it guarded. It probed
    IPv4 loopback only — the exact family mismatch that lets a port look free and still refuse to bind.
  - The local-dev publish folder is keyed on the app instead of the app *and its port*, which would now
    leave a fresh multi-hundred-megabyte publish behind on every run; concurrent publishes of the same app
    are serialised, and it re-publishes rather than reusing whatever it finds.
- **A failing browser journey now leaves a Playwright trace.** The existing dump explains a page that
  *threw*; it says nothing about a page that is merely never still, which is how #625 presented — a 30s
  "element is not stable" naming the element and nothing about what was moving. The journey records a trace
  with DOM snapshots throughout and keeps it only on failure, printing the path and the command that opens
  it. Always on rather than behind a flag, because the run worth tracing is the one that unexpectedly failed.
- **Hot reload can apply an edit in this repo again.** Every save under `rask dev` on any sample failed
  with `error CS7038: … Changing the version of an assembly reference is not allowed during debugging:
  'Rask.Bootstrap, Version=0.0.0.0' changed version to '1.0.0.0'`, so the edit never reached the running
  app — for a one-character change to a `Render()` body, the most hot-reloadable thing there is.
  - MinVer sets `AssemblyVersion` from a **target**; hot reload never runs targets, because Roslyn's EnC
    service compiles in-process from the project's **evaluated** properties, where MinVer has not run and
    the SDK falls back to `Version 1.0.0` → `AssemblyVersion 1.0.0.0`. Every emit therefore disagreed with
    the assembly already loaded. `Directory.Build.props` now pins `<AssemblyVersion>0.0.0.0</AssemblyVersion>`
    at evaluation time — the same value MinVer's target produces on the `0.x` line, so nothing about the
    shipped binaries changes and the real version keeps riding on `FileVersion`/`InformationalVersion`.
  - **It only affected the packable projects**, which is why it went unnoticed: MinVer is referenced under
    `Condition=" '$(IsPackable)' != 'false' "`, so `Rask.Core` read `1.0.0.0` on both sides and agreed by
    accident. That is also why `AssemblyVersionStabilityTests` asserts against a packable assembly — a
    guard reading `Rask.Core` would pass with the pin removed.
  - It would equally have hit **any app that project-references a Rask project**, and it quietly devalued
    the hot-reload work in #534/#569: all of it behaves correctly, and none of it could be demonstrated on
    this repo's own samples.
- **A build that fails under `rask dev` is reported as a build failure, not as a lost connection.** Saving
  a file that didn't compile took the app down, and the browser — which only knows that its socket closed —
  said "Reconnecting…", then "Still trying to reconnect…" with a **Retry now** button that could not
  possibly succeed. A compile problem reported as a network problem, with an action that cannot help. The
  page now shows the compiler errors, and clears itself the moment the code builds: the reconnect ladder
  keeps running underneath the panel, so a fixed typo brings the app back with no reload and nothing to
  click.
  - **It could not be a live-protocol frame, which is the whole point.** The existing out-of-band frames
    (`hotReload`, `shutdown`) are broadcast *by the app*; when a rebuild fails the app process is **down**,
    so there is nothing left to send. The signal has to come from something that outlives the app, and the
    only such thing is `rask dev` itself — which now reads `dotnet watch`'s output as it passes it through
    to the terminal, and serves what it learned from a read-only loopback endpoint it owns for the life of
    the session.
  - Its URL is stamped onto each page the app serves (`data-rask-dev-status` on `<body>`), so the browser
    still has somewhere to ask **after** the server that sent it is gone. Development only, gated twice:
    production HTML never carries the attribute, and the client will not poll without it.
  - The watcher keys on **MSBuild's diagnostic format**, not on watch's prose — watch decorates its lines
    with emoji, localises them, and has reworded them between SDK releases, where
    `path(line,col): error CS0103: …` is stable and locale-independent. Repeats of one diagnostic across
    referencing projects are counted once, so a single typo in a shared library reads as one error rather
    than three.
  - Failure to bind the endpoint is never fatal: `rask dev` runs exactly as before and the browser falls
    back to the reconnect overlay.

### Fixed
- **A component that calls `ToHtml()` on a tree containing a `<head>` no longer corrupts the page around
  it.** `HeadSentinelIndex` is a byte offset into whichever builder is being serialized, and `ToHtml()`
  serializes into a private one whose string is handed straight back — so a nested call published an offset
  that meant nothing to the render owning the context, and the live root then spliced the scoped-CSS/JS
  block *there*: into the middle of an unrelated opening tag, cutting it in half and losing its attributes.
  Recording is first-wins, so a page with its own `<head>` was safe by accident (the shell's head goes
  first) while a render without one — every `RaskTest.Render`, so every unit test, in a shipped package —
  was not. `ToHtml()` now saves and restores the offset; its output never goes through the splice, so it
  had no use for it. Found by the new demo-markup timer guard below, which caught it corrupting a demo that
  had been snapshotting the damage as normal.

### Changed
- **Three tests that went red under load rather than on merit.** Each one taught `--no-verify`.
  - **The persisted-state budget diagnostic** asserted `Assert.Single` over everything the process-global
    `RaskDiagnostics.Sink` saw while it was swapped in — which is really "no other test in the assembly
    reported anything in this millisecond", true almost always and so failing rarely and confusingly. It
    now asserts on the events its own call site produces, and moved into the serialised collection that
    already owns that global: two tests swapping the sink concurrently lose it entirely, since both save
    `previous`, the second saves the first's capturing delegate, and restoring in that order leaves the
    sink pointing at a list nobody reads.
  - **The log-writer shutdown-drain test** started the writer's loop, on the belief that a five-minute
    `FlushInterval` meant nothing could reach disk on the timer. `ExecuteAsync` is a `do/while` over a
    `PeriodicTimer`, so its first cycle runs *before* the first tick — and under load that cycle can pull
    the entry out of the channel and still be inside `AppendAsync` when shutdown cancels the token, at
    which point the entry is gone for good and the drain finds nothing. The loop is no longer started, so
    the drain is the only code that can append and the test's premise is true by construction.
  - **The demo-markup golden** captured `LiveTicker` mid-race. Fixed at the source rather than by
    regenerating, which would only have moved the flake to the other state — see below.
- **`LiveTicker` renders one layout, not two.** The headline price switched class list (`fs-3 text-secondary`
  → `fs-2 fw-bold`) and the chart swapped a `<p>` placeholder for its `<svg>` when the first tick landed
  50 ms after mount — so the number resized and the chart appeared, shoving the page around, and the demo's
  golden markup became a race against machine load. The price span now keeps one class list and changes only
  its text, and the `<svg>` is always emitted (`Sparkline` already draws a labelled empty frame for an empty
  series, so the placeholder was a second, worse answer to the same question).
- **A new guard catches the next demo that does this.** `EveryDemoSkeleton_IsReproducible` renders twice
  back-to-back, so both renders land on the same side of any mount-time timer and agree — which is exactly
  how `LiveTicker` walked past it. The new check holds one instance and reads it again after its timers have
  fired, and names the line that moved. It found two offenders on its first run: the ticker, and the
  `ToHtml()` corruption above.
- **The browser gate no longer depends on Google's font CDN, which is what #625 turned out to be.** The
  showcase shell links three font families with `display=swap`. Swap means paint with the fallback now and
  **reflow** when each webfont lands — several files, over the public internet, on a page the journey
  throttles to Slow-3G. Every arrival moves the text and therefore the bounding box of everything below it,
  and Playwright's actionability check requires a box that is identical across two consecutive animation
  frames. Hence a 30s `element is not stable` on whichever guide page the walk had reached, on all three
  hosts (they share the shell), with the text assertions on the very same subtree passing — `innerText`
  does not care what font it is in. The journey now aborts requests to `fonts.googleapis.com` /
  `fonts.gstatic.com`, so the page renders in the fallback immediately and settles once. No assertion is
  about typography, and "the browser gate is green" no longer includes "a third-party CDN was fast today".
  - **The gate had also been green for the wrong reason.** Capturing a Playwright screenshot on every
    action was settling the page: with screenshots on the suite ran 3/3 green and with them off 4/4 red,
    same machine and commit, back to back. Traces now record snapshots only — `TestArtifacts` already
    writes a full-page PNG per test, and what a stuck journey needs is the DOM.
- **The docs that ship inside every package now describe what actually ships.** `NUGET.md` is packed into
  all 24 packages via `Directory.Build.props`, which makes it the most-read page the project publishes and
  the one nobody edits — it listed **7**. Missing: every battery (Jobs, Mail, Cache, Outbox, Data, Logging,
  Dashboard), both SQLite satellites, both alternative database providers, and `Rask.Testing`. It also
  showed only `rask new`, though `generate`, `db`, `dev` and `deploy` have shipped since, and still led
  with the pre-OPF tagline the README and `llms.txt` had moved on from. Rewritten by role — pick a host,
  add the batteries you want, pick a database — and **a test now fails the build if a packable project is
  not named there**, next to the existing guard that catches one missing from the pack workflow.
- **`CONTRIBUTING.md` no longer contradicts itself about CI.** It said tests don't run in CI and then, 20
  lines later, that CI runs them; it credited a CodeQL workflow this repo doesn't have; it gave the
  diagnostic range as RASK001–029 twice when it is 001–035; and it opened with bare `dotnet test`, which
  pulls in the Playwright suite the rest of the page tells you to avoid. It now says what `ci.yml`
  contains, and points at the two gate scripts the hooks actually run.
- **`llms.txt`** framed deployment as "opt-in `--docker`" when `rask deploy` — SSH, blue-green, health-gated
  cutover, Caddy auto-HTTPS, bare-VPS provisioning — has shipped, and its battery list stopped at four.
- **`AGENTS.md`** listed 8 of the 12 committed skills, omitting `add-codefix` and all three
  "drive the real app" playbooks — the ones an agent needs precisely when a passing test isn't proof.

- **The six diagnostics that told you production would break, without telling you how to stop it, now
  say.** #275 gave the route family an actionable fix clause but only reached the descriptors in
  `RoutesGenerator`; the two that deserved it most were in other files and were missed.
  - **RASK029** (a CQRS handler) and **RASK035** (a job or outbox event) are *Warnings* announcing a
    guaranteed runtime failure — the type is skipped, so dispatching it throws or a queued message
    dead-letters — and they stopped at the bare phrase (`is abstract`, `is a file-local type`, `is not
    accessible from generated code`). A warning you can miss, telling you a crash is coming and not how to
    avoid it, is the worst shape a diagnostic has. Each reason now arrives with its remedy from a single
    switch in `SymbolRegistration`, so the two halves cannot drift.
  - **RASK003** was the one route diagnostic #275 skipped, in the file it edited: it named the offending
    segment and never showed a correct one. Each of the four parse failures now shows the template you
    meant (`/users/{id:int}`, `/files/{folder}/{name}`, a parameter in its own segment).
  - **RASK011** stated the constraint and stopped. The remedy was only in `docs/diagnostics.md`, which is
    not where you are at 2am; it now names both escapes — a parsable type, or take it as `string` and
    convert inside the page.
  - **RASK015 / RASK017** were purely descriptive, so the `RaskScopedCssAutoInclude` /
    `RaskScopedJsAutoInclude` opt-out — the right answer whenever the orphan file is a deliberate global
    one — was undiscoverable from the error that fired.
- **BREAKING — every RASK diagnostic reports under one category, `Rask`.** There used to be two, split by
  what produced the diagnostic: generators used `Rask.Generators`, analyzers used `Usage`. That is an
  implementation detail of this repo, and it leaked to the consumer — a category is what an
  `.editorconfig` rule or an IDE's group-by keys on, so
  `dotnet_analyzer_diagnostic.category-Rask.severity = …` silently covered 22 of the 35 and quietly
  ignored the other 13. **If you have a rule keyed on `Usage` or `Rask.Generators`, point it at `Rask`**;
  one line now covers the family. A test fails the build if a descriptor drifts out of it.
- **Quick fixes for the three most-hit mechanical diagnostics.** `Rask.Generators.CodeFixes` shipped two
  providers; the highest-value candidate wasn't among them.
  - **RASK014** — `new Widget()` → the generated `Widget()` factory. It is an **Error**, so it stops the
    build, and it is the first thing a Blazor or plain-C# migrant hits, because `new` is simply what you
    reach for. **Deliberately withheld when the construction has arguments or an object initializer**: the
    factory's parameters are generated from the component's public properties in an order that is not the
    constructor's, so carrying positional arguments across would compile and mean something *else*, and an
    object initializer is only legal after `new`. A quick fix that silently changes meaning is worse than
    none — in those cases the error stands with its message, which already names the factory.
  - **RASK026** — deletes the redundant `StateHasChanged()` statement, which is what its message says to
    do. Offered only when the call is the whole statement, so it can't change what an expression-bodied
    lambda returns.
  - **RASK027** — removes the `OnXAsync` argument and keeps the sync one. The diagnostic is already
    anchored on the exact argument, so there is nothing to infer.
- **All 35 diagnostics now carry a `description`, so the IDE's expanded tooltip says something.** Only 9
  did; the other 26 included *every* build-breaking Error (003, 014, 019, 021, 032), where the reader's
  only route to more detail was clicking through to the docs — not something you do mid-keystroke. Each
  description explains the consequence and the surprising part rather than restating the message, which is
  already on screen.

- **`Rask.Testing` can read structure, not just attributes.** Its scan had no notion of elements or
  nesting — it said so in its own remarks — so every structural assertion in a consumer's test degraded to
  `Assert.Contains("<span class=\"badge\">3</span>", page.Html)`, brittle against exactly the
  attribute-order invariant this framework's own suite goes to lengths to pin.
  - **`Find` / `FindAll` / `Exists` / `TextOf` / `TestId`** over a parsed tree. `Find` throws both when
    nothing matches *and* when several do — a test that silently took the first of several keeps passing
    after somebody adds a second — and a failure names the near miss (`'#items' matches 1, so the rest of
    the selector is what fails`) and the path of each candidate.
  - The **selector is a documented subset** (`tag`, `*`, `#id`, `.class`, `[attr]`, `[attr="v"]` and the
    `^= $= *=` variants, `:has-text("…")`, descendant and `>`), and **anything outside it throws**. A
    selector that quietly matched nothing because `:nth-child` was ignored would turn a green test into a
    lie. The parser is ~200 lines rather than a dependency: `Rask.Testing` is a shipped package, so a
    parser dependency lands in every consumer's test project, and Rask's own serializer emits the markup —
    always double-quoted, always encoded — which is what makes a small reader correct here.
  - **`page.On("#save").ClickAsync()`** targets an element by name. `HandlerId` returns the *first* match
    in the document and `HandlerIds` is indexed by position, so adding an unrelated button above the one
    under test silently re-points every such assertion and the test keeps passing. A handle rather than a
    `ClickAsync(selector)` overload, because `ClickAsync` already takes a `string` — the JSON payload.
  - **`TestDownloadSink`.** `Navigator.Download` refuses to run without an `IDownloadSink` and its message
    says *"If you're in a unit test, register a fake"* — while the testing package shipped none, so
    everyone wrote the same twenty lines.
  - **Event dispatch now enters the `Navigator`'s handler scope**, as a live session does. Without it
    `NavigateTo` / `Download` / `SetQuery` all refused with "can only be used from event handlers" —
    true of the harness, not of the component — so a page that navigates or exports on click could not be
    unit-tested at all, only through Playwright.
  - **`TestRoute.At("/search?q=hello%20world")`** seeds a `RouteState` with its query string parsed,
    decoded and repeated keys kept. Seeding a path was already a one-liner; seeding a query meant building
    an `IQueryCollection` by hand, so most tests simply didn't.
  - **`CapturingDiagnostics`** captures framework diagnostics, so an app author can finally assert that a
    swallowed fault happened — or that none did. A public wrapper rather than a public `RaskDiagnostics`:
    `Rask.Testing` is already on Core's `InternalsVisibleTo` list, and a public seam is irreversible where
    a wrapper is not.
  - **`TestJSRuntime` stops failing silently on a type mismatch.** `SetResponse("getCount", 1)` against a
    component calling `InvokeAsync<long>` returned `0` — indistinguishable from "not configured", so the
    test read as though the component had ignored the value. Unconfigured still returns `default` (that is
    deliberate and documented); configured-with-the-wrong-type now throws and names both types.

### Fixed
- **Hot reload stops claiming success for an edit that never reached the page.** The green
  "Hot reload applied" pill is driven by the coordinator's `Applied` signal, and the repaint is the whole
  of what a developer can see — so announcing it after a failed repaint told them the opposite of the
  truth, with the only evidence on the server's stderr. `RerenderAllForHotReloadAsync` catches per session
  (one faulting tree must not stop the others repainting) but it was also *swallowing*: it reported
  success upward whatever happened. It now reports the fault as a diagnostic naming the consequence
  ("its page still shows the previous render"), returns whether every session repainted, and the
  coordinator announces only when they did. A missing pill is the honest signal — nothing visibly changed,
  because nothing did. Partially addresses #603.

### Added
- **In development, a fault the tree survived is shown *over* the app instead of replacing it (#607).** The
  full-document swap is right in production and wrong in development, where a handler that throws is the
  common case rather than the exceptional one: it takes the scroll position, the form input, the expanded
  panels and the route with it, at the moment you most want to look at the state that produced the bug.
  The app now stays mounted and live behind a dismissible panel carrying the exception type, the message
  and a collapsible stack — the shape React's and Next's dev overlays settled on, and for the same reason.
  - **Only for a fault the tree survived.** A *render* fault still replaces the page, in development as in
    production: re-rendering the subtree that just threw would only throw again. `ErrorBoundary` now
    records whether it was tripped by a render, a handler or an async lifecycle hook, which is what makes
    that distinction possible at all.
  - **It rides inside the render payload**, not a new frame — the same reasoning as `resume`, `history`
    and `auth`. The frame stream is a documented contract, and an extra frame is observable in ways an
    extra field is not. Its bytes are discounted from the diff-vs-full comparison for the same reason the
    resume record's are: it sits on both sides, so counting it only against the diff would ship the whole
    body precisely when a minimal frame is most readable.
  - **Both dedup gates now let it through.** A click whose only effect was the exception renders
    byte-identical HTML, so without this the overlay would never arrive in the simplest case of all.
  - Production is unchanged and cannot leak: `DevErrorInfo.From` returns `null` outside development, so no
    stack trace can reach a browser even if a call site forgot to check, and the client requires the
    `data-rask-dev` flag as a second, independent gate.

### Added
- **`IOriginPrivateFileSystem` — a private, persistent file tree the app owns outright (#642).** The two
  storage wrappers Rask shipped both stop short of a file an app writes to repeatedly and reopens on the
  next visit: `IIndexedDb` is a key/value store, and `IFileSystemAccess` is a *picker* — it needs a user
  gesture and models a document the user chose, not storage the app manages. OPFS is the missing third
  case, and the one a local database file needs.
  - **Reads and writes take a byte offset**, so a large file is worked in chunks and the payload crossing
    the interop boundary is bounded by the range asked for, not the size of the file. A ranged write
    leaves the rest intact; writing past the end extends the file, zero-filling the gap.
    `ReadAllBytesAsync`/`WriteAllBytesAsync` are the single-round-trip convenience over the same store.
  - **Paths, not handles.** The tree is app-owned and persistent, so the same path is reopened every
    session — there is nothing to pick and nothing to keep alive between calls. Parent directories are
    created on write.
  - **A missing path returns `null` rather than throwing**, matching `IKeyValueStore.GetAsync`, and a
    ranged read past the end returns the bytes that were there — an ordinary short read, not an error.
  - Works on both transports, but every call is a round trip: under the Server transport that crosses the
    WebSocket, so the local-database scenario this exists for is in practice a WASM one.
- **`IStorageEstimator` can now ask for storage to survive eviction.** `IsPersistedAsync` and
  `RequestPersistAsync` wrap `navigator.storage.persisted()`/`persist()` — the same object `estimate()`
  already came from. Without this, OPFS is persistent but still reclaimable under storage pressure, which
  is the difference between a cache and a database. Both resolve `false` where unsupported, so an app can
  treat "not persisted" and "cannot be persisted" the same way: writes are evictable either way.
  Chromium grants from engagement heuristics without prompting; Firefox prompts, so call it from a
  gesture handler.
- **`Rask.ObjectStore` — S3 and Azure Blob with no cloud SDK behind it (#642).** The AWS and Azure SDKs
  are large, reflection-heavy, and not usable from a browser, which ruled them out for the one place this
  is most useful: a WASM app talking to a bucket with no backend in between. Signing SigV4 is a few dozen
  lines of HMAC, and an Azure SAS needs no signing at all, so the client does both itself and runs
  unchanged server-side and in the browser. One interface covers S3, Cloudflare R2, Google Cloud Storage
  (through its S3 interop keys), MinIO, Backblaze B2, DigitalOcean Spaces and Azure Blob.
  - **Ranged reads, streamed writes.** Object storage charges per byte moved, so `GetRangeAsync` asks for
    a range rather than an object; `PutAsync(key, Stream, length)` uploads without buffering, keeping
    object size and memory use unrelated.
  - **A missing object returns `null`; a range past the end returns a short read.** Those two stay
    distinguishable on purpose — anything walking an append-only log has to tell "gone" from "nothing new
    yet", and collapsing them into one answer is how a sync client silently decides its peers vanished.
  - **`TryCreateAsync` is mutual exclusion without a lock service** — an atomic compare-and-create
    (`If-None-Match: *`) that S3, Azure Blob and GCS all support. Preferred over an Azure blob lease,
    which exists on one provider, needs renewal, and strands the resource if the holder disappears.
  - **Credentials are asked for per request**, so an expiring STS session or SAS refreshes without
    rebuilding the store. `InMemoryObjectStoreCredentials` — the browser case, where the user supplies the
    credential — holds it for the life of the process and offers *no* persistence option: a credential
    that survives a reload is one any later script injection can read back, so getting there has to be a
    deliberate act rather than an overload someone reaches for.
  - **Clock skew is handled rather than assumed away.** SigV4 rejects a request more than 15 minutes off
    the service's clock and device clocks are genuinely wrong; the service's own `Date` is read from the
    rejected response and later requests sign against corrected time, so a wrong clock costs one round
    trip instead of an error that explains nothing.
  - The signer is verified against a separate implementation of the algorithm written from the AWS
    specification, not against its own recorded output, and against the encoding rules the specification
    names individually — `%20` rather than `+`, no double-encoding, slashes preserved in a key, query
    parameters sorted after encoding.
- **`Rask.Sync` — the merge engine offline-first sync rests on (#642).** Two devices edit the same data
  while offline; both come back; the data has to be something. This answers that deterministically, and
  it is deliberately a package with **no dependencies and no I/O** — it does not know where operations
  come from or go. This is the piece where a mistake silently destroys a user's work, so it is the piece
  with nothing else in it to hide behind.
  - **Three properties, asserted by brute force rather than by example.** Replaying a log is
    order-independent, idempotent and convergent. The tests permute every ordering of each log, replay it
    twice, interleave duplicates, and split it at every point — because a hand-picked ordering only proves
    that ordering works, where the actual claim is that order does not matter. Together these are what
    remove the need for a server: a client never has to know what it already sent, never has to coordinate
    with a peer, and never has to be right about the order.
  - **Operations carry changed fields, not whole rows**, so two devices editing different fields of one
    record offline both keep their work — a whole-row operation would silently discard one of them. Values
    are raw JSON, opaque to the engine, so it needs no knowledge of the application's types.
  - **Conflicts are reported, not hidden.** Last-writer-wins loses data by design: something has to lose,
    and no cleverer rule avoids that. What it can avoid is nobody being told. Every merge that discards
    another node's value returns a record carrying both values and both stamps. Merging stays fully
    automatic. Deliberately *not* reported: a device overwriting its own earlier value, two devices writing
    the same value, and duplicate delivery — a conflict feed that fires on every ordinary save is one
    people learn to ignore, which is the same outcome as not reporting at all.
  - **A hybrid logical clock, because wall clocks lose data.** Device clocks disagree, users set them by
    hand, and they run backwards over NTP corrections — so an edit made later can carry an earlier
    timestamp and be discarded silently. The clock never moves backwards and advances on every stamp it
    observes, so anything issued after receiving an operation sorts after it. Stamps are fixed-width hex,
    so sorting them as strings equals comparing them as values — which is what lets a log be ordered by
    object key with no parsing and no index. Node identity is the final tie-break: without it two devices
    can mint identical stamps and the winner depends on arrival order, which is divergence, not a merge.
  - Rows are addressed by entity name plus a `Guid`, because an offline insert has to mint its own key.

### Added
- **`Rask.Sync.Client` — several devices sharing data with no server between them (#642).** Joins
  `Rask.Sync`'s merge engine to an object-storage bucket. The design rests on one rule: **each device
  writes only under its own prefix** (`clients/{id}/ops/`) and never touches another's. No two clients
  ever write the same key, so there is nothing to lock, nothing to retry on conflict, and no lease to
  renew or to leak if a device disappears mid-write. Everything else follows from it.
  - **Forward-only reads.** Keys carry the hybrid logical clock in fixed-width hex, so they sort in the
    order things happened and a remembered key resumes exactly where the last sync stopped — the cost of
    a sync is what changed, not what exists. Peers are found with a grouped listing, so discovery costs
    one response listing the *devices* rather than one listing every object they have ever written.
  - **Offline is the normal case, not an error.** `RecordAsync` never touches the network, so the app
    behaves identically with or without connectivity. A failed upload leaves the queue intact and the next
    sync re-sends it, which is safe precisely because applying an operation twice changes nothing.
    `SyncPhase.Offline` is deliberately not a failure state — showing it as one trains people to ignore
    the indicator that matters.
  - **The status carries the two questions apps usually leave unanswered**: `Pending` ("if I close this
    tab now, do I lose anything?") and `Conflicts` ("did syncing throw away something I typed?"). Neither
    can be answered unless the engine counts them, so they belong on the status rather than in the app.
  - `ISyncStore` keeps the queue and watermarks across reloads. Losing the queue loses a user's offline
    edits; losing the watermarks costs only re-reading, because replay is idempotent — an asymmetry worth
    knowing when choosing an implementation.

### Changed
- **`IObjectStore` gained `startAfter` on `ListAsync`, and a `ListPrefixesAsync` grouped listing.** Both
  exist because syncing forward-only needs them: without `startAfter` every sync re-reads the whole
  history, and without a grouped listing, discovering which devices exist means listing every object they
  have ever written — which would undo the saving. `startAfter` is server-side on S3 (`start-after`);
  **Azure Blob has no equivalent**, so there it filters the results and the listing still costs the same.
  The behaviour is identical either way, and the difference is documented on the interface rather than
  left for someone to discover from a bill.

## [0.20.0] - 2026-08-06

### Fixed
- **`Rask.Postgres` and `Rask.SqlServer` are actually published now.** Both had everything a shipped
  package has — `IsPackable`, a `PackageId`, a description, their own `NUGET.md`, tests — except a
  `dotnet pack` step, and the list of those in `release.yml` and `nightly.yml` is written out by hand.
  So the two packages `--database postgres|sqlserver` scaffolds a `PackageReference` to, and that
  [`databases.md`](docs/databases.md) links to a nuget.org page for, existed on no feed: a generated
  project couldn't restore. Nothing caught it because the packages themselves were fine — they built,
  their tests passed, and the repo's packaging guard only checked that a shipped package never
  *depends* on an unpublished one. It now also checks that every packable project under `src/` is
  named by a pack step in **both** workflows, so the next package can't be forgotten the same way.
  (Closes #616.)
  - Corrected two stale claims in `llms.txt` that `docs/databases.md` already contradicted: that
    multi-instance is unsafe on every provider (the jobs, mail and outbox processors lease the work
    they claim — several instances are safe, with the caveat that a lease bounds rather than removes a
    duplicate side effect), and a trailing "SQL Server is NOT shipped".

### Changed
- **A wrong `rask` command line now tells you what's wrong, what's allowed, and what to run next.** The
  pieces were all there — a styled console, a self-documenting argument schema, a `--help` renderer that
  can't drift from what parses — but the *error* path had never been wired through them, so roughly forty
  hand-written validations each printed their own phrasing, unstyled, and returned the wrong exit code.
  They now share one path: red, followed by the usage line and `Run 'rask <command> --help' for details.`
  A misspelling gets a nearest-match suggestion (`Unknown command 'genrate'. Did you mean 'generate'?`)
  for commands, options, actions, and option values alike — deliberately conservative, so an
  unrecognizable word gets no guess rather than a confident wrong one.
  - **Options with a closed set of values are declared, not hand-checked.** `--template`, `--database`,
    `--host`, `--id` and `--validation` now name their values in the schema, which means one phrasing for
    a bad one, the set printed in `--help`, the values offered by tab completion, and
    `--template SERVER` accepted and normalized rather than mysteriously rejected.
  - **Subcommands are declared too**, so `rask db --help` lists its seven actions with descriptions, an
    unknown one lists them back at you, and `rask db <tab>` completes them in bash, zsh and fish. This
    also makes `rask generate`'s `ca` → `cache` alias discoverable, which it never was.
  - **Exit code `2` now means what `docs/cli.md` always said it meant.** A wrong command line — unknown
    command, option, action, or value, a missing value, contradictory options — exits `2`; only work that
    was attempted and failed exits `1`. Previously only two paths in the whole CLI returned `2`, so a
    script couldn't tell a typo from a failed deploy. **This changes the exit code of every rejected
    invocation from `1` to `2`.**
  - **`-h` is `--help`, everywhere.** `rask deploy -h box` used to print help and never deploy, silently:
    the router resolves `-h` before a command parses its own arguments, and `deploy` had claimed it for
    `--host`. `--host` keeps its long form and loses the short one, and a test now holds `-h` reserved.
  - Fixed the "couldn't find a single .csproj" message, which said the same thing whether it found none or
    found several. Backup, restore and the other outcomes that were printing plain text are now styled
    like their siblings, and `rask generate`'s overwrite refusal mentions `--dry-run`.

### Added
- **A redeploy reload no longer throws away what the user had typed.** When a replacement server can't
  carry a session over, the page reloads — and until now that restored your scroll position and focus but
  silently discarded every field you were part-way through filling in. The reason it did was real: there
  was no way to tell a value the user typed from one the server had rendered, so writing stale client
  copies back over correct server output would have turned a cosmetic loss into a data one. What makes it
  answerable is that the DOM keeps the server-rendered `value` **attribute** separate from the user's live
  `.value` **property**, which gives a merge *base* — so this is a **three-way merge**, not a guess.
  Only fields the user actually edited are candidates, each carrying the value the server had rendered
  when they first touched it (captured *then*: every echo of their own keystrokes rewrites the attribute,
  so reading it later would compare the user's text against itself and restore nothing). After the reload
  a field is re-applied only when the replacement rendered that same base — its state is unchanged, so the
  edit is still the newest thing anyone knows. A different base means the replacement knows something the
  stale copy doesn't: it wins, and the edit is dropped silently, exactly as a reload behaved before. What
  *is* restored is then pushed back over the socket, so the server's model ends up holding what the page
  shows rather than the pristine values it just rendered — a form displaying values the server doesn't
  have is the data loss this feature would otherwise create, not prevent. The restore also arms the
  existing lagging-frame guards, so the server's first catch-up render (computed from its own pristine
  model, before the converge message can reach it) is held off rather than wiping the text and flickering
  it back. Secrets never enter `sessionStorage` at all — password, file, hidden and one-time-code inputs,
  and anything with a `cc-*` / `current-password` / `new-password` `autocomplete` — and
  `data-rask-no-restore` opts out a field or a whole subtree. A field needs an `id` or a `name` to be
  restorable (a bound `Input` gets one from the bound property for free), and a key matching more than one
  control is skipped rather than guessed at, because writing a restored value into the wrong field is the
  failure worth refusing. Handler ids are never persisted: they are positional per render, so one carried
  over from the old page would name a *different* handler on the new one. The field snapshot lives under
  its own `sessionStorage` key, so a large editor that fails the quota can't cost you the scroll position
  too. `<select>` is not covered yet — it has no lagging-frame guard to hold the first re-render off with
  (tracked separately).
- **The application log now stores the scope state each entry was written under.** `RaskLoggerProvider`
  returned `null` from `BeginScope`, so the request id, user id and correlation id an app opens a scope with
  were dropped — and the whole point of keeping logs is answering *"what else happened on that request?"*,
  which without them has to be reconstructed from message text. Whatever `ILogger.BeginScope` was given is
  now flattened (outermost first, message templates keeping their values and dropping the format string) and
  stored beside the entry, queryable through `LogQuery.ScopeKey` / `ScopeValue` and shown on the row in the
  dashboard's History mode. The filter matches the stored key exactly via `json_extract`, so a request id
  cannot match an entry that merely mentioned it in a message. Cost sits where it must: flattening happens at
  the log call, because scope state is short-lived and may be reused the moment the scope closes, while the
  JSON encoding is deferred to the writer's own thread — the "a log call never waits on the disk" invariant
  is intact. Bounded by `MaxScopeValues` (16) and `MaxScopeValueLength` (256) so nested scopes can't grow a
  row without limit, and switchable off with `CaptureScopes = false` for scopes carrying values you would
  rather not keep at rest. A store created by the previous release gains the column on first use — there is
  no migration to run, because this database is framework-owned and deliberately outside yours.
- **`session-load` — what a host does when its sessions are actually used.** The existing capacity reports
  answer how many sessions *fit*: a retained-memory question, measured against a stub socket that never
  receives. Nothing said what happens when those sessions are used, and a capacity number you can't serve
  isn't a capacity number. This one drives real `ClientWebSocket`s against a real Kestrel host and times
  the round trip a user feels — the click, the render it causes, and the ack that closes it — reporting
  events/sec with exact p50/p95/p99. Closed-loop, so throughput and latency stay honest with each other
  rather than reporting queue depth dressed as latency. The headline: **page size costs throughput before
  it costs memory** — an empty shell and a 5-row page are within noise, while a 200-row grid costs ~4× the
  per-event time. Wired into the nightly alongside `session-footprint` and `session-churn`, which had
  never run in CI at all, so the numbers in `docs/configuration.md` stop being one person's local run.
  It reports no memory column on purpose: the generator shares a process with the host, so a heap reading
  counts the client's own sockets — an early version did exactly that and produced a figure that didn't
  rise with page size.
- **A restart or a redeploy no longer costs your users their page.** A live session is a component tree, a
  DI scope and a set of cancellation tokens — it cannot be serialized, so it cannot be moved or saved. Until
  now that meant the process holding it going away took the page with it: every `rask deploy` blue-green
  swap answered every connected client with *"Your session timed out. Reload to continue."* The swap was
  zero-downtime for HTTP and a full reload for everyone actually using the app.
  What travels instead is a small sealed record of where the page was and what the app declared through the
  new **`IPersistentState`** — and a host that has never heard of the session **rebuilds** the page around
  it. Nothing resumes; the page is built again, which is why what you declare is what comes back. Declare
  the state a user would be annoyed to lose (a filter, a wizard step, an unsaved draft) and let the rest
  come back from the database. Even an app that declares nothing gets the route, so a deploy becomes a
  re-render rather than a reload.
  The record is held by the browser, so this needs **no shared store, no sticky routing and no new
  infrastructure** — the same property that will later let a reconnect land on a different replica. It is
  encrypted and authenticated under its own data-protection purpose; expiry is enforced by ASP.NET's
  time-limited protector, so an expired record cannot be opened at all rather than relying on a field
  somebody remembers to check; it carries no principal but is bound to the identity it was issued to and
  compared in fixed time, so it cannot be replayed onto another account or inherited across a sign-in; and
  a rebuild takes a `MaxSessions` slot through the same atomic reservation a `GET` uses, so the reconnect
  storm after a deploy sheds like ordinary traffic instead of walking past the cap. The bag is capped at
  16 KB — over budget a session keeps working but declares itself unresumable and falls back to the reload
  it would have had, with a diagnostic saying so.
  It rides **inside the render payload**, beside `history` and `auth`, rather than arriving as its own
  frame: a `hello` with nothing pending must still emit no frame at all. Costs nothing when absent — the
  payload-byte benchmark is unchanged to the byte. Configure with `SessionResume` (default on) and
  `ResumeTokenLifetime` (default 1 hour). **Requires a persisted data-protection key ring** — a record
  sealed before a redeploy cannot be opened after one otherwise, which is what the scaffold's `/data/keys`
  change below is for.
- **One shutdown ladder instead of three unrelated numbers.** `docker stop -t 20` and the scaffolded
  `HostOptions.ShutdownTimeout = 15s` were two hardcoded constants coupled only by a code comment, free to
  drift apart silently — and the generator test pinned the literal `15`, so it would have kept passing.
  Both now derive from a single `ShutdownBudget`, and the test asserts the *relationship* (the app budget
  fits inside the deploy grace, with margin) rather than the number. Scaffolded apps also set
  **`ServicesStopConcurrently = true`**, which turns out to be load-bearing rather than a tune-up: stopped
  one at a time — the .NET default — each pillar's own shutdown grace *sums* inside the single budget
  (10 + 10 + 5 + 5 = 30s against 15s), so whichever hosted service stopped last got no grace at all, and
  *which* one that was depended on the order of `AddRaskX` calls in someone's `Program.cs`. Stopped
  concurrently they overlap at 10s. `rask deploy` also now pauses briefly between pointing Caddy at the new
  container and stopping the old one: `caddy reload` returns as soon as the config applies, but Caddy still
  holds pooled connections to the old upstream, and a request it was about to write onto one when SIGTERM
  landed became an un-retried 502 (`lb_try_duration` defaults to 0). There was previously no gap at all.
- **`rask dev` now says which native head hot-reloads, because one of them does.** Refusing a native
  project and pointing at `dotnet build -t:Run` was correct but only half the answer, and the missing half
  was the useful one. Applying new IL to an app already running on a device needs a device-side delta agent
  that .NET doesn't ship, so a **Native + Local** head genuinely has to be restarted. A **Native + Server**
  head does not: it loads a remote Rask Server, so as far as hot reload is concerned it *is* a browser —
  point `ConnectToServer` at your dev machine, run `rask dev` against the server project, and every applied
  edit repaints the device over the ordinary live connection, "Hot reload applied" pill included. That path
  worked already and nothing said so, which read as "native means no edit loop". The refusal message, `What
  hot-reloads` and the Local-vs-Server section now all draw the same line.

### Fixed
- **The WASM client no longer logs every event payload to the browser console.** `send()` opened with
  `console.log("[Rask] send", payload)` — behind no flag, in production builds, on a path the comment
  directly above it documents as firing ~60×/sec while someone types. `payload` carries the event's
  value, so everything a user typed into a form was written to a place nobody expects data to land, and
  the console was useless at 60 lines/sec for the debugging it was there to serve. Two quieter traces
  (`setExports`, `navlink click`) went with it; the boot one is now an `error` raised only when the .NET
  dispatch export is *unreachable*, which is a dead app and worth saying. The Server client had zero
  `console.log` all along, which is the standard the others now meet — pinned by a test over all six
  shipped `.js` files, including `Browser/rask.wasm.js`, the committed build artifact the browser
  actually downloads and the copy most able to drift unnoticed. `console.warn` / `console.error` stay:
  they report a fault rather than narrating the happy path.
- **A full reply now moves a `<select>`'s live selection, not just its `selected` attributes.**
  `morph()` synced the IDL properties that attributes can't reach for `INPUT` and `TEXTAREA` only, so a
  reconnect, a scoped-CSS full reply, the WASM boot/navigation morph and the redeploy reload all moved a
  select through its `selected` **attribute** alone. Per the HTML spec an option carries a *dirtiness*
  flag, and once set the content attribute stops driving selectedness — so the server's answer was
  silently ignored. Measured in Chromium rather than argued from the spec, and the result is narrower
  and stranger than expected, which is why it survived: an attribute-only move is **not** broken by user
  interaction as such. Dirtiness blocks the attribute only on the option that is dirty, so a move onto a
  *pristine* option still lands, and a single-select is rescued again by the spec's "ask for a reset".
  What actually failed: a single-select whose target the user had already touched (the box simply
  ignored the server), and multi-selects, which get no reset — a user-picked option the incoming render
  did not mark could not be cleared, so the control accumulated selections neither side ever chose. The
  new `SELECT` arm applies the selection through the select, and consults the #588 lagging-frame guard
  first, because making full replies start moving selects would otherwise let a reconnect clobber a
  just-made pick — trading one bug for its mirror image. A select whose render marks nothing shows its
  first enabled option, which is what a fresh parse of the same markup shows, rather than blanking.
- **A `<select multiple>` reports every option the user picked, not just the first.** The change
  dispatch sent `el.value`, and for a multiple select that is *the first selected option, or `""` when
  none* — the DOM has no multi-value `value`. So picking three reported one and the server's model
  converged on that one: correctly, from a report that was the wrong shape rather than merely late,
  which is why #588's lagging-frame guard could not help. The frame now carries a `values` array
  alongside `value`; `value` keeps its exact meaning, and `values` is omitted entirely for every other
  control, so no existing payload grows a byte. `Select<T>(Bind: …, Multiple: true)` binds the whole
  selection when `T` is a string collection (`string[]`, `List<string>`, `HashSet<string>`, or the
  read-only/mutable collection interfaces), marking every picked option on render and *replacing* the
  collection on each change rather than editing membership — the report is absolute, so a replace
  re-syncs even when an intermediate render was coalesced, which a snapshot-based add/remove cannot.
  `Multiple: true` over a scalar keeps the single-value binding: that model can hold one answer, and
  widening it silently would be the more surprising change. The element type is deliberately `string` —
  the reflective version needs `MakeGenericType`/`Array.CreateInstance`, both `RequiresDynamicCode`, and
  the WASM sample has to publish with zero trim warnings. All three hosts now build the change frame
  through one shared helper rather than three hand-copied copies, which is the drift that produced this
  bug and #588 before it; a contract test holds them to it, and the frame-shape guard from #592 knows
  `values` rides on `change` alone, so an `input` frame can't collapse a selection to one value.
- **You get the development error page whenever you are actually in Development, not only when an
  environment variable said so.** `DefaultErrorPage` decided by reading `ASPNETCORE_ENVIRONMENT` /
  `DOTNET_ENVIRONMENT` and nothing else. The reason was sound — `Rask.Core` takes no
  `Microsoft.Extensions.Hosting` dependency — but the consequence was not: `dotnet run --environment
  Development`, `appsettings.json`, assigning `builder.Environment.EnvironmentName`, and IDE profiles
  that set configuration rather than the process environment all select Development *without* setting a
  variable, and every one of them silently produced the production page — no stack trace, no source
  excerpt, and no hint why. It only looked fine because `rask dev` exports the variable itself, so the
  failure appeared the moment you stepped off it, which is exactly when the stack trace matters. The
  host now answers: `UseRask` resolves `LiveOptions.IsDevelopment` from `IWebHostEnvironment`, and the
  variables remain as the fallback for a standalone host or a component rendered outside one. A host
  reporting Production is not overridden by a stale variable left in a shell.
- **A page that crashed no longer answers `200 OK`.** The root boundary catches the exception and
  renders the error document, so the response was ordinary HTML and nothing downstream could tell it
  from a healthy page — caches stored it, crawlers indexed it, uptime checks reported green. The initial
  GET for a render that faulted now answers **500**, with the same body, so the error page is still
  served and the live session still attaches.
- **The framework's error page offers `Try again`, not just `Reload this page`.** `ErrorBoundary`'s
  fallback has always received the boundary's `Recover` as its second argument, and the root boundary
  discarded it — `(ex, _) =>` — leaving a full round trip as the only way out of a fault that had
  damaged nothing. The common case is a handler that threw, where the tree is intact, so clearing the
  error puts the app straight back with its state and scroll position. A render that faults
  deterministically lands back on the error page, which is the honest outcome and what React's boundary
  does too; the reload button is still there for it.
- **Framework exception messages name the fix, not just the fault.** Most of `Rask.Core` already did —
  `Navigator`, `Context`, `RouteAuthorizationGuard`, `ExpressionAccessor`, `RaskJSRuntime` are the
  models. These did not, and all of them are page-fatal now that a render throw becomes a whole-document
  swap:
  - `RoutePattern`'s three parameter errors were the runtime siblings of RASK003 and worse off than it:
    they echoed the offending segment, showed no correct one, and carried nothing to say *which* route
    it came from — so `Empty parameter in route segment '{}'` was the whole story. They now name the
    template and show a valid segment (`{id}`, `{**path}`, `{id:guid?}`).
  - `EditContext`'s sync-validate refusal named the remedy but not the cause, so on a form carrying
    several validators you found the culprit by bisecting them. It now names what made the context
    async — the validator types, an async form-level `Validate`, or the field whose `Validate` is async.
  - `Outlet` and `RouteChainRenderer` threw two different sentences for one condition, and which you got
    depended only on whether there was a live context or merely no route in it. Converged on the better
    one, which names `Router(...)`.
  - `DragDrop`, `VirtualizeModel`, `ServerFileBackend`, `RaskEndpointExtensions` and `RootErrorBoundary`
    now show the API shape, the packaging remedy, or what the reader actually did. `VirtualizeModel`'s
    `ItemSize` check is an `ArgumentOutOfRangeException` carrying the offending value rather than an
    `InvalidOperationException` describing it — **note this is not a widening**, so a `catch
    (InvalidOperationException)` around it needs updating.
  - `Form`'s `Form requires Model or Context.` is fixed too, and turned out to be **unreachable**: both
    callers of `ResolveContext` already gate on exactly the condition it checks, and a `Form` with
    neither renders as a plain `<form>` by design. Kept and reworded for the next caller; nobody has
    seen the old text.
- **Framework diagnostics carry their level and category on every host.** The default sink rendered
  `message` (plus the exception), dropping the severity and subsystem the event has always carried — so
  on any host without a logging bridge, "framework said something" and "framework reported an error in
  the diff codec" printed identically. It now reads `[Rask:Rask.Diff] error: …`, which fixes every
  unbridged host at once. **WASM additionally gains an `ILogger` bridge**: the host already called
  `AddLogging()`, but nothing consumed it, so a browser app was the one host where a swallowed framework
  fault — a navigate fault, a JS dispatch fault, a malformed frame — never reached the app's configured
  providers at all.
- **A `<select>` no longer snaps back to the old option when a lagging re-render lands.** `value` and
  `checked` each had a guard against a frame the server computed *before* the user's edit reached it;
  `selected` — the third property the diff codec mirrors onto its IDL twin — had none, so it was applied
  unconditionally. Pick an option, have a re-render computed a moment earlier arrive, and the box reverted
  to the server's older answer until the echo caught up. The focus guard doesn't help, for the same reason
  it doesn't help a date input: a select commits on *change*, so focus has already moved on by the time
  the stale frame lands. The change dispatch now records the pre-pick `selected` attribute of **every**
  option in the select — the whole control, exactly as the checked guard records the whole radio group,
  because a stale frame re-selecting the previously chosen option natively deselects the new one — and the
  apply path suppresses a frame that still carries it, releasing as soon as an authoritative frame differs
  so server-driven changes keep winning. Selection is also applied through the `<select>` itself rather
  than by poking each option, so one write moves the whole group instead of leaving a single-select
  momentarily showing its first option between the remove and the set. And the three guards are now armed
  from **one** shared recorder rather than a copy hand-maintained in each host runtime — the drift that
  produced this bug, since both copies covered `value` and `checked` and neither covered `selected`; a
  source contract test now pins that both hosts go through it. Restoring a `<select>` across a redeploy
  reload is still not covered (see `docs/configuration.md`): the guard it was waiting on now exists, but
  the save/apply side is separate work, and `<select multiple>` first needs a change dispatch that reports
  every selected option rather than just the first.
- **A stale handler id can no longer fire whatever now sits in its slot.** Handler ids are positional per
  render, so the same id names a different handler after the tree changes — and dispatch keyed on the id
  alone. The frame's `type` was read to route a few special messages (`hello`, `navigate`, `jsResult`,
  `dotNetInvoke`) but never cross-checked against the handler the id resolved to, so
  `{"id":"h37","type":"input","value":"…"}` arriving at a page where `h37` is now a parameterless
  `OnClick` **ran that callback**, with nothing to say the wrong thing had fired. Not a cross-origin hole
  — the socket is same-origin and session-bound, so the sender is already the user whose page it is — but
  positional ids make the collision ordinary rather than exotic, and the silence is what made it bad. The
  frame's declared type is now checked against the argument the delegate demands (a value frame needs a
  value handler, a `submit` needs `FormData`, a click needs a parameterless one), and a mismatch is
  answered exactly like the stale id it is: `false`, no handler, no render. Deliberately **not** a
  whitelist — a type this build has never heard of is still dispatched, so a browser holding a cached
  client from another deploy doesn't have its events silently swallowed. Two events of the *same* shape
  (a `focus` frame against a `click` handler, both parameterless) still pass; separating those needs the
  event name carried per live handler, which costs a reference per handler in every session and buys
  nothing for two empty payloads. The check reads the frame's raw UTF-8 through `JsonElement.ValueEquals`
  — 14 ns and **zero allocation** on the accepted path, on a path that already parses JSON.
- **The browser E2E gate can pass again — it had been red on `main` since #470.** The playground journey
  waits for `.pg-ide.is-ready` to know the in-browser Roslyn workspace has its references, and the
  dark-first design overhaul rewrote the readiness pill into a `BsBadge`, dropping the `is-ready` /
  `is-off` / `is-loading` classes in favour of a Bootstrap colour. The selector matched nothing from then
  on. What kept it alive is *how* it failed: an unresolvable Playwright locator fails by **timing out**,
  and this one sits right after the multi-megabyte reference download — so the report looked like a slow
  network rather than a missing class, and the whole suite got waved through with `RASK_SKIP_E2E=1`, which
  is the same habit that would wave a real regression through. The pill carries its state as a class
  again (`IdeBadgeState`), and the mapping is pinned by unit tests that read both the view and the E2E's
  own source: the exact edit #470 made now fails the **fast** gate in under a millisecond with a message
  naming the cause, instead of three minutes into a suite people have learned to skip. Swept the
  playground's other E2E selectors (`pg-run`, `pg-preview`, `pg-example`, `pg-code-host`) while in there —
  `pg-ide` was the only casualty. And the suite can now pass **twice**: the shop's stored-log journey
  asserted on `GetByText("Application started")` strictly, while the log store is a file in the sample's
  publish directory that the fixture reuses — so the second run found two start-up lines and failed with a
  match-count error that reads like a UI bug. It now asserts what it means (a start-up line reached the
  store), which is what makes the gate survive a re-run after fixing something else.
- **The static-host E2E fixtures take a port from the OS, so a second run — or a second worktree — no
  longer fails as "address already in use".** Each fixture declared a hard-coded port and a comment
  explaining that the parallel collections need distinct ones. True as far as it went, but every *copy*
  of the suite on the machine claimed the same numbers, and this repo's workflow puts several checkouts
  side by side — so a straggler host, a concurrent suite or another worktree produced a bare
  `HttpListenerException` in one of two shapes, neither of which names a port: a run where all 41 tests
  passed and the *run* still failed (xUnit reports a collection-cleanup throw as a failed run, and
  `DisposeAsync` guarded `Stop()` but not the `Close()` that throws), or a host that never came up and
  twelve `ERR_CONNECTION_REFUSED` tests that read like a broken app. Both are now gone: the OS assigns
  the port, `Close()` is guarded like its siblings, and a genuinely unbindable machine gets a message
  naming the fixture and what to look for instead of a bare listener exception.
  - The obvious fix does not work, which is worth recording: **`HttpListener` rejects a `:0` prefix**
    ("Invalid port in prefix") and cannot report an assigned port back. So the port is taken from a
    throwaway `TcpListener` and `HttpListener.Start()` is the authoritative test — a clash costs another
    candidate rather than the run. The probe binds the family `localhost` actually resolves to: a
    `localhost` prefix holds **`[::1]` only** where IPv6 is available, which is why a port can look free
    on `127.0.0.1` and still refuse to bind, and the explicit `http://[::1]:port/` form that would let
    us bind both is itself rejected as an invalid prefix.
  - It also removes a collision that was assumed not to exist: `SiteWasmAppFixture` and
    `WasmWatchAppFixture` both hard-coded **5101**, in different collections, which xUnit runs in
    parallel. The fixtures that launch a real `dotnet` process keep their fixed ports — they have to
    tell a child process where to listen, and they already fail fast on a busy one — so that half is
    tracked separately rather than folded in here.
- **A logging test no longer passes by winning a race.** `NoDrainRunsWhenTheTimeoutIsZero` wrote an
  entry to a started writer and asserted the store stayed empty, on the grounds that a zero
  `ShutdownDrainTimeout` drops it. But the writer's loop drains on its first cycle and nothing held it
  back, so the entry only survived to shutdown if the test got there first; on a loaded machine the loop
  won and appended it — which is not a bug, since draining an entry claimed before shutdown is exactly
  right. It now asserts what the option actually governs — that `StopAsync` runs no drain — with the
  loop deliberately not started, so the drain branch is the only code that can reach the store, and a
  new sibling pins the positive direction. No timing, no scheduler dependency.
- **The style pass is part of the local gate again, and the "spurious CS1503" that kept it out is
  root-caused.** `dotnet format Rask.slnx` failed on `main` with `error IMPORTS: Fix imports ordering` in
  `RaskEndpointExtensions.cs` — a `using` that drifted out of order and stayed there, because nothing ran
  the check. The pre-commit gate ran `dotnet format whitespace` only, on the belief that the style and
  analyzer passes flagged CS1503 in the routing tests spuriously. They did not. `dotnet format` evaluates
  the solution in the **default configuration**, so it resolves the `OutputItemType="Analyzer"` project
  references to `src/*.Generators/bin/Debug/` — while the gate builds Release. On a machine that has never
  built Debug, those DLLs are simply absent, Roslyn loads no source generator, `Routes.*` is never emitted,
  and every call site fails to bind. It looked machine-dependent because a stale Debug DLL from any earlier
  build hides it entirely. `scripts/run-unit-local.sh` now builds `src/*.Generators` in Debug (~2s) and runs
  the full pass (~36s, one workspace load), so import ordering is enforced rather than trusted to a "run it
  before a PR" note — which is exactly the kind of advice this violation survived. Nothing else in the
  repo was out of order: a comparison matching Roslyn's own comparer over all 2062 `.cs` files found this
  one file, and no bespoke ordering test was added, since a hand-rolled sorter has to reproduce
  `UsingsAndExternAliasesDirectiveComparer` exactly — a naive ordinal comparison produces 8 false
  positives here — for no gain once the real formatter runs in the gate.
- **A contended SQLite `COMMIT` no longer loses the write and blames the wrong statement.**
  `ExecuteInImmediateTransactionAsync` drove `COMMIT;` through the busy-retry with no transaction-state
  guard — the only statement that had none. SQLite documents that a statement inside a multi-statement
  transaction answered with `SQLITE_BUSY` may be **rolled back automatically**, and that
  `sqlite3_get_autocommit` is the only way to find out; the loop instead slept its poll interval and
  re-issued `COMMIT` into autocommit, which fails with the non-retryable *"cannot commit - no transaction
  is active"* (#578). That is the mirror of the `BEGIN` bug fixed in #504: there a contended attempt left
  a transaction behind, here it takes one away. The retry now recognises the rollback, and — since
  everything the work delegate wrote went with it — the **whole transaction is re-run from `BEGIN`**,
  bounded by the same `SqliteBusyRetryOptions.Timeout` measured from entry so the caller's budget is not
  multiplied. A loss that outlives the budget surfaces as SQLite's own `SQLITE_ABORT_ROLLBACK` naming
  what was discarded, instead of an error about the wrong statement. **Behaviour change:** the work
  delegate now runs *at least* once rather than exactly once — keep it re-runnable and put side effects
  that must not repeat outside the transaction (`docs/sqlite.md`). A transaction that ends *while* the
  delegate runs is still reported rather than retried, because kept-versus-discarded cannot be told
  apart there and a duplicated write is worse. Two things found alongside it are fixed too: the failure
  message now carries the attempt number and the autocommit state on entry to that attempt, without
  which the original report could not distinguish "arrived with no transaction" from "lost one while
  running"; and the teardown rollback no longer takes the caller's cancellation token, which made an
  already-cancelled operation skip the rollback entirely and hand a mid-transaction handle back to the
  connection pool for the next lease — a plain query, EF or the pragma batch — to inherit. That teardown
  is bounded by a one-second budget rather than the caller's own timeout, so ignoring the token cannot
  turn a cancelled write into a multi-second stall on shutdown.
- **`RaskTest.Render` now mounts the component it renders.** `OnMount` and `OnMountAsync` never ran, so a
  component that loads asynchronously rendered its placeholder forever and could not be unit-tested past
  it (#555). The cause: `Render` wraps the component in a forwarding root, and the render walk fires the
  lifecycle on the **root** only — which is the wrapper, not the component under test. The framework
  already solves exactly this for its own two wrapper roots (`RootErrorBoundary` for the App,
  `RouteChainRenderer` for a page); the test root was the third and was missing it. It now adopts and
  notifies its child the same way, so `OnMount`, `OnMountAsync`, `OnRendered` and `OnUnmount` all fire,
  and the component renders through a handle, which records requests and coalesces them the way a live
  session does inside a dispatch — answering them inline instead re-enters the walk in progress, and
  renders halfway through a multicast event before its later subscribers have run. Adoption deliberately
  does **not** go through
  `GetOrCreate`: that path's reuse branch clears the instance's children, which would delete the subtree
  of a tree built at the call site (`RaskTest.Render(Div()[Span()])`) on its second render and put
  `.Instance`'s documented identity under positional-cache rules. Both guarantees are now pinned by
  tests. New **`RenderedComponent.WaitForAsync(text | predicate, timeout?)`** re-renders until the markup
  matches and returns it, throwing with the last markup on timeout — the awaitable an asynchronous mount
  needs, in place of a fixed delay. First use: the dashboard's Logs page History mode is render-tested
  end to end, which was previously reachable only through E2E; no dashboard page had ever been
  render-tested past its placeholder.
- **The local unit gate no longer flakes on two background-processor tests.** Both passed every time in
  isolation and failed only under a full-suite load, which is the worst shape for a gate the pre-commit
  hook enforces: the practical workaround is `--no-verify`, which skips the format and unit checks
  entirely. `OutboxRetentionTests` started the real hosted service and slept a fixed 500 ms per step, so
  under load the first poll had not finished when the processor was stopped and the retention sweep never
  ran — the assertions then read as "the sweep is broken" rather than "the sweep never happened". It now
  drives `OutboxProcessor.RunCycleAsync` directly (one explicit poll per step; retention is throttled on
  the injected clock, so that is exactly what the test means). `MailProcessorTests` waited for
  `Sender.Sent.Count == 1` and then asserted on `ProcessedAt`, which the processor writes *after* the
  sender returns — so it asserted on state it had never waited for. It now waits for the row.
- **A failed port-mode deploy no longer leaves the box serving nothing.** Without `--domain` the app is
  published on a single port, so the old container is stopped before the new one starts — there is no
  blue-green swap to fall back on, and the health gate could only report the failure. A bad image (bad
  config, a migration that won't apply) therefore took the app down and left it down, with the gate's own
  code comment admitting it "can't roll back". It now re-enters the deploy with `:previous` — the last
  image that passed this same gate — on either post-start failure: a container that doesn't stay running,
  or one that never answers the health probe. The deploy still exits non-zero, because it still failed;
  the difference is that the box is serving the previous version rather than nothing. The `tag` parameter
  is its own recursion guard (the restore passes `:previous`, so it cannot re-enter), and tags are
  deliberately *not* swapped the way `rask deploy rollback` swaps them — nothing about the configuration
  succeeded, so the next deploy should overwrite `:current` rather than file the last known-good image
  away as `:previous`. The inherent downtime of one published port is unchanged and still documented.
- **Every web-host sample now budgets its shutdown.** Nine of the ten inherited .NET's default 30s
  `ShutdownTimeout`, which *exceeds* the 20s `rask deploy` allows between SIGTERM and SIGKILL — so a sample
  deployed as written would be killed mid-shutdown. Only `Rask.Example.Shop` was right, which meant a reader
  copying from any other sample inherited the wrong lesson. A repo-scanning test now guards the next one
  somebody adds, since that is the real failure mode.
- **A graceful shutdown for live sessions — a redeploy no longer tells every user their session timed
  out.** `rask deploy` advertises zero-downtime deploys, and the container swap really is blue-green, but
  the app had no drain: `ApplicationStopping` fired `ws.Abort()` on every socket, so the browser saw an
  abnormal (1006) closure with no close frame, reconnected onto the replacement container, was told
  `session/unknown`, and displayed **"Your session timed out. Reload to continue."** for four seconds
  before reloading. Nothing had timed out — and because the browser could not tell a deployment from a
  crash, it had no better option. On `SIGTERM` the host now closes admission (new sessions get
  `503` + `Retry-After: 1`, and a new readiness health check goes unhealthy so a probing proxy stops
  routing), tells every connected browser it is going away, lets in-flight handlers finish — a click that
  was mid-`SaveChangesAsync` used to be cancelled and dropped — closes each socket with a real `1001`
  "going away" handshake, and disposes the sessions *awaited*, where the old path fired an unawaited
  `RemoveAsync` that raced process exit. The client shows **"Updating…"** and, because the drop is now
  *expected* rather than guessed at, reconnects immediately instead of walking a 500 ms → 5 s backoff
  ladder — leaving what happens to the page to the host that answers. If that host cannot rebuild the
  session it reloads, in ~250 ms instead of 4 s, restoring scroll position and focus (and, since the
  entry below, the fields the user had edited).
  Budget via `RaskServerOptions.ShutdownDrainTimeout` (default 5s; `Zero` restores the old abort), which
  must fit inside `HostOptions.ShutdownTimeout`; a startup warning says so when it doesn't, and
  `rask.shutdown.sessions.abandoned` counts anything still connected when the budget ran out.
  `LiveSessionStore.DisposeAsync` also became idempotent: the store is a DI singleton, so a host or a test
  that disposed it *as well as* the container reached `Cancel()` on an already-disposed token source.
  The announcement itself is bounded by the drain budget and fanned out with a bounded degree of
  concurrency. Neither was true when the drain and the outbound-send bound were written against separate
  bases: a client that had stopped reading TCP held the announcement for the whole 30s `SendTimeout` —
  longer than the drain budget, longer than `HostOptions.ShutdownTimeout`, and long enough for `rask
  deploy`'s `SIGKILL` to land in the middle of a SQLite checkpoint. Measured at 30s before, 200ms after.
  The load-bearing change is a one-line token substitution: the socket's cancellation token now derives
  from the drain's hard deadline rather than from `ApplicationStopping`, which is what makes it possible
  to send anything at all — including the shutdown frame — after the stop signal arrives.
- **The signals that say what a host is doing, not just what it refused to do.** The meter could report a
  rejected frame and an evicted session, and little about the state that produced either. Four additions:
  `rask.sessions.connected` separates people actually looking at the app from the `active` count, which
  also includes `GET`-minted sessions whose socket never arrived and sessions riding out their reconnect
  grace — so "the host is filling up" becomes actionable instead of ambiguous. `rask.handlers.pending`
  exposes the backpressure breaker's *input*; only its output was visible, so you could watch it trip and
  never watch it coming. `rask.render.duration` times the framework's half of an interaction, which
  `rask.handler.duration` (your half) was routinely mistaken for. `rask.payload.bytes` makes a page that
  quietly stopped diffing visible as a distribution shift, long before it shows up as bandwidth.
- **`/health` now degrades on memory, not just on the session count.** A cap alone can't keep a host
  healthy, because what a session costs is a property of the page rather than of the user — the same host
  holds ~66,000 sessions of a trivial page or ~735 of a 200-row grid, so a cap sized for one is no
  protection on the other. And the common configuration, `MaxSessions` uncapped, previously reported
  `Healthy` unconditionally: the one signal an orchestrator polls could say nothing at all until an OOM
  said it. The reading comes from `GCMemoryInfo`, which honours a container limit, so it reflects the
  ceiling a deployed app actually runs under. A memory position the runtime won't disclose is treated as
  healthy rather than full — a host must not shed load because it can't measure itself.
- **Jobs, the outbox and mail now let in-flight work finish when the host shuts down.** All three passed the
  host's stopping token straight into user code, so a `SIGTERM` — a redeploy, a container recycle —
  cancelled a handler *mid-call*: a job halfway through a `SaveChangesAsync` was simply torn in two. For mail
  it was worse than untidy: cancelling during the SMTP `DATA` phase drops the connection, but the server may
  already have accepted the message, so the row stayed unsent and the next boot **sent it again**. Each
  pillar now gives the item already running a bounded `ShutdownGracePeriod` (jobs and outbox 5s, mail 10s)
  before cancelling it, using the same shape `Rask.SQLite.Litestream` already used for its final WAL flush:
  the host token *arms* a deadline rather than cancelling, so user code keeps a live token past the stop
  signal. The loop still refuses to *start* anything new the instant the stop arrives, so shutdown is
  extended by at most one grace period, never one per remaining item. Work that outlives its grace is
  cancelled and re-runs whole on the next boot, and deliberately **does not count a failed attempt** — a
  redeploy is not a failure, and counting it would march never-failing work toward its dead letter at the
  cadence you deploy (`MaxAttempts` is 10 for outbox and mail). New `rask.{jobs,outbox,mail}.interrupted`
  counters plus a warning make a too-short grace visible instead of silent; `rask.mail.interrupted` is the
  direct answer to "did that deploy duplicate any mail?". `Rask.Cache` deliberately gets no knob: its only
  in-flight work is one bulk delete of expired rows, which is abort-safe by construction.

### Fixed
- **`AddRaskOutbox` now validates its options.** It was the only battery whose registration didn't — jobs,
  mail and cache all call `Validate()` — and `OutboxOptions` had no `Validate()` method at all. So
  `PollInterval = TimeSpan.Zero` didn't fail fast at registration; it threw out of `new PeriodicTimer(...)`
  on the background thread, which with the default `BackgroundServiceExceptionBehavior.StopHost` took the
  host down at an unrelated moment with an unrelated-looking stack. Every value it now rejects already threw
  at runtime, just later and less legibly.
- **PostgreSQL as an opt-in database, via `Rask.Postgres` and `rask new --database postgres`.** SQLite
  remains the default and the recommendation — one file, no server, and every pillar riding it is still what
  a single-developer product should reach for first. But the docs have long promised that *"when you outgrow
  one box, the door to a client-server database is open"*, and until now nothing in the CLI, the deploy path
  or `rask db` knew another provider existed. This opens it properly. `UseRaskPostgres(...)` is a drop-in for
  `UseNpgsql` that applies the production session settings on every connection — `statement_timeout`,
  `lock_timeout`, `idle_in_transaction_session_timeout` — and turns on Npgsql's transient-failure retrying;
  it is the direct counterpart of what `UseRaskSqlite` does with pragmas. Unlike `Rask.SQLite` there is no
  separate raw-ADO package, because Npgsql already provides the pooling and retry that one had to add by
  hand. `lock_timeout` is validated to sit below `statement_timeout`: above it, the statement timeout always
  fires first and lock contention gets reported as a slow query, which is exactly the misdiagnosis the
  setting exists to prevent.

  The provider is chosen once, at `rask new`, and then **detected** everywhere else — `rask generate
  feature`, `rask db` and `rask deploy` all read it off the project's package references rather than asking
  again, so there is no second source of truth to drift. Choosing it changes what gets scaffolded: no
  Litestream, no scheduled snapshots, no `/data` volume in the Dockerfile, and a generated persistence test
  that creates and drops a database of its own instead of a temp file. Those are not degraded — they
  replicate or copy *a file*, and there isn't one. For the same reason `--snapshots` with a server database
  is a usage error rather than a silently dropped flag (a backup you believe you configured is worse than
  one you know you haven't), while `--all-batteries` simply expands to the batteries that do apply.

  `rask deploy` stops injecting a volume and a `Data Source=/data/app.db` connection string for a
  server database, and **refuses the deploy** when no `ConnectionStrings__App` was supplied — otherwise the
  app would start against the placeholder connection string compiled into `Program.cs`, which on the server
  is either nothing or somebody else's database. `rask db backup`/`restore` refuse too, pointing at
  `pg_dump`: both copy a SQLite file, and a backup command that quietly does nothing is the worst kind of
  bug it could have. Migrations are unaffected — `rask db add`/`update` forward to `dotnet ef`, which was
  always provider-agnostic.

  Running several instances is safe — see the leasing entry below.

- **`ICache` can be backed by Redis — or any `IDistributedCache` — with a non-generic `AddRaskCache()`.**
  The database-backed cache stays the default and the recommendation; the case against Redis in
  [`cache.md`](docs/cache.md) is about not standing one *up* for a cache, not about refusing to use one you
  already operate.

  The typed layer always worked over the interface, so this mostly removes a trap rather than adding a
  feature: `AddRaskCache<TContext>()` also registers the purge worker, so an app that pointed `ICache` at
  Redis still had to map the `CacheEntry` table and run a migration for it, or watch the purger throw every
  five minutes. The new overload registers `ICache` alone.

  It takes **no `CacheOptions`**, deliberately — `PurgeInterval` and `DefaultSlidingExpiration` are both
  implemented by the database-backed store, so against another one they would be settings that silently did
  nothing. Expiry is that store's business. There is no `Rask.Cache.Redis` package either:
  `Microsoft.Extensions.Caching.StackExchangeRedis` is the standard .NET API and wrapping it would only add
  a layer to keep in step. Only the cache moves — jobs, mail and the outbox stay on the database, because
  they need transactions.

- **SQL Server too, via `Rask.SqlServer` and `rask new --database sqlserver`.** `UseRaskSqlServer(...)` is
  the SQL Server counterpart of `UseRaskPostgres`: `SET LOCK_TIMEOUT` and `SET XACT_ABORT ON` on every
  connection open, a client command timeout, and `EnableRetryOnFailure`.

  Deliberately not a mirror of the PostgreSQL options. SQL Server has no server-side statement timeout — so
  the ceiling on a runaway query has to be the *client* command timeout — and nothing corresponding to
  `idle_in_transaction_session_timeout`, so neither is invented. What it has and PostgreSQL does not need is
  `XACT_ABORT`, on by default: with it off, a statement error inside an explicit transaction leaves that
  transaction open and holding locks, and the connection returns to the pool in that state.

  Verified against a real engine, not asserted: `scripts/run-providers-local.sh` now races 20 processor
  instances for 200 jobs on SQL Server as well as PostgreSQL. Doing that also caught a bad assertion in the
  PostgreSQL suite — it required all 200 jobs to be claimed in a single round, which passed on PostgreSQL by
  timing alone. Every instance runs the same deterministic candidate query, so most legitimately claim
  nothing and poll again; the invariant is that no job is claimed *twice*, not that one round drains the
  queue.

- **Jobs, mail and the outbox now lease the work they claim, so more than one instance is safe.**
  **⚠️ Action required: `rask db add AddLeases && rask db update`.**

  Until now the three processors polled for due work and ran it without claiming it first. Two instances
  therefore read the *same* rows and both processed them: every job ran twice, and every queued email was
  **sent twice**. On SQLite that constraint was natural and documented ("run one processor per app"), but
  PostgreSQL invites `replicas > 1`, where it became a trap.

  A batch is now taken with a single `UPDATE` whose predicate re-tests claimability. Every provider
  re-evaluates that predicate against the row version the winner committed, so the row goes to exactly one
  instance — no `SKIP LOCKED`, no provider-specific SQL, the same code path on SQLite. The claim marks rows
  with a token and an expiry (`LeaseDuration`, default 5 minutes, must exceed `PollInterval`). A processor
  that dies keeps nothing: its lease runs out and the work becomes claimable again, so there is no sweeper
  to run and nothing to clean up by hand.

  **Be clear about what this buys.** A lease stops one instance overwriting another's outcome; it does not
  make a side effect happen once. An instance that overruns its `LeaseDuration` can have its row taken while
  it is still working — the database stays consistent, because the loser's write is rejected and discarded,
  but an email it already sent is out. At-least-once was always the contract; the lease narrows the window
  from *always, on every instance* to *only on an overrun*, and logs which `LeaseDuration` to raise when one
  happens. Set it above your slowest handler.

  **`Attempts` changes meaning: it now counts attempts *started*, not failures.** The claim increments it.
  A job that takes the whole process down with it — an OOM, a pod eviction — never reaches the failure path,
  so counting only failures left it retried by every instance forever and `MaxAttempts` never dead-lettered
  it. Visible in the `/_ops` dashboard and in metrics: a job that succeeds first time now shows
  `Attempts = 1` rather than `0`.

  **A shutdown is the one interruption that does not count.** Where the two behaviours meet — an item
  claimed, then cut off by a redeploy outliving its `ShutdownGracePeriod` — the processor gives the
  increment back and hands the lease over with it. Without that, `MaxAttempts` would silently become a
  function of *deploy cadence*: at the default of 10, ten unlucky redeploys would dead-letter an item that
  never once failed, and nothing in the logs would connect the two. Releasing the lease also means the next
  boot sees the item immediately instead of waiting out a claim held by a process that no longer exists.

  The migration adds two nullable columns to each of the three tables — additive, no backfill. Skipping it
  is the one failure mode worth knowing about: the processors throw on every poll, the exception is caught
  rather than crashing the app, and it logs the same error every five seconds while looking healthy. All
  three now recognise that specific failure and print the two commands above instead of a stack trace.

- **Two instances no longer double-enqueue every recurring job.** A hazard the drain's lease does nothing
  about: both processors read the same `RecurringJobState`, both saw it due, and both enqueued — and the two
  `LastEnqueuedAt` writes raced with no concurrency token, so neither lost. The result was N× every
  recurring job, for as long as the app ran. The tick is now claimed with a compare-and-swap on due-ness.
- **`Rask.Logging` — the application log, kept in a database of its own.** The failures that matter most
  leave no row in any table: Litestream exiting, a job type that won't deserialize, a handler that threw on
  the one request that mattered. Those are log lines, and on a single box they lived in a container's stdout
  that the next restart took with it — the dashboard's tail said so itself, in as many words: *"a tail, not
  a log store"*. `builder.Services.AddRaskLogging("Data Source=logs.db")` is the whole setup. It registers a
  standard `ILoggerProvider`, so it captures exactly what every other sink sees, and the schema is created on
  first use, so there is no migration to add. `rask new --logs` scaffolds it.
  - **A log call never waits on the disk.** Entries go into a bounded in-memory channel and a background
    writer batches them out on an interval; when the buffer is full the entry is *dropped* and counted on
    `rask.logs.dropped`, rather than queued unbounded or blocked on. A batch lost to a failed write is
    counted the same way, so that one number stays the honest answer to *"is the log I'm reading complete?"*
    Shutdown drains what's buffered under a bounded timeout — the lines just before a stop are the ones most
    worth keeping.
  - **Retention by age *and* row count**, both on by default (14 days / 100,000 entries), swept in pages of
    1,000 so the write lock is never held for a whole sweep. Age alone doesn't bound the disk: a log storm
    fills it well inside the window.
  - **Its own SQLite file, deliberately** — the one battery that doesn't map onto your `DbContext`, and the
    one flag that doesn't imply `--data`. Log lines arrive at machine rates and would put a high-frequency
    writer on the same single write lock the request path already contends for; and the most valuable line
    is the one written *while a transaction is failing*, which on the app's context would roll back with the
    failure. The trade-off is stated rather than hidden: `logs.db` is **not** covered by `rask db backup` or
    Litestream, and log lines can contain secrets. `rask deploy` points it at the same mounted volume.
- **The dashboard's Logs page gained a History mode.** With `Rask.Logging` installed, `/_ops/logs` offers a
  paged, searchable view of the stored log (level, category, and full-text over the message and exception)
  beside the existing live tail. Two modes rather than one merged view because the store's writer flushes on
  an interval — the newest lines are buffered but not yet on disk, and a merged view would quietly disagree
  with itself for a second at a time. Live remains the default and still reads nothing from disk.
- **[`docs/scaling.md`](docs/scaling.md) — how far one box goes, measured rather than asserted.** The docs
  said "one server runs the whole product" in several places and then never said what one server does. This
  puts both numbers next to each other — sessions held and events served, each reproducible from a report
  in this repo — and they turn out to have different shapes: in memory a 5-row page already costs 3× an
  empty shell, while in throughput the two are within noise and the cost arrives later but harder. The
  practical version: **look at your largest page before you look at your user count.**
  It also states where the wall is, which is not the session count or the event rate but the single SQLite
  writer, and what it takes to get past it. And it says plainly that `rask deploy` ships **no `--replicas`
  flag** — every DB-backed pillar writes to the app's own database, so a second replica of a normal Rask
  app is a corruption risk rather than a scaling step. Recorded in the roadmap's "not shipped" list next to
  the other honest gaps. What did change: a session reconnecting to a host that never knew it is now
  rebuilt rather than refused, so sticky routing is an optimisation for sessions — though uploads,
  downloads and sign-in redeem still require it.
- **`rask db backup` and `rask db restore` — get the deployed database down, and a copy back up.** Continuous
  backup (Litestream) covers the box dying; this covers the two things a solo developer actually reaches
  for: *"something looks wrong in production, let me get a copy"* and *"that migration was a mistake,
  restore last night's file"*. A plain file copy of a live SQLite database is not a backup — with WAL on,
  committed transactions sit in the `-wal` sidecar until a checkpoint, so the `.db` alone is torn or stale.
  Both paths go through SQLite instead: locally through the Online Backup API, remotely through
  `VACUUM INTO` in a throwaway container mounted on the app's data volume, fetched over the
  `docker -H ssh://…` connection the deploy already uses — nothing to install on the host. `restore`
  behaves like `rask db drop`: it asks first, takes `--force`, and refuses when there is no terminal to ask
  on. A remote restore stops the app before replacing the file and starts it afterwards, and removes the
  stale WAL sidecars with the old database — restoring under a live writer, or leaving a `-wal` behind,
  silently produces a hybrid of the two. The CLI gains its first package reference for this
  (`Microsoft.Data.Sqlite`); the alternative was requiring a `sqlite3` binary on the machine, a dependency
  we could neither pin nor install.

### Added
- **`rask dev` hot-reloads a wasm-hosted app.** WASM had no watch channel at all: the host serves its
  client's *published* bundle, which a nested `dotnet publish` (an emscripten relink) rebuilt on every
  save, and which is trimmed — and trimming folds `MetadataUpdater.IsSupported` to false, so an applied
  update could never reach the browser session even if one arrived. Under `rask dev` the host now serves
  the client's **build** output instead, read through its static-web-assets manifest (the build
  `wwwroot/` holds only `_framework/`; the shell, `main.js`, `rask.wasm.js`, scoped-asset bundles and
  RCL content live in other content roots). The nested publish disappears from the inner loop, the
  bundle is untrimmed, and everything downstream was already wired — `WasmLiveSession` derives from the
  same base as the Server one, so it already registers for hot reload and already repaints.
  Opt out with `rask dev --no-hot-reload` or `--once`, both of which keep the published bundle.
  `rask dev` also now recognises a wasm-hosted app by what it *references* rather than by a `.Client`
  naming convention.

  The **"Hot reload applied" pill now shows on every transport**, from one implementation. It moved out
  of the Server client into a shared module spliced into all three (`rask-hotreload.js`), so Server,
  WASM and native cannot drift; only the trigger differs — a pushed frame, a `[JSImport]` call from
  .NET, or the WebView bridge. The native half is wired for completeness and costs a device build
  nothing: native hot reload still needs a device-side delta agent that does not exist, so nothing ever
  raises it there.

### Fixed
- **Scoped CSS and scoped JS now work in apps built against the NuGet packages — they never had.** The
  generators read nothing but `@(AdditionalFiles)`, and the `**\*.css` / `**\*.js` globs that populate it
  live in `Rask.Core.targets`, which reached no consumer at all: `Rask.Core` is `IsPackable=false`, so its
  own `Pack="true"` item was inert, and `Rask.Server` / `Rask.Wasm` / `Rask.Native` each packed only their
  own `build/` folder. So in every app `rask new` produced, a `Counter.css` beside `Counter.cs` was
  silently ignored, RASK015/RASK017 could never fire, and `RaskGlobalUsings` / `RaskScopedJsAutoInclude` /
  `RaskFactoryNavigation` were not compiler-visible, so setting any of them to `false` did nothing. Nothing
  caught it because every in-repo project imports the file directly through `Directory.Build.targets` — the
  defect existed only on the far side of a `dotnet pack`. The file is now packed into each host package's
  `build/` folder and imported from that package's `build/<PackageId>.targets` (NuGet auto-imports nothing
  else), guarded by a pack-time error and by tests on both sides: a structural contract in the default gate
  and a build-E2E that compiles a scaffolded app with a scoped `.css` and requires an orphan one to fail
  with RASK015.

  **This can turn a previously-green consumer build red, which is the feature working.** RASK015 and
  RASK017 are errors, so a stray `.css`/`.js` with no matching component in the same folder now fails the
  build. `wwwroot/`, `bin/`, `obj/` and `node_modules/` are excluded, which covers the documented home for
  global stylesheets; otherwise move the file under `wwwroot/`, or opt out with
  `<RaskScopedCssAutoInclude>false</RaskScopedCssAutoInclude>` / `<RaskScopedJsAutoInclude>false</…>`.
  Note NuGet applies `build/` only to a **direct** `PackageReference`, so a class library that gets a host
  package transitively still needs its own reference to glob — the same reach the global usings in
  `build/Rask.Server.props` have always had.
- **A deploy no longer signs out every user.** Nothing in the scaffolded app persisted the Data Protection
  key ring, so it landed in the container's own filesystem — and `rask deploy` replaces the container on
  every deploy. The replacement came up with a fresh ring, every auth cookie already issued failed to
  unprotect, and everyone who was signed in was silently signed out. Nothing said so: the deploy reported
  success, `/health` was green, and users simply found themselves logged out. `rask new` now writes the ring
  to `/data/keys` — the volume the deploy already mounts for the database — so it outlives the container the
  same way the data does. `SetApplicationName` is set alongside the path and is equally load-bearing: the
  default discriminator is derived from the content root, which differs between the build and runtime
  images, so a persisted ring on its own would still have failed to unprotect. Override the location with
  `Rask:DataProtection:KeyPath`; a plain `dotnet run` has neither that nor `/data`, so it skips the block
  and keeps ASP.NET's per-user development ring. Existing deployments sign everyone out once more, when the
  persisted ring first replaces the ephemeral one, and never again.
- **`rask dev` hot reload now works when the project sits under a symlinked path.** `dotnet watch`
  computes an *empty* Edit-and-Continue delta — with no error, no warning, and every diagnostic
  reporting success — when the project path it is handed traverses a symlink. It logs `File updated`,
  updates the document in its Roslyn workspace, and then says `No managed code changes to apply`. The
  edit never reaches the running app. `rask dev` now resolves the project path through every symlinked
  segment before launching watch, so the same edit applies. macOS is where this bites: `/var` and
  `/tmp` are symlinks into `/private`, so anything under the temp directory — or under a symlinked
  working directory — was affected. Note `Path.GetFullPath` does *not* do this; it normalises `..` and
  separators but never follows a link, which is why an earlier attempt to rule this out came back
  negative. This was also the cause of the framework's own watch E2E never being green (#536); those
  two cases now run in the default gate instead of being skipped.
- **The hot-reload dev channel is now actually exercised by the unit gate.** `MetadataUpdater.IsSupported`
  is a per-process feature switch the SDK turns off in Release, and the gate runs Release — so both
  hot-reload gates were closed, no server subscribed, no session registered, and the one test that
  checked them degraded to `false == false` and passed proving nothing. A new
  `Rask.Server.HotReload.Tests` assembly turns the switch on (it needs the `MetadataUpdaterSupport`
  property *and* `DOTNET_MODIFIABLE_ASSEMBLIES=debug`, only one of which has an MSBuild property) and
  drives the real chain over a real WebSocket: an applied update repaints every open session and *then*
  announces itself, in that order. It is a separate assembly because the session registry is
  process-global.
- **A live session now hands its pooled arrays back when it ends.** Tearing a session down released its
  file stores, its locks and its DI scope, but simply dropped the rendered-HTML buffer pair and the two
  frame writers behind the render cache — all of them `ArrayPool` rentals held for the session's whole
  life. Not a leak: they were collected. But they never went *back*, so every teardown quietly cost the
  pool the arrays it had just sized to the page, and the next session paid to allocate them again. On a
  large page these are the dominant per-session term, so the waste scaled with page size. `FrameWriter`
  gained the return path it never had (it rented in its constructor and had no way to give the array
  back), and the session releases both **inside** the render lock — everything else in teardown fails
  loudly on a racing caller, whereas returning an array early would fail silently, in whichever unrelated
  session later rented it. Worth **~19% of the allocation** a create-render-dispose cycle costs on a
  200-row page (2,418,177 → 1,959,329 bytes/cycle, `session-churn`), and the residue a 500-cycle run
  leaves behind drops from 96 bytes to zero.
- **A client that stops reading can no longer wedge its live session, or hold up delivery to everyone
  else.** `WebSocket.SendAsync` completes when a frame reaches the transport, not when the client reads it,
  so a client that simply stops reading TCP fills the send buffer and the send never returns. Every send
  happens under the session's render lock, which also guards its teardown — so that one client cost its
  session every future render *and* a `Dispose` that could never take the lock, for as long as it cared to
  stall. A new `SendTimeout` (default 30 s, `0` disables) bounds it: on expiry the socket is aborted, which
  unwinds the receive loop and releases the lock, while the **session** is left alone to live out its
  normal grace period — so a briefly-stalled link reconnects to the page it already had. The default is a
  stuck-connection backstop, not a latency budget; a slow mobile link will not trip it.
  The store's whole-session fan-outs (`RerenderAllAsync`, `BroadcastAsync`) were also sequential, so their
  cost was the *sum* of every session's send rather than the slowest, and one stalled client held up every
  session behind it. Both now run with a bounded degree of concurrency. Only the dev-time hot-reload
  signal uses them today, but this is the code the planned Broadcast pillar inherits, where a fan-out is a
  user-facing feature — and it is what the shutdown drain's announcement rides, which has to finish
  inside the shutdown budget before `SIGKILL` interrupts a SQLite checkpoint.
- **The jobs retention sweep fought a second instance.** `JobProcessor.PurgeAsync` loaded the stale rows and
  `RemoveRange`d them; a tracked delete of a row that vanished underneath the sweep raises
  `DbUpdateConcurrencyException` and fails the whole cycle — precisely what two processors purging
  concurrently do to each other. The outbox sweep already deleted by id through `ExecuteDelete` for exactly
  this reason. `MailProcessor.PurgeAsync`'s single unbounded `DELETE` was correct but held SQLite's one
  write lock for the length of the sweep; both are now paged like the outbox's.
- **`rask new`'s template catalog listed a `litestream` flag that could never be requested.** It had been
  removed from the flag list but left in the server template's supported set, where it read as a feature.
  A parity guard now fails the build if a template ever claims a flag `rask new` doesn't accept.
- **`rask deploy --help` and the generated GitHub Actions workflow both showed `ConnectionStrings__Db`**,
  while everything that works uses `ConnectionStrings__App`. Copy-pasteable and wrong: an app started with
  the misspelled key falls back to its default database. A guard now checks the examples against the key the
  deploy actually injects.
- **A contended write no longer fails with "cannot start a transaction within a transaction".** The
  busy-retry loop re-issued the identical statement on every pass with no cleanup between attempts, and
  cleared a leaked transaction only *once*, before the loop. That caught a transaction the pooled handle
  arrived with, and missed one that appeared later — including `BEGIN IMMEDIATE`'s own, which an extended
  `SQLITE_BUSY` (`BUSY_SNAPSHOT`, `BUSY_RECOVERY`, hidden behind the primary result code) can leave open.
  The next pass then began a transaction inside the one its own previous attempt had opened, turning a
  waitable lock into a non-retryable `SQLITE_ERROR`. The rollback is now part of every attempt, so a
  contended rollback simply costs a pass instead of poisoning the next one. The `finally` rollback is
  retried on the same footing rather than fire-and-forget, so a still-active statement no longer returns a
  mid-transaction handle to the pool. Only ever seen under real multi-writer WAL load — it was the
  intermittent stress-test failure that blocked commits.
- **Renaming or deleting a job, outbox event or handler under `rask dev` now takes effect.** The generated
  registries upserted their entries, so a refresh could only ever add or overwrite: rename
  `SendWelcomeEmail` to `SendWelcomeMail` and the registry held *both*, with the old name still resolving to
  a type the generator no longer produced. The same shape bit CQRS harder — delete the last handler for a
  command and dispatch kept succeeding through the invoker built from the old IL, instead of reporting that
  nothing handles it. Each generated `RefreshAll()` now installs its assembly's complete set in one call,
  keyed on its own registry class, so a removal is a removal while every other assembly's contributions and
  any direct registration are left alone. The swap lands in a single store, so a job dequeued mid-refresh
  cannot observe a half-built table.

### Added
- **`Element.Title`** — the global `title` attribute, available on every element, so a cell can show an
  abbreviated value with the exact one behind it on hover. There was previously no way to express a
  tooltip at all: `Aria` reaches screen readers but renders nothing visible, and `Data` only emits
  `data-*`. The dashboard now puts precise UTC instants behind its relative timestamps, and the full text
  behind its truncated cells.
  Two details. It renders in a new slot — `id, class, style, **title**, data-*, role, tabindex, aria-*` —
  and it is **declared last among `Element`'s properties on purpose**: factory parameters are ordered
  derived-first then by declaration span, so putting it beside `Style` would have shifted the positional
  index of `Data`/`Role`/`TabIndex`/`Aria` on every element in the framework. `Style` no longer declares
  its own `Title`; it inherits the global one, which moves `<style title="…">` into the global attribute
  group (the one observable behaviour change here).
- **Every DB-backed pillar now publishes metrics.** `Rask.Jobs`, `Rask.Outbox` and `Rask.Mail` each own a
  meter (`Rask.Jobs`, `Rask.Outbox`, `Rask.Mail`) with processed/failed/**dead-lettered** counters, a
  duration histogram, and pending/dead-letter gauges — so the number that matters can drive an alert
  instead of only a dashboard someone has to be looking at. `docs/observability.md` covered only
  `Rask.Server` before; the batteries had none at all.
  Jobs and the outbox tag by their registered type, a closed set fixed at build time. **Mail is untagged on
  purpose** — its only per-message dimensions are subject and recipient, both unbounded, and tagging by
  either would mint a time series per email sent.
  The queue-depth gauges are **sampled by the processor's existing poll, and only while a listener is
  attached**, rather than running `COUNT(*)` inside the observable-gauge callback: a collector on a
  one-second schedule would otherwise put continuous read load on the app's own database just by
  subscribing.
  Instrumenting this also surfaced a gap in what counts as a failure: a message whose **type is no longer
  registered** — a renamed job or event nobody re-registered, and the most ordinary way a production queue
  starts abandoning work — recorded an error on the row but was invisible to the new counters, because it
  never reaches a handler. It now counts as a failed attempt and as a dead letter like any other.
- **`Rask.Dashboard` — a built-in operator dashboard for the batteries.** One package reference and one
  `AddRaskDashboard<AppDbContext>()` mounts `/_ops`, reading the outbox, jobs, mail and cache out of the
  app's own database. Every queue is split into due / delayed / **failed** / processed, where failed means
  what the processors mean by giving up — out of attempts and still unprocessed — with the last error and
  the stored payload one click away. This closes the roadmap's *"no UI, CLI command, or metric shows you
  what has given up"* gap for reading; the retry actions follow.
  A panel appears only when its battery is both registered *and* mapped into the `DbContext`, so the nav is
  an inventory of what the deployment actually runs. Also shows cache keys with size and expiry, SQLite
  pragmas read live, database size, and the recurring-job schedule joined to when each job last fired.
  **It fails closed:** pages are gated on a `RaskDashboard` authorization policy applied to the route
  layout (so it covers every page and is re-checked on in-app navigation). Define that policy and it
  decides; leave it undefined and the dashboard is open only in Development — with a warning banner — and
  **denies everyone** in every other environment. Panels poll, compare, and re-render only on a real
  change, and the loop is bounded, because every open tab competes with the processors for SQLite's single
  write lock.
- **The dashboard can fix what it finds, and tail the log.** `Retry` puts a dead letter back in the queue
  (`Attempts = 0`, due now, error cleared); `Retry all failed` does the queue at once; `Purge processed`
  clears completed rows older than a week. Each is a single `ExecuteUpdate`/`ExecuteDelete` whose guard
  lives in the `WHERE` clause, evaluated by the database at the moment of the write — and the retry guard
  is the **inverse of the drain query**, so it can only ever match rows a processor has already given up
  on. A row in flight is invisible to it, which is what makes retry safe against a live queue with no
  coordination. Purge only ever touches `ProcessedAt IS NOT NULL`, so outstanding work and dead letters
  survive whatever cutoff you pass.
  Deleting a row, and flushing the whole cache, destroy work rather than reschedule it, so they need
  `Actions = RaskDashboardActions.All`; the default stays `Safe`. Buttons for a tier that's off are hidden
  rather than disabled.
  A new **Logs** panel keeps a bounded in-memory tail of the standard `ILogger` pipeline (last N entries at
  or above a level, filterable by level and category, with stack traces inline). It is fed by a registered
  `ILoggerProvider`, so it sees exactly what every other sink sees — including Litestream exiting, a job
  type that won't deserialize, and handler faults, none of which leave a row in any table. It's a tail,
  not a log store: the buffer is memory-only and gone on restart.
- **`rask new --ops`** scaffolds the dashboard, and `--all-batteries` includes it. With `--auth` the
  generated `Program.cs` also defines the authorization policy that gates it; without auth that line is
  scaffolded commented out, next to a note that `/_ops` is Development-only until you add one — the flag
  can't hand you an open dashboard either way. New [`docs/dashboard.md`](docs/dashboard.md), and the
  roadmap's *"no UI, CLI command, or metric shows you what has given up"* entry moves out of **Not
  shipped**. Tutorial chapter 10 still builds an `/ops` page by hand — the exercise is what proves the
  pillars are ordinary tables — and now closes by pointing at the finished one.
- **`BsStat`** — a stat-tile primitive for `Rask.Bootstrap`: a number, its label, an optional caption and
  tone. Tone colours the value rather than the card, so one red number reads as a signal instead of one
  panel among many coloured panels.
- **`LitestreamStatus`** — a singleton published by the Litestream supervisor reporting whether replication is
  currently running, when it last started and exited, its last exit code or error, and how many times it has
  restarted. "Is my backup actually running?" was previously answerable only by the absence of a log line.
- **`ISqliteSnapshotStore.ListAsync`** — enumerate stored snapshots (name, size, timestamp), newest first and
  scoped to the store's search pattern, so what you can see is what retention manages. A default interface
  implementation returns an empty list, so existing custom stores keep compiling.
- **`JobOptions.RecurringJobs`** — the registered recurring schedule (name, interval, factory) is now public.
  Pair an entry with the `RecurringJobState` row of the same name to see when it last fired, or call its
  factory to enqueue an off-schedule run.

### Fixed
- **The outbox table no longer grows for the life of the application.** It was the only DB-backed pillar
  with no retention: every domain event ever raised was kept forever, payload included, on the same SQLite
  file the app serves from — and that Litestream replicates and snapshots copy. New
  `OutboxOptions.RetentionPeriod` (default 7 days, matching jobs and mail; `TimeSpan.Zero` keeps
  everything) purges published messages hourly. **Dead letters are never purged** — they have no
  `ProcessedAt`, so the retention predicate cannot match them whatever cutoff you set. The sweep runs in
  pages and deletes by id rather than as one unbounded statement, because the first sweep on an app that
  has been running without retention would otherwise hold SQLite's single write lock for its whole
  duration; it loops until drained so a large backlog actually clears instead of shrinking by one page an
  hour.
- **A row changing underneath the jobs or outbox drain no longer discards the whole batch's progress.** Both
  processors ran a batch and then wrote every outcome in a single `SaveChangesAsync`. Anything else modifying
  one of those rows meanwhile — a manual `UPDATE`/`DELETE` against `app.db`, which is currently the only way
  to clear a dead letter — raised a `DbUpdateConcurrencyException` that rolled the transaction back and
  stripped `ProcessedAt` from **every** row in the batch that had already run. The side effects had happened,
  so on the next poll up to `BatchSize` (default 100) jobs re-ran and outbox events were **published a second
  time**, repeating every poll until the interference stopped. Each outcome is now persisted on its own, as
  the mail processor already did, so the blast radius is the single affected row.
- **A failing jobs or outbox poll no longer stops the host.** Neither processor guarded its cycle, so a
  transient database error (a `SQLITE_BUSY`, say) escaped `ExecuteAsync` and faulted the `BackgroundService` —
  which, under the default `BackgroundServiceExceptionBehavior.StopHost`, takes the whole application down.
  Both now log the failure and retry on the next poll, matching the mail processor.
- **The outbox marks an event published even while the host is shutting down.** The drain saved with the
  shutdown token, so an event delivered during shutdown could lose its `ProcessedAt` and be published again on
  restart.

- **Editing a `[Route]` template under `dotnet watch` now takes effect.** Routes, CQRS handlers, jobs and
  outbox events are all registered from a `[ModuleInitializer]`, and the runtime never re-runs one after a
  hot-reload apply — so changing a route silently did nothing, and an edited command handler kept dispatching
  through the invoker built from the old IL, with no error either way. Each generator now emits a
  re-invocable `RefreshAll()` alongside its initializer, which the hot-reload coordinator calls. The CQRS
  registry deliberately refreshes only its dispatch table: its DI registrations go onto a queue that is never
  drained, so re-running them on every save would grow it without bound.
- **A hot reload no longer repaints against stale scoped CSS.** Rask declared three independent
  `[MetadataUpdateHandler]`s — scoped CSS, scoped JS, and the live-session re-render — and the runtime does
  not define the order it invokes them in. When the re-render happened to run first, the frame carried the
  *previous* bundle hash, so a `.css` edit only appeared on the next interaction. A single coordinator
  (`RaskHotReloadHandler`) now runs the phases in a fixed order — scoped assets, then the generated
  registries, then the repaint — and a test asserts the assembly declares exactly one handler so the
  ambiguity cannot come back.
- **A render concurrent with a scoped-CSS refresh can no longer emit unscoped elements or a stylesheet-less
  `<head>`.** The refresh cleared the registry and then repopulated it, leaving two windows open to any
  render in flight: one where the scope-id lookup missed, so elements were written without their
  `data-r-xxxx` attribute, and one where the bundle rebuilt as empty, so `<head>` carried no `<link>` at all
  and the client morph tore the tag out. Registrations now stage into a replacement map that is installed in
  a single store, so a reader observes either the complete old set or the complete new one.
- **Deleting a component's only `.css` file now repaints.** Bulk invalidation deliberately raises no
  `AssetChanged`, and the surviving siblings re-registered byte-identical content — which hits the no-op
  early return — so nothing fired and the deleted rules stayed on screen until a manual refresh.

### Added
- **`LitestreamStatus`** — a singleton published by the Litestream supervisor reporting whether replication is
  currently running, when it last started and exited, its last exit code or error, and how many times it has
  restarted. "Is my backup actually running?" was previously answerable only by the absence of a log line.
- **`ISqliteSnapshotStore.ListAsync`** — enumerate stored snapshots (name, size, timestamp), newest first and
  scoped to the store's search pattern, so what you can see is what retention manages. A default interface
  implementation returns an empty list, so existing custom stores keep compiling.
- **`JobOptions.RecurringJobs`** — the registered recurring schedule (name, interval, factory) is now public.
  Pair an entry with the `RecurringJobState` row of the same name to see when it last fired, or call its
  factory to enqueue an off-schedule run.

- **The browser tells you a hot reload landed.** Under `dotnet watch` in Development, the server pushes a
  `hotReload` frame once every session has repainted, and the client shows a brief "Hot reload applied"
  pill. Two independent gates keep it out of production: the server only subscribes when the app is in
  Development *and* the process supports metadata updates, and the client acts only on a `data-rask-dev`
  flag that production HTML never carries. The indicator is built lazily, so a production bundle
  constructs no DOM and injects no CSS for it.
- **A restart for a rude edit gets back on screen in ~250 ms instead of 4 s.** Adding a type or changing a
  signature is an edit hot reload cannot apply, so `dotnet watch` restarts the process — and the browser,
  holding a session id the new process has never heard of, showed *"Your session timed out"* and sat there.
  In development it now says *"Server restarted — reloading…"* and reloads promptly. Production keeps the
  original grace period and wording.

### Changed
- **`rask dev` finds the project and sets up the loop.** It was 35 lines of argv over `dotnet watch run`:
  no project detection, no environment, no output. It now resolves the project the way `rask db` does —
  picking the `.Server` host in a wasm-hosted solution, and refusing a native app with the
  `dotnet build -t:Run` command it actually needs, rather than running `dotnet watch` at a simulator. It
  sets `ASPNETCORE_ENVIRONMENT=Development` when you have set no environment yourself, prints a banner with
  the URL, and adds `--urls`, `--launch-profile`, `--open`, `--no-open`, `--no-restart`, `--once` and
  `--no-banner`.
- **A rude edit no longer hangs `rask dev`.** When `dotnet watch` meets an edit hot reload cannot apply it
  prompts `Yes (y) / No (n) / Always (a) / Never (v)` — and with no terminal to answer on, that blocked
  forever. `rask dev` now sets the `HotReloadAutoRestart` MSBuild property so the app restarts instead
  (`--no-restart` to be asked), and passes `--non-interactive` when stdin is redirected. The property is
  passed through the environment because `dotnet watch` has no `--property` switch — that is also why
  `IProcessRunner.RunAsync` gained an optional environment overlay.
- **`rask dev --no-hot-reload` now means what it says.** It used to run a plain `dotnet run`, which stopped
  watching altogether *and* cleared `DOTNET_WATCH` — switching off more framework behaviour than its name
  claims. It now keeps watching and restarts on change; **`--once`** is the new name for the old behaviour.
- **`RouteRegistry` groups registrations by contributor.** The new `Replace(groupKey, registrations)`
  installs one contributor's complete set, replacing whatever it registered before. This is what lets the
  generated per-assembly route registry re-run under `dotnet watch` without duplicating its own routes
  (`Add` appends), dropping another assembly's, or clearing the default 404 fallback — which is seeded once
  by a `[ModuleInitializer]` and could not be restored. `Add` keeps its existing append semantics.

### Fixed
- **A row changing underneath the jobs or outbox drain no longer discards the whole batch's progress.** Both
  processors ran a batch and then wrote every outcome in a single `SaveChangesAsync`. Anything else modifying
  one of those rows meanwhile — a manual `UPDATE`/`DELETE` against `app.db`, which is currently the only way
  to clear a dead letter — raised a `DbUpdateConcurrencyException` that rolled the transaction back and
  stripped `ProcessedAt` from **every** row in the batch that had already run. The side effects had happened,
  so on the next poll up to `BatchSize` (default 100) jobs re-ran and outbox events were **published a second
  time**, repeating every poll until the interference stopped. Each outcome is now persisted on its own, as
  the mail processor already did, so the blast radius is the single affected row.
- **A failing jobs or outbox poll no longer stops the host.** Neither processor guarded its cycle, so a
  transient database error (a `SQLITE_BUSY`, say) escaped `ExecuteAsync` and faulted the `BackgroundService` —
  which, under the default `BackgroundServiceExceptionBehavior.StopHost`, takes the whole application down.
  Both now log the failure and retry on the next poll, matching the mail processor.
- **The outbox marks an event published even while the host is shutting down.** The drain saved with the
  shutdown token, so an event delivered during shutdown could lose its `ProcessedAt` and be published again on
  restart.

- **The CLI build gates no longer pass over stale packages.** They pack this commit's Rask packages to a
  local feed, but MinVer derives the version from the commit and its height — so every pack of an
  uncommitted working tree produces the *same* version string with different content. NuGet keys its global
  cache on id+version alone, so once a version was extracted there, every later restore reused it and
  silently ignored the freshly packed nupkg: the gate built against whatever the first pack of that version
  happened to contain, and any change made afterwards was never actually tested. The gates now evict that
  version before restoring. (Found while building the watch gate below, which reported a feature missing
  that was demonstrably present in the packed assembly.)

### Docs
- **"What hot-reloads and what doesn't" is written down.** [`docs/cli.md`](docs/cli.md#what-hot-reloads) now
  lists every edit and what actually happens to it — applied live, or a rude edit that restarts — including
  the two gaps: WASM and native apps have no watch channel, and a rude edit is never announced (the process
  simply restarts). `docs/development-workflow.md` gained an inner-loop section, since it described the
  definition-of-done gate but never how to run the thing you're changing.

### Removed
- **The dev-time `.cs` `FileSystemWatcher` in `Rask.Server`.** It fired on *save* — before the new IL was
  applied — so it repainted against the old code and the real hot-reload repaint then did it again: a
  wasted frame and a visible flash. It also watched the entire current directory recursively, including
  `obj/` and `bin/`, so `dotnet watch`'s own rebuild retriggered it, and it was a never-disposed static.
  The only thing lost is a repaint on saving a file that does not compile.

### Docs
- **The roadmap says what isn't shipped, not only what is.** Every pillar was marked ✅, which made the page
  useless for the decision it exists to support — whether Rask fits your product. A new **Not shipped**
  section names the gaps and what to reach for instead: a **user store and account lifecycle** (registration,
  password hashing, reset, lockout, MFA), **file/blob storage**, **rate limiting** (including the absence of
  any login-attempt throttle), and a **dead-letter surface** for jobs/mail/outbox, which retry, give up, and
  leave the row with nothing to show you it happened.
- **Auth is split into what ships and what doesn't.** The sign-in machinery is real — cookie and JWT
  sessions, claims, authorization, hardening guidance — but `rask new --auth` scaffolds a *demo* credential
  store with hardcoded logins, and one ✅ covering both read as a user system that exists. The doctrine
  page's battery list and its "everything a solo developer needs" claim now carry the same caveat.
- **Every doc is reachable from the docs index**, which the doctrine page calls "the full map".
  `elements.md` (the DSL reference) and `repo-administration.md` were reachable from nothing at all; the
  operationally important `observability.md` and `configuration.md` now have index rows of their own rather
  than being buried a level down. A new test enforces reachability — links through a hub page count, since
  the docs are deliberately hub-and-subpage; being findable from *nowhere* is the failure.

### Fixed
- **The raw SQLite transaction helper no longer discards a failed rollback.**
  `ExecuteInImmediateTransactionAsync` clears a leaked transaction from a pooled handle before
  `BEGIN IMMEDIATE`, but it dropped that `ROLLBACK`'s result code and ran `BEGIN` regardless — so a
  rollback that failed turned into the misleading, non-retryable "cannot start a transaction within a
  transaction". The rollback now goes through the same fair-interval retry as everything else (it has to:
  `busy_timeout` is set to `0` just above it, so nothing else waits). `BEGIN` also moved inside the `try`,
  so a partially-failed begin can no longer return a mid-transaction handle to the pool and poison every
  later lease of it. Hardening rather than a confirmed fix for #504 — see that issue for what was ruled
  out.
- **A `decimal` input no longer silently refuses to submit.** An `Input` bound to a fractional type
  rendered `<input type="number">` with no `step`, and HTML's default is `step="1"` — so the browser's own
  constraint validation rejected `42.50` and never fired the submit event. Nothing threw, no validation
  message appeared, and the form simply did nothing, which reads as the framework being broken rather than
  an attribute being missing. `decimal`/`double`/`float`/`Half` now default to `step="any"`; integral types
  keep the implicit whole-number constraint, and an explicit `Step:` still wins. `BsInput` needed the same
  fix separately: it renders through `Input<string>` with a pre-formatted value, so `Input<T>`'s own default
  never saw the bound type.
- **The wasm-hosted build gate no longer fails at random.** `Generated_wasm_hosted_solution_builds` is
  the only gate that built a `.sln`, and the generated Server carries a cross-TFM `ProjectReference` to the
  Client whose target framework is deliberately never negotiated. That put the Client in the restore graph
  twice — once as a solution entry, once as that reference — and the two writers raced on its `obj/`
  restore artefacts, failing with `The file '…project.assets.json' already exists`. `-m:1` doesn't help (it
  caps MSBuild nodes, not NuGet's parallelism) and neither does splitting restore from build, since both
  entries are present within the single restore. The gate now builds the Server project, which references
  the other two, so all three still compile with one graph entry each. Since #502 wired this gate into
  `pre-push`, the flake had gone from an occasional annoyance to intermittently blocking pushes.
- **`rask new` no longer overwrites files it didn't create.** The guard checked only for the project file, so
  scaffolding into a directory that already held a `Program.cs`, a `Features/` tree or a `wwwroot` silently
  overwrote them — with no `--force` to consent to and nothing to undo it. Any existing file the template
  would write now stops the command and lists what it would have replaced; `--force` opts in.
- **A failed `dotnet restore` is reported as a failure.** It was a warning followed by exit `0`, so
  `rask new && dotnet build` walked straight past a project whose packages hadn't restored. New
  `--no-restore` covers the deliberate offline case.
- **`rask generate` reports the packages it couldn't add.** A `dotnet add package` failure (offline, say)
  printed a warning and exited `0` — a success from a project that cannot compile. Falling back to *printing*
  the `Program.cs` registrations stays exit `0`: that's a documented fallback for a project shape Rask won't
  edit blind, not a failed action.
- **`rask generate --output` must stay inside the project.** `--output ../../..` wrote files outside it and
  quietly gave them the root namespace instead of failing — generated code is namespaced by its folder, so a
  folder outside the project can't produce a coherent one. Combining `--output` with `--feature` is also
  rejected now instead of silently discarding `--feature`.
- **`rask db drop` asks before dropping the database**, which its own `--force` help has always claimed it
  did. `dotnet ef` prompts only when it has a terminal, so a drop run from a script destroyed the database
  with nothing asked. Without a terminal it now refuses unless `--force` is given.
- **`rask info` rejects arguments it doesn't understand**, instead of ignoring them and printing the plain
  report for `rask info --json`.
- **The pinned package version for generated projects is derived, not hardcoded.** It was a constant that had
  rotted two minor versions behind the repo. A released CLI pins itself; a dev/CI prerelease walks back to
  the release it came after.
- **A filesystem error is a message, not a stack trace.** A read-only directory or a file held open by an
  editor surfaced as an unhandled .NET exception, burying the one line naming the path. `RASK_DEBUG=1` still
  prints the full trace.

### Changed
- **Usage errors exit with `2`**, distinct from `1` ("what you asked for failed"), so a script driving the
  CLI can tell a mistyped invocation from a broken deploy. Exit codes are now documented in
  [`cli.md`](docs/cli.md).

### Added
- **Scaffolded apps get a production error page.** An unhandled exception thrown *outside* a component
  tree used to return a bare 500 with an empty body (`ErrorBoundary` already covered inside one).
  `rask new` now emits `Features/Shared/ErrorPage.cs` — a routed `/error` page that renders through the app
  shell — and registers `app.UseExceptionHandler("/error")` outside Development, where the developer
  exception page is strictly better. The page shows a correlation id (`Activity.Current?.Id`) and
  deliberately nothing about the exception: it is served to whoever hit the error, so rendering the message
  or a stack trace would be worse than the blank page it replaces. The detail goes to `ILogger`, matched by
  that id. `[AllowAnonymous]`, so adding a fallback authorization policy later can't make the error page
  redirect to `/login`.
- **Continuous backup is on the golden path.** `Rask.SQLite.Litestream` shipped, was documented, and was
  referenced by **no template** — so `rask new --data` → `rask deploy` produced a live app whose only copy of
  the database was a volume on one box, while `rask deploy`'s own code comments explained that the graceful
  stop existed to let "the Litestream replicator flush" — protecting a replicator that was never running.
  Now `--data` wires it: the replication code is scaffolded but **inert until you set a replica URL**, so
  turning it on is one variable at deploy time (`rask deploy --env "Litestream__ReplicaUrl=s3://bucket/app"`)
  rather than a docs safari. `--docker` puts the `litestream` binary in the image (copied from its official
  image — one layer, no package manager), because wiring without the binary would be a backup that silently
  never runs. The startup restore is guarded: `RestoreSqliteFromLitestreamAsync` throws when no replica is
  configured, so an unguarded call would stop every app without one from starting at all.
- **A deploy with no replica configured says so**, every time: `! No Litestream replica configured — this
  app's database exists only on this box's disk.` The one-box story is only safe when the box is disposable,
  and that is worth stating rather than assuming.

### Fixed
- **A redeploy can no longer silently drop your secrets.** `--env` values were never remembered — only the
  `--env-file` *path* was — so a bare `rask deploy` after one that carried `--env`, and the workflow
  `--github-actions` writes (which passes no `--env` at all), would start the app **without** its database
  password. It boots, answers its health check, takes traffic, and is quietly misconfigured: the worst shape
  a failure can take. `.rask/deploy.json` now records the variables' **names** (never their values — that
  file is committed), and a deploy that doesn't supply one of them refuses, naming it and listing the four
  ways to resolve it. New [`docs/secrets.md`](docs/secrets.md) covers the whole story, including what Rask
  deliberately doesn't do.
- **`rask deploy rollback` no longer erases remembered settings.** It reuses the deploy path, which ended by
  rewriting `.rask/deploy.json` — with the nulls a rollback passes for `project`/`envFile`, wiping both. A
  rollback changes which image runs, not how the app is configured, so it no longer persists at all.

### Security
- **Runtime environment values are passed to Docker through a file, not the command line.** `-e KEY=VALUE`
  puts every secret in the local process table, readable by any other user on the machine — and on the CI
  runner, in the workflow `--github-actions` writes. They now go through `--env-file`, which the docker CLI
  reads locally and sends over the API; the temporary file is deleted as soon as the container has them.
  Values that span lines (a PEM key) can't round-trip through that format and are still passed inline
  rather than being silently truncated.

### Added
- **A scaffolded app is now configured for the place it gets deployed to.** `rask new` produced an app that
  compiled and ran locally but was missing the pieces that only matter once something is in front of it:
  - **`appsettings.json` + `appsettings.Production.json` are scaffolded.** Neither existed, so there was
    nowhere to put the `Logging:LogLevel:Rask.Live` setting [`observability.md`](docs/observability.md)
    tells you to write, and all production configuration had to arrive as environment variables.
  - **Health checks report live-session capacity** (`AddRaskLiveSessions`). The endpoint `rask deploy` gates
    its blue-green swap on answered a flat 200 even while the host was refusing new sessions with `503` — so
    a deploy could switch traffic onto a server that couldn't take it. (Server template only: a wasm-hosted
    host has no live-session pool.)
  - **Forwarded headers are honoured**, so behind the Caddy proxy `rask deploy` runs, `Request.Scheme` is
    `https` and `RemoteIpAddress` is the visitor rather than the proxy. Without it `UseHsts` never emitted
    and every logged client IP was wrong.
  - **Shutdown completes inside the deploy's grace period** (15s vs the 20s before `SIGKILL`), so in-flight
    requests drain and SQLite checkpoints instead of being killed mid-write. The host default of 30s was
    longer than the deploy would wait.
  - `UseStatusCodePages()` gives an unmatched route a readable body instead of a blank page.
- **Deployed containers get production runtime defaults**: `ASPNETCORE_ENVIRONMENT=Production` (previously
  left to whatever the base image assumed — so `appsettings.Production.json` would never have been read),
  bounded logs (`max-size=10m`, `max-file=3` — Docker's default is unbounded, and on a one-box deploy an app
  filling the disk takes every other app down with it), and `--security-opt no-new-privileges`.
- **`rask deploy status` / `logs` / `rollback` — the CLI now covers operating the app, not just shipping
  it.** Until now `rask deploy` took you to production and left you there: seeing what was running,
  reading its logs, or undoing a bad release all meant hand-writing `docker -H ssh://…` commands, which is
  precisely the SSH session the deploy story promises you never have to open. All three read the same
  `rask.*` container labels a deploy writes, so they describe the box as it actually is rather than as
  `.rask/deploy.json` remembers it, and they need no Dockerfile and no build.
  - **`status`** lists every Rask-managed app sharing the host — URL or published port, blue/green colour,
    uptime — and says whether a rollback is currently possible.
  - **`logs`** tails the live container (`--tail <n|all>`, `--follow`).
  - **`rollback`** exists for the failure the blue-green swap cannot catch. That swap protects you from a
    release that *fails*; it can do nothing about one that starts, answers its health check, and is simply
    wrong. Each deploy now moves the image it replaces to `<app>:previous` before building — previously the
    build overwrote `:latest` and the old image was left untagged and unrecoverable — and `rollback` starts
    it back up through the same gates a deploy uses (running → healthy → reload the proxy → retire the old
    container). It then swaps the two tags, so running it again undoes the rollback rather than repeating
    it. Images are now built as `<app>:current` (plus `:latest`, kept so `docker images` still reads the way
    you'd expect).

### Changed
- **The pre-push CLI build gate now runs only when a push can change what the generators emit** — the CLI's
  scaffolding, the `Rask.*` packages a generated project references, the tutorial, or the gate itself.
  It packs 15 packages and runs several full builds, so running it on a docs typo was minutes of tax for a
  foregone conclusion. Same conditional treatment the deploy gate already had.

### Security
- **`rask deploy --domain` is now validated before it reaches the shared proxy.** The domain was written
  verbatim into the Caddyfile that fronts *every* app on the box, so a value containing `{`, `}` or a
  newline could close the generated site block and inject arbitrary Caddy directives — a global options
  block, a `file_server` over `/`, an open admin endpoint — and an embedded tab or newline could forge a
  row in the tab-separated `docker ps` label listing the routing is rebuilt from. Because the domain is
  remembered in the **committed** `.rask/deploy.json` and read by CI, a hostile value could arrive by pull
  request and reconfigure the proxy of every host the repo deploys to. This is the same threat model that
  motivated the existing SSH-host check, and it now has the same kind of boundary: `--domain` must be an
  RFC-1123 host name (optionally with a leading `*.`), validated wherever the value comes from.
- **A failed deploy no longer prints the values you passed it.** The container's last log lines are dumped
  to stderr on failure — which, in the workflow `--github-actions` writes, is a CI job log — so any
  `--env` / `--env-file` value appearing in them is now masked first. Likewise an unparseable `--env-file`
  line is reported by **line number** instead of by echoing the line, which was a credential.

### Fixed
- **The standalone `wasm` template can be deployed.** Its nginx image listened on port 80 while
  `rask deploy` had the container port hardcoded to 8080, so the proxy and the readiness probe both aimed
  at a closed port and the deploy could never succeed. The generated `nginx.conf` now listens on `8080`
  and serves `/health`, matching the server and wasm-hosted templates, and a new `--container-port` flag
  (remembered, and recorded as a label on the container) covers any hand-written Dockerfile that listens
  elsewhere. Because the port is stored per container, a host running several apps that don't agree on one
  keeps each app's routing correct.
- **The `wasm-hosted` Dockerfile prepares `/data` like the server template does.** `rask deploy` mounts a
  named volume at `/data` and points the app at `/data/app.db` for *every* template, but this image never
  created the directory or gave it to the non-root runtime user — so the mount landed root-owned and the
  app couldn't create its database.
- **A port-mode deploy is visible to the host inventory.** Its container carries `rask.*` labels (without a
  domain, so it is never proxied), which means moving an app to `--domain` later retires the old container
  instead of leaving it running and unaccounted for.
- **The generated Caddyfile is removed from the temp directory** once it has been copied to the host,
  rather than left behind under a predictable name.

### Added
- **`rask deploy` is now verified against a real host.** Every deploy test in the repo was mocked — a fake
  process runner recorded the argv and returned a scripted exit code — so the suite proved the command line
  Rask *builds* and nothing about whether deploying *works*. A new opt-in gate
  (`DeployHostE2ETests`, `RASK_DEPLOY_E2E=1`, `scripts/run-deploy-e2e-local.sh`) points the real
  `rask deploy` at a throwaway container standing in for a bare VPS — sshd plus its own Docker daemon —
  and asserts on what happened *on the host*: the image built over SSH, the container answers its health
  check, a redeploy keeps the named volume's contents (the database-survives-redeploy contract), the
  blue-green swap moves the domain from blue to green and retires the old colour, a real Caddy accepts the
  generated Caddyfile, and a container that starts but never answers is removed with the previous version
  left serving. It needs a `docker` CLI and a daemon that can run a privileged container; it installs
  nothing and never reads or writes your `~/.ssh`. The `pre-push` hook runs it only when the push touches
  the deploy path. Real DNS + Let's Encrypt issuance remain uncovered — the gate uses a `.test` domain.

### Fixed
- **The CLI build gates no longer pass without running.** The tests that prove the code `rask new` and
  `rask generate` write actually *compiles* are opted into with `RASK_CLI_BUILD_E2E=1` — but that variable was
  set nowhere in the repo, and each case returned early when it was absent, so all 20 reported **passed**
  while never executing. Every other CLI test asserts on generated strings, so in practice nothing was
  checking that a scaffolded project builds. The gates now use `Skip.IfNot` (the `Xunit.SkippableFact`
  pattern the Appium suite already uses) and report **SKIPPED** with the reason, and a new
  `scripts/run-cli-build-e2e.sh` runs them for real — wired into `.githooks/pre-push`, so a scaffolding
  break is caught before it leaves the machine instead of in a beginner's terminal. Bypass with
  `RASK_SKIP_CLI_BUILD_E2E=1`.

### Added
- **`samples/Rask.Example.Shop` — every One Person Framework battery in one running app.** Data, CQRS,
  outbox, jobs, mail, cache, production SQLite, snapshots, Litestream, Web Push, PWA, auth and Docker,
  wired together and covered by nine browser tests that assert each pillar *ran* rather than that a row was
  written. It is the CLI's output rather than a hand-written showcase — the exact commands are in its
  README, and a provenance test re-runs the generators and compares them against the committed files, so
  "generated by `rask new`" stays true instead of becoming a claim that rots.
- **`rask new` scaffolds every One Person Framework battery.** The `server` template gains `--jobs`,
  `--mail`, `--cache`, `--outbox`, `--push`, `--snapshots`, and `--all-batteries` for the lot (continuous
  backup is already on the `--data` golden path). Each flag adds its package, its
  `builder.Services.AddRaskX<AppDbContext>()` registration, and the `modelBuilder.AddRaskX()` call that
  gives the pillar its tables — so `rask new Shop --all-batteries` is a running app one `rask db add Init`
  away, instead of a page of wiring copied out of the docs. Every flag implies what it needs (`--jobs` →
  `--data` → `--cqrs`, `--push` → `--pwa`). The composed `Program.cs` gets the load-bearing order right and
  says why in comments: the outbox registered before the `DbContext` factory so its interceptor joins the
  `SaveChanges` pipeline, and `ApplyRaskConventions()` after the entity configurations because it walks the
  model as it stands. `--outbox` also turns **off** the in-process domain-event publisher — leaving it on is
  a silent trap in which the outbox table stays empty, delivery quietly stops being durable, and nothing
  fails because the handlers still run. `--push` registers Web Push only once a VAPID key pair is
  configured, because `AddRaskWebPush` validates its options and throws at startup without one — a freshly
  scaffolded app has to run before you have generated any keys.
  `--mail`, `--cache`, `--outbox`, `--push`, `--snapshots`, `--litestream`, and `--all-batteries` for the
  lot. Each flag adds its package, its `builder.Services.AddRaskX<AppDbContext>()` registration, and the
  `modelBuilder.AddRaskX()` call that gives the pillar its tables — so `rask new Shop --all-batteries` is a
  running app one `rask db add Init` away, instead of a page of wiring copied out of the docs. Every flag
  implies what it needs (`--jobs` → `--data` → `--cqrs`, `--push` → `--pwa`). The composed `Program.cs`
  gets the load-bearing order right and says why in comments: the outbox registered before the `DbContext`
  factory so its interceptor joins the `SaveChanges` pipeline, `ApplyRaskConventions()` after the entity
  configurations because it walks the model as it stands, and the Litestream restore before anything opens
  the database. `--outbox` also turns **off** the in-process domain-event publisher — leaving it on is a
  silent trap in which the outbox table stays empty, delivery quietly stops being durable, and nothing
  fails because the handlers still run.
- **`rask generate feature` output passes `dotnet format`.** Three template indentation bugs made the
  generated code compile but fail a `--verify-no-changes` check in the new project: the `--concurrency`
  form-load block and its `catch` clause were indented one level too deep, and a conditional token that
  resolves to nothing (`--soft-delete`'s toggle button, among others) left its indentation behind as a
  whitespace-only line. Trailing whitespace is now stripped once after token substitution rather than
  every template having to keep its spacing exact.
- **`rask new --litestream` produces an app that runs.** The registration was gated on
  `Litestream:ReplicaUrl` but `RestoreSqliteFromLitestreamAsync()` was called unconditionally, and it
  throws when Litestream was never registered — so a scaffolded app exited at startup until a replica was
  configured. The restore now sits behind the same check.
- **`rask new --push` produces an app that runs.** `AddRaskWebPush` validates its options and throws at
  startup without a VAPID key pair, so registering it unconditionally meant a freshly scaffolded app
  couldn't start until you had generated keys. It is now registered only once `WebPush:PublicKey` and
  `WebPush:PrivateKey` are configured; the subscription store and its endpoints are always registered, and
  `/_push/key` answers with an empty key until then.
- **`rask generate cache <Name>`** — a read-through cache accessor that owns its key and its invalidation
  in one place, so a stale entry is something you can find and drop rather than hunt for across inline
  string keys. Alias `rask g ca`; `--feature` co-locates it in a slice like `job`/`email`.
- **The `docs/tutorial/` walk-through is now a compile gate.** A new opt-in end-to-end test
  (`TutorialWalkthroughE2ETests`, `RASK_CLI_BUILD_E2E=1`) reproduces the tutorial's chapters 1–8 exactly as a
  reader would — the real `rask new`/`generate feature`/`generate job`/`generate email` generators, the real
  `Program.cs`/`DbContext` splices, and the hand-written code the prose tells the reader to type (the job body,
  the `IMailQueue` send, the `ICache.GetOrCreateAsync` read-through, the `IOutboxEvent` + handler, the
  `Entity.Raise`, the Litestream wiring) — and builds the fully-wired `Shop` under `-warnaserror`. If a Rask
  package changes a signature the tutorial uses, the tutorial now breaks a test in the same commit rather than
  in a beginner's terminal. The shared pack/build plumbing was lifted into `CliBuildE2E`, shared with the
  existing scaffold gate so the local feed is packed once per session.
- **`rask generate` gains a `--feature <Name>` flag.** `component`, `job`, and `email` now default to the
  cross-cutting `Features/Shared/` bucket; `--feature <Name>` (`-F`) co-locates the file into that feature's
  slice (`Features/<Name>/`) instead — for a job or email that belongs to one feature. The namespace follows
  the folder either way. Rejected on `page`/`feature` (a page derives its slice from the class name; a feature
  *is* a slice).
- **`rask new --data` scaffolds a database-ready app.** The `server` template gains a `--data` flag that
  pre-wires SQLite + EF Core: an empty `AppDbContext` (applying Rask conventions so generated feature configs
  are picked up), `AddRaskData()`, and a `UseRaskSqlite` (WAL + `busy_timeout` production pragmas)
  `AddDbContextFactory<AppDbContext>` whose connection string honours a `ConnectionStrings:App` override (so a
  deploy can point it at a mounted volume). It implies `--cqrs`, so a single `rask new Blog --data` produces an app where the
  first `rask generate feature <Name> --context AppDbContext` is immediately runnable with `rask db add` /
  `rask db update` — no manual DI. Verified end to end: the generated project (alone and with `--auth`)
  builds under `-warnaserror`. (Closes #478.)

### Fixed
- **A database-backed app no longer downloads the litestream binary at build time.** `--data` references
  `Rask.SQLite.Litestream`, whose build props fetch a release asset from GitHub unless
  `RaskLitestreamDownload` is set — so a scaffolded app couldn't be built offline, and errored outright on
  a RID with no published asset. The scaffold now opts out: the binary belongs in the Docker image (which
  `--docker` already copies it into), not in everyone's build.
- **`rask generate feature --outbox` no longer leaves the outbox silently disabled.** The `Program.cs` splice
  recognised an existing registration by matching the whole first line, so it never saw the multi-line
  `AddRaskData(o => { … })` that `rask new --outbox` writes and appended a second one. `AddRaskData` is
  guarded so the *first* call wins, which meant a later call's options were quietly dropped — and in the
  common flow (`rask new App --data`, then `rask g f Order --outbox`) that dropped exactly the option the
  outbox depends on. With the in-process publisher left on, `DomainEventInterceptor` drains and clears every
  entity's events before `OutboxInterceptor` can copy them: the outbox table stays empty and delivery stops
  being durable, while every handler still runs so nothing appears wrong. The splice now matches on the
  extension method rather than the line text, and upgrades a bare `AddRaskData()` to the outbox-safe form.
- **A background job or outbox event declared in a namespace whose name is a C# keyword no longer silently
  dead-letters.** The `Rask.Jobs` and `Rask.Outbox` registry generators derived *one* string from
  `ISymbol.ToDisplayString()` and used it for two incompatible jobs: the registry key (which has to equal the
  runtime `Type.FullName`) and the emitted `typeof(...)` operand (which has to be valid C#). Those differ —
  `FullName` is unescaped, C# syntax escapes keyword identifiers — so a job in `namespace Demo.@event`
  registered as `Demo.@event.Job` while the runtime stored `Demo.event.Job`. The key never matched:
  `Deserialize` returned `null`, the processor recorded `No registered job type '…'`, and the job burned an
  attempt on every poll until it hit `MaxAttempts`. The key and the operand are now derived from two separate
  `SymbolDisplayFormat`s, and the emitter no longer concatenates `global::` onto a display string.
- **A job, outbox event, or CQRS handler that is `file`-local or `private`/`protected` no longer breaks the
  build.** All three generators emitted a `typeof(...)` for types the generated file cannot name, producing
  `CS0234`/`CS0122` in generated code rather than skipping the type. They now check the whole containing-type
  chain. `Rask.Cqrs` reports these through the existing **RASK029**; jobs and outbox events report the new
  **RASK035**, so a type that can never be dispatched says so at build time instead of failing in production.
  Closed generics (`IQueryHandler<Page<int>, string>`) are unaffected — they are perfectly nameable.
- **`rask generate` no longer corrupts `Program.cs` when wiring a registration after a multi-line one.** The
  Program.cs splice anchored on the line that *starts* a `builder.Services.` statement, so a second wiring pass
  (e.g. `rask generate email`'s `AddRaskMail<…>` running after `rask generate feature` had left the multi-line
  `AddDbContextFactory<…>((sp, o) => o …)` — the tutorial's exact chapter 2 → chapter 5 flow) inserted the new
  registration *inside* that statement, producing a `Program.cs` that no longer compiled. The splice now
  advances to the end of the statement (its terminating `;`) before inserting.

### Docs
- **The tutorial builds its app with the CLI, and gained two chapters.** Chapters 6–8 hand-typed the cache,
  outbox and production-SQLite wiring; they now use `rask generate cache`, `rask generate feature --outbox`
  and the `--snapshots`/`--litestream` flags, with the prose explaining *why* an ordering is load-bearing
  rather than walking you through retyping it. Chapter 1 scaffolds with `--all-batteries`, so no later
  chapter needs a wiring detour before it can teach what its pillar is for. Two new chapters cover the
  pillars the tutorial never reached — **push notifications** (9) and **watching it run** (10), an `/ops`
  page over every pillar's own table — and deploy moves to chapter 11. Every chapter now links to
  `samples/Rask.Example.Shop`, the committed app the same commands produce, and
  `TutorialWalkthroughE2ETests` compiles the whole walk-through including the new chapters (it caught a
  wrong `WebPushStatus` member in chapter 9's prose before this shipped).
- **Email bodies carry data on public properties and are built through their generated factory.** The
  tutorial (chapter 5), `mail.md`, `Rask.Mail/NUGET.md`, and the `rask generate email` scaffold comment all
  showed `new EmailComponent(ctorParams)`, which doesn't compile — `RASK014` forbids constructing a component
  with `new`, and the generated factory passes data via public properties, not constructor arguments. They now
  declare a public property (e.g. `public decimal Total { get; set; }`) and send through the factory
  (`Body(OrderReceipt(OrderId: …, Total: …))` / `Body(WelcomeEmail(Name: …))`). (Closes #500.)
- **Tutorial (chapter 2): the field-type list notes the `text` → `string` and `money` → `decimal` aliases**,
  which chapter 3's relationship example (`Body:text`) relies on.

### Changed
- **Everything the CLI generates now lives under `Features/` — one consistent vertical-slice layout.**
  Previously `rask generate component`/`job`/`email` wrote to root-level `Components/`, `Jobs/`, and `Emails/`
  folders while `page`/`feature` already used `Features/`, and `rask new` scattered `App.cs`, `Auth/`, and
  `Data/` at the project root. Now a screen is its own `Features/<Name>/` slice and cross-cutting code (the app
  shell, components, jobs, emails, the `DbContext`) sits in `Features/Shared/`, matching the layout Rask's own
  samples use. Concretely: `generate component/job/email` default to `Features/Shared/<Name>.cs` (namespace
  `<Root>.Features.Shared`; use `--feature` to co-locate in a slice), and `rask new` emits the shell at
  `Features/Shared/App.cs`, the welcome page at `Features/Home/HomePage.cs`, `--auth` under `Features/Auth/`,
  and `--data`'s `AppDbContext` under `Features/Shared/`. Verified end to end: every `rask new` flag
  combination (server, wasm, wasm-hosted) builds under `-warnaserror` with the reorganized namespaces.
- **Generated features use `UseRaskSqlite` (production pragmas) instead of raw `UseSqlite`.** A `rask generate
  feature` run that owns its `DbContext` now registers it with `UseRaskSqlite` — the WAL + `busy_timeout` +
  `foreign_keys` pragma set — so a generated app survives concurrent writers (jobs, email, outbox) instead of
  hitting `database is locked`. The connection string honours a `ConnectionStrings:App` override (defaulting to
  a local `app.db`), so a deploy can point it at a persistent volume. Adds `Rask.SQLite.EntityFrameworkCore` to
  the project.
- **`rask generate feature` pins the `Rask.*` packages it adds to the CLI's version.** Previously it floated
  them to the latest on nuget.org (`dotnet add package` with no version), which could pair a template's
  `Rask.Server` with a newer `Rask.Data`/`Rask.Cqrs`. Non-Rask packages (EF Core, SQLitePCLRaw) still float.
- **`rask generate email` auto-wires into your DbContext — no manual paste.** It previously scaffolded the
  email-body component and then *printed* four setup steps. It now applies them: when it finds a single
  `DbContext` in the project (or you pass `--context <Name>`), it registers `AddRaskMail<Ctx>` in `Program.cs`
  and adds `modelBuilder.AddRaskMail()` to that context's `OnModelCreating` (both idempotent), leaving only the
  SMTP config and the migration. With no context — or several and no `--context` — it prints the manual steps
  as before. This closes the asymmetry with `rask generate feature`, which already wrote its DI. (`--context`
  is now accepted on `generate email` too.)
- **OPF docs & landing-site polish.** The landing site now advertises **46** typed browser APIs (was a stale
  "43", matching `docs/apis/` and the docs index); the roadmap's CRUD-scaffolder entry now credits the
  `rask generate job`/`email` scaffolders alongside `feature`; the tutorial's Chapter 2 `--validation` note
  leads with the flag being optional and the `valueobjects` default; and the One Person Framework manifesto no
  longer implies a blob-store pillar Rask doesn't ship.

### Fixed
- **`rask new` rejects an invalid project name up front.** The name becomes the root namespace and csproj
  filename, so a value like `my-app` or `9Lives` (a dash, a leading digit, a keyword, an empty dotted segment)
  used to scaffold a project that never compiled. It's now validated before any files are written, with a clear
  message; dotted names like `Contoso.Shop` are still accepted.
- **`rask deploy` no longer destroys the SQLite database on every deploy.** The app's database lived in the
  container's writable layer, so each deploy — which always runs a fresh container — wiped it. `rask deploy`
  now mounts a per-app named volume and points the app at it (`ConnectionStrings:App` → `Data Source=/data/app.db`),
  so the database persists across redeploys, and it stops the retiring container **gracefully** (SIGTERM,
  `docker stop -t 20`) before removing it, so the in-process Litestream replicator flushes and SQLite
  checkpoints the WAL instead of being SIGKILLed. The `rask new --docker` image now prepares a writable `/data`
  owned by the non-root runtime user so the volume is writable (a custom Dockerfile needs the same:
  `RUN mkdir -p /data && chown $APP_UID:$APP_UID /data`). Deploy/tutorial docs now explain the persistence
  model and steer replica credentials to `--env-file` (a one-shot `--env` isn't remembered on the next deploy).
- **The `Program.cs` DI splice is now compile-gated.** `rask generate feature`'s one runtime-critical edit —
  inserting the `AddRaskCqrs`/`AddRaskData`/`AddDbContextFactory` registrations + their usings into `Program.cs`
  — was only verified by string assertions; the build E2E wrote the feature files but never ran the splice.
  The splice is extracted to a pure `SpliceProgramCs` (unit-tested for anchor placement + idempotency), and the
  multi-entity build E2E now applies the real splice to the scaffolded `Program.cs` and compiles it under
  `-warnaserror`, so the edit that turns generated files into a running app can't silently produce
  uncompilable code. Also corrected a stale `llms.txt` line that still described the feature DI as a "manual
  paste" (it's been auto-written since #482).

## [0.19.0] - 2026-07-20

### Added
- **`rask generate feature` now generates relationships.** The `<card> <Target> <fields…>` grammar
  (`rask g f Post Title:string 1:n Comment Body:text`) is no longer parse-only — it scaffolds the related
  entity *and* emits the relationship: `1:n`/`n:1`/`1:1` add the foreign key (as a real column + form input),
  navigation properties both ways, and the EF `HasOne`/`WithMany`/`HasForeignKey` mapping; `n:n` maps a
  many-to-many through EF Core's implicit join table (no join entity to generate). Verified end to end —
  every cardinality (and a multi-relationship star like `Post 1:n Comment n:n Tag`) generates compiling
  code. (Closes #479.)
- **`rask generate feature` produces compiling, wired code — no manual paste.** The generator no longer just
  *prints* the `AddRaskCqrs()` / `AddRaskData()` / `AddDbContextFactory<Ctx>()` registrations — it inserts
  them (and the `using`s they need) into `Program.cs` idempotently. **`--context` now works too:** it locates
  the target `DbContext` in the project, adds the new `DbSet<Entity>` (and its `using`) to it, and imports the
  context's namespace in the generated slice — so an `--context` run compiles as-is instead of erroring on a
  missing `DbSet`/`using`. When a file can't be found or safely edited, the change is printed as a fallback.
  The migration next-steps now point at `rask db add`/`rask db update` instead of raw `dotnet ef`.
  (Closes #475, #476, #477.)
- **`rask generate feature --tests` scaffolds a runnable test project.** The first `--tests` run now creates
  the sibling `<Project>.Tests` project — its `.csproj` and a `GlobalUsings.cs`, wired with the test SDK,
  xUnit, and a reference back to the app (and added to the solution if there is one) — so the generated
  domain + SQLite-persistence tests build and `dotnet test` passes with no manual setup. Later runs reuse the
  project. (Closes #480.)
- **A zero-to-deploy tutorial — build a whole product, one pillar per chapter.** New `docs/tutorial/`
  series (10 chapters) walks a beginner from an empty folder to a deployed, database-backed "Shop":
  scaffold (`rask new`), the first DB-backed feature (`rask generate feature` → the `Program.cs` DI wiring
  → `rask db add`/`update`), auth (`[Authorize]` + the `Authorize` component), background jobs, transactional
  email, cache, domain events + outbox, production SQLite (`UseRaskSqlite` + Litestream), and
  `rask deploy` to one box. Every step is a real command, the code it generates, and a verify check; each
  command was run against the current CLI. Linked from the docs index, getting-started, the doctrine, the
  README, and `llms.txt`.
- **One design across the site, docs and playground — dark-first, with a shared light/dark toggle.** The
  marketing site's violet dark-first look is now the shared design language for all three GitHub Pages
  apps. The palette + Bootstrap 5.3 `--bs-*` bridge live in one shared static asset,
  `_content/Rask.Bootstrap/tokens.css` (linked via a new `RaskTokens()` helper), so the showcase/docs
  (Server + WASM + native) and the playground reskin to the same tokens instead of hand-syncing per-app
  copies. A theme toggle in the showcase navbar and the playground bar flips `data-theme` +
  `data-bs-theme` together and persists to `localStorage`, so a light/dark choice carries across the
  site ↔ docs ↔ playground on the same origin (re-applied after the WASM full-document morph, and set
  pre-boot so there's no flash). The docs/playground chrome is built with `Bs*` primitives throughout,
  including a new **`BsLink`** (an anchor styled as a Bootstrap button — the link counterpart to
  `BsButton`) used for the Playground/Docs/GitHub CTAs. Monaco stays `vs-dark`; the playground's preview
  canvas stays a light "paper" so user-authored components render readably in either theme.
- **`rask deploy` sets up a bare host for you — you never SSH in to prepare a box.** Handed a fresh VPS
  (root, an SSH key, nothing else), deploy used to stop at a two-line hint telling you to go install
  Docker yourself; it now checks what the box has, prints exactly what it wants to change, asks once,
  and does it: installs Docker from [get.docker.com](https://get.docker.com), creates a non-root
  `deploy` login with your `authorized_keys` + docker group + passwordless sudo, enables `ufw`, and
  hardens sshd (password login off, root login off). `.rask/deploy.json` is rewritten to the new login,
  so later deploys keep working after root SSH is disabled. **Setup only ever happens to a box that
  can't already deploy** — once Docker runs, `rask deploy` just deploys, so a host that's fine as it is
  (a least-privilege login with no sudo, a cloud firewall instead of `ufw`) is never prompted about or
  touched, and re-running is a no-op. `--setup-host` prepares a host anyway. A single read-only SSH probe
  replaces the old `docker -H ssh:// version` check (same one round-trip), so failures now say which of
  "Docker isn't installed" / "the daemon isn't running" / "you're not in the `docker` group" / "SSH
  itself failed" actually happened, quoting ssh's own words. Opt out per step with `--no-firewall`,
  `--no-harden-ssh`, `--no-deploy-user`, or `--deploy-user <name>`; skip the prompt with `--setup-host`;
  refuse to touch the host with `--no-setup-host`.

  Nothing that can revoke your access happens until a **brand-new** connection (`ControlPath=none`)
  proves the replacement works: the deploy login is tested before anything is hardened, and root SSH is
  only disabled once a working non-root login exists. The firewall and hardening then run behind a
  `systemd-run` rollback timer armed on the box itself, disarmed only after we prove we're still in — so
  a lockout heals in ~5 minutes even if the CLI is killed. Anything that can't be done safely is skipped
  **out loud**: if sshd's real listening ports can't be read, the firewall isn't enabled (Rask won't
  guess port 22), and if `sshd_config` has no `Include` line the hardening drop-in would be ignored, so
  it's skipped rather than reported as done.

  **Behavior change:** `rask deploy` may now modify the host. It only does so after showing the plan and
  asking; on a non-terminal (CI, piped stdin) it refuses and tells you to pass `--setup-host`, so no
  scripted deploy starts changing a box unattended. This deliberately narrows the CLI's "we never
  auto-install Docker" stance to your *local* machine — the remote box is what you're asking `rask
  deploy` to manage. It also means host setup is the one remote step that isn't `docker -H ssh://…`
  (installing Docker over Docker is chicken-and-egg), so it shells out to plain `ssh`. Documented in
  `docs/deployment.md` and `docs/cli.md`.
- **`rask deploy --github-actions` writes a deploy workflow.** Generates `.github/workflows/deploy.yml`
  that runs the same `rask deploy` on every push to `main` (and on demand), and prints the two
  `gh secret set` lines it needs — an SSH key and the host's fingerprint from `ssh-keyscan`. Everything
  else comes from the committed `.rask/deploy.json`, so the workflow is identical for every project.
  It's pure scaffolding: no host, no network, works before the box exists, honours `--dry-run`, and
  won't overwrite a workflow you've edited. The generated job deploys with `--no-setup-host` on purpose —
  a host that isn't ready should fail the build rather than be reconfigured from a runner.
- **`rask generate feature` can emit several entities in one run.** Groundwork for relationship support: a run
  now scaffolds every entity its spec names, each as an independent root in its own `Features/<Plural>/` folder
  and namespace with a full CRUD slice (entity, request, configuration, pages, CQRS handlers), all sharing one
  generated `DbContext` that holds a `DbSet` per entity. Because `ApplyConfigurationsFromAssembly` is
  assembly-wide, every entity's configuration is picked up with no extra wiring — so a multi-entity run needs no
  `--context`. Not yet reachable from the command line: `rask g f Post Title:string 1:n Comment Body:text` still
  refuses, because generating the entities without the relationship between them would silently drop what was
  asked for. A single-entity run is byte-for-byte unchanged.
- **Bootstrap layout primitives: `BsContainer`, `BsRow`/`BsCol`, `BsStack`.** The typed answer to the page
  shell and the 12-unit responsive grid, so a layout no longer means hand-writing
  `Div(Class: "container")` / `Div(Class: "row g-4")` / `Div(Class: "d-flex gap-2")`. `BsContainer` takes
  `Fluid` and `FluidBelow: Bp` (named for the behaviour — Bootstrap's `.container-md` is really the fluid
  one *below* md, capped from md up). `BsRow` takes `Gutter` (`.g-0`…`.g-5`). `BsCol` takes per-breakpoint
  spans that stack exactly as the class names do — `BsCol(Md: 6, Lg: 4)` → `.col-md-6 .col-lg-4` — plus
  `Span` and `Auto`. `BsStack` is a flex row (or `Vertical` column) with `Gap`, `Justify`, `Align` and
  `WrapItems`. **`BsStack` builds on `d-flex`, not Bootstrap's `.vstack`/`.hstack`**: neither shorthand is
  a superset of `d-flex` (`.hstack` also sets `align-items:center`, `.vstack` also sets `flex:1 1 auto`,
  and both add `align-self:stretch`), so building on them would silently restyle any plain `d-flex` they
  replaced — `BsStack(Align: BsAlign.Center)` says that alignment out loud instead. It also means
  responsive direction
  composes (`BsStack(Vertical: true, Class: Flex.Row(Bp.Md))`), which the shorthands can't express at all
  since Bootstrap ships no breakpoint variant of either. The `Grid` utility group gains
  `Container`/`ContainerFluid`/`ContainerBelow(Bp)` and is now documented (it was missing from the group
  table in `docs/bootstrap-utilities.md`), remaining the typed escape hatch under the components. New
  guide `docs/bootstrap-layout.md` with a live demo.
- **`rask deploy` gates the blue-green swap on an HTTP health check.** After the new container reports
  `Running`, deploy now probes it over HTTP (`GET /health` by default) and only reloads Caddy onto it
  once it answers `2xx` — a container that boots but fails its first request (bad connection string,
  failed migration, missing env var) is removed and the previous version keeps serving, instead of
  taking live traffic while broken. The probe is an ephemeral pinned `curlimages/curl` container joined
  to the target's network namespace, so it works in domain mode (no published port) and needs no HTTP
  client in the app image. Apps scaffolded with `rask new` now ship an ASP.NET Core `/health` endpoint
  (`AddHealthChecks()` + `app.UseHealthChecks("/health")`, mapped before `UseHttpsRedirection` so the
  internal probe gets a plain-HTTP `200`). Customize the path with `--health-path <path>` or skip the
  probe with `--no-health-check` (both remembered in `.rask/deploy.json`). **Behavior change:** an
  existing custom app without a `/health` endpoint should deploy with `--no-health-check` (or point
  `--health-path` at its readiness route); the failure message says so. Documented in `docs/deployment.md`
  and `docs/cli.md`.
- **`rask completion <bash|zsh|fish>` prints a shell completion script.** Generated from the live command
  list and each command's option schema, so it always matches the CLI — new commands and flags are completed
  without a separate list to maintain. Install e.g. `rask completion fish > ~/.config/fish/completions/rask.fish`.
- **`rask generate feature` reads team defaults from `.rask/generate.json`.** Persist a project's preferred
  `--bs` / `--validation` / `--id` / `--tests` (etc.) once and every `generate feature` inherits them;
  explicit flags on the command line always win. `--save-defaults` writes the current run's feature flags
  back to the file. Booleans are opt-in (absent = off), and the file is trim-safe (source-generated JSON).
- **`rask new --dry-run` previews the project plan without writing anything.** Prints the files it would
  create (and skips `dotnet restore`), matching `rask generate --dry-run`.
- **`rask new` with no name starts an interactive wizard.** On a terminal, running `rask new` (no project
  name) now prompts for the name, a numbered template picker, and a yes/no for each feature flag the chosen
  template supports — then scaffolds exactly as if you'd typed the flags. The answers flow back through the
  same validation and generation path, so nothing new can drift. **Non-interactive is unchanged**: when
  stdin is piped/redirected (scripts, CI), a missing name is still the same hard error, so automation is
  unaffected. Backed by a new dependency-free `Prompt` helper (Ask/Confirm/Select), EOF-safe so a command
  can never hang.
- **The `rask` CLI now speaks in color, with consistent output and progress feedback.** Written files
  are reported with one shared green `+ <path>` marker across `rask new` and `rask generate` (previously
  `  + path` vs `Created path`); action headings are bold, warnings are yellow, deploy failures are red,
  and "Deployed. The app is live at …" is green. Otherwise-silent long operations — the `rask deploy`
  readiness poll (up to ~20s) — now animate a spinner. All of it is **terminal-aware**: color and the
  spinner switch off automatically when output is piped/redirected or `NO_COLOR` is set, so scripts, CI
  logs, and captured output are byte-for-byte unchanged. Pure BCL — no new dependencies.
- **`rask <command> --help` now teaches — an aligned options table, arguments, and examples.** Every command's
  help was three lines (summary + one usage string); it now renders a described **Options** table straight from
  the same schema that parses the arguments (so it can never drift), a **Arguments** section, and copy-pasteable
  **Examples**. This surfaces `rask generate`'s previously **undiscoverable** feature flags — `--bs`, `--modal`,
  `--soft-delete`, `--concurrency`, `--events`, `--outbox`, `--tests`, `--validation`, `--no-restore` — grouped
  under "Feature options". Output is colorized when writing to a terminal and stays plain when piped or when
  `NO_COLOR` is set (so `rask info | cat` and CI logs are unaffected). Parse errors now point at
  `rask <command> --help`. No new dependencies — the styling and help layers are pure BCL.
- **Live demos for `BsTable` and `BsPagination`, plus unit tests for `BsBadge`.** The "Cards, lists &
  tables" guide gains a `BsTable` demo (typed style toggles over core `Thead`/`Tbody` markup) and an
  interactive `BsPagination` demo (click a page — the `.active` marker and readout follow, zero-JS). Both
  components were already unit-tested; `BsBadge` (already shown on the buttons demo) now gets a
  rendered-markup unit test too. The shared browser E2E journey asserts the table rows, drives the
  pagination page-click, and checks a badge renders — closing the last `Bs*` demo/test-coverage gaps.
- **Live demos and unit tests for `BsOffcanvas` and `BsConfirmDialog`.** The two interactive overlay
  components — previously documented in prose only — now each have a showcase demo (code-above /
  live-result-below) on the "Modals, offcanvas & dropdowns" guide: an offcanvas drawer that slides in
  over a backdrop, and a `BsModal`-backed confirm/cancel prompt with a status readout. `BsOffcanvas`
  gains a rendered-markup unit test, and the shared browser E2E journey now drives both (open the
  drawer + backdrop-dismiss it, and confirm the destructive-delete dialog).
- **`Rask.Cache` — a developer-facing cache on the app's own database.** The roadmap's next DB-backed pillar:
  implements the standard `IDistributedCache` (so it drops into ASP.NET session state and output caching) plus a
  typed `ICache` with read-through `GetOrCreateAsync<T>`, backed by a `CacheEntry` table — no broker, no Redis.
  Entries carry absolute and sliding expirations; a read renews a sliding entry and evicts an expired one
  lazily, and a hosted `CachePurger` sweeps expired rows. The typed JSON layer has trim-safe `JsonTypeInfo<T>`
  overloads. Wire via `AddRaskCache<AppDbContext>()` + `modelBuilder.AddRaskCache()` + `rask db add AddCache`.
  New `/cache` slice in `Rask.Example.EfCore` (with a Playwright E2E). Docs: `docs/cache.md`.
- **CI now packs `Rask.Jobs`, `Rask.Mail`, and `Rask.Cache`** in the release and nightly workflows (the Jobs and
  Mail pillars were previously built but never packed/published).
- **`Rask.Mail` — durable transactional email on the app's own database.** The roadmap's next DB-backed
  pillar: compose an email with a fluent `Email` builder — its body is a **Rask component rendered to HTML**
  (`Body(new WelcomeEmail(name))`) — call `IMailQueue.SendAsync(email)`, and a hosted `MailProcessor` delivers
  it off the request thread over SMTP (MailKit) — **at-least-once**, with exponential-backoff retries up to
  `MaxAttempts` (then a dead letter kept for inspection) and a retention purge. Rides the existing SQLite
  database (no broker, no Redis) with a single hosted poller per app; the message is persisted fully rendered,
  so nothing is rehydrated at send time. **Zero-config in development** — with no SMTP configured, mail is
  logged, or point `PickupDirectory` at a folder to write `.eml` files. Delayed send via
  `ScheduleAsync(email, delay)`; swap in a custom `IMailSender` (e.g. a provider API) by registering it before
  `AddRaskMail`. Wire with `services.AddRaskMail<AppDbContext>(o => { o.From = …; o.Smtp = …; })` +
  `modelBuilder.AddRaskMail()`, then `rask db add AddMail`. Documented in `docs/mail.md`.
- **`rask generate email <Name>` (alias `rask g e`).** Scaffolds an email-body component under `Emails/` (a
  Rask `Component` rendered to HTML by `Email.Body(...)`), adds the `Rask.Mail` package, and prints the
  `AddRaskMail` / `modelBuilder.AddRaskMail()` / `rask db add AddMail` registration steps — mirroring
  `rask generate job`.
- **`Rask.Example.EfCore` gains a mail demo.** A new `/mail` slice queues a message through `IMailQueue` on the
  same SQLite database the catalog uses and delivers it — with no SMTP configured — to a pickup directory as an
  `.eml` file, wired with `AddRaskMail<CatalogDbContext>` + `modelBuilder.AddRaskMail()` and covered by a
  Playwright E2E test.
- **`ISpeechRecognition` — typed speech recognition / dictation with native iOS/Android backends.** The
  counterpart to `ISpeechSynthesis`: `StartAsync(onResult, options)` prompts for the microphone and streams
  each recognised phrase (final, and with `InterimResults` the live hypotheses) to the callback, returning an
  `IAsyncDisposable` that stops listening. `SpeechRecognitionOptions` sets `Lang`, `Continuous`, and
  `InterimResults`. A Core-tier wrapper, so it works on **both transports**; browser support is Chromium-only,
  but in the [native shell](docs/native.md) it upgrades to a real OS backend the WebView can't provide — iOS
  `SFSpeechRecognizer` + `AVAudioEngine` and Android `SpeechRecognizer` (needs mic permission: iOS
  `NSMicrophoneUsageDescription` + `NSSpeechRecognitionUsageDescription`, Android `RECORD_AUDIO`). Showcased on
  the Browser APIs page and documented in [`docs/apis/speech-recognition.md`](docs/apis/speech-recognition.md).
  The iOS backend honours `Continuous`: without it the session stops after the first final utterance, matching
  the browser and Android contract (`AVAudioEngine` otherwise streams until disposed).
- **`BsDataGrid` column chooser and reordering.** `ColumnChooser: true` renders a "Columns" menu above the
  grid — a checkbox to show or hide each column, and move earlier/later buttons to reorder it — and makes each
  header a drag source so a column can also be dragged onto another to reorder it. Both axes are controlled or
  uncontrolled just like grouping: `HiddenColumns`/`ColumnOrder` (with `On…Change`/`…Async`) carry `Field`-name
  tokens, so a laid-out grid is URL-serialisable (`?hide=region&cols=amount,name,region`) and survives a
  reload. Every action is a real `<button>` or checkbox, so the feature works from the keyboard alone; header
  drag is only a mouse accelerator, and a header dropped on the group panel still groups rather than reorders.
  Hide, reorder and grouped-column folding funnel through one visible-column list, so sort (which tracks a
  column's identity, not its slot), footers and band colspans all follow; a hidden-but-sorted column keeps its
  sort, an explicit hide overrides `ShowGroupedColumns`, and the grid never renders a bodyless table. Columns
  opt out with `BsColumn.Hideable`/`Reorderable`. New **RASK034** warns when a chooser column has no `Field`
  (so it could never be shown or reordered). Documented in `docs/data-grid.md`; the showcase gains a columns
  demo. A grid that uses none of this renders byte-identical markup and allocates the same as before.
- **Live demos and unit tests for `BsBreadcrumb`, `BsListGroup`, and `BsPlaceholder`.** The three static
  content components — previously documented in prose only — now each have a showcase demo embedded in the
  "Cards, lists & tables" guide (`docs/bootstrap-cards.md`, code-above / live-result-below) and a dedicated
  rendered-markup unit test, bringing them to coverage parity with the other `Bs*` components.
- **Live demos and unit tests for `BsCollapse`, `BsSpinner`, and `BsProgress`.** The disclosure and feedback
  guides previously showed these components in prose only. Each now has a showcase demo embedded with its
  source (code-above / live-result-below): `BsCollapse` on the "Tabs, accordion & collapse" guide with an
  interactive toggle, and `BsSpinner`/`BsProgress` on the "Alerts, spinners & progress" guide. Each gains a
  rendered-markup unit test, and both guide pages are now walked by the shared browser E2E journey.
- **`BsSelect` and `BsMultiSelect` gain option groups.** A new `OptionGroup` selector (`item => string`) groups
  the options by the returned key in first-seen order — rendered as `<optgroup label>` in `Native` mode and as
  non-interactive `.dropdown-header` rows in the custom dropdown. Grouping reorders the options into group order
  while keeping keyboard navigation walking that flat visual order (headers are skipped), and composes with
  `Filter` and `OptionDisabled`. The shared group/flatten/cursor logic lives in `BsSelectNav`.
- **`IBattery` — typed Battery Status API with native iOS/Android backends.** Inject it and
  `GetStatusAsync()` reads the charge level + charging state once (or `null` where unavailable), while
  `WatchAsync(onChange)` subscribes to level/charging changes and returns an `IAsyncDisposable`. A Core-tier
  wrapper, so it works on **both transports**; browser support is Chromium-only, but in the
  [native shell](docs/native.md) it upgrades to a real OS backend the WebView can't provide — iOS
  `UIDevice` battery monitoring and Android `BatteryManager`. Showcased on the Browser APIs page and
  documented in [`docs/apis/battery.md`](docs/apis/battery.md).
- **`BsSelect` and `BsMultiSelect` gain per-option disabling, and `BsMultiSelect` gains a "Select all / Clear
  all" header.** A new `OptionDisabled` predicate (`item => bool`) on both controls marks individual options
  non-selectable: the option renders greyed with `aria-disabled`, takes no click, and the keyboard cursor
  skips over it (in `Native` mode it becomes a disabled `<option>`). `BsMultiSelect` also accepts
  `SelectAll: true`, which adds a header row that toggles every shown, enabled option in one click —
  respecting an active `Filter` and never touching a disabled option, in both bound and controlled modes.
- **`BsMultiSelect` is now fully keyboard-operable and reaches listbox accessibility parity with `BsSelect`.**
  The multiselect dropdown previously responded only to Escape; it now has a roving highlight driven by
  **Arrow Up/Down** (with **Home/End** to jump), opens from a closed box on Arrow/Enter/Space seeding the
  highlight to the current selection, and **Enter/Space toggle** the highlighted option's membership while
  leaving the dropdown open (Space still types a literal space in the search field). The listbox is marked
  `aria-multiselectable`, each option is a proper `role="option"` carrying `aria-selected`, and the box
  advertises the highlight through `aria-activedescendant` — matching `BsSelect`'s existing combobox wiring.
  The shared cursor/id logic lives in a new internal `BsSelectNav` helper both controls consume.
- **`Rask.Jobs` — durable background jobs on the app's own database.** The roadmap's #1 DB-backed pillar:
  enqueue a unit of work and a hosted `JobProcessor` runs it off the request thread — **at-least-once**, with
  exponential-backoff retries up to `MaxAttempts` (then a dead letter kept for inspection). A job is a
  `Rask.Cqrs` command (`record SendWelcomeEmail(Guid Id) : IJob`) handled by an ordinary
  `ICommandHandler<TJob>`; inject `IJobQueue` and `EnqueueAsync(job)` or `ScheduleAsync(job, delay)`. Supports
  durable **interval-recurring** jobs (`o.AddRecurring<T>("name", every, () => new T())`, tracked so a restart
  never double-runs them) and a retention purge of completed jobs. Rides the existing SQLite database (no
  broker, no Redis) with a single hosted poller per app, and a source generator registers each `IJob` type for
  reflection-free rehydration. Wire with `services.AddRaskJobs<AppDbContext>()` + `modelBuilder.AddRaskJobs()`,
  then `rask db add AddJobs`. Scaffold one with **`rask generate job <Name>`** (alias `g j`). Complements
  `Rask.Outbox` (transaction-derived events) — jobs are work you explicitly schedule. Documented in `docs/jobs.md`.
- **`BsTimePicker` keyboard parity — `Home`/`End` and a seconds nudge.** Rounding out the picker keyboard
  story: `Home`/`End` jump the clock to the earliest/latest selectable time (the `Min`/`Max` bound, or the
  day edge — `00:00`, and `23:59`/`23:59:59` with seconds), and when `Seconds` is on, `Shift`+`ArrowUp`/
  `ArrowDown` nudges the second by `SecondStep` (plain arrows stay on the minute, `PageUp`/`PageDown` on the
  hour). Every nudge still clamps to `[Min, Max]`. Documented in `docs/bootstrap-pickers.md`.
- **`IWebLocks` — typed Web Locks API for coordinating work across an origin's tabs and workers.** Inject it
  and `RequestAsync(name, work)` waits for a named lock, runs your callback while holding it, then releases —
  even if the callback throws; `TryRequestAsync` uses `ifAvailable` (returns `false` without waiting when the
  lock is held, so it's a natural "leader tab" election), and `QueryAsync` snapshots held/pending locks. A
  Core-tier wrapper, so it works on **both transports** (no user gesture needed). Showcased on the Browser APIs
  page and documented in [`docs/apis/web-locks.md`](docs/apis/web-locks.md).

### Documentation
- **Split the eight largest guides into focused pages.** The oversized narrative guides — `authentication`
  (787 lines), `forms` (588), `native` (532), `architecture/live-rendering` (468), `composition` (426),
  `js-interop` (411), `browser-apis` (411), and `data-grid` (405) — were each split along their H2 seams into
  a slim hub plus focused sub-pages (14 new pages), so no guide is a wall of text. Each **original slug stays a
  hub** with an "On this page" index, so every existing inbound link still resolves; anchored links that
  pointed into moved sections were repointed to the new sub-pages. All 14 sub-pages are in the guides catalog
  (the reverse-parity guard enforces it), and every moved live demo still mounts.
- **The landing site leads with the One Person Framework.** `samples/Rask.Example.Site` reframed its hero
  from "web and native apps in pure C#" to the OPF thesis — *Ship a whole product. Just you, and C#.* — and
  added a "One person's whole back end" section covering the DB-backed pillars (generate feature, background
  jobs, transactional email, outbox, cache, production SQLite, one-command deploy, Web Push). The three-hosts
  and Rask-vs-Blazor sections are unchanged; the front door now sells what the docs sell.
- **Every doc is now on the docs site.** The showcase guides catalog surfaced only a curated ~37 of the
  60 top-level `docs/*.md`, and none of the subfolders. It now surfaces **every** `docs/**/*.md` — the OPF
  doctrine, the full 10-chapter tutorial, all the back-half pillars (`cli`/`data`/`cqrs`/`jobs`/`mail`/
  `cache`/`outbox`/`sqlite`/`deployment`), the 46 browser-API reference pages, and the contributor/internals
  docs — grouped in an OPF-led order (Start here → Tutorial → One Person Framework → … → Browser API
  reference → Contributing & internals). Subfolder docs are embedded via a recursive glob with a bare-leaf
  slug (matching the in-doc link rewriter), and a new reverse-parity test fails the build if a doc is ever
  added to the repo without a catalog entry — so the site can't silently hide a doc again.
- **A dedicated Web Push guide, and a fuller outbox guide.** New [`docs/webpush.md`](docs/webpush.md) documents
  the shipped `Rask.WebPush` pillar as a first-class guide — `VapidKeys.Generate()`, `AddRaskWebPush(...)`,
  storing the client `PushSubscription`, `IWebPushSender.SendAsync` and acting on the `WebPushResult`
  (`ShouldDelete`/`ShouldRetry`), the default-service-worker payload shape and the `RawPayload` escape hatch —
  and is wired into the docs index, the roadmap, the One Person Framework batteries table, the guides catalog,
  `llms.txt`, and a pointer from `pwa.md`. [`docs/outbox.md`](docs/outbox.md) gains an ordering/idempotency note
  and an explicit "outbox vs. jobs" pointer to round it out to the depth of its sibling pillar guides.
- **OPF positioning fixes and independent-brand cleanup.** Removed every third-party-framework reference from
  user-facing surfaces — the showcase guides index no longer says it "reads like a Rails guide", the SQLite
  showcase page and the whole `Rask.SQLite`/`Rask.SQLite.EntityFrameworkCore` published metadata (package
  `Title`/`Description`/`PackageTags`, both `NUGET.md`s, and the XML-doc comments) now describe the tuned
  production pragma set and the fair-interval busy handler on their own terms (the two upstream PR links are
  kept as bare provenance URLs), and the "flash" toast analogies across Core/Server/WASM/Bootstrap are stated
  independently. Also corrected drifted docs: the docs index and guides catalog named `Rask.TestSupport`
  (the in-repo helper) for the shipped **`Rask.Testing`** unit-testing package; the One Person Framework
  batteries table now lists Jobs/Mail/Cache/Outbox (previously prose-only); the cheat sheet's
  `AddDbContextFactory` one-liner now includes the required `.AddInterceptors(...)`; and the roadmap's auth
  row matches the doctrine ("cookie & JWT"). The root README trims the four hero links to one and shows a
  badge for every published NuGet package.
- **A cheat sheet and a recipes cookbook for faster day-to-day DX.** New [`docs/cheatsheet.md`](docs/cheatsheet.md)
  puts every CLI command, feature field token, `AddRask…` wiring one-liner, and code idiom on one scannable
  page; new [`docs/recipes.md`](docs/recipes.md) answers "how do I do X?" (add a feature to an existing
  database, gate a page, run a job, cache a query, deploy an update, …) with the command, the wiring line, and
  links to the reference + the tutorial chapter that teaches it. Both are linked from the docs index, and each
  pillar reference (`jobs`/`mail`/`cache`/`outbox`/`data`/`sqlite`/`authentication`/`deployment`/`cli`) now
  carries an "In practice" pointer back to its tutorial chapter and recipe, so every entry point leads to the
  others. Also corrected two drifted lines: the Chapter 1 goal box (`rask new Shop --auth --docker`) and the
  CLI reference (`rask generate` now **writes** the DI registration into `Program.cs`, printing only as a
  fallback).

### Changed
- **Renamed `Rask.Data`'s `AggregateRoot<TId>` to `Entity<TId>` (BREAKING, pre-1.0).** The base class every
  `rask generate feature` entity inherits is now `Entity<TId>`. The old name claimed an aggregate boundary
  the type never enforced and the scaffolder never produced — every generated entity is an independent root
  with its own `DbSet` and CRUD slice, so "aggregate root" described an intent, not a behaviour. `Entity<TId>`
  says what it is: a persisted domain entity with an identity. The namespace (`Rask.Data`), the members
  (`Id`/`CreatedAt`/`UpdatedAt`/`Raise`/`DomainEvents`/`ClearDomainEvents`), the marker interfaces
  (`ISoftDeletable`, `IVersioned`, `ITimestamped`, `IHasDomainEvents`), and every interceptor are unchanged —
  this is a pure rename with no behaviour change. **Migrate by replacing the type name**
  (`: AggregateRoot<Guid>` → `: Entity<Guid>`); no alias is shipped, so the old name will not compile.
- **The live showcase moved from `/demo/` to `/docs/` on the GitHub Pages site, and the landing page now
  leads with it as "Docs".** The showcase (`Rask.Example.Wasm`) is guides-first — the repo's `docs/*.md`
  rendered on-site with the interactive demos embedded inline — so it *is* the documentation. The marketing
  landing page's "Demo" nav entry is renamed "Docs" and points at that showcase; the redundant external
  "Docs → GitHub markdown folder" links were removed. The Pages workflow now publishes the showcase to
  `/<repo>/docs/` (and boots it as the 404 deep-link fallback), and every `live demo` link across the
  README, `NUGET.md`, and the guides now targets `/docs/`.

### Security
- **`rask deploy` now validates the SSH host before it reaches the `ssh` binary.** `ssh` can't tell a
  destination from an option, so a "host" of `-oProxyCommand=<cmd>` is read as a flag and runs `<cmd>` on
  the machine invoking rask. Because the host is remembered in `.rask/deploy.json` — which is committed
  and read by CI — a hostile value merged by pull request would have executed on any maintainer who ran
  `rask deploy`, or on a runner holding the deploy secrets. Hosts that would parse as an option (or that
  contain whitespace/control characters) are now rejected up front with a clear message, and the
  destination is additionally passed after `--` so ssh can't reinterpret it. Found while reviewing the
  new host-setup path; the same hardening covers the pre-existing `docker -H ssh://…` host.
- **`rask generate feature` no longer scaffolds a project carrying a known high-severity vulnerability.** The
  generated slice references `Microsoft.EntityFrameworkCore.Sqlite`, which pins the `SQLitePCLRaw` 2.1.11 family;
  its `lib.e_sqlite3` bundles SQLite 3.49.1, vulnerable to
  [CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) (memory corruption). `generate feature` now
  also adds `SQLitePCLRaw.bundle_e_sqlite3`, whose 3.x family drops the vulnerable package entirely in favour of
  `SourceGear.sqlite3` — a **direct** reference being the only lever that lifts a transitive pin. Every project in
  this repo that touches EF Core Sqlite already carried that reference; the scaffolder was missed when the CVE was
  closed, so the framework was clean while everything it generated was not. Existing scaffolded projects can add
  the package themselves: `dotnet add package SQLitePCLRaw.bundle_e_sqlite3`.

### Fixed
- **Docs correctness pass across every page.** Getting-started's §2–§3 "tour" no longer describes a
  Counter/Weather starter the template stopped generating (it now matches the real single-welcome-page
  scaffold); fixed broken/stale cross-links and anchors (best-practices → getting-started sections,
  `browser-apis` self-anchors, `data-grid`/`routing` `/table` and query-param links, mislabeled `Rask.Data`
  links that pointed at `data-access.md`); reconciled the Web-API wrapper count (46) and the diagnostic range
  (RASK001–034) across `docs/README.md`, `README.md`, and `llms.txt`; corrected the `mail.md` email-body
  base type (`Component`, not `Element`) and stale `rask-*` template ids; and reorganized the docs index to
  group the DB-backed One-Person-Framework pillars into their own section. Also removed Ruby/Rails framing
  from user-facing docs in favor of .NET-native wording, and added a "Why one server, no PaaS" section to
  `sqlite.md`.
- **The capability matrix no longer claims `IFileSystemAccess` and `IWebPush` work on Native.**
  `docs/browser-capabilities.md` marked both ✅ in the Native column, but neither API exists in the WebView:
  `window.showOpenFilePicker` is `undefined` on WebKit (the File System Access API is effectively
  Chromium-desktop-only), and `window.PushManager` is `undefined`, so there is nothing to subscribe with
  (service workers do register — push specifically is missing). Both are now ⬜ with a note pointing at the
  alternative (`<input type="file">`) and the tracked APNs/FCM follow-up. `INotifications` is unaffected — it
  has a native backend, so local notifications work on device. No code changed: these APIs never worked on
  Native, the matrix just said they did. The on-device Appium suite now asserts the app origin is a secure
  context and prints what each WebView-only wrapper actually resolves to, so the Native column has evidence
  behind it instead of assumption. `docs/native.md` also documents *why* both origins are secure contexts —
  Android by its `https` scheme, iOS because WebKit treats a custom `WKURLSchemeHandler` scheme as
  trustworthy whatever the host, which is easy to get wrong when writing a custom `INativeWebView`.
- **`rask deploy --help` stated the wrong default for `--health-path`.** It read `(default: /)` while the
  probe actually uses `/health` (as `docs/cli.md` documents), so anyone trusting `--help` would think an
  app without a root-path readiness route needed the flag when it didn't.
- **`rask generate page --no-restore` (and any other feature-only option) is now rejected instead of silently
  accepted.** The guard that keeps feature options off a page/component/job/email was a hand-kept list that had
  drifted from the option schema, so `--no-restore` — declared as a feature option — slipped through and did
  nothing. The check now derives from the schema's own grouping, which closes the drift for good. The message
  also names only the options you actually passed (`--no-restore only applies to 'generate feature'.`) rather
  than reciting all thirteen.
- **`rask new --template wasm --auth` scaffolded a project that couldn't build.** The wasm auth scaffold pins
  `Microsoft.JSInterop` / `Microsoft.AspNetCore.Authorization` itself (a browser-wasm app has no
  `Microsoft.AspNetCore.App` framework reference to supply them), but the pinned version had drifted to `10.0.9`
  while `Rask.Wasm` — which references the same two packages — moved to `10.0.10`. That put the generated project
  *below* its own dependency, so NuGet reported a package downgrade (`NU1605`): a warning on a plain build, and a
  hard error under `-warnaserror`. The scaffold now pins the same version the repo does, and a test holds the two
  in sync so they can't drift apart again. The build gate missed this because it compiled generated projects
  against the latest *published* packages (whose deps were still `10.0.9`) rather than the ones in the same
  commit — it now uses a local feed packed from the repo, so a break surfaces in the commit that causes it.
- **`Rask.Outbox` now delivers nested `IOutboxEvent` types.** The source generator registers each event by
  its dot-separated display name, but `OutboxSerializerRegistry.Serialize` stored `Type.FullName` — which
  uses `+` between a nesting type and a nested type — so a nested event (a record declared inside a class)
  was stored under a name the registry never had: it deserialized to `null` and was silently left unpublished
  until it exhausted its attempts. `Serialize` now normalizes the name to match the generator. (Top-level
  events were unaffected.)

### Fixed
- **`BsDataGrid` group panel: dragging a chip out of the panel now ungroups that level.** The gesture the docs
  describe was wired to `dragstart`/drop but never to `dragend`, so releasing a chip on empty space did nothing
  and the only way to remove a level was its `×` button. The chip's `dragend` now runs the drag-out handler —
  a no-op after a real drop (which already consumed the drag), an ungroup when the chip was released on nothing.

## [0.18.0] - 2026-07-16

### Changed
- **The `rask` CLI now owns all scaffolding — `rask new --template wasm-hosted` is generated directly, and the
  `Rask.Templates` package is discontinued.** `wasm-hosted` was the last template still shelling out to
  `dotnet new`; it now emits its files directly like `server`/`wasm`/`native`, so `rask new` no longer depends
  on `Rask.Templates` at all. The generated hosted app is the idiomatic three-project trio — **`{Name}.Client`**
  (the browser-WASM SPA), **`{Name}.Server`** (the ASP.NET host you run and deploy), and **`{Name}.Shared`** (a
  class library both reference; with `--auth`, the `LoginRequest`/`MeDto` contracts live here instead of being
  duplicated). `rask new` restores the generated solution, and `rask info` drops its "Rask templates installed"
  row (the CLI *is* the scaffolder). **BREAKING:** the `Rask.Templates` NuGet package and its `dotnet new rask-*`
  templates are no longer published — scaffold with `rask new [--template server|wasm|wasm-hosted|native]`
  instead (install the CLI once with `dotnet tool install -g Rask.Cli`). Docs, README, `llms.txt`, and the site
  install tabs updated to the `rask new` flow.

### Added
- **`rask deploy` — one-command deploy to a single host over SSH.** Builds the app's Docker image on a
  remote box and runs it, with no registry, no local Docker daemon, and no image tarball: every step is
  `docker -H ssh://<host> …`, so the build context ships to the host's daemon and builds there. It deploys
  the `--docker` Dockerfile the templates scaffold. With `--domain` it runs a shared **Caddy** reverse
  proxy that obtains an automatic **Let's Encrypt** certificate — one command to a live HTTPS site — and
  deploys are **zero-downtime** (blue-green: start the new container, health-check it, reload Caddy, then
  retire the old; a failed start leaves the previous version serving). **Multiple apps share one host**:
  each container is labelled and the proxy's routing is regenerated from the host's live containers every
  deploy, so a second `--domain` never disturbs the first. Without `--domain` it publishes `--port`
  (default 8080) for your own reverse proxy. `--host`/`--domain`/`--port` are remembered in
  `.rask/deploy.json` (never secrets — pass those via `--env`/`--env-file`); `--dry-run` prints the exact
  docker commands. Documented in `docs/cli.md` and `docs/deployment.md`.
- **Date/time pickers gain keyboard navigation, seconds, time ranges and localizable chrome.** The
  `BsDatePicker`/`BsTimePicker`/`BsDateTimePicker` calendar and clock are now fully keyboard-operable
  (the WAI-ARIA combobox + grid pattern the docs already described but never implemented): a first nav
  key opens the popover, then arrows move a day/week, `PageUp`/`PageDown` a month (`Shift` a year),
  `Home`/`End` the week edge, and `Enter` selects the navigated day — with `aria-activedescendant`
  tracking the cursor while the box keeps focus. `BsTimePicker` takes `Min`/`Max` (`TimeOnly`); both time
  pickers take `Seconds`/`SecondStep` to add a seconds column, and the date-time picker greys out-of-range
  time items on a boundary day. A new `Labels` (`BsPickerLabels`) parameter translates the month-nav
  buttons, the time-column headings and the clear button — the chrome that has no `CultureInfo` source.
  Documented in `docs/bootstrap-pickers.md` with a new keyboard table; showcased in the pickers guide.
- **`rask db` — EF Core migrations from the CLI.** A friendly wrapper over `dotnet ef` for the everyday
  migration lifecycle, pairing with what `rask generate feature` scaffolds: `rask db add <Name>`,
  `rask db remove`, `rask db list`, `rask db update [<target>]`, and `rask db drop [--force]`. It resolves
  the target project itself (the single `.csproj` at or above the working directory; override with
  `--project`/`--startup-project`, or select a `--context`), forwards anything after `--` to `dotnet ef`
  verbatim, and installs the `dotnet-ef` global tool on first use if it's missing. When the startup
  project doesn't reference `Microsoft.EntityFrameworkCore.Design` (which the tools require) it adds the
  package for you rather than leaving EF's terse message. Closes the gap between scaffolding an EF-backed
  feature and having a live schema. Documented in `docs/cli.md`.

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

### Changed
- **Breaking: `BsDataGrid` now hides a grouped column by default.** A grouped column holds the same value for
  every row in its band, and the band header already names it (`Region: EMEA (4)`), so repeating it in-row was a
  column of duplicates. While a column is grouped its header, cells, subtotal and footer are now dropped and the
  band-header/detail-row colspans shrink to match; its ungroup control lives on the panel chip. Set the new
  `ShowGroupedColumns: true` to restore the previous behaviour (the value in the band header **and** repeated
  down every row). Grouping still orders, bands and subtotals exactly as before. Documented in
  `docs/data-grid.md`; the grouping demo gains a "Show grouped column" toggle.
- **The showcase page, layout and guide suites render through `Rask.Testing`.** They called the internal
  `RenderAsLiveRoot(services)` straight on a page component; they now use
  `RaskTest.Render(new SomePage(), services).Html`, which is the same thing a consumer writes. Test-only;
  84 tests before and after, and the suites still catch a layout that drops its `navbar-brand`.
- **The lifecycle demo suites drive mount/unmount through `Rask.Testing`.** The five suites that assert on
  lifecycle hooks (`LiveTicker`, `LifecycleProbe`, `CancellationProbe`, `DisposableTimerProbe`, `MetricsView`)
  now render with `RaskTest.Render`, and unmount by having their factory return `null` instead of flipping a
  flag on a test-only host wrapper. This confirms in a real suite what the package documents: a `null` factory
  result drives a generated-factory child through its full unmount path — `OnUnmount`, `OnUnmountAsync`,
  subscription teardown and cancellation all fire. Test-only; 22 tests before and after, and the suites still
  catch a component that forgets to unsubscribe on unmount.
  `LiveTickerTests` loses a self-referencing `LiveHost? host = null; host = new LiveHost(… host!.Log.Add …)`
  knot that existed only because its lifecycle log lived on the host wrapper — the test owns the log now.
- **The showcase demo suites drop their copy-pasted markup helpers for `Rask.Testing`.** Five files each kept
  their own `Empty()` / `Json()` / `ClickIds()` / `HandlerIds()` — the same helpers, written five times, over
  the same markup. `InvokeAsync`'s `"{}"` payload default deletes every `Empty()` outright, and
  `Markup.Attrs` / `page.HandlerIds(evt)` replace the hand-rolled scanners. Test-only; 44 tests before and
  after, and the suite still catches a toast that won't dismiss.
  One helper deliberately survives: `FormControlsDemoTests.ClickIds(html, cssClass)` filters handlers by the
  CSS class on their element, which is *structural*. `Markup` reads attributes, not structure, and that line
  is the same one that made a CSS-selector API the wrong shape for this package — so the honest outcome is to
  leave the structural helper where it is rather than grow the package toward a DOM.
- **`Rask.Core`'s Forms suite drives its handlers through `Rask.Testing`'s public API.** All eight files that
  rendered and dispatched — the canonical `StubComponent` + `RenderAsLiveRoot` + `Markup.Attr` +
  `JsonDocument.Parse` + `TryInvokeHandlerAsync` shape — now read as `RaskTest.Render(() => Form(m)[…])` +
  `await page.ChangeAsync("{\"value\":\"…\"}")`, 232 lines shorter. `NestedBindingValidationTests`' hand-rolled
  `ExtractAttrAfter` (which existed only to find the *second* input's handler) is deleted in favour of
  `HandlerIds("input")[1]`. Test-only; 258 tests before and after, and the suite still catches an `EditContext`
  that stops marking fields modified.
  Two tests keep the internal entry points, deliberately and with the reason in the code: they install a render
  handle so the dispatcher's mid-await render produces a real cached subtree, and that cache is what they exist
  to pin. A render handle is a live-session mechanism, below the HTML + dispatch seam the package covers, so
  it is not something `Rask.Testing` should grow.
- **The validation suites are the first to run on nothing but the shipped package.**
  `Rask.Validation.{DataAnnotations,FluentValidation}.Tests` now use `RaskTest.Render` + `EditContextProbe`
  instead of `StubComponent`/`RenderAsLiveRoot`/`ContextCapture`, and they no longer need `Rask.TestSupport`
  **or** `Rask.Core`'s internals — both `InternalsVisibleTo` entries are removed. They reference the package
  and the validator under test, exactly like an app author's test project would, which is the proof that a
  Rask validation package is testable from outside.
  Their `RegisterValidator` helper used to hand-push an `EditContextScope` — a mechanism that is `internal`,
  that only `Form` ever pushes, and that therefore no consumer could reach. It now renders a real `Form`, so
  the suites cover the actual registration path rather than an approximation of it. Test-only; no assertion
  changed and the suites still catch a validator that drops its service snapshot.
- **The `BsDataGrid` suites drive their clicks through `Rask.Testing`'s public API.** They were the proof that
  the package's first-match-only helpers weren't enough: each of the four files carried its own copy of a
  `Regex` that scraped handler ids out of the markup. They now use `grid.HandlerIds("click")[n]` (and
  `Markup.Attrs` for markup held as a string), and the duplicated helper is gone. Test-only; no assertion in
  the diff changed, and the suite still catches a deliberately broken sort.
- **Rask's own test helpers build on `Rask.Testing` instead of duplicating it.** `Rask.TestSupport`'s
  `Markup.Attr` was a character-for-character copy of the package's scanner — two implementations of one
  algorithm, already at risk of drifting apart. The duplicate is deleted: `Markup.Attr`/`Attrs` now come from
  the shipped `Rask.Testing`, putting the public API on the compile path of every suite that uses TestSupport.
  What remains there is what the package deliberately doesn't ship — the `Assert`-calling and live-wire-payload
  helpers — renamed to `MarkupAssert` (`RequireAttr`, `SessionId`, `FirstHandlerId`), because two classes named
  `Markup` in scope together make every unqualified use ambiguous. Test-only; no shipped behaviour changes.

### Fixed
- **`Rask.SQLite`'s raw immediate-transaction path is hardened against pooled-handle reuse, and its
  failures are now diagnosable.** `ExecuteInImmediateTransactionAsync` drives `BEGIN`/`COMMIT`/`ROLLBACK`
  through the pooled native `sqlite3` handle, outside Microsoft.Data.Sqlite's transaction bookkeeping. It
  now clears a leaked transaction before `BEGIN IMMEDIATE` (a handle that arrived mid-transaction would
  otherwise hit a non-retryable `SQLITE_ERROR`) and, via a `finally`, never returns a mid-transaction
  handle to the pool. When a statement genuinely fails, the thrown `SqliteException` now carries the
  extended result code and the autocommit state (e.g. `SQLite Error 1 (errcode 1, extended 1, autocommit
  1): '…'`) instead of the opaque `SQLite Error 1: 'not an error'` a bare exec code plus `errmsg` produced
  when a pooled handle's returned code and error slot disagreed. The retry classifier now compares the
  primary result code so an extended `BUSY`/`LOCKED` variant is still waited out rather than misread as fatal.
- **`BsDataGrid`'s pager prev/next buttons had no accessible name.** They render an icon-only child (a
  decorative, `aria-hidden` `BsIcon` chevron), so a screen reader announced two unlabelled buttons on every
  grid with `PageSize > 0` — the numbered items were fine (their text names them), which made it easy to miss
  until you tabbed to the arrows. `BsPageItem` now takes an `Aria` bag (matching `BsButton`/`BsTable`), and the
  grid's arrows carry `aria-label="Previous page"` / `"Next page"`. A disabled arrow keeps both its name and its
  `aria-disabled` state.
- **The playground accumulated an empty `.pg-code-host` per full-HTML frame.** `PlaygroundView` put
  `data-rask-managed` on the editor host div it *also renders* — inverting the marker's contract (it flags
  nodes that are live but absent from the render payload). The live-diff `morph` filtered the marked host out
  of the existing DOM but not the incoming tree, so it appended a fresh empty host every full-HTML frame,
  unbounded for the tab's life. The marker now goes on Monaco's own library-created child nodes (where it
  belongs — the same placement the Gantt wrapper uses), and `rask-morph.js` is hardened to ignore
  `data-rask-managed` on a node that *is* in the incoming tree (always a misuse), so the mistake now fails safe
  instead of duplicating silently. The playground holds a single host again.
- **`ShowcaseLayoutTests.OnMount_SubscribesToRouteChanged_ActiveLinkRefreshesOnNav` didn't pin the
  subscription it was named for** — it passed with `route.Changed += OnRouteChanged` deleted. It drove two full
  root renders and asserted the active link refreshed, but that class is owned by `NavLink`, which subscribes
  to `RouteState.Changed` itself; the layout's subscription actually drives the mobile drawer-close and the
  active-group accordion auto-expand. The test now asserts those effects (and goes red when the subscription is
  removed), and a new `Rask.Core.Tests` case pins the underlying framework invariant: a clean subtree that
  depends on untracked external state is served stale from the render cache unless a `Changed` subscription
  marks it dirty.
- **`PackageDependencyTests` crashed instead of running, once `Rask.Templates` had been packed.** The guard
  added alongside the `PrivateAssets="all"` packaging fix scanned `src/` for `*.csproj` with
  `SearchOption.AllDirectories` — which also reaches `src/Rask.Templates/obj/`, where packing copies the
  `dotnet new` content. Every template project was therefore found twice and the name lookup threw
  `An item with the same key has already been added. Key: Company.RaskWasm` before a single reference was
  checked, so the invariant it guards was silently unenforced on any machine that had packed. The scan now
  skips `bin`/`obj` segments: build output is never the thing under test. The guard itself is unchanged and
  still names the offender when `PrivateAssets="all"` is removed from `Rask.Wasm.Hosting`.
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
- **`--bs-primary` now actually themes the Bootstrap components.** Bootstrap 5.3 derives most of a component's
  colours from CSS variables (`.pagination` reads `var(--bs-link-color)`, `var(--bs-body-bg)`, …) but bakes the
  literal hex `#0d6efd` into the part that matters most — the active/checked/selected state. So an app that set
  `--bs-primary` got a brand-coloured surface with a **Bootstrap-blue active page**, blue progress bars, blue
  checked checkboxes (including `BsDataGrid`'s new selection column) and blue focus rings on every input. The
  only workaround was to re-declare each component's variables by hand, with literal hexes, in every app.
  Swept the bundled stylesheet and re-pointed all of it at the runtime variable, in two forms: the custom
  properties on `.btn-primary`, `.btn-outline-primary`, `.pagination`, `.list-group`, `.progress`,
  `.dropdown-menu`, `.nav-pills`, `.accordion` and `.btn-close`; and the **plain declarations** — which no
  variable could reach and so needed real rule overrides — on `.form-check-input:checked`, the focus ring of
  `.form-control`/`.form-select`/`.form-check-input`, `.form-range`'s thumb, and `.nav-link:focus-visible`.
  Shades and tints follow Bootstrap's own ladder (hover = shade 15%, active = 20%, active border = 25%, focus
  border = tint 50%) via `color-mix`, so they track any `--bs-primary`; override `--rask-primary-hover`,
  `--rask-primary-active`, `--rask-primary-active-border` or `--rask-primary-focus-border` to hand-pick one.
  Set `--bs-primary-rgb` alongside `--bs-primary` for the focus rings, which is Bootstrap's own convention.
  The showcase samples accordingly **drop their hand-patched `.btn-primary`/`.btn-outline-primary` blocks** —
  seven copies of the same literal-hex workaround, now unnecessary.
- **A controlled form control whose `OnChange` captured a local silently stopped re-rendering its consumer.**
  `IFormControl<T>.ControlledChangeHandler` resolved the component to notify with a bare
  `Target as Component`. A handler that captures a local *alongside* `this` — `OnChange: v => Rename(i, v)`
  inside a loop, or a data grid's per-row checkbox — is lowered by Roslyn to a compiler display class, so that
  cast returned null and nothing was notified. When the control was also rendered inside a wrapper component
  (the shape every composite table produces: the cells are built by one component and handed to another), the
  fallback owner was the wrapper, so the component whose state actually changed stayed render-cached showing
  stale values, with no error. It now resolves the consumer through `DelegateOwner`, the same
  unwrap-the-captured-`this` rule `RegisterHandler` and `AutoCallback` already applied — and which refuses to
  resolve to an `Element`, so it cannot regress to dirty-marking the control itself.
- **`BsPageItem(Disabled: true)` now says it is disabled.** It only added the `.disabled` class, which greys
  the item and sets `pointer-events: none` — that stops a mouse and nothing else, so a "disabled" page link
  stayed focusable, announced as enabled, and still fired on Enter. The control now carries
  `aria-disabled="true"` (Bootstrap's own documented markup for a disabled page link), on the link/button that
  actually takes focus rather than on the `<li>`. Deliberately `aria-disabled` rather than the `disabled`
  attribute, which would drop focus to `<body>` the moment a page click starts a fetch; callers still guard
  their handlers, which is what `BsDataGrid`'s pager has always done.

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
- **`rask new` scaffolds a minimal project — `App.cs`, `Program.cs`, the csproj and the launch profile.** A new
  app is one file of components, not a folder of samples to delete first: `App.cs` now holds both the shell and
  the routed welcome page, styled with Bootstrap. The demo content every web template used to write —
  `HomePage.cs`/`.css`, `Counter.cs`, `Weather.cs`, `WeatherForecast.cs`, `LocalWeatherForecastService.cs` — is
  gone, along with the generated `README.md` / `AGENTS.md`. The nav bar went with the pages it linked. `--cqrs`
  is now wiring-only (`AddRaskCqrs()` + the package reference, no `Cqrs/` sample slice); `--pwa` and `--docker`
  keep their feature assets (`icon.svg` / `offline.html` / `Dockerfile`), which the manifest, service worker and
  build actually reference. The `native` template drops its `Counter` page and its geolocation backend demo (and
  with it the iOS location usage string and the Android `ACCESS_FINE_LOCATION` permission + runtime request).
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
- **`Rask.Testing`: ships `TestJSRuntime` and `RaskTest.EditContextProbe`.** A component that injects
  `IJSRuntime` was untestable without hand-writing a fake — so everyone wrote the same one (Rask itself had
  four near-identical copies). `TestJSRuntime` records every call (`Calls` in invocation order, `ArgsFor(id)`,
  `CallCount(id)`) and returns what you configure (`SetResponse`/`SetException`); an unconfigured call returns
  `default`, matching a real absent value. It records and replays — nothing more. Adds a `Microsoft.JSInterop`
  dependency, which every consumer already has transitively via `Rask.Core`.
  `RaskTest.EditContextProbe(capture)`, placed inside a `Form`'s children, hands a test the form's
  `EditContext` — the only way to assert validation state (`GetValidationMessages`, `IsModified`,
  `IsValidating`), which never reaches the markup. It's a factory method rather than a constructible type
  because RASK014 makes `new` on a component an error, including on types Rask ships.
- **`Rask.Testing`: `RenderedComponent<T>.Instance` and a non-throwing `TryInvokeAsync`.**
  `RaskTest.Render(component)` now returns a `RenderedComponent<T>` whose `Instance` is the object you passed
  in, so a test can assert the component's own state instead of parsing it back out of the markup. The
  forwarding test root renders that object directly rather than reconciling it, so `Instance` is guaranteed
  to stay the same instance for the handle's lifetime. `TryInvokeAsync(id, json?)` dispatches only if the id
  is still live and returns `false` otherwise — for asserting a handler is *gone*, where `InvokeAsync` throws.
  Source-compatible: existing `Render(x)` calls bind to the generic overload and get a subtype.
- **`Rask.Testing`: query every match — `HandlerIds(domEvent)`, `Attrs(name)`, and a public `Markup`.**
  `HandlerId`/`Attr` reach only the first match, which is useless for a component that wires many elements to
  one event; the workaround was to scrape the markup with a hand-rolled `Regex` (Rask's own `BsDataGrid`
  suites did exactly that). The new list accessors return every match in document order, so
  `page.HandlerIds("click")[1]` drives the second wired element. `Markup.Attr`/`Markup.Attrs` expose the same
  lookups over any HTML string — for markup lifted out of a live payload rather than a `RenderedComponent`.
  Still a dependency-free substring scan, not an HTML parser. Purely additive.
- **`Rask.Testing`: `RaskTest.Render(factory, services?)` renders from a component factory.** The existing
  `Render(component)` overload renders one fixed instance, so a tree built at the call site keeps the values
  it was built with — a re-render can never show changed props. The new overload re-runs the factory on every
  render, which is what a test needs whenever state changes between renders:
  `RaskTest.Render(() => Form(model)[Input(() => model.Name)])`. Returning `null` renders nothing (and, for a
  child built by its generated factory, drives it through its unmount path). Purely additive.
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
- **`BsTable` gained `MaxHeight`, `StickyHeader` and `Aria`.** `MaxHeight` (any CSS length) bounds the
  table's scroll container so a long table scrolls in its own box instead of running down the page, and
  `StickyHeader` freezes the header row while the body scrolls under it — the pair a list screen has always
  had to hand-roll. They go together: a sticky header sticks to its nearest scroll container, so without
  `MaxHeight` there is nothing to stick to. `MaxHeight` implies the wrapper even when `Responsive` is off,
  since a height with no scroll container would only clip. `Aria` passes ARIA attributes through to the
  `<table>` itself, which is what lets a caller mark it `aria-busy` while refetching without the wrapper
  enclosing — and so deferring — any live region rendered beside it. All three are appended and optional, so
  no existing call site moves. `BsDataGrid<T>` forwards them.
- **`BsDataGrid<T>` gained row clicks, conditional row styling and a sticky header.** `OnRowClick` /
  `OnRowClickAsync` raise the clicked row, `RowClass` computes a row's classes from the row itself (the
  overdue invoice, the cancelled order), and `StickyHeader` + `MaxHeight` forward to `BsTable` so a long grid
  scrolls in its own box under a frozen header, with the pager outside it.
  The row click is attached to the **cells** of `RowClickable` columns rather than to the `<tr>`, and
  `BsColumn<T>.RowClickable` defaults to auto: `Value` columns are clickable, `Template` columns are not. That
  is a safety rule, not a style. Rask's client cancels the default action of every click it dispatches, so
  under a handler a checkbox never fires `change`, an `<a href>` never navigates, and a bare `<button>`
  swallows the click instead — all silently. A `Value` cell is plain encoded text and can never hold any of
  them; a `Template` cell is exactly where they live, so it opts out unless you set `RowClickable = true`.
  The grid deliberately adds no `role`/`tabindex` to the row: faking a button on a `<tr>` would destroy the
  row semantics screen readers depend on. A clickable row is a pointer shortcut, so the action needs a real
  control too — the demo pairs it with a button, which is also what proves the cells rule in the browser.
  The per-cell design has a real price, now measured rather than guessed: on a 100-row × 5-column unpaged grid
  `OnRowClick` is 500 handlers, **+45% allocation and ~2× render time** (new `BsDataGridBenchmarks`, which
  gives the grid's render path the same before/after scrutiny as the Core hot paths). Paging it cuts both
  roughly in proportion, and `RowClickable = false` trims further. `RowClass` is free.
- **`BsDataGrid<T>` gained a `Loading` state.** v0.17.0 shipped `OnPageChangeAsync`/`OnSortChangeAsync`, so a
  click could await a database round-trip with no feedback at all and nothing stopping a second click. Set
  `Loading` around the fetch and the grid dims the table behind a spinner, marks it `aria-busy`, and ignores
  further sort/page clicks until it clears. The empty state is suppressed while loading — a fetch in flight is
  not "no results", and the first load would otherwise flash the placeholder before the rows land.
  It is `bool?` on purpose: `null` means the grid isn't using the feature and its markup is unchanged, while
  `false`/`true` mean in-use-idle and fetching. Once in use the grid renders a `position-relative` wrapper in
  **both** states so it never appears or disappears under the table — the live diff matches sibling elements by
  tag name, so a wrapper that came and went would be paired against whatever element sat at its slot. Keeping
  it also preserves the table's DOM identity, and with it focus and scroll position, across a refetch. For the
  same reason the overlay is appended after the pager rather than between the two.
  `aria-busy` goes on the `<table>`, not the wrapper, and the spinner stays outside it: `BsSpinner` renders
  `role="status"`, and a live region inside an `aria-busy` subtree has its announcement deferred until busy
  clears — by which point the spinner is gone and the load was never announced. Controls get `aria-disabled`
  rather than `disabled`, which would drop focus to `<body>` mid-fetch; the handlers guard for real.
- **`BsDataGrid<T>` gained row selection.** `Selectable` adds a leading checkbox column, and
  `OnSelectionChange` / `OnSelectionChangeAsync` report the selection so a toolbar can drive a bulk action;
  `SelectedKeys` takes control of it the same way `Page` and `Sort` do for paging and sorting. Unlike `Sort`,
  "is it set?" is a sound signal here — an empty list is a valid controlled selection meaning *nothing picked*
  — so `null` unambiguously means the grid owns it.
  Selection is tracked **by `RowKey`**, so it follows a row through a sort and accumulates across pages (three
  on page 1 and two on page 2 is five). It reports **keys, not rows**: under `TotalCount` or an `IQueryable`
  the grid only ever holds the current page and cannot turn a key from a page you have left back into a row.
  Re-check reported keys server-side — a key can name a row since deleted, or one this user may not touch.
  Select-all covers **this page**, and says so (`"Select all rows on this page"`), because the page is all the
  grid holds; next to a pager, "select all" would be a lie. It has no indeterminate state — `indeterminate` is
  a JavaScript-only DOM property and this grid renders without any. Row checkboxes are named from their row
  ("Select Espresso Machine") rather than twenty identical "Select row"s, which read as one control repeated.
- **`BsDataGrid<T>` gained a group panel.** `GroupPanel` renders a chip per group level above the grid and a
  group control on every `Groupable` header: drag a header into the panel to group by it, drag the chips to
  renest, drag one out to ungroup. **Every gesture is also a real `<button>`** — the chips carry ungroup and
  move in/out, the headers carry group-by with `aria-pressed` — so the whole feature works from the keyboard
  alone and drag is only an accelerator. That ordering is deliberate: a feature whose primary action is
  drag-only cannot be reached by keyboard at all. Drag state is one field on the grid rather than the
  `DragDrop` primitive, which sets `BypassRenderCache` and would re-execute the whole table's subtree on every
  render for the sake of a panel; the client already `preventDefault`s `dragover` and dedupes the hover
  round-trip to one message per element, so a drag never floods the socket.
- **`BsDataGrid<T>` gained grouping.** Rows band by a column's value — nested, collapsible, with a subtotal per
  band — via `Grouped` / `OnGroupedChange` and `Groupable` columns, plus `GroupCollapsible` and
  `GroupSubtotals`. Controlled and uncontrolled follow `Sort`'s three-way opt-in.
  **`BsColumn<T>.Field` names a column, once.** `Field = d => d.Region` calls it `"region"` — the token
  `Grouped` carries, `OnSortChange` reports, and a URL serialises (`?group=region,rep`). It is an expression,
  so the name is read off the member and cannot drift from the property; `Value` could never supply it, being a
  compiled `Func` with no member name to read. One `Field` also doubles as the `ORDER BY` under `IQueryable`.
  `SortField` and `SortBy` still win where set, so nothing existing moves.
  A band is a run of **consecutive** rows, so the grid **orders by the group keys itself** wherever it owns the
  order — in memory, and by prepending them to an `IQueryable`'s `ORDER BY` — and the user's sort then applies
  *within* each band. Under a `TotalCount` slice it never holds the set and cannot: order by those fields in
  your query, which is exactly what `OnGroupedChange` hands you. `Grouped` is URL input, so unknown or
  non-`Groupable` names are ignored rather than thrown.
  Collapse is keyed by the band's **value path**, never an index, so it follows the band across a sort or page.
  Subtotals reuse each column's `Footer`/`FooterTemplate` over the band's rows — one hook, not two — and, like
  the grand footer in the server-side modes, see only the rows on this page. The band toggle carries
  `aria-expanded` but deliberately no `aria-controls`: it governs a run of sibling `<tr>`s with no element to
  point at, and honouring the id-list would mean minting an id for every row in every band.
  Measured (`BsDataGridBenchmarks`): **+5%** render allocation for one level over 100 rows, **+19%** for two.
  Grouping by a near-unique column is one band per row and costs **+87%** — group by the low-cardinality
  things, which is what a band is for.

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

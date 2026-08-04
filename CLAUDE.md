# CLAUDE.md

Target `net10.0` (`net10.0-browser` for WASM); nullable + implicit usings on. **Rask** is a C#
component framework (Blazor-like): Roslyn factory generator, scoped CSS/JS, routing, live diff
runtime over WS (Server) or JSImport/JSExport (WASM). This file is the **map** — read the code,
the `docs/`, and the tests for depth. Keep this file small; put how-to detail in `.claude/skills/`.

## Workflows → skills (use them automatically)
`.claude/skills/` holds the committed playbooks; apply the matching one without being asked.
- **rask-ship** — definition-of-done gate before any commit/PR: `dotnet format` (.editorconfig) →
  `dotnet build -warnaserror` (analyzers clean) → tests → benchmarks → CHANGELOG → review → PR.
- **add-html-tag** · **add-diagnostic** · **add-codefix** — scaffolding (component+test / RASK0xx+docs+test / IDE quick-fix+test).
- **run-benchmarks** — before/after `Allocated` delta for render-hotpath changes (required evidence).
- **rask-review** — security / performance / memory / .NET-C# review lens (wraps /code-review, /security-review).
- **open-pr** — branch off main, Conventional-Commit, **no AI-attribution footers**, delete branch after merge.
- **cut-release** — CHANGELOG promote + `vX.Y.Z` tag. **check-nuget-updates** — dependency hygiene.

Standing rules: do your best every PR, holding **UX + security + performance** together; prefer
standard .NET APIs (don't reinvent); refactor duplication you touch; unit-test every feature (E2E
only when unreachable); E2E for every `samples/` change — **tests run locally, not in CI**: `dotnet
format` + unit via `scripts/run-unit-local.sh` (enforced by `.githooks/pre-commit`), browser E2E via
`scripts/run-e2e-local.sh` (enforced by `.githooks/pre-push`), on-device native Appium manually
(`tests/Rask.Native.Appium.Tests`, needs an emulator/simulator); benchmark every framework-code change;
**user-facing change → update a sample + docs/README/NUGET.md/llms.txt/template AGENTS.md**; keep
everything up to date; CHANGELOG `[Unreleased]` per notable change; Conventional Commits
(commitlint); no `Co-Authored-By`/`Generated-with`. Build is warnings-as-errors + analyzers
(`Directory.Build.props`; see `docs/code-analysis.md`). Releases: tag→`release.yml`; nightly
prerelease on `main`→`nightly.yml`. AI artifacts: `AGENTS.md`, `llms.txt`, template `AGENTS.md`,
`docs/ai-agents.md`. Full detail: `docs/development-workflow.md`. Ask only when truly blocked.

## Projects
- `src/Rask.Core` — rendering, live context, routing, scoped CSS/JS, lifecycle.
- `src/Rask.Generators` — `Generated.{Type}(...)` factories, `Routes.{Type}(...)`, `[Route]` registration.
- `src/Rask.Server` — ASP.NET host (`AddRask()`/`UseRask<TApp>()`, WS dispatcher). `src/Rask.Wasm` — browser host.
- `src/Rask.Wasm.Hosting` — static-file host for a published WASM bundle. `src/Rask.Wasm.Tasks` — `BakeScopedAssetsTask`.
- `src/Rask.Validation.{DataAnnotations,FluentValidation}` — opt-in validators. `src/Rask.Cli` — the `rask` CLI (owns all scaffolding via `rask new`).
- `src/Rask.WebPush` — opt-in server-side Web Push sender (VAPID + RFC 8291; pairs with `IWebPush`). Zero external deps.
- `samples/` — showcase apps. `tests/` — sibling `*.Tests` per project + `Rask.Examples.E2E.Tests` (Playwright). `benchmarks/`.

## Commands
```bash
dotnet build Rask.slnx
dotnet test Rask.slnx --filter "FullyQualifiedName!~Rask.Examples.E2E"   # fast inner loop
dotnet test Rask.slnx --filter FullyQualifiedName~ATests                 # one class
dotnet run --project samples/Rask.Example.Server
```

## Primitives & rules (the load-bearing invariants)
- `Component` (base: `Render`, `Children`, `Key`, `TagName`, `WriteAttributes`) → `Element` (universal
  HTML attrs). `Text` encodes; `Raw` is verbatim. `Fragment`/`Doctype` special-cased in `HtmlSerializer`.
- **Attribute render order: id, class, style, data-*, role, tabindex, aria-*, then tag-specific — tests assert it; preserve it.**
- Children via the indexer `Div()[Span(), "hi"]` (no `Children:` param; `..` spread breaks — pass enumerables).
  Page root must render the full shell (`Doctype`/`Html`/`Head`/`Body`) — RASK021. Runtime `<script>` auto-appended.
- **Factory params** (generated per public prop): nullable→optional(null); non-nullable no-initializer→**required**
  (RASK001); initializer/`[SkipFactory]`/`Children`→excluded. Inject framework services via the **ctor**, not
  settable non-nullable props (those become required params; `required`+DI ctor→RASK002).
- **`Key`** — reconciliation identity (last factory `Key:` param), enables trusted structural diff; not a reactive prop.
- **Callbacks** are plain delegate props (`Action<T>`/`Func<T,Task>`); the factory wraps them to re-render the
  owning parent. **Refs**: `ElementRef.New()` in a field, pass to `IJSRuntime`. **Context**: `Context.Provide<T>` /
  `Context.Get<T>`/`Required`/`Has`. Construct components via the **factory**, never `new` outside Core (RASK014).

## Subsystems → read `docs/`
Routing/lifecycle (`docs/routing.md`, `docs/lifecycle.md`), scoped CSS/JS + typed browser APIs
(`docs/js-interop.md`, `docs/browser-apis.md` — the 46-wrapper map), forms +
validation (`docs/forms.md`), auth (`docs/authentication.md`), context/callbacks (`docs/composition.md`),
diagnostics RASK001–035 (`docs/diagnostics.md` — analyzer descriptors are the source of truth), getting
started / migration / testing / architecture (`docs/`). Trimming: `samples/Rask.Example.Wasm` must
`dotnet publish -c Release` with zero IL warnings — new reflection needs a DAM annotation or justified suppression.

## Conventions
- **New HTML tag** → `add-html-tag` skill (`src/Rask.Core/Components/{Tag}.cs` + `tests/Rask.Core.Tests/Components/{Tag}Tests.cs`).
- **New diagnostic** → `add-diagnostic` skill. Diagnostic IDs RASK001–035 are documented in `docs/diagnostics.md`.

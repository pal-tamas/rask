# CLAUDE.md

Target `net10.0` (`net10.0-browser` for WASM); nullable + implicit usings on. **Rask** is a C#
component framework (Blazor-like): Roslyn factory generator, scoped CSS/TypeScript, routing, live diff
runtime over WS (Server) or JSImport/JSExport (WASM). This file is the **map** — read the code,
the `docs/`, and the tests for depth. Keep this file small; put how-to detail in `.claude/skills/`.

## Workflows → skills (use them automatically)
`.claude/skills/` holds the committed playbooks; apply the matching one without being asked.
- **rask-ship** — definition-of-done gate before any commit: `dotnet format` (.editorconfig) →
  `dotnet build -warnaserror` (analyzers clean) → tests → benchmarks → CHANGELOG → review → land on main.
- **add-html-tag** · **add-diagnostic** · **add-codefix** — scaffolding (component+test / RASK0xx+docs+test / IDE quick-fix+test).
- **run-benchmarks** — before/after `Allocated` delta for render-hotpath changes (required evidence).
- **rask-review** — security / performance / memory / .NET-C# review lens (wraps /code-review, /security-review).
- **rask-seo** — search + AI-assistant discoverability of rask.sh and the packages (page/guide copy, JSON-LD, sitemap, llms.txt); on EVERY site/docs/package-metadata change.
- **land-on-main** — Conventional-Commit, gated merge of `origin/main`, `git push origin HEAD:main`. **Never open a PR** for own work.
- **cut-release** — CHANGELOG promote + `vX.Y.Z` tag. **check-dependency-updates** — NuGet + Node LTS + the pins outside CPM.

Standing rules: do your best every change, holding **UX + security + performance** together; prefer
standard .NET APIs (don't reinvent); refactor duplication you touch; unit-test every feature (E2E
only when unreachable); E2E for every `src/Rask.Site` change — **tests run locally, not in CI**.
**BOTH HOOKS ARE HELD TO A HARD ONE-MINUTE BUDGET**, at any scope: `.githooks/pre-commit` and
`.githooks/pre-push` each run `scripts/run-unit-local.sh` scoped to what changed (staged files /
`origin/main...HEAD`). When a gate goes over budget the answer is to **make the tests faster, never to
skip or narrow a gate** — a slow gate beats a lying one. Everything that could not fit runs BY HAND:
`scripts/run-all-gates.sh` (browser E2E, CLI build, watch, deploy, meta publish, installer).
**Benchmarks run ONLY when you ask** — `scripts/run-benchmarks-local.sh`, in no hook and no CI;
the public installer is `rask.sh`/`rask.ps1` at the ROOT (published to Pages by `pages.yml`, gated by
`scripts/tests/install-script.test.sh` + `scripts/run-install-e2e-local.sh`, `docs/installation.md`);
**user-facing change → update `src/Rask.Site` + docs/README/NUGET.md/llms.txt/template AGENTS.md**; keep
everything up to date; CHANGELOG `[Unreleased]` per notable change; Conventional Commits
(commitlint); no `Co-Authored-By`/`Generated-with`. Build is warnings-as-errors + analyzers
(`Directory.Build.props`; see `docs/code-analysis.md`). **Every public name obeys
`docs/api-style.md`**; the build records the surface in `src/*/PublicAPI/<tfm>/`, so an unrecorded
public member is a build error (RS0016/RS0017). Releases: tag→`release.yml`; nightly
prerelease on `main`→`nightly.yml`. AI artifacts: `AGENTS.md`, `llms.txt`, template `AGENTS.md`,
`docs/ai-agents.md`. Full detail: `docs/development-workflow.md`. Ask only when truly blocked.

## Projects
- `src/Rask.Core` — rendering, live context, routing, scoped CSS/TypeScript, lifecycle, AND the whole
  HTML/SVG element family (`Div`…`Svg`, `Doctype`) in `Rask.Core.Components`. `IsPackable=false`,
  bundled into every host package. The tags live HERE so their entries land on `RaskMarkup` and reach
  every component by INHERITANCE — a referenced library's must be injected per host (~8.7k members).
- `src/Rask.Generators` — `Generated.{Type}(...)` factories, `Routes.{Type}(...)`, per-page `Url()`/`Go()`, `[Route]` registration.
- `src/Rask.Server` — ASP.NET host (`AddRask()`/`UseRask<TApp>()`, WS dispatcher). `src/Rask.Wasm` — browser host.
- `src/Rask.Wasm.Hosting` — static-file host for a published WASM bundle. `src/Rask.Wasm.Tasks` — `BakeScopedAssetsTask`.
- `src/Rask.Validation.{DataAnnotations,FluentValidation}` — opt-in validators. `src/Rask.Cli` — the `rask` CLI (owns all scaffolding via `rask new`).
- `src/Rask.WebPush` — opt-in server-side Web Push sender (VAPID + RFC 8291; pairs with `IWebPush`). Zero external deps.
- `src/Rask.Blazor` — a REAL Blazor component as an ordinary Rask component: derive a `partial` class from
  `BlazorComponent<T>` (T from an RCL/MudBlazor/Radzen — the Razor SDK compiles `.razor` untouched). Rendered
  server-side into the FIRST response via `OnPropsChangedAsync` + quiescence; params cross as live C# objects
  (no serialization). The hosted component's own `@onclick` works with NO circuit — `BlazorFrameWriter` rewrites
  Blazor's handler ids as `data-rask-on-*` over the existing socket. **NOT opaque when static** (opaque ⇒
  `FrameDiffer` skips children ⇒ island freezes after first paint). **Both hosts, trimmed publish included** —
  `BlazorComponent<T>`'s type parameter is DAM-annotated, or the trimmer eats the hosted `[Parameter]` setters
  and the island renders EMPTY with a green build; never in the meta-package. Compiling `.razor`→chain was rejected: Razor's syntax layer is `internal`
  in every version and the .NET 10 SDK compiler is closed (23 IVT friends) — see `docs/blazor-components.md`.
- `src/Rask.External` + `src/Rask.External.Tasks` — a `.tsx`/Lit file as an ORDINARY component: derive a
  `partial` class from `ReactComponent`/`PreactComponent`/`SolidComponent`/`VueComponent`/`SvelteComponent`/
  `AngularComponent`/`LitComponent` (the base class IS the
  declaration — no attribute, and the BUILD reads it too: three runtimes write `.tsx` and two write `.ts`, so the
  extension names a family and the generator carries the declared runtime out as a constant). Two runtimes sharing
  an extension are scoped by DIRECTORY and overlapping trees are refused; React+Preact is refused (npm cannot
  install both). Front-end file paired by filename like scoped JS. Props declared in C#, serialized reflection-free — EXCEPT a
  **package island** (`Module => "@mui/material/Button"`, no front-end file), whose props are generated from the
  committed `{Island}.props.json` beside it by BOTH the island and factory generators through one shared
  `PackageIslandProps` resolver (one generator never sees the other's output); unset props are omitted and
  callbacks carry `$a` so no event object is ever serialized. The BUILD writes that snapshot before the compile
  (Rask-pinned `typescript` 6.x JS API under node; locked — RASKISLAND008, never rewrites — under CI);
  callbacks re-enter C# over the existing handler channel AND escalate the page to interactive. Its subtree is a
  **diff boundary** (`Component.OpaqueSubtree` + `data-rask-opaque`). `rask dev` serves islands from Vite on 5174
  for HMR — see `docs/islands.md`.
- `src/Rask.Site` — the ONE app published to rask.sh: landing page at `/`, showcase + guides at `/docs`.
  Browser-WASM only; `samples/` is deleted. It is `IsPackable=false` + `RaskPublicApiTracked=false`:
  an APP under `src/` would otherwise be treated as a shippable library by both repo-wide gates.

## Layout — TWO roots, no others
`src/` is what ships, `tests/` is what verifies or measures. There is no `site/`, `samples/` or
`benchmarks/` any more. Adding a third root means teaching `.githooks/pre-commit`'s path filter and
`scripts/lib/affected_projects.py`'s `PROJECT_ROOTS` about it, or the one commit that touches only it
is the one commit that skips the gate.
- `tests/Rask.*.Tests` — unit/integration, one per `src/` project. `tests/Rask.*.E2E.Tests` — the
  end-to-end suites. `tests/Rask.Benchmarks*` — BenchmarkDotNet; not test projects, so `dotnet test`
  skips them and the scoped runner filters them out by the `.Tests` suffix.

## Commands
```bash
dotnet build Rask.slnx
dotnet test Rask.slnx --filter "FullyQualifiedName!~Rask.Site.E2E"   # fast inner loop
dotnet test Rask.slnx --filter FullyQualifiedName~ATests                 # one class
dotnet run --project src/Rask.Site
```

## Primitives & rules (the load-bearing invariants)
- `Component` (base: `Render`, `Children`, `Key`, `TagName`, `WriteAttributes`) → `Element` (universal
  HTML attrs). `Text` encodes; `Raw` is verbatim. `Fragment`/`Doctype` special-cased in `HtmlSerializer`.
- **Attribute render order: id, class, style, title, the plain globals (lang, dir, hidden, inert,
  popover, contenteditable, spellcheck, translate), data-*, role, tabindex, aria-*, `Attributes`
  (the verbatim escape hatch), then tag-specific — tests assert it; preserve it.**
- Markup is a CHAIN: `Div.Class("panel")[Span["hi"]]` — no `new`, no factory call. Children via the
  indexer (no `Children:` param; `..` spread breaks — pass enumerables). A component's REQUIRED props are
  chain steps taken first (any order); `Bind` vs `Value` are mutually exclusive openings — both live on
  the ENTRY, so taking one leaves the other unreachable; type arguments are inferred from the opening
  step, or stated with `.Of<T>()`, which hands back the state still owing any required steps. See `docs/building-components.md`.
- **The chain's receiver IS the component** — one shape, and a step hands back exactly what it was called
  on. `Build<T>`, the mode-carrying `Build<T, TMode>`, `FormBuild<T>` and `GridBuild<T, TKey>` are gone.
  What makes that safe is that every event prop is a `Callback<T>` — a non-invocable STRUCT, so
  `x.OnClick(fn)` finds no applicable member and falls through to the extension setter. A delegate-typed
  prop would be invocable, stop lookup dead and never reach it (CS1593), so keep events on `Callback<T>`.
  A shape-only indexer (a form's submit state, a grid's columns) is declared on the component itself.
  Page root renders into `<body>`; Rask adds the shell (`Head`/`HtmlLang`/`BodyClass`/`Shell`) + runtime `<script>` — RASK021.
- **A routable component carries `[Route("/x")]`** — repeat it for a page that answers several URLs (first
  declared is canonical, the rest are alternates the router matches but nothing generates); `[ParentRoute(typeof(Layout))]`
  for nesting, `[NotFound]` for the catch-all. Generates `X.Url(...)`/`X.Go(...)` (C# 14 static
  extensions, need the page's namespace imported). **Inside a markup host the bare `X` is the chain's
  chain ENTRY, not the type**, so qualify or use `Routes.X()`.
- **Factory params** (generated per public prop): nullable→optional(null); non-nullable no-initializer→**required**
  (RASK001); initializer/`[SkipFactory]`/`Children`→excluded. Inject framework services via the **ctor**, not
  settable non-nullable props (those become required params; `required`+DI ctor→RASK002).
- **`Key`** — reconciliation identity (last factory `Key:` param), enables trusted structural diff; not a reactive prop.
- **One `Callback`/`Callback<T>` property per event**, taking either handler shape (sync or async) at the
  call site — a plain `Func<…>` still types a template or a selector. It is a STRUCT, which is what keeps
  the setter reachable now the component is the receiver (above). Auto-wrapped to re-render the owning parent.
  **Refs**: `ElementRef.New()` in a field, pass to `IJSRuntime`. **Context**: `Context.Provide<T>` /
  `Context.Get<T>`/`Required`/`Has`. Construct components via the **chain**, never `new` outside Core (RASK014).

## Subsystems → read `docs/`
Routing/lifecycle (`docs/routing.md`, `docs/lifecycle.md`), scoped CSS/TypeScript + typed browser APIs
(`docs/js-interop.md`, `docs/browser-apis.md` — the 50-wrapper map), forms +
validation (`docs/forms.md`), auth (`docs/authentication.md`), context/callbacks (`docs/composition.md`),
diagnostics RASK001–080, RASK027/030/032/034/042/047/048–050 retired (`docs/diagnostics.md` — analyzer descriptors are the source of truth), getting
started / migration / testing / architecture (`docs/`). Trimming: `src/Rask.Site` must
`dotnet publish -c Release` with zero IL warnings — new reflection needs a DAM annotation or justified suppression.

## Conventions
- **New HTML tag** → `add-html-tag` skill (`src/Rask.Core/Components/{Tag}.cs` + `tests/Rask.Core.Tests/Components/{Tag}Tests.cs`).
- **New diagnostic** → `add-diagnostic` skill. Diagnostic IDs RASK001–080 are documented in `docs/diagnostics.md`
  (RASK027/030/032/034/042/047/048/049/050 are retired and never recycled; RASK063/065 are RESERVED for Rask.Blazor and unimplemented; the next free id is RASK081). **Grep `src/`
  for the id before you claim it, AND again before you merge** — FOUR assemblies allocate in this space
  (`Rask.Generators`, `Rask.Batteries.Generators`, `Rask.Api.Generators`, and `Rask.Generators.Shared`'s
  source-linked `RegistryGeneratorBase`) and
  RS1019 only checks one compilation, so this line goes stale silently. This has now bitten five times on one
  branch: #865 took RASK054, #871 took RASK055, and #864 took RASK056–059 out from under #880's own RASK056,
  caught only at merge; the API client generator then wrote RASK061–064 against a checkout whose base already
  had them. #984 then took RASK067–070 out from under #985's own RASK067, again caught
  only at merge. Treat a merge from main as invalidating every id you hold.

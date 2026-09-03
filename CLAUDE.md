# CLAUDE.md

Target `net10.0` (`net10.0-browser` for WASM); nullable + implicit usings on. **Rask** is a C#
component framework (Blazor-like): Roslyn factory generator, scoped CSS/TypeScript, routing, live diff
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
- **cut-release** — CHANGELOG promote + `vX.Y.Z` tag. **check-dependency-updates** — NuGet + Node LTS + the pins outside CPM.

Standing rules: do your best every PR, holding **UX + security + performance** together; prefer
standard .NET APIs (don't reinvent); refactor duplication you touch; unit-test every feature (E2E
only when unreachable); E2E for every `samples/` change — **tests run locally, not in CI**: `dotnet
format` + unit via `scripts/run-unit-local.sh` (enforced by `.githooks/pre-commit`), browser E2E via
`scripts/run-e2e-local.sh` (enforced by `.githooks/pre-push`); benchmark every framework-code change;
the public installer is `rask.sh`/`rask.ps1` at the ROOT (published to Pages by `pages.yml`, gated by
`scripts/tests/install-script.test.sh` + `scripts/run-install-e2e-local.sh`, `docs/installation.md`);
**user-facing change → update a sample + docs/README/NUGET.md/llms.txt/template AGENTS.md**; keep
everything up to date; CHANGELOG `[Unreleased]` per notable change; Conventional Commits
(commitlint); no `Co-Authored-By`/`Generated-with`. Build is warnings-as-errors + analyzers
(`Directory.Build.props`; see `docs/code-analysis.md`). **Every public name obeys
`docs/api-style.md`**; the build records the surface in `src/*/PublicAPI/<tfm>/`, so an unrecorded
public member is a build error (RS0016/RS0017). Releases: tag→`release.yml`; nightly
prerelease on `main`→`nightly.yml`. AI artifacts: `AGENTS.md`, `llms.txt`, template `AGENTS.md`,
`docs/ai-agents.md`. Full detail: `docs/development-workflow.md`. Ask only when truly blocked.

## Projects
- `src/Rask.Core` — rendering, live context, routing, scoped CSS/TypeScript, lifecycle.
- `src/Rask.Html` — the HTML/SVG element family (`Div`…`Svg`, `Doctype`) in `Rask.Html.Components`;
  `IsPackable=false`, bundled into every host package. Core keeps only the tags its engine builds.
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
  install both). Front-end file paired by filename like scoped JS. Props declared in C#, serialized reflection-free;
  callbacks re-enter C# over the existing handler channel AND escalate the page to interactive. Its subtree is a
  **diff boundary** (`Component.OpaqueSubtree` + `data-rask-opaque`). `rask dev` serves islands from Vite on 5174
  for HMR — see `docs/islands.md`.
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
- **Attribute render order: id, class, style, title, the plain globals (lang, dir, hidden, inert,
  popover, contenteditable, spellcheck, translate), data-*, role, tabindex, aria-*, `Attributes`
  (the verbatim escape hatch), then tag-specific — tests assert it; preserve it.**
- Markup is a CHAIN: `Div.Class("panel")[Span["hi"]]` — no `new`, no factory call. Children via the
  indexer (no `Children:` param; `..` spread breaks — pass enumerables). A component's REQUIRED props are
  chain steps taken first (any order); `Bind` vs `Value` are mutually exclusive openings; type arguments
  are inferred from the opening step, or stated with `.Of<T>()`. See `docs/building-components.md`.
- **The chain's receiver is `Build<TComponent>`, never the component** — that is what lets a callback prop
  be a plain delegate: C# stops at a delegate-typed property when resolving `x.OnClick(fn)` and never
  reaches an extension method (CS1593). Converts implicitly to the component (and so to `Component`), so
  markup is unaffected. Two consequences: a component's STATIC members need qualifying inside a markup
  host (the "Color Color" rule no longer merges them), and `cond ? chain : null` needs a `Component?`
  target rather than `var`.
  Page root renders into `<body>`; Rask adds the shell (`Head`/`HtmlLang`/`BodyClass`/`Shell`) + runtime `<script>` — RASK021.
- **A routable component carries `[Route("/x")]`** — repeat it for a page that answers several URLs (first
  declared is canonical, the rest are alternates the router matches but nothing generates); `[ParentRoute(typeof(Layout))]`
  for nesting, `[NotFound]` for the catch-all. Generates `X.Url(...)`/`X.Go(...)` (C# 14 static
  extensions, need the page's namespace imported). **Inside a markup host the bare `X` is the chain's
  `Build<X>` entry, not the type**, so qualify or use `Routes.X()`.
- **Factory params** (generated per public prop): nullable→optional(null); non-nullable no-initializer→**required**
  (RASK001); initializer/`[SkipFactory]`/`Children`→excluded. Inject framework services via the **ctor**, not
  settable non-nullable props (those become required params; `required`+DI ctor→RASK002).
- **`Key`** — reconciliation identity (last factory `Key:` param), enables trusted structural diff; not a reactive prop.
- **Callbacks are PLAIN DELEGATES** — `Action?`, `Func<Task>?`, `Action<T>?`, `Func<T, Task>?`, and any
  `Func<…>` for a template or selector. No wrapper types: the `Build<T>` receiver is what lets the setter
  keep the property's name. Auto-wrapped to re-render the owning parent.
  **Refs**: `ElementRef.New()` in a field, pass to `IJSRuntime`. **Context**: `Context.Provide<T>` /
  `Context.Get<T>`/`Required`/`Has`. Construct components via the **chain**, never `new` outside Core (RASK014).

## Subsystems → read `docs/`
Routing/lifecycle (`docs/routing.md`, `docs/lifecycle.md`), scoped CSS/TypeScript + typed browser APIs
(`docs/js-interop.md`, `docs/browser-apis.md` — the 50-wrapper map), forms +
validation (`docs/forms.md`), auth (`docs/authentication.md`), context/callbacks (`docs/composition.md`),
diagnostics RASK001–067, RASK030/032/034/042/047/048–050 retired (`docs/diagnostics.md` — analyzer descriptors are the source of truth), getting
started / migration / testing / architecture (`docs/`). Trimming: `samples/Rask.Example.Wasm` must
`dotnet publish -c Release` with zero IL warnings — new reflection needs a DAM annotation or justified suppression.

## Conventions
- **New HTML tag** → `add-html-tag` skill (`src/Rask.Html/Components/{Tag}.cs` + `tests/Rask.Html.Tests/Components/{Tag}Tests.cs`).
- **New diagnostic** → `add-diagnostic` skill. Diagnostic IDs RASK001–060 are documented in `docs/diagnostics.md`
  (RASK030/032/034/042/047/048/049/050 are retired and never recycled; RASK063/065 are RESERVED for Rask.Blazor and unimplemented; the next free id is RASK068). **Grep `src/`
  for the id before you claim it, AND again before you merge** — three assemblies allocate in this space and
  RS1019 only checks one compilation, so this line goes stale silently. This has now bitten four times on one
  branch: #865 took RASK054, #871 took RASK055, and #864 took RASK056–059 out from under #880's own RASK056,
  caught only at merge. Treat a merge from main as invalidating every id you hold.

# CLAUDE.md

Every package targets `net10.0` (plus `net10.0-browser` for WASM), from `RaskNetTargets` in
`Directory.Build.props`; **`global.json` pins the SDK to 10.0.x** — .NET 11 support was removed 2026-10-01.
Test projects build for `$(RaskTestTarget)`. Nullable + implicit usings on. **Rask** is a C#
component framework (Blazor-like): Roslyn chain generator, scoped CSS/TypeScript, routing, live diff
runtime over WS (Server) or JSImport/JSExport (WASM). This file is the **map** — read the code,
the `docs/`, and the tests for depth. Keep this file small; put how-to detail in `.claude/skills/`.

## Workflows → skills (use them automatically)
`.claude/skills/` holds the committed playbooks; apply the matching one without being asked.
- **flux-component** — a `Rask.Ui` component, EXACTLY as Flux UI draws and behaves it: measure fluxui.dev's live docs
  (`scripts/flux/`), write it from the measurements, prove it with `parity.mjs`. Never read `livewire/flux` (proprietary).
- **rask-ship** — definition of done before any commit: format the files you changed → build + test the
  project you touched → CHANGELOG → review → land on main; CI runs the full gates after the push.
- **add-html-tag** · **add-diagnostic** · **add-codefix** — elements from MDN (refresh + hand partial) / RASK0xx+docs+test / IDE quick-fix+test.
- **run-benchmarks** — before/after `Allocated` delta for render-hotpath changes (required evidence).
- **rask-review** — security / performance / memory / .NET-C# review lens (wraps /code-review, /security-review).
- **rask-seo** — search + AI-assistant discoverability of rask.sh and the packages (page/guide copy, JSON-LD, sitemap, llms.txt); on EVERY site/docs/package-metadata change.
- **land-on-main** — Conventional-Commit, merge `origin/main`, `git push origin HEAD:main`, CI reports after. **Never open a PR** for own work.
- **cut-release** — CHANGELOG promote + `vX.Y.Z` tag. **check-dependency-updates** — NuGet + Node LTS + the pins outside CPM.

Standing rules: do your best every change, holding **UX + security + performance** together; prefer
standard .NET APIs (don't reinvent); refactor duplication you touch; **SOLID + Clean Code** (one
responsibility per type and file, small well-named methods, no copied helper — `docs/code-analysis.md#design`);
unit-test every feature (E2E
only when unreachable); **PARALLEL BY DEFAULT** — independent reads/searches/commands go out in ONE batch,
independent sub-tasks fan out to subagents (a worktree each: two builds in one tree fight over `obj/`), a long
build or gate runs in the background while the next step is prepared; a new script, gate or test runs
concurrently on all cores under ONE shared `-m` budget, and anything serial states its reason at the site
(a shared `obj/`, a port, a global sink); E2E for every `src/Rask.Site` change.
**THE GATES RUN IN CI, NOT ON THIS MACHINE — never wait on one.** The hooks take seconds (commit-msg,
front doors, attribution) and nothing blocks a commit or a push. AFTER the push to `main`, `ci.yml`
runs the gates the change can REACH, each its own job (`scripts/lib/affected_gates.py` decides, and runs
everything for whatever it cannot narrow); `full.yml` runs the whole set — build, unit, format, every
browser E2E, CLI build, templates, a job per front-end template — behind every push (one run at a time), and
`nightly.yml`/`pages.yml` publish only from a commit THAT run passed. Locally, build and test
ONLY the project you touched (`dotnet test tests/Rask.X.Tests`), never the solution, never a gate script
— several worktrees doing that at once is what made every gate take ten minutes. A red `main`
(`gh run list --branch main --workflow ci`, and `--workflow full` for what a scoped run cannot see) is
fixed forward, first; a job is reproduced with the script
it names. Risky change → push to a `ci/**` branch first (`ci/release/**` adds the release-only gates:
watch, deploy, storage providers, installer, providers — `release.yml` runs them all before it packs).
**The benchmark gates run in CI** — `scripts/run-benchmarks-local.sh` is the `benchmarks` job: wire bytes,
bundle size and the allocation budget, all exact counts; times are in no gate (a runner's clock proves nothing);
the public installer is `rask.sh`/`rask.ps1` at the ROOT (published to Pages by `pages.yml`, gated by
`scripts/tests/install-script.test.sh` + `scripts/run-install-e2e-local.sh`, `docs/installation.md`);
**user-facing change → update `src/Rask.Site` + docs/README/NUGET.md/llms.txt/docs/ai-agents.md**; keep
everything up to date; CHANGELOG `[Unreleased]` per notable change; Conventional Commits
(commitlint); no `Co-Authored-By`/`Generated-with`. Build is warnings-as-errors + analyzers
(`src/`: CA latest-recommended, Meziantou, Roslynator, Sonar, `BannedSymbols.txt`; see `docs/code-analysis.md`) —
**FIX a finding; silence one only if unfixable, at that site, with a `#pragma` reason**. **Every public name obeys
`docs/api-style.md`**; the build records the surface in `src/*/PublicAPI/<tfm>/`, so an unrecorded
public member is a build error (RS0016/RS0017). Releases: tag→`release.yml`; nightly
prerelease on `main`→`nightly.yml`. AI artifacts: `AGENTS.md`, `llms.txt`, `docs/ai-agents.md` (a scaffold has
no `AGENTS.md`; `ProjectGeneratorTests` keeps it that way). Full detail: `docs/development-workflow.md`. Ask only when truly blocked.

## Projects
- `src/Rask.Core` — **the `Rask` package** (assembly stays `Rask.Core`): rendering, live context, routing, scoped
  CSS/TypeScript, lifecycle, AND the whole HTML/SVG element family (generated from MDN by `src/Rask.Dom.Tasks`, MDN-named types in `Rask.Core`);
  ships the analyzers and the build hooks (`build/Rask.props|targets`, twinned into `buildTransitive/`). Both hosts
  depend on it `PrivateAssets="none"`; a component library references it alone. The tags live HERE so their entries
  land on `RaskMarkup` and reach every component by INHERITANCE — a referenced library's must be injected per host (~8.7k members).
- `src/Rask.Generators` — the chain entries, ONE `Routes` class per project in its root namespace (`Routes.{Type}(...)`,
  nested by folder only on a type-name clash: `Routes.Admin.HomePage()`), per-page `Url()`/`Go()`, `[Route]` registration.
- `src/Rask.Server` — ASP.NET host + EVERY server battery + `RaskApp` (`App/`: `RaskApp.Create(args).Run<App>()`,
  `RaskBatteryWiring`, `RaskAppDbContext`). `AddRask()`/`MapRask<TApp>()` stay for a hand-wired host.
  `src/Rask.Wasm` — browser host; `Batteries/` wires the client halves (Cqrs, Query, Auth.Client, Cqrs.Client, validation, Ui).
  There is NO meta-package any more: `Rask` = shared core, `Rask.Server`/`Rask.Wasm` = host + batteries.
- `src/Rask.Spa.Hosting` — `MapRaskSpa()`: serves a built SPA from its ASP.NET host — a Rask WASM app (WASM is a SPA, never a render mode) or an npm front end in `client/`, which its build targets `npm ci` + `npm run build` and publish into `wwwroot` (`docs/spa.md`); a JS component inside a Rask page is an island. `src/Rask.Wasm.Tasks` — `BakeScopedAssetsTask`.
- `src/Rask.Validation.FluentValidation` — opt-in validator (DataAnnotations is in Core). `src/Rask.Cli` — the `rask` CLI (owns all scaffolding via `rask new`).
- `src/Rask.Web` — every web API from MDN (generated at build from the Core snapshot by `src/Rask.Dom.Tasks`'s WebEmitter): globals in `Rask.Web`, MDN interfaces in `Rask.Web.Types`, each chain one `__raskWeb.run` round trip, kept objects as `IJSObjectReference` handles. Imported for every Server/WASM app by its own `build/Rask.Web.props` (buildTransitive): `global using Rask.Web;` + `global using Types = Rask.Web.Types;` — never the `Types` namespace itself (CS0104 with the globals); Rask's `EditContext`/`FormData`/`DataTransfer`/`EventTarget`/`Touch` keep the bare name by global alias there; a library on `Rask` alone gets none of it.
- `src/Rask.WebPush` — opt-in server-side Web Push sender (VAPID + RFC 8291; browsers subscribe via MDN's `PushManager`). Zero external deps.
- `src/Rask.Blazor` — a REAL Blazor component as an ordinary Rask component: derive a `partial` class from
  `BlazorComponent<T>` (T from an RCL/MudBlazor/Radzen — the Razor SDK compiles `.razor` untouched). Rendered
  server-side into the FIRST response via `OnUpdated` + quiescence; params cross as live C# objects
  (no serialization). The hosted component's own `@onclick` works with NO circuit — `BlazorFrameWriter` rewrites
  Blazor's handler ids as `data-rask-on-*` over the existing socket. **NOT opaque when static** (opaque ⇒
  `FrameDiffer` skips children ⇒ island freezes after first paint). **Both hosts; trimming is annotated, NOT gated end to end** —
  `BlazorComponent<T>`'s type parameter is DAM-annotated, or the trimmer eats the hosted `[Parameter]` setters
  and the island renders EMPTY with a green build. Compiling `.razor`→chain was rejected: Razor's syntax layer is `internal`
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
`benchmarks/` any more. Adding a third root means teaching
`scripts/lib/affected_projects.py`'s `PROJECT_ROOTS` about it, or a scoped run skips it.
- **Every test is named as a sentence and shaped in three blocks** (setup · action · checks, blank-line separated):
  `Remember_loads_once_then_serves_from_the_cache`. `tests/Shared/TestNamesReadAsSentences.cs`, linked into every
  `*.Tests` project, fails the build's tests on a name that isn't one.
- **A test that reads a file from disk outside its project DECLARES it** (`<RaskTestReads/>` in a `Condition="false"`
  ItemGroup of its csproj) — the gates are scoped, and an undeclared read is skipped when that file changes.
- `tests/Rask.*.Tests` — unit/integration, mostly one per `src/` project. `tests/Rask.*.E2E.Tests` — the
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
  popover, contenteditable, spellcheck, translate), data-*, role, tabindex, aria-* (typed, then the Aria bag), `Attributes`
  (the verbatim escape hatch), then tag-specific — tests assert it; preserve it.**
- Markup is a CHAIN: `Div.Class("panel")[Span["hi"]]` — no `new`, no factory call. Children via the
  indexer (no `Children:` param; `..` spread breaks — pass enumerables). A component's REQUIRED props are
  chain steps taken first (any order); `Bind` vs `Value` are mutually exclusive openings — both live on
  the ENTRY, so taking one leaves the other unreachable; type arguments are inferred from the opening
  step, or stated with `.Of<T>()`, which hands back the state still owing any required steps. See `docs/building-components.md`.
- **Names:** elements + markup primitives are BARE (inherited by components; `using static Rask.Markup` elsewhere;
  `Markup.Footer` when a member hides one; `<html>` is `Html`). Families are GROUPED, never bare — `Ui.Button`/`Ui.Tone`
  (kit, namespace `Rask`), `Trigger.*`, `Validation.*`, an npm package class (`Mui.Button`) — via `[RaskChainGroup]`.
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
  chain ENTRY, not the type**, so qualify or use `Routes.X()` (reachable from anywhere under the root namespace, no using).
- **Chain steps** (generated per public prop): nullable→optional; non-nullable no-initializer→**required**
  (RASK001); initializer/`[SkipFactory]`/`Children`→excluded. Inject framework services via the **ctor**, not
  settable non-nullable props (those become required steps; `required`+DI ctor→RASK002).
- **`Key`** — reconciliation identity, a chain step that can go ANYWHERE in the chain (generic components too; #1118, RASK046 retired); enables trusted structural diff; not a reactive prop.
- **One `Callback`/`Callback<T>` property per event**, declared NON-nullable and fired `await OnX.Invoke(v)`
  (unset = no-op, never a required step), taking either handler shape (sync or async) at the
  call site — a plain `Func<…>` still types a template or a selector. It is a STRUCT, which is what keeps
  the setter reachable now the component is the receiver (above). Auto-wrapped to re-render the owning parent.
  **Refs**: `ElementRef<HTMLDialogElement>` in a field carries MDN's members (`await _d.ShowModal()`, generated); untyped `ElementRef.New()` passes to `IJSRuntime`. **Context**: `Context.Provide<T>` /
  `Context.Get<T>`/`Required`/`Has`. Construct components via the **chain**, never `new` outside Core (RASK014).

## Subsystems → read `docs/`
Routing/lifecycle (`docs/routing.md`, `docs/lifecycle.md`), scoped CSS/TypeScript + typed browser APIs
(`docs/js-interop.md`, `docs/browser-apis.md` — the wrapper map), forms +
validation (`docs/forms.md`), auth (`docs/authentication.md`), context/callbacks (`docs/composition.md`),
diagnostics RASK001–101, RASK027/030/032/034/042/046/047/048–050/054/075/081 retired (`docs/diagnostics.md` — analyzer descriptors are the source of truth), getting
started / migration / testing / architecture (`docs/`). Trimming: `src/Rask.Site` must
`dotnet publish -c Release` with zero IL warnings — new reflection needs a DAM annotation or justified suppression.

## Conventions
- **HTML elements are GENERATED from MDN** (`src/Rask.Core/Dom/mdn.snapshot.json`, refreshed daily by the local build): MDN type names
  (`HTMLAnchorElement`), entries named after tags (`A`), IDL attribute names (`ColSpan`). Missing/behaviour → `add-html-tag` skill.
- **New diagnostic** → `add-diagnostic` skill. Diagnostic IDs RASK001–101 are documented in `docs/diagnostics.md`
  (RASK027/030/032/034/042/046/047/048/049/050/054/075/081 are retired and never recycled; RASK063/065 are RESERVED for Rask.Blazor and unimplemented; the next free id is RASK102). **Grep `src/`
  for the id before you claim it, AND again before you merge** — FOUR assemblies allocate in this space
  (`Rask.Generators`, `Rask.Batteries.Generators`, `Rask.Api.Generators`, and `Rask.Generators.Shared`'s
  source-linked `RegistryGeneratorBase`) and
  RS1019 only checks one compilation, so this line goes stale silently. This has now bitten five times on one
  branch: #865 took RASK054, #871 took RASK055, and #864 took RASK056–059 out from under #880's own RASK056,
  caught only at merge; the API client generator then wrote RASK061–064 against a checkout whose base already
  had them. #984 then took RASK067–070 out from under #985's own RASK067, again caught
  only at merge. Treat a merge from main as invalidating every id you hold.

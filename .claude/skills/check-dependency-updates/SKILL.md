---
name: check-dependency-updates
description: Check every dependency axis in the Rask repo — NuGet, the Node LTS line, and the pins that live outside central package management — and propose safe bumps. Use periodically and before a release. Reports outdated/vulnerable/deprecated packages, names the pins Dependabot cannot see, and applies bumps behind the rask-ship gate.
---

# check-dependency-updates

The on-demand path. Dependabot covers NuGet, GitHub Actions, the site's own npm manifest and the seven
front-end templates' clients weekly (`.github/dependabot.yml`; `dependabot-merge.yml` lands a pull
request `ci` passed), and
`.github/workflows/upstream.yml` moves the stated Node line when the Active LTS does. Neither sees the
pins in §2, and nothing schedules a vulnerability scan any more — see the warning under §1.

## 1. NuGet (central package management)

```bash
dotnet restore Rask.slnx
dotnet list Rask.slnx package --outdated
dotnet list Rask.slnx package --vulnerable --include-transitive
dotnet list Rask.slnx package --deprecated
```

> **This is the only vulnerability scan that runs anywhere.** CI used to run one; that job went with
> `ci.yml` in #923, and `CHANGELOG.md`'s "CI now scans for vulnerable and deprecated dependencies"
> line is stale. Nothing will tell you but this command.

### Do NOT bump these without reading why

| Pin | Why it is held |
| --- | --- |
| `SQLitePCLRaw.*` | Held at 3.x **ahead** of what `Microsoft.Data.Sqlite` asks for, to escape CVE-2025-6965. Never "resolve" it down to the 2.1.x family the graph requests. Both halves move together. |
| `Microsoft.CodeAnalysis.CSharp{,.Features,.Workspaces}` | Must not exceed the Roslyn in the build SDK — a newer analyzer than the running `csc` is CS9057, and it raises the compiler floor for every downstream consumer. Dependabot ignores these; bump by hand with an SDK-band change. |
| `Microsoft.Build.Utilities.Core` (+ `Microsoft.Build.Framework`, which it pins exactly) | Held at **18.9.x**. 18.10 is the .NET 11 SDK line and ships `lib/net11.0` + `lib/net472` only, where 18.9.6 ships `lib/net10.0`. A `net10.0` project still COMPILES — it binds `ref/netstandard2.0` — so the build is green and only the tests that load a task fail, with `FileNotFoundException` on `Microsoft.Build.{Utilities.Core,Framework}, Version=15.1.0.0`: 70 failures across the four `*.Tasks.Tests` projects (PR #1064, closed for this). Dependabot ignores `>=18.10`; lift that with the `net11.0` move and bump both halves together. |
| `Spectre.Console` / `.Testing` | One version, always — the testing package is built against the exact matching library. |
| `RaskTsgoVersion` | A deliberately **dated** dev build. `@typescript/native-preview` publishes to `latest` daily, so `latest` there means "whatever was built this morning". |

`PackagePinFamilyTests` asserts the family rules, so a bump that splits one fails the unit gate
rather than shipping. It does not know your intent — if a test fails, fix the bump, not the test.

## 2. The pins outside CPM (Dependabot is blind to all of these)

| Pin | Where |
| --- | --- |
| esbuild | `src/Rask.Core/build/Rask.Core.targets` → `RaskEsbuildVersion`. **Bumping it means replacing its rows in `src/Rask.TypeScript.Tasks/TypeScriptToolPins.cs`** — one `sha512-…` per platform package, from `https://registry.npmjs.org/@esbuild%2f<os>-<arch>/<version>` → `dist.integrity`. The build verifies downloads against those rows, and `Every_pinned_tool_version_has_a_recorded_digest_for_every_platform_it_publishes` fails until they are there. |
| tsgo | same file → `RaskTsgoVersion` (dated on purpose — see above). Same digest table, `@typescript/native-preview-<os>-<arch>`. |
| TypeScript (props extractor) | `src/Rask.External/build/Rask.External.props` → `RaskExternalTypeScriptVersion`. The compiler's **JavaScript API**, so it stays on **6.x**: 7.x is the native compiler and publishes no JS API. It decides what every committed `*.props.json` says, so after a bump rebuild the projects with package islands and review the snapshot diff — a checker that answers differently rewrites them. |
| Tailwind | `src/Rask.Tailwind/build/Rask.Tailwind.props` → `RaskTailwindVersion`. **Bumping it means replacing `src/Rask.Tailwind.Tasks/TailwindPins.cs`** (its `Version` and all seven digests) from that release's `sha256sums.txt`; `The_pinned_version_has_a_recorded_digest_for_every_asset` fails until it matches. The `typescript` row above has one row in `TypeScriptToolPins.cs` too. |
| daisyUI | **Vendored, not a version string:** `src/Rask.Ui/Styles/daisyui.mjs` is the standalone bundle itself. Read the current version from `var version = "…"` inside it; the same number is restated in the `@plugin` comment in `src/Rask.Ui/Styles/ui.css`, so **bump both**. Bumping means re-downloading the pinned release asset (`daisyui.mjs` from daisyUI's releases page), not an npm install — this project deliberately has no `package.json`, because Tailwind resolves `@plugin "daisyui"` the Node way and the standalone engine carries no package tree. Nothing else watches this pin: `dependabot.yml`'s npm entries are the site's and the template clients' `package.json`. After a bump run the unit gate — `DaisyUiVersionPinTests` fails if the bundle and the `ui.css` sentence disagree, if the MIT header went missing, or if the compiled sheet no longer carries the themes the bundle defines; `UiClassNamesTests` fails if the new bundle stopped emitting a class the kit writes, which is the silent failure mode (the component renders unstyled and the build stays green). |
| Adapter fixtures | `tests/Rask.External.Tests/Rask.External.Tests.csproj` → `RaskPreactFixtureVersion`, `RaskHappyDomFixtureVersion`, `RaskReactFixtureVersion` (react and react-dom), `RaskVueFixtureVersion`, `RaskSolidFixtureVersion`, `RaskSvelteFixtureVersion`, `RaskAngularFixtureVersion` (core, compiler, common and platform-browser, always one number) and `RaskRxjsFixtureVersion`. Exact versions, npm-installed together into `obj/preact-fixture` for `PreactAdapterTests`, `ReactAdapterTests`, `VueAdapterTests`, `SolidAdapterTests`, `SvelteAdapterTests`, `AngularAdapterTests` and `LitAdapterTests` (happy-dom only), which are the only coverage those adapters have below a browser — island children included. Bump, delete `tests/Rask.External.Tests/obj/preact-fixture` (the install is stamped) and run the unit gate; a major that changed a framework's render, reconcile or unmount semantics fails there, which is the point of the pins. |
| Node build floor | `Rask.External.props` → `RaskExternalMinimumNode` (islands, RASKISLAND001) and `src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.props` → `RaskSpaMinimumNode` (front ends, RASKSPA005): 22.12.0 in both — vite's requirement — and `NodeRequirement.BuildFloor` in the CLI. `NodeRequirementTests` fails if the three part. |
| Angular template's Node floor | `src/Rask.Templates/angular/Company.RaskServer.csproj` → `RaskSpaMinimumNode`, 22.22.3: the lowest version in `@angular/cli`'s `engines.node` (`^22.22.3 \|\| ^24.15.0 \|\| >=26.0.0`). `TemplateNodePinTests` reads that range out of `angular/client/package-lock.json`, so a Dependabot lockfile bump that moves it goes red until this line follows — raise it, and the figures in `docs/spa.md`, by hand. |
| Template client ranges | `src/Rask.Templates/{react,preact,vue,angular,solid,svelte,lit}/client/package.json` + `package-lock.json`. **Dependabot's** (one weekly PR for all seven, a second for majors; lockfile-only inside a range), gated by each template's `front end <key>` CI job. By hand only: `tailwindcss` / `@tailwindcss/*` past what `RaskTailwindVersion` allows (`TailwindVersionPinTests` — move both together), `typescript` 7 (ignored, as for the site), and a red majors PR. Reproduce one with `scripts/run-template-e2e.sh --front-end=<key>`; re-lock with `npm install --package-lock-only` in the client. |
| Template images' Node | `src/Rask.Templates/<front end>/Dockerfile` → `https://deb.nodesource.com/setup_NN.x`. The major of `ScaffoldLine`; `scripts/upstream/node-lts.sh` moves it with the line and `TemplateNodePinTests` holds it. |
| Node scaffold line | `src/Rask.Cli/NodeRequirement.cs` → `ScaffoldLine`. **The source of truth**; everything else is held to it by `NodeRequirementTests`. |
| Scaffolded test project | `src/Rask.Templates/{server,wasm}/Company.RaskServer.Tests/Company.RaskServer.Tests.csproj` → `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio`. Exact versions, the same as `Directory.Packages.props`; `TestProjectScaffoldTests` fails when a CPM bump leaves them behind, so bump them together. |
| Island npm ranges | `src/Rask.Templates/_islands/*/island.json` → the `devDependencies` `rask new --islands <runtime>` merges into the app's `package.json` (react, vue, svelte, solid, preact, lit, angular — and their Vite plugins, `vite`, `typescript`). **Hand-bumped: it is not a `package.json`, so Dependabot cannot read it.** Caret ranges, so they float within a major — check each for a **major** bump, which is the case a caret hides, and keep `vite` and `typescript` on the same range across runtimes. A fragment may also carry npm `overrides`, merged the same way: solid's forces `seroval` and `seroval-plugins` to `^1.6.8` because `solid-js` 1.9.15 pins a line `npm audit` reports critical — **drop that override (here, in `src/Rask.Site/package.json` and in the adapter fixtures' generated `package.json`) once a `solid-js` release depends on seroval 1.6**. |

**Deliberately unpinned, leave alone:** an existing app's island dependencies, which live in the
user's own `package.json` once scaffolded.

## 3. Node

`upstream.yml` follows this daily (`scripts/upstream/node-lts.sh`), but to check by hand:

```bash
curl -fsS https://nodejs.org/dist/index.json | grep -o '"lts":"[^"]*"' | head -1
```

Raise `NodeRequirement.ScaffoldLine`, then run the unit gate — the tests name every file that has to
follow, the front-end templates' Dockerfiles included. The build floor (`RaskExternalMinimumNode` and
`RaskSpaMinimumNode`) is a separate, lower number on purpose.

## 3b. MDN data (the element surface)

MDN is the source of truth for every element type, its attributes and its members.
`src/Rask.Core/Dom/mdn.snapshot.json` is built from the **latest** `@webref/elements`, `@webref/idl` and
`@mdn/browser-compat-data`. Refresh it on every run of this skill, on the latest LTS Node:

```bash
scripts/mdn/refresh.sh
git diff --stat src/Rask.Core/Dom/mdn.snapshot.json
```

A diff is MDN moving: a new element or attribute that now ships in two engines, or a member BCD
deprecated. Commit it through `rask-ship`; `MdnSnapshotTests` fails if a tag Rask renders stops shipping.

## 4. Apply + verify

Edit the pins, then run the **`rask-ship`** gate (warnings-as-errors, so analyzer-rule changes from an
SDK bump surface immediately). Note the bump in `CHANGELOG.md` when it is user-visible; routine
Dependabot patch waves are not changelogged.

**Read the PR's `ci` run before landing a Dependabot PR.** `ci.yml` runs the push set of gates on
every pull request, Dependabot's included, but `main` has no required checks: a red run does not
block the merge button, so the run is yours to look at. Then land it locally, not from the web UI:
check the branch out, drop the sign-off below, and push.

Its commit ends `Signed-off-by: dependabot[bot] <support@github.com>`. CI lets that one line through
on a PR Dependabot opened (so the PR's `commitlint` check is green and means something); `pre-push`
does not. Start with `git commit --amend --reset-author` and delete the line.

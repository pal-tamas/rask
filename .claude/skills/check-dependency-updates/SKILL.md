---
name: check-dependency-updates
description: Check every dependency axis in the Rask repo — NuGet, the Node LTS line, and the pins that live outside central package management — and propose safe bumps. Use periodically and before a release. Reports outdated/vulnerable/deprecated packages, names the pins Dependabot cannot see, and applies bumps behind the rask-ship gate.
---

# check-dependency-updates

The on-demand path. Dependabot covers NuGet + GitHub Actions weekly (`.github/dependabot.yml`), and
`.github/workflows/lts-watch.yml` opens an issue when Node's Active LTS line moves. Neither sees the
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
| `Spectre.Console` / `.Testing` | One version, always — the testing package is built against the exact matching library. |
| `RaskTsgoVersion` | A deliberately **dated** dev build. `@typescript/native-preview` publishes to `latest` daily, so `latest` there means "whatever was built this morning". |

`PackagePinFamilyTests` asserts the family rules, so a bump that splits one fails the unit gate
rather than shipping. It does not know your intent — if a test fails, fix the bump, not the test.

## 2. The pins outside CPM (Dependabot is blind to all of these)

| Pin | Where |
| --- | --- |
| esbuild | `src/Rask.Core/build/Rask.Core.targets` → `RaskEsbuildVersion` |
| tsgo | same file → `RaskTsgoVersion` (dated on purpose — see above) |
| TypeScript (props extractor) | `src/Rask.External/build/Rask.External.props` → `RaskExternalTypeScriptVersion`. The compiler's **JavaScript API**, so it stays on **6.x**: 7.x is the native compiler and publishes no JS API. It decides what every committed `*.props.json` says, so after a bump rebuild the projects with package islands and review the snapshot diff — a checker that answers differently rewrites them. |
| Tailwind | `src/Rask.Tailwind/build/Rask.Tailwind.props` → `RaskTailwindVersion`, and the caret range in `ProjectGenerator.Spa.cs`. `TailwindVersionPinTests` asserts the range accepts the pin. |
| daisyUI | **Vendored, not a version string:** `src/Rask.Ui/Styles/vendor/daisyui.mjs` is the standalone bundle itself. Read the current version from `var version = "…"` inside it; the same number is restated in the `@plugin` comment in `src/Rask.Ui/Styles/ui.css`, so **bump both**. Bumping means re-downloading the pinned release asset (`daisyui.mjs` from daisyUI's releases page), not an npm install — this project deliberately has no `package.json`, because Tailwind resolves `@plugin "daisyui"` the Node way and the standalone engine carries no package tree. Nothing else watches this pin: `dependabot.yml` declares no npm ecosystem at all. After a bump run the unit gate — `DaisyUiVersionPinTests` fails if the bundle and the `ui.css` sentence disagree, if the MIT header went missing, or if the compiled sheet no longer carries the themes the bundle defines; `UiClassNamesTests` fails if the new bundle stopped emitting a class the kit writes, which is the silent failure mode (the component renders unstyled and the build stays green). **There is a SECOND daisyUI pin:** the 13 front-end templates install it from npm, as `DaisyUiRange` in `src/Rask.Cli/Scaffolding/ProjectGenerator.Spa.cs`, because a JS lane has a package tree while the standalone engine does not. `DaisyUiVersionPinTests` fails if the range stops accepting the vendored version, so bump the two together — otherwise the same starter page renders differently depending on which half of the toolchain compiled its stylesheet. |
| Adapter fixtures | `tests/Rask.External.Tests/Rask.External.Tests.csproj` → `RaskPreactFixtureVersion`, `RaskHappyDomFixtureVersion`, `RaskReactFixtureVersion` (react and react-dom), `RaskVueFixtureVersion` and `RaskSolidFixtureVersion`. Exact versions, npm-installed together into `obj/preact-fixture` for `PreactAdapterTests`, `ReactAdapterTests`, `VueAdapterTests`, `SolidAdapterTests` and `LitAdapterTests` (happy-dom only), which are the only coverage those adapters have below a browser — island children included. Bump, delete `tests/Rask.External.Tests/obj/preact-fixture` (the install is stamped) and run the unit gate; a major that changed a framework's render, reconcile or unmount semantics fails there, which is the point of the pins. |
| Node build floor | `Rask.Spa.Hosting.props` → `RaskSpaMinimumNode`, `Rask.External.props` → `RaskExternalMinimumNode`. Both 22.12.0, both vite's requirement. |
| Node scaffold line | `src/Rask.Cli/NodeRequirement.cs` → `ScaffoldLine`. **The source of truth**; everything else is held to it by `NodeRequirementTests`. |
| npm ranges for scaffolded apps | `ProjectGenerator.Spa.cs` → `TailwindRange`, `QueryRange`, `SvelteQueryRange`, `LitQueryRange`, `RouterRange`. Caret ranges, so they float within a major — check for a **major** bump, which is the case a caret hides. |

**Deliberately unpinned, leave alone:** the external scaffolders (`create-vite@latest`,
`@angular/cli@latest`) and island dependencies, which come from the user's own `package.json`.

## 3. Node

`lts-watch.yml` reports this monthly, but to check by hand:

```bash
curl -fsS https://nodejs.org/dist/index.json | grep -o '"lts":"[^"]*"' | head -1
```

Raise `NodeRequirement.ScaffoldLine`, then run the unit gate — the tests name every file that has to
follow. Do not touch the places quoting **Angular's** floor (`^22.22.3 || ^24.15.0 || >=26.0.0`);
that is a fact about someone else's CLI. The build floor is a separate, lower number on purpose.

## 4. Apply + verify

Edit the pins, then run the **`rask-ship`** gate (warnings-as-errors, so analyzer-rule changes from an
SDK bump surface immediately). Note the bump in `CHANGELOG.md` when it is user-visible; routine
Dependabot patch waves are not changelogged.

**Land a Dependabot PR locally, never from the web UI.** Its PRs are authored server-side, so they
never touch `.githooks/` — and `main` has no required checks, so a merge from the web is a version
change that nothing built, formatted, or tested. Check the branch out, let `pre-commit` and
`pre-push` run, and push. `Directory.Packages.props` is in `pre-push`'s `generator_paths`, so the CLI
build gate runs too; budget for it.

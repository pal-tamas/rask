# Contributing to Rask

Thanks for your interest in Rask — a C# component framework (Blazor-like) with a Roslyn
factory generator, scoped CSS/JS, routing, and a live diff runtime over WebSockets
(Server) or `JSImport`/`JSExport` (WASM).

**Contributions are open.** Anyone can [open an issue](https://github.com/pal-tamas/rask/issues/new/choose)
or send a pull request (fork → branch → PR). Review and merge are handled by the maintainer
(@pal-tamas) — see [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) and the
[full development workflow](docs/development-workflow.md).

## Prerequisites

- .NET 10 SDK (`net10.0`; `net10.0-browser` for the WASM projects).
- For the E2E suite: Playwright browsers (`pwsh tests/Rask.Examples.E2E.Tests/bin/.../playwright.ps1 install`).

## Build & test loop

```bash
dotnet build
dotnet test

# Faster inner loop — skip the heavy Playwright E2E suite:
dotnet test --filter "FullyQualifiedName!~Rask.Examples.E2E"

# A single class:
dotnet test --filter FullyQualifiedName~SessionUploadStoreTests

# Run a sample app:
dotnet run --project samples/Rask.Example.Server
dotnet run --project samples/Rask.Example.Wasm.Host
```

The WASM trimming path is load-bearing: `samples/Rask.Example.Wasm` must
`dotnet publish -c Release` with **zero IL trim warnings**. Any new reflection there needs
a `[DynamicallyAccessedMembers]` annotation or a justified `[UnconditionalSuppressMessage]`.

## Testing expectations

- **Unit-test first.** Every bug fix and new feature gets a unit test; reach for E2E only
  when a unit test genuinely can't reach the path (the Playwright suite is heavy).
- New tests mirror the layout and style of the sibling `+ Tests` project.
- `Highlight_DeepLinkToCodeSamplePage_HighlightsOnFirstPaint` is a known first-paint flake
  — rerun before assuming a regression.

## Repository layout

| Path | What lives there |
|------|------------------|
| `src/Rask.Core/` | Rendering, live diff codec, routing, lifecycle, scoped CSS/JS, primitives. |
| `src/Rask.Generators/` | Roslyn factory/route generators and analyzers (RASK001–029). |
| `src/Rask.Server/`, `src/Rask.Wasm/`, `src/Rask.Wasm.Hosting/` | The three hosts. |
| `src/Rask.Cli/` | The `rask` CLI — scaffolds every project via `rask new` (server, wasm, wasm-hosted, native). |
| `samples/` | Runnable feature showcases. | 
| `tests/`, `benchmarks/` | Test suites and render hot-path baselines. |

Most `src/` projects have a sibling `+ Tests` project. Deeper rationale lives in
[`docs/`](docs/README.md) and the [architecture notes](docs/architecture/live-rendering.md).

## Conventions

- **Adding an HTML tag:** add `src/Rask.Core/Components/{Tag}.cs`
  (`sealed class {Tag} : Element`, `TagName` override, `WriteAttributes` calling `base`
  then `AppendAttr` per attribute; void elements set `SelfClosing => true`) plus a
  `tests/Rask.Core.Tests/Components/{Tag}Tests.cs` asserting exact attribute order
  (id, class, style, data-*, then tag-specific). The factory is generated automatically.
- **Don't `new` a `Component`** outside `Rask.Core` — use the generated factory (RASK014).
- Diagnostics RASK001–029 are documented in [docs/diagnostics.md](docs/diagnostics.md);
  the analyzer descriptors are the source of truth.

## Commits & pull requests

- Keep PRs focused; include tests; ensure `dotnet build` (warnings-as-errors) and
  `dotnet test` are green. Run `dotnet format` before committing.
- **[Conventional Commits](https://www.conventionalcommits.org/)** are required and enforced by
  CI (`commitlint`): `type(scope): subject` with type ∈
  `feat, fix, perf, refactor, docs, test, build, ci, chore, revert`. The local git hooks are **enabled
  automatically on your first `dotnet build`** (a `Directory.Build.targets` target points git at
  `.githooks/`; skipped in CI and for restored packages) — or enable them by hand with
  `git config core.hooksPath .githooks`. That installs the `commit-msg` (Conventional Commits) hook, the
  `pre-commit` hook that runs the local **format + unit** gate, and the `pre-push` hook that runs the local
  **E2E** gate (see below). Hooks are advisory — bypass any with the git no-verify flag,
  `RASK_SKIP_UNIT=1`, or `RASK_SKIP_E2E=1`.
- **Tests run locally, not in CI.** The unit/integration suite and both E2E suites were moved out of the
  CI pipeline; CI keeps only the deterministic benchmark byte-gates, the native compile gate, commitlint,
  and CodeQL.
- **Format + unit tests — `pre-commit`.** The `pre-commit` hook runs `scripts/run-unit-local.sh` when a
  commit stages code (`src/`, `tests/`, `benchmarks/`, `Rask.slnx`, `Directory.*`); docs-only commits skip
  it. The script builds once, runs `dotnet format whitespace --verify-no-changes`, then every test except
  the browser E2E. (It uses the *whitespace* pass, not full `dotnet format`: full format's style/analyzer
  passes run their own compile of the `Routes.*` source generator and spuriously report CS1503 in the
  routing tests. Run `dotnet format Rask.slnx` before a PR for the style pass — the warnings-as-errors build
  already enforces error-severity analyzer rules.) Bypass with `git commit --no-verify` or `RASK_SKIP_UNIT=1`.
- **E2E runs locally, not in CI.** The browser-journey E2E (`tests/Rask.Examples.E2E.Tests`, Playwright)
  and the on-device native E2E (`tests/Rask.Native.Appium.Tests`, Appium) are not part of the CI
  pipeline. Run the browser gate with `scripts/run-e2e-local.sh` (the `pre-push` hook runs it for you on
  `git push`; bypass a docs-only push with `git push --no-verify` or `RASK_SKIP_E2E=1`). The on-device
  native suite needs a booted emulator/simulator + Appium — run it manually before shipping native
  changes (see [docs/native.md](docs/native.md)). CI still runs unit/integration tests, the deterministic
  benchmark byte-gates, and a native compile gate.
- **Do not** append `Co-Authored-By` or `Generated-with` footers to commits or PR descriptions.
- Add a note to [`CHANGELOG.md`](CHANGELOG.md) under `[Unreleased]` for user-visible changes.
- User-facing changes must update a sample under `samples/` and the relevant docs
  (`docs/`, `README.md`, `NUGET.md`). See the [development workflow](docs/development-workflow.md).
- The maintainer merges (squash); the branch is deleted afterwards.

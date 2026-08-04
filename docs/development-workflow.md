# Development workflow

How changes are made, verified, and shipped in this repo. GitHub is the single source of truth;
everything here is committed. AI assistants encode these as playbooks under `.claude/skills/`
(see [`AGENTS.md`](../AGENTS.md) / `CLAUDE.md`).

## The definition-of-done gate

Every change passes this gate before a PR (the `rask-ship` skill):

1. **Format + analyzers** — `dotnet format Rask.slnx` then `--verify-no-changes`.
2. **Clean build, warnings-as-errors** —
   `dotnet build Rask.slnx -c Release -warnaserror -p:EnforceCodeStyleInBuild=true`.
   Enforced in `Directory.Build.props` (`TreatWarningsAsErrors`, `EnableNETAnalyzers`,
   `EnforceCodeStyleInBuild`), so a plain build enforces it too. See [code-analysis.md](code-analysis.md).
3. **Tests** — unit-test every feature/fix (`tests/Rask.*.Tests`); add E2E only when a unit test
   can't reach the path. **Every `samples/` change gets an E2E** journey update
   (`tests/Rask.Examples.E2E.Tests`). Inner loop — **build once, then test with `--no-build`** so
   each run doesn't rebuild the whole solution (test execution itself is fast; the build dominates):
   ```bash
   dotnet build Rask.slnx -c Release
   dotnet test Rask.slnx -c Release --no-build --filter "FullyQualifiedName!~Rask.Examples.E2E"
   ```
   Narrow further to one project (`dotnet test tests/Rask.Core.Tests --no-build`) or one class
   (`--filter FullyQualifiedName~ATests`) while iterating. The build runs in parallel by default —
   don't add `-m:1` (the former WASM copy-race workaround is fixed at the source in
   `Rask.Wasm.Hosting.targets`).
4. **Benchmarks** — any render/live-runtime hot-path change runs `benchmarks/Rask.Benchmarks`
   before/after and quotes the `Allocated` delta in the PR.
5. **Docs & examples** — user-facing changes update a `samples/` app, the relevant `docs/*.md`,
   `README.md`, `NUGET.md`, `llms.txt`, and the template `AGENTS.md`. Add a `CHANGELOG.md`
   `[Unreleased]` entry (Keep a Changelog).
6. **Review** — security, performance, and memory held together with UX; prefer standard .NET
   APIs over hand-rolled code; refactor duplication you touch (the `rask-review` skill).
7. **PR** — Conventional Commit `type(scope): subject` (enforced by commitlint), structured body,
   no AI-attribution footers; delete the branch after squash-merge.

## Versioning & releases

- **Versions come from git tags via MinVer** (`vX.Y.Z`); assemblies carry `AssemblyVersion`,
  `FileVersion`, and `InformationalVersion` automatically.
- **Stable release:** promote `CHANGELOG.md` `[Unreleased]` to a dated section, tag `vX.Y.Z`,
  push — `release.yml` runs the unit gate, packs the NuGets, and publishes to nuget.org + a GitHub
  release (the `cut-release` skill). Run the local E2E gate (`scripts/run-e2e-local.sh`) before tagging.
- **Nightly:** every push to `main` runs `nightly.yml` — unit gate, then packs the MinVer
  prerelease versions and publishes them to nuget.org (prerelease) and GitHub Packages.

## CI

- `ci.yml` — the deterministic benchmark byte-gates and a native compile gate (both native samples ×
  android/ios). **Tests do not run in CI** — the unit/integration suite and the E2E suites run locally
  (see below).
- **Format + unit tests run locally, enforced before commit.** `scripts/run-unit-local.sh` builds the
  solution once, runs `dotnet format whitespace --verify-no-changes`, then every test except the browser
  E2E. (Whitespace pass, not full `dotnet format`: full format's style/analyzer passes recompile the
  `Routes.*` source generator through their own workspace and spuriously flag CS1503 in the routing tests;
  the whitespace pass is compile-independent and reliable. The warnings-as-errors build already enforces
  error-severity analyzer rules — run full `dotnet format Rask.slnx` before a PR for the style pass.) The
  `.githooks/pre-commit` hook runs it whenever a commit stages code (enable hooks with
  `git config core.hooksPath .githooks`; bypass with `git commit --no-verify` or `RASK_SKIP_UNIT=1`).
- **E2E runs locally, enforced before push.** The browser-journey E2E
  (`tests/Rask.Examples.E2E.Tests`, Playwright) and the on-device native E2E
  (`tests/Rask.Native.Appium.Tests`, Appium) were moved out of the CI pipeline. Run the browser gate
  with `scripts/run-e2e-local.sh`; the `.githooks/pre-push` hook runs it on `git push` (enable hooks
  with `git config core.hooksPath .githooks`; bypass with `git push --no-verify` or `RASK_SKIP_E2E=1`).
  The on-device native suite needs an emulator/simulator + Appium — run it manually (see
  [native.md](native.md)).
- **The CLI build gate runs locally, enforced before push.** `scripts/run-cli-build-e2e.sh` is the only
  thing proving the code the CLI *writes* actually compiles — every other CLI test asserts on generated
  strings. It packs this commit's Rask packages to a local feed, scaffolds every `rask new` flag
  combination plus a multi-entity `rask generate feature` and the whole [tutorial](tutorial/00-overview.md)
  walk-through, then builds each one with `-warnaserror`. Because it packs 15 packages and runs several
  full builds it is too slow for the pre-commit loop, so the `.githooks/pre-push` hook runs it instead
  (bypass with `git push --no-verify` or `RASK_SKIP_CLI_BUILD_E2E=1`). The gates are opted into by
  `RASK_CLI_BUILD_E2E=1`, which the script exports; without it every case reports **SKIPPED** rather than
  passing silently, so an un-run gate is always visible in the test output.
- `commitlint.yml` — Conventional Commits check on PRs.
- `nightly.yml` — prerelease publish on `main`.
- `release.yml` — tag-triggered stable publish.
- Dependencies are kept current by `.github/dependabot.yml` (NuGet + Actions, weekly) and the
  `check-nuget-updates` skill.

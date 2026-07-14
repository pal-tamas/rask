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

- `ci.yml` — unit/integration gate, deterministic benchmark byte-gates, and a native compile gate
  (both native samples × android/ios). **E2E does not run in CI** (see below).
- **E2E runs locally, enforced before push.** The browser-journey E2E
  (`tests/Rask.Examples.E2E.Tests`, Playwright) and the on-device native E2E
  (`tests/Rask.Native.Appium.Tests`, Appium) were moved out of the CI pipeline. Run the browser gate
  with `scripts/run-e2e-local.sh`; the `.githooks/pre-push` hook runs it on `git push` (enable hooks
  with `git config core.hooksPath .githooks`; bypass with `git push --no-verify` or `RASK_SKIP_E2E=1`).
  The on-device native suite needs an emulator/simulator + Appium — run it manually (see
  [native.md](native.md)).
- `commitlint.yml` — Conventional Commits check on PRs.
- `nightly.yml` — prerelease publish on `main`.
- `release.yml` — tag-triggered stable publish.
- Dependencies are kept current by `.github/dependabot.yml` (NuGet + Actions, weekly) and the
  `check-nuget-updates` skill.

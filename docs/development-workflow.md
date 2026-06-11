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
   (`tests/Rask.Examples.E2E.Tests`). Inner loop:
   `dotnet test Rask.slnx --filter "FullyQualifiedName!~Rask.Examples.E2E"`.
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
  push — `release.yml` runs the unit gate + sharded E2E, packs the six NuGets, and publishes to
  nuget.org + a GitHub release (the `cut-release` skill).
- **Nightly:** every push to `main` runs `nightly.yml` — unit gate, then packs the MinVer
  prerelease versions and publishes them to nuget.org (prerelease) and GitHub Packages.

## CI

- `ci.yml` — unit gate + sharded E2E matrix (one host per runner) on PRs/`main`.
- `commitlint.yml` — Conventional Commits check on PRs.
- `nightly.yml` — prerelease publish on `main`.
- `release.yml` — tag-triggered stable publish.
- Dependencies are kept current by `.github/dependabot.yml` (NuGet + Actions, weekly) and the
  `check-nuget-updates` skill.

# AGENTS.md — contributing to Rask with an AI assistant

Cross-tool guide for AI assistants working **on the Rask framework itself** — Rask is the full-stack .NET web
framework, for a team of one or fifty (UI, data, auth, background work, realtime and deploy, all in C#; the
philosophy is `docs/one-person-framework.md`). (The Claude-specific map is `CLAUDE.md`; guidance for an assistant writing an
app ON Rask is `docs/ai-agents.md` and `llms.txt` — `rask new` scaffolds no `AGENTS.md`, deliberately.)
GitHub is the source of truth — keep docs, examples, and these guides up to date with every change.

## Repo workflows (`.claude/skills/`)
Apply the matching playbook automatically:
- **rask-ship** — definition-of-done gate before any commit.
- **add-html-tag** / **add-diagnostic** / **add-codefix** — elements from MDN (refresh + hand partial) / RASK0xx+docs+test /
  IDE quick-fix+test). **run-benchmarks** — hot-path Allocated delta.
- **run-rask** / **run-rask-cli** — build, launch and drive the real thing (the site, the `rask` CLI)
  when a test passing isn't the same as it working.
- **rask-review** — security/perf/memory/best-practices. **land-on-main** — Conventional-Commit, land straight on `main`.
- **rask-seo** — search + AI-assistant discoverability, on every site/docs/package-metadata change.
  **rename-public-member** — one-pass Roslyn rename of a public name, then baselines, templates and docs.
- **cut-release** — tag `vX.Y.Z`. **check-dependency-updates** — NuGet + the pins outside CPM.
- **flux-component** — a `Rask.Ui` component exactly as Flux UI draws and behaves it, proved with `flux parity`.
  **upstream-follower** — anything generated from or pinned to an upstream release follows it from
  `scripts/upstream/followers.json`.

## The gate (every change)
1. `dotnet format src/Rask.X/Rask.X.csproj --include <the files you changed>` — the project you touched,
   never the solution, and the full pass, not `whitespace`; CI's `analyzers K/N` jobs verify the files
   each change touched after the push.
2. `dotnet build src/Rask.X -c Release -warnaserror` (analyzers clean) — again the project, not the solution.
   The same build runs the **public-API gate**: a public member you added, renamed or removed is an
   error until it is recorded in `src/<Project>/PublicAPI/<tfm>/PublicAPI.Unshipped.txt`. Names obey
   [`docs/api-style.md`](docs/api-style.md); that file's diff is the API review.
3. **Unit test every feature** (`dotnet test tests/Rask.X.Tests`); **E2E test every `src/Rask.Site` change**.
4. **Benchmark every framework/render-hotpath change** (quote the Allocated delta).
5. **User-facing change → update `src/Rask.Site` + docs/README** (keep `docs/`, `README.md`, `NUGET.md`,
   `llms.txt`, and `docs/ai-agents.md` current). Add a `CHANGELOG.md` `[Unreleased]` entry.
6. Review (security + performance + UX together; prefer standard .NET APIs; refactor duplication).
7. Land it on `main` (`type(scope): subject`, Conventional Commits — enforced by `.githooks/commit-msg`): merge
   `origin/main` in, then `git push origin HEAD:main`. **No pull request** — PRs are for external
   contributions from forks. No hook builds or tests: `ci.yml` runs the gates the change can reach AFTER the push
   (`full.yml` runs all of them daily), each job the same `scripts/run-*.sh` you can run by hand. A red
   `main` is fixed forward. Until 1.0 a `ci/**` pre-run is only for a change to CI itself, a Linux-only
   risk, or the release-only gates (`ci/release/**`).

## CI hygiene (`.github/workflows/`)
Keep the annotation panel clean. Pin runners to an explicit image, never a moving `*-latest` label. Keep
`actions/*-artifact` (and other JS actions) on the current major so they run on the supported Node
runtime, not a deprecated one.

## Principles
Do your best on every change. Hold UX, security, and performance together. Don't reinvent the wheel —
use the BCL/framework. SOLID + Clean Code: one responsibility per type and file, small
well-named methods, no copied helpers. Automate what you can. Ask only when genuinely blocked.

See `docs/development-workflow.md` for the full details.

# AGENTS.md — contributing to Rask with an AI assistant

Cross-tool guide for AI assistants working **on the Rask framework itself** (the Claude-specific
map is `CLAUDE.md`; downstream app-authoring guidance ships as `AGENTS.md` inside the templates).
GitHub is the source of truth — keep docs, examples, and these guides up to date with every change.

## Repo workflows (`.claude/skills/`)
Apply the matching playbook automatically:
- **rask-ship** — definition-of-done gate before any commit/PR.
- **add-html-tag** / **add-diagnostic** — scaffolding. **run-benchmarks** — hot-path Allocated delta.
- **rask-review** — security/perf/memory/best-practices. **open-pr** — Conventional-Commit PR, no AI footers.
- **cut-release** — tag `vX.Y.Z`. **check-nuget-updates** — dependency hygiene.

## The gate (every change)
1. `dotnet format Rask.slnx` (+ `--verify-no-changes`).
2. `dotnet build Rask.slnx -c Release -warnaserror -p:EnforceCodeStyleInBuild=true` (analyzers clean).
3. **Unit test every feature**; **E2E test every `samples/` change**.
4. **Benchmark every framework/render-hotpath change** (quote the Allocated delta).
5. **User-facing change → add/update a sample + docs/README** (keep `docs/`, `README.md`, `NUGET.md`,
   `llms.txt`, and template `AGENTS.md` current). Add a `CHANGELOG.md` `[Unreleased]` entry.
6. Review (security + performance + UX together; prefer standard .NET APIs; refactor duplication).
7. Open a PR (`type(scope): subject`, Conventional Commits — enforced by commitlint); delete the
   branch after squash-merge.

## Principles
Do your best on every PR. Hold UX, security, and performance together. Don't reinvent the wheel —
use the BCL/framework. Automate what you can. Ask only when genuinely blocked.

See `docs/development-workflow.md` for the full details.

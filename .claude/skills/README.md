# Rask skills

Committed, shareable playbooks for the recurring Rask workflows. They auto-surface from their
`description` — **apply the matching skill automatically** when a task fits, no need to be asked.

| Skill | Use it when |
|---|---|
| [`rask-ship`](rask-ship/SKILL.md) | Before any commit — the definition-of-done gate (format → warnings-as-errors build → tests → benchmarks → CHANGELOG → review → land on main). |
| [`add-html-tag`](add-html-tag/SKILL.md) | Adding an HTML element to `Rask.Core` (component + ordered-attribute test; factory auto-generated). |
| [`add-diagnostic`](add-diagnostic/SKILL.md) | Adding a RASK0xx generator/analyzer diagnostic (descriptor + `docs/diagnostics.md` + test). |
| [`add-codefix`](add-codefix/SKILL.md) | Adding an IDE quick-fix (CodeFixProvider) for a RASK0xx diagnostic in `Rask.Generators.CodeFixes` + test. |
| [`run-benchmarks`](run-benchmarks/SKILL.md) | Changing render/live-runtime hotpath — before/after `Allocated` delta. |
| [`rask-review`](rask-review/SKILL.md) | Reviewing a diff for security, performance, memory, and .NET/C# best practices. |
| [`land-on-main`](land-on-main/SKILL.md) | Landing a finished change **directly on `main`** (Conventional-Commit, gated merge, `git push origin HEAD:main`) — never a PR. |
| [`cut-release`](cut-release/SKILL.md) | Publishing a version (CHANGELOG promote + `vX.Y.Z` tag → `release.yml`). |
| [`check-dependency-updates`](check-dependency-updates/SKILL.md) | Auditing/bumping every dependency axis — NuGet, the Node LTS line, and the pins Dependabot cannot see. |

`rask-ship` is the orchestrator; the scaffolding skills (`add-html-tag`, `add-diagnostic`) hand
off to it. Each skill is self-contained so it works on a fresh clone.

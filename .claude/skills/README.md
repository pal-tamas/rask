# Rask skills

Committed, shareable playbooks for the recurring Rask workflows. They auto-surface from their
`description` — **apply the matching skill automatically** when a task fits, no need to be asked.

| Skill | Use it when |
|---|---|
| [`rask-ship`](rask-ship/SKILL.md) | Before any commit/PR — the definition-of-done gate (format → warnings-as-errors build → tests → benchmarks → CHANGELOG → review → PR). |
| [`add-html-tag`](add-html-tag/SKILL.md) | Adding an HTML element to `Rask.Core` (component + ordered-attribute test; factory auto-generated). |
| [`add-diagnostic`](add-diagnostic/SKILL.md) | Adding a RASK0xx generator/analyzer diagnostic (descriptor + `docs/diagnostics.md` + test). |
| [`run-benchmarks`](run-benchmarks/SKILL.md) | Changing render/live-runtime hotpath — before/after `Allocated` delta. |
| [`rask-review`](rask-review/SKILL.md) | Reviewing a diff/PR for security, performance, memory, and .NET/C# best practices. |
| [`open-pr`](open-pr/SKILL.md) | Opening a PR (branch off main, Conventional-Commit, no AI attribution, delete branch after merge). |
| [`cut-release`](cut-release/SKILL.md) | Publishing a version (CHANGELOG promote + `vX.Y.Z` tag → `release.yml`). |
| [`check-nuget-updates`](check-nuget-updates/SKILL.md) | Auditing/bumping NuGet dependencies (complements Dependabot). |

`rask-ship` is the orchestrator; the scaffolding skills (`add-html-tag`, `add-diagnostic`) hand
off to it. Each skill is self-contained so it works on a fresh clone.

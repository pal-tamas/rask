# Rask skills

Committed, shareable playbooks for the recurring Rask workflows. They auto-surface from their
`description` — **apply the matching skill automatically** when a task fits, no need to be asked.

| Skill | Use it when |
|---|---|
| [`rask-ship`](rask-ship/SKILL.md) | Before any commit — the definition of done (format → build + test the touched project → benchmarks → CHANGELOG → review → land on main; CI runs the full gates after the push). |
| [`land-on-main`](land-on-main/SKILL.md) | Landing a finished change **directly on `main`** (Conventional Commit, merge `origin/main`, `git push origin HEAD:main`) — never a PR. |
| [`rask-review`](rask-review/SKILL.md) | Reviewing a diff for security, performance, memory, and .NET/C# best practices. |
| [`add-html-tag`](add-html-tag/SKILL.md) | An HTML/SVG element or attribute missing or wrong in `Rask.Core` — refresh the MDN snapshot, a hand partial only for behaviour MDN cannot know. |
| [`add-diagnostic`](add-diagnostic/SKILL.md) | Adding a RASK diagnostic (descriptor + `docs/diagnostics.md` + test). |
| [`add-codefix`](add-codefix/SKILL.md) | Adding an IDE quick-fix (CodeFixProvider) for a RASK diagnostic in `Rask.Generators.CodeFixes` + test. |
| [`rename-public-member`](rename-public-member/SKILL.md) | Renaming a public Rask member across the whole repository in one Roslyn pass. |
| [`flux-component`](flux-component/SKILL.md) | A `Rask.Ui` component exactly as Flux UI draws and behaves it — measure, write, prove with `flux parity`. |
| [`run-benchmarks`](run-benchmarks/SKILL.md) | Changing render/live-runtime hotpath — before/after `Allocated` delta. |
| [`run-rask`](run-rask/SKILL.md) | Building, launching and driving `src/Rask.Site` (and the `/_rask` console) headlessly. |
| [`run-rask-cli`](run-rask-cli/SKILL.md) | Building and smoke-testing the `rask` CLI, including a real `rask new` on disk. |
| [`rask-seo`](rask-seo/SKILL.md) | Any change to the site, `docs/**`, README/NUGET.md, `llms.txt` or package metadata — search and AI-assistant discoverability. |
| [`upstream-follower`](upstream-follower/SKILL.md) | Something generated from or pinned to an upstream release — register it in `scripts/upstream/followers.json`. |
| [`check-dependency-updates`](check-dependency-updates/SKILL.md) | Auditing/bumping every dependency axis — NuGet and the pins Dependabot cannot see. |
| [`cut-release`](cut-release/SKILL.md) | Publishing a version (CHANGELOG promote + `vX.Y.Z` tag → `release.yml`). |

`rask-ship` is the orchestrator; the scaffolding skills (`add-html-tag`, `add-diagnostic`) hand
off to it. Each skill is self-contained so it works on a fresh clone.

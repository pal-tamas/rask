---
name: rask-ship
description: The Rask "definition of done". Use before committing or landing any change in the Rask repo — it formats the changed files, builds and tests the touched project warnings-as-errors (the full gates run in CI after the push, never locally), requires unit tests for new features and E2E tests for site changes, runs benchmarks for framework/render-hotpath changes, updates the CHANGELOG, reviews for security/perf/memory, then lands the change on main directly (no pull request).
---

# rask-ship — definition-of-done gate

Run this before every commit. Each step is a gate: do not advance with a failing step.
Steps fire based on what changed (step 0 classifies). For the slnx use `Rask.slnx`.

**Principles** (apply throughout): do your best on every change; weigh **user experience, security,
and performance together**, never one at the cost of another; prefer **standard .NET / BCL APIs
over hand-rolled code** (don't reinvent the wheel); **refactor opportunistically** when you touch
code that's duplicated or unclear, holding **SOLID + Clean Code** (`docs/code-analysis.md#design`); **automate** whatever can be automated; **ask only when truly
blocked** — otherwise pick the sensible default and proceed.

## 0. Classify the change
Look at `git status` / `git diff --stat` and bucket the change:
- **Framework code** — anything under `src/` (esp. `src/Rask.Core/`, `src/Rask.Server/`, `src/Rask.Wasm/`, `src/Rask.Generators/`).
- **The site** — anything under `src/Rask.Site` (the app published to rask.sh).
- **New HTML tag** → also run the `add-html-tag` skill.
- **New diagnostic (RASK0xx)** → also run the `add-diagnostic` skill.
- **User-facing change** (new/changed component, API, prop, behavior visible to app authors) →
  triggers step 3b (the site + docs/README).
- **Docs only** — `docs/`, `*.md` → step 4 (benchmarks) is skipped; still format/build/review.

The bucket decides whether steps 3/3b/4 apply.

## The rule that keeps this fast: the SOLUTION is CI's, the PROJECT is yours
**Never build, test or format `Rask.slnx` locally, and never run a gate script** (`run-unit-local.sh`,
`run-e2e-local.sh`, `run-all-gates.sh`) as part of shipping. Every worktree doing that at once is what
turned a one-minute gate into ten. After the push `ci.yml` runs the gates your change can reach, each
its own job, and `full.yml` runs every gate behind every push, one run at a time.
Locally you prove the thing you changed, in the project you changed, and move on.

Before you start: `gh run list --workflow ci --branch main --limit 3`, and the same with
`--workflow full` — the run of everything behind each push, which is where a break a scoped push could not see
shows up. A red `main` is fixed first —
read the failing job (`gh run view <id> --log-failed`), reproduce it with the script the job names.

## 1. Format the files you changed
```bash
dotnet format src/Rask.X/Rask.X.csproj --include <the .cs files you changed>
```
The project, not the solution, and only your files. CI runs the full `--verify-no-changes`; import
ordering is caught by `dotnet format` alone, never by the build, so do not skip this.

## 2. Build the project you touched — warnings as errors
```bash
dotnet build src/Rask.X -c Release -warnaserror
```
Zero warnings: .NET analyzers (CAxxxx), Meziantou (MAxxxx), Roslynator (RCSxxxx), Sonar (Sxxxx),
banned APIs (RS0030), code-style (IDExxxx), nullable, and Rask's own RASK0xx generators. FIX a finding;
silence one only when the code cannot satisfy it, at that site, with a `#pragma` reason (docs/code-analysis.md). The WASM trimming path must stay IL-warning-free — if you touched anything
reflection-adjacent, also:
```bash
dotnet publish src/Rask.Site -c Release   # zero IL trim warnings required
```

### The public-API gate
Anything public that you added, renamed or removed fails this build until it is recorded in
`src/<Project>/PublicAPI/<tfm>/PublicAPI.Unshipped.txt` — RS0016 for a member that is missing,
RS0017 for an entry with nothing behind it. The signature the file wants is quoted verbatim in the
RS0016 message; the IDE's "Add to public API" quick-fix writes it for you.

Do not route around it. The diff in those files **is** the API review — read it as a reviewer would
before you commit, against the rules in `docs/api-style.md`. A name you would not want to explain in
a review comment is a name to change now, while changing it is free.

## 3. Tests — unit-first; E2E for site changes
Policy: **unit-test first** for every new feature/bug fix; add E2E **only** when a unit test
can't reach the path (E2E is heavy). **Any `src/Rask.Site` change requires an E2E** journey update.

- Add/adjust unit tests in the sibling `tests/Rask.*.Tests` project (e.g. `tests/Rask.Core.Tests`).
- For `src/Rask.Site` changes, extend the journey in `tests/Rask.Site.E2E.Tests`
  (one comprehensive journey — see `SharedSmokeTests` / `SharedSmokeTests.Journey.cs`).

Run:
```bash
dotnet test tests/Rask.X.Tests --filter FullyQualifiedName~TheClassYouTouched
```
The rest of the unit suite and every browser journey run in CI after the push — write the journey,
do not run the suite. Reproduce a red E2E job with `RASK_E2E_FILTER=<test> bash scripts/run-e2e-local.sh`.

The E2E suite drives ONE published app now (`src/Rask.Site`), served by a plain static host the way
GitHub Pages serves it — so it must be published before it can be driven, which is what that script
does. `SiteExampleTests` covers the landing page, `WasmExampleTests` and `WasmIslandsExampleTests` the
showcase.

## 3b. User-facing changes → sample + docs (keep everything up to date)
If the change is visible to app authors (new/changed component, API, prop, default, behavior):
- **Add or update a demo** in `src/Rask.Site` (and extend the E2E journey in
  `tests/Rask.Site.E2E.Tests` — every `src/Rask.Site` change needs E2E).
- **Update the docs**: the relevant `docs/*.md` guide, the matching section of `README.md`, and
  `docs/diagnostics.md` if a RASK0xx changed. Keep the AI guides current too
  (`docs/ai-agents.md`, root `llms.txt`, and the `AGENTS.md` in the repo root).
- Nothing user-facing ships without a runnable demo on the site + updated docs.
- **Discoverability**: a change to `src/Rask.Site`, `docs/`, `README.md`/`NUGET.md` or a package's
  `Description`/`PackageTags` → run the **`rask-seo`** skill. A new guide needs its `SearchTitle` and
  `Description` (it will not compile without them); a new page needs `PageMeta.For`.

## 4. Benchmarks (framework/render-hotpath changes only)
If you changed render/live-runtime code → run the **`run-benchmarks`** skill, capture the
`Allocated` before/after delta, and quote it in the commit body. Do not skip this for hotpath changes.

## 5. CHANGELOG
Add an entry under `## [Unreleased]` in `CHANGELOG.md` (Keep a Changelog format: `### Added/
Changed/Fixed/Security/Performance/...`) in the same commit.

## 6. Review — security / performance / memory
Run the **`rask-review`** skill on the diff and address findings before submitting.

## 7. Land it on `main`
Run the **`land-on-main`** skill: commit, merge `origin/main` in, `git push origin HEAD:main` — seconds,
nothing gates it. CI reports afterwards; do not wait for it. **Do not open a pull request** — PRs are for
external contributions only. Never add a `Co-Authored-By` or `Generated-with` footer —
`.githooks/commit-msg` rejects it.

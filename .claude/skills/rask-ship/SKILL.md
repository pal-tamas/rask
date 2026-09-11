---
name: rask-ship
description: The Rask "definition of done" gate. Use before committing or landing any change in the Rask repo — it formats with dotnet format (.editorconfig), enforces a warnings-as-errors analyzer-clean build, requires unit tests for new features and E2E tests for site changes, runs benchmarks for framework/render-hotpath changes, updates the CHANGELOG, reviews for security/perf/memory, then lands the change on main directly (no pull request).
---

# rask-ship — definition-of-done gate

Run this before every commit. Each step is a gate: do not advance with a failing step.
Steps fire based on what changed (step 0 classifies). For the slnx use `Rask.slnx`.

**Principles** (apply throughout): do your best on every change; weigh **user experience, security,
and performance together**, never one at the cost of another; prefer **standard .NET / BCL APIs
over hand-rolled code** (don't reinvent the wheel); **refactor opportunistically** when you touch
code that's duplicated or unclear; **automate** whatever can be automated; **ask only when truly
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

## 1. Format + analyzers
```bash
dotnet format Rask.slnx                      # applies .editorconfig style + analyzer fixers
dotnet format Rask.slnx --verify-no-changes  # must exit 0
```
The `pre-commit` gate (`scripts/run-unit-local.sh`) runs the verify itself, so this step is a fast
pre-check, not the last line of defence. Run the full pass — not `dotnet format whitespace`: import
ordering is caught by `dotnet format` alone, never by the warnings-as-errors build.

If it reports CS1503 in the routing tests, the generators are missing from `bin/Debug` — `dotnet format`
evaluates the solution in the default configuration. Fix it, don't work around it:
```bash
for p in src/*.Generators/*.csproj; do dotnet build "$p" -c Debug --nologo -v quiet; done
```

## 2. Clean build — warnings as errors
```bash
dotnet build Rask.slnx -c Release -warnaserror -p:EnforceCodeStyleInBuild=true
```
Zero warnings: .NET analyzers (CAxxxx), code-style (IDExxxx), nullable, and Rask's own
RASK0xx generators. The WASM trimming path must stay IL-warning-free — if you touched anything
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
dotnet test Rask.slnx --filter "FullyQualifiedName!~Rask.Site.E2E"   # fast inner loop
# the site changed → the browser suite, which needs the published bundle:
bash scripts/run-e2e-local.sh
```
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
Run the **`land-on-main`** skill: commit, merge `origin/main` in (with `--no-commit`, or the merge
lands ungated), then `git push origin HEAD:main`. **Do not open a pull request** — PRs are for
external contributions only. Never add a `Co-Authored-By` or `Generated-with` footer —
`.githooks/commit-msg` rejects it.

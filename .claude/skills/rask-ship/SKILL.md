---
name: rask-ship
description: The Rask "definition of done" gate. Use before committing or opening a PR for any change in the Rask repo — it formats with dotnet format (.editorconfig), enforces a warnings-as-errors analyzer-clean build, requires unit tests for new features and E2E tests for example-app changes, runs benchmarks for framework/render-hotpath changes, updates the CHANGELOG, reviews for security/perf/memory, then opens a PR with no AI attribution.
---

# rask-ship — definition-of-done gate

Run this before every commit/PR. Each step is a gate: do not advance with a failing step.
Steps fire based on what changed (step 0 classifies). For the slnx use `Rask.slnx`.

**Principles** (apply throughout): do your best on every PR; weigh **user experience, security,
and performance together**, never one at the cost of another; prefer **standard .NET / BCL APIs
over hand-rolled code** (don't reinvent the wheel); **refactor opportunistically** when you touch
code that's duplicated or unclear; **automate** whatever can be automated; **ask only when truly
blocked** — otherwise pick the sensible default and proceed.

## 0. Classify the change
Look at `git status` / `git diff --stat` and bucket the change:
- **Framework code** — anything under `src/` (esp. `src/Rask.Core/`, `src/Rask.Server/`, `src/Rask.Wasm/`, `src/Rask.Generators/`).
- **Example app** — anything under `samples/`.
- **New HTML tag** → also run the `add-html-tag` skill.
- **New diagnostic (RASK0xx)** → also run the `add-diagnostic` skill.
- **User-facing change** (new/changed component, API, prop, behavior visible to app authors) →
  triggers step 3b (sample + docs/README).
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
dotnet publish samples/Rask.Example.Wasm -c Release   # zero IL trim warnings required
```

### The public-API gate
Anything public that you added, renamed or removed fails this build until it is recorded in
`src/<Project>/PublicAPI/<tfm>/PublicAPI.Unshipped.txt` — RS0016 for a member that is missing,
RS0017 for an entry with nothing behind it. The signature the file wants is quoted verbatim in the
RS0016 message; the IDE's "Add to public API" quick-fix writes it for you.

Do not route around it. The diff in those files **is** the API review — read it as a reviewer would
before you commit, against the rules in `docs/api-style.md`. A name you would not want to explain in
a PR comment is a name to change now, while changing it is free.

## 3. Tests — unit-first; E2E for example changes
Policy: **unit-test first** for every new feature/bug fix; add E2E **only** when a unit test
can't reach the path (E2E is heavy). **Any `samples/` change requires an E2E** journey update.

- Add/adjust unit tests in the sibling `tests/Rask.*.Tests` project (e.g. `tests/Rask.Core.Tests`).
- For `samples/` changes, extend the host's journey in `tests/Rask.Examples.E2E.Tests`
  (one comprehensive journey per host — see `SharedSmokeTests` / `SharedSmokeTests.Journey.cs`).

Run:
```bash
dotnet test Rask.slnx --filter "FullyQualifiedName!~Rask.Examples.E2E"   # fast inner loop
# example changed → run that host's E2E:
dotnet test tests/Rask.Examples.E2E.Tests --filter "FullyQualifiedName~Rask.Examples.E2E.Tests.{Host}"
```
`{Host}` ∈ ServerExampleTests, WasmExampleTests, StandaloneWasmExampleTests, WasmSubPathExampleTests,
AuthExampleTests, JwtServerAuthExampleTests, WasmCookieAuthExampleTests, WasmJwtAuthExampleTests.
(E2E flake note: `Highlight_DeepLinkToCodeSamplePage_HighlightsOnFirstPaint` has a first-paint
race — rerun before assuming a regression.)

## 3b. User-facing changes → sample + docs (keep everything up to date)
If the change is visible to app authors (new/changed component, API, prop, default, behavior):
- **Add or update a sample** demonstrating it under `samples/` (and extend that host's E2E
  journey in `tests/Rask.Examples.E2E.Tests` — every `samples/` change needs E2E).
- **Update the docs**: the relevant `docs/*.md` guide, the matching section of `README.md`, and
  `docs/diagnostics.md` if a RASK0xx changed. Keep the AI guides current too
  (`docs/ai-agents.md`, root `llms.txt`, and the `AGENTS.md` in the repo root).
- Nothing user-facing ships without a runnable example + updated docs.

## 4. Benchmarks (framework/render-hotpath changes only)
If you changed render/live-runtime code → run the **`run-benchmarks`** skill, capture the
`Allocated` before/after delta, and quote it in the PR. Do not skip this for hotpath changes.

## 5. CHANGELOG
Add an entry under `## [Unreleased]` in `CHANGELOG.md` (Keep a Changelog format: `### Added/
Changed/Fixed/Security/Performance/...`) in the same commit.

## 6. Review — security / performance / memory
Run the **`rask-review`** skill on the diff and address findings before submitting.

## 7. Open the PR
Run the **`open-pr`** skill. Never add `Co-Authored-By` or `Generated-with` footers to commits
or the PR body.

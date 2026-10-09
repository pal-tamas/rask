# Contributing to Rask

Thanks for your interest in Rask — a C# component framework (Blazor-like) with Roslyn
generators for chain markup and routes, scoped CSS/TypeScript, routing, and a live diff runtime over WebSockets
(Server) or `JSImport`/`JSExport` (WASM).

**Contributions are open.** Anyone can [open an issue](https://github.com/pal-tamas/rask/issues/new/choose)
or send a pull request (fork → branch → PR). Review and merge are handled by the maintainer
(@pal-tamas) — see [`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) and the
[full development workflow](../docs/development-workflow.md).

## Prerequisites

- .NET 10 SDK (`net10.0`; `net10.0-browser` for the WASM projects).
- For the E2E suite: Playwright browsers (`pwsh tests/Rask.Site.E2E.Tests/bin/.../playwright.ps1 install`).

## Build & test loop

```bash
dotnet build Rask.slnx

# The inner loop. Bare `dotnet test` pulls in the Playwright browser suite, which needs the published
# site and takes minutes — this is what you want while you work:
dotnet test Rask.slnx --filter "FullyQualifiedName!~Rask.Site.E2E"

# A single class:
dotnet test Rask.slnx --filter FullyQualifiedName~SessionUploadStoreTests

# The two main gates, exactly as CI runs them (see "Commits & pull requests" below):
scripts/run-unit-local.sh      # format + everything except the browser E2E
scripts/run-e2e-local.sh       # build, publish the site, then the browser journeys

# Run the site (the app behind rask.sh — landing page, guides and every demo):
dotnet run --project src/Rask.Site
```

The WASM trimming path is load-bearing: `src/Rask.Site` must
`dotnet publish -c Release` with **zero IL trim warnings**. Any new reflection there needs
a `[DynamicallyAccessedMembers]` annotation or a justified `[UnconditionalSuppressMessage]`.

## Testing expectations

- **Unit-test first.** Every bug fix and new feature gets a unit test; reach for E2E only
  when a unit test genuinely can't reach the path (the Playwright suite is heavy).
- New tests mirror the layout and style of the sibling `+ Tests` project.
- `Highlight_DeepLinkToCodeSamplePage_HighlightsOnFirstPaint` is a known first-paint flake
  — rerun before assuming a regression.

## Repository layout

| Path | What lives there |
|------|------------------|
| `src/Rask.Core/` | Rendering, live diff codec, routing, lifecycle, scoped CSS/JS, primitives. |
| `src/Rask.Generators/` | Roslyn chain/route generators and analyzers (RASK001–102; see [docs/diagnostics.md](../docs/diagnostics.md)). |
| `src/Rask.Server/`, `src/Rask.Wasm/`, `src/Rask.Spa.Hosting/` | The host packages. |
| `src/Rask.Cli/` | The `rask` CLI — scaffolds every project via `rask new` (server, wasm). |
| `src/Rask.Site` | The app published to rask.sh: landing page, guides and every runnable demo. |
| `tests/` | Test suites, and the `tests/Rask.Benchmarks*` render hot-path baselines. |

Most `src/` projects have a sibling `+ Tests` project. Deeper rationale lives in
[`docs/`](../docs/README.md) and the [architecture notes](../docs/architecture/live-rendering.md).

## Conventions

- **HTML and SVG elements are generated from MDN** (`src/Rask.Core/Dom/mdn.snapshot.json`), not written
  by hand. A missing tag or attribute is a snapshot refresh; a hand-written partial is only for
  behaviour MDN cannot know. Tests assert the exact attribute order — preserve it.
- **Markup is a chain** — `Div.Class("panel")[Span["hi"]]`. Don't `new` a `Component` outside
  `Rask.Core` (RASK014).
- Diagnostics RASK001–102 are documented in [docs/diagnostics.md](../docs/diagnostics.md);
  the analyzer descriptors are the source of truth.

## Commits & pull requests

- Keep PRs focused; include tests; ensure `dotnet build` (warnings-as-errors) and
  `dotnet test` are green. CI checks `dotnet format` on your pull request — run it by hand first if
  you'd rather not wait for the run to tell you.
- **[Conventional Commits](https://www.conventionalcommits.org/)** are required and enforced by
  CI (`commitlint`): `type(scope): subject` with type ∈
  `feat, fix, perf, refactor, docs, test, build, ci, chore, revert`. The local git hooks are **enabled
  automatically on your first `dotnet build`** (a `Directory.Build.targets` target points git at
  `.githooks/`; skipped in CI and for restored packages) — or enable them by hand with
  `git config core.hooksPath .githooks`. The hooks take seconds and build nothing: `commit-msg`
  (Conventional Commits, no attribution trailers), `pre-commit` (the Counter sample is the same on every front door,
  checked only when `README.md` or `NUGET.md` is staged) and `pre-push` (the attribution check
  again, over the commits being pushed). Bypass any with the git no-verify flag.

  `core.hooksPath` is relative, and git resolves it against the **top level of the worktree you are
  pushing from** — not the main checkout. If a branch contains no `.githooks/` at all, no hook runs
  and nothing reports it.
- **No attribution trailers.** Commit messages carry no `Co-authored-by:`, no `Claude-Session:`, and no
  "Generated with …" footer. This is not a style preference: GitHub's contributor list credits
  co-authors as well as authors, so one such footer puts a second account in the repository sidebar,
  and the only way back off it is a history rewrite — two of them cost a rewrite of all 970 commits
  and a force-push of `main` and 18 release tags. `commit-msg` rejects them at commit time and
  `pre-push` re-checks the commits being pushed, because the commit-time hook only runs once
  `core.hooksPath` is set and a fresh clone has not set it. A human `Signed-off-by:` is fine; the
  rule is table tested in `scripts/tests/attribution-guard.test.sh`. (CI lets Dependabot's own
  sign-off through on the pull requests Dependabot opens; the hooks still reject it.)
- **The gates run in CI, on your pull request.** `ci.yml` runs each gate as its own job: format + unit
  (`scripts/run-unit-local.sh`), the browser E2E (`scripts/run-e2e-local.sh`), the devtools, browser
  SQLite and data demo journeys, the CLI build and the templates. Nothing runs them on your machine
  unless you do. Every job runs one script from `scripts/`, so a red job is reproduced by running the
  script its log names. `commitlint.yml` checks the commit messages and the PR title (the squash
  subject, which no local hook sees). Benchmarks run in no workflow — `scripts/run-benchmarks-local.sh`,
  when a change touches the render path.
- **Format before you push.** `scripts/run-unit-local.sh` builds once, then runs the full
  `dotnet format Rask.slnx --verify-no-changes` (whitespace + style + analyzers) beside every test
  except the browser E2E. The full pass matters because import ordering is caught by nothing else —
  the warnings-as-errors build enforces the analyzer rules, but sorting using directives is
  `dotnet format`'s own job. The script first builds `src/*.Generators` in **Debug**, because
  `dotnet format` evaluates the solution in the default configuration and resolves the generator
  project references to `bin/Debug/`; without those DLLs no source generator runs and the routing tests
  fail with CS1503. Run `dotnet format Rask.slnx` by hand after a Release-only build and you'll see the
  same thing — build the generators in Debug first.
- **Do not** append `Co-Authored-By` or `Generated-with` footers to commits or PR descriptions.
- Add a note to [`CHANGELOG.md`](../CHANGELOG.md) under `[Unreleased]` for user-visible changes.
- User-facing changes must update `src/Rask.Site` and the relevant docs
  (`docs/`, `README.md`, `NUGET.md`). See the [development workflow](../docs/development-workflow.md).
- The maintainer merges (squash); the branch is deleted afterwards.

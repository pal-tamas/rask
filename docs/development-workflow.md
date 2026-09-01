# Development workflow

How changes are made, verified, and shipped in this repo. GitHub is the single source of truth;
everything here is committed. AI assistants encode these as playbooks under `.claude/skills/`
(see [`AGENTS.md`](../AGENTS.md) / `CLAUDE.md`).

## The inner loop

Run the app you're changing with `rask dev`. It wraps `dotnet watch run`, so an edit to a component's
`Render()`, a scoped `.css`/`.ts`, a `[Route]` template or a CQRS handler is applied to the running
process and every open session repaints in place — a "Hot reload applied" pill confirms it. Edits the
runtime can't apply (adding a type, changing a signature) restart the app instead, and the page reloads
itself. [What hot-reloads](cli.md#what-hot-reloads) has the full list, including what doesn't. WASM is
covered — the host serves the client's build output for the session, since a published bundle is trimmed
and could never apply an update.

When something breaks, it says what broke. A save that doesn't compile shows the compiler errors in the
page — not "Reconnecting…" — and clears itself once the code builds; an exception from a handler or an
async lifecycle hook shows over the running app, which stays mounted with its state intact. See
[when the build fails](cli.md#when-the-build-fails).

The framework side of that loop has its own gate, `scripts/run-watch-e2e.sh` — it scaffolds an app, runs
it under a real `dotnet watch`, edits a file, and asserts the change reached the open live session
without it being torn down. It's opt-in (`RASK_WATCH_E2E=1`); run it when you touch the hot-reload
coordinator, the scoped-asset registry, the generated registries, or `rask dev`.

`scripts/run-wasm-watch-e2e.sh` is the browser half of the same thing, opt-in on `RASK_WASM_WATCH_E2E=1`.
Both are run by `.githooks/pre-push` when a push touches the hot-reload path (bypass with
`RASK_SKIP_WATCH_E2E=1`). The WASM one used to be invoked by nothing at all: its tests live in the E2E
project, so the browser gate's namespace-wide filter *selected* them and they then reported **SKIPPED**,
because only this script sets the variable that enables them. Green on every push, never once run.

## The definition-of-done gate

Every change passes this gate before a PR (the `rask-ship` skill):

1. **Format + analyzers** — `dotnet format Rask.slnx` then `--verify-no-changes`. The `pre-commit` gate
   runs the verify for you, so this is a fast pre-check rather than the last line of defence.
2. **Clean build, warnings-as-errors** —
   `dotnet build Rask.slnx -c Release -warnaserror -p:EnforceCodeStyleInBuild=true`.
   Enforced in `Directory.Build.props` (`TreatWarningsAsErrors`, `EnableNETAnalyzers`,
   `EnforceCodeStyleInBuild`), so a plain build enforces it too. See [code-analysis.md](code-analysis.md).
   The same build runs the **public-API gate**: anything public you added, renamed or removed fails
   until it is recorded in `src/<Project>/PublicAPI/<tfm>/PublicAPI.Unshipped.txt`. That diff is the
   API review — read it against [api-style.md](api-style.md) before you commit.
3. **Tests** — unit-test every feature/fix (`tests/Rask.*.Tests`); add E2E only when a unit test
   can't reach the path. **Every `samples/` change gets an E2E** journey update
   (`tests/Rask.Examples.E2E.Tests`). Inner loop — **build once, then test with `--no-build`** so
   each run doesn't rebuild the whole solution (test execution itself is fast; the build dominates):
   ```bash
   dotnet build Rask.slnx -c Release
   dotnet test Rask.slnx -c Release --no-build --filter "FullyQualifiedName!~Rask.Examples.E2E"
   ```
   Narrow further to one project (`dotnet test tests/Rask.Core.Tests --no-build`) or one class
   (`--filter FullyQualifiedName~ATests`) while iterating. The build runs in parallel by default —
   don't add `-m:1` (the former WASM copy-race workaround is fixed at the source in
   `Rask.Wasm.Hosting.targets`).

   `Directory.Build.rsp` turns **MSBuild node reuse off** for every build started from the repository
   root. That is deliberate and load-bearing: the scoped-asset bake is not safe across reused workers
   ([#650](https://github.com/pal-tamas/rask/issues/650)), so with reuse on, publishing two different
   WASM samples in a row fails the second one. It costs about a fifth of a second per incremental
   build. If you are working on `Rask.Wasm.Tasks` itself, note that a reused node also pins the **task**
   assembly — run `dotnet build-server shutdown` before judging any change to it, or you are measuring
   the previous build's DLL.
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
  push — `release.yml` runs the unit gate, packs the NuGets, and publishes to nuget.org + a GitHub
  release (the `cut-release` skill). Run the local E2E gate (`scripts/run-e2e-local.sh`) before tagging.
- **Nightly:** every push to `main` runs `nightly.yml` — unit gate, then packs the MinVer
  prerelease versions and publishes them to nuget.org (prerelease) and GitHub Packages.
- **The released version is the only one left listed.** After publishing, `release.yml` runs
  [`scripts/unlist-old-versions.sh`](../scripts/unlist-old-versions.sh), which unlists every older
  version of each package it just pushed — previous stables and the nightly prereleases alike. A
  nightly cadence puts hundreds of `-alpha` versions on the gallery between releases (`Rask.Server`
  reached 478 versions, only 23 of them stable), and a version list nobody would install is noise on
  every package page.

  **Unlisting is not deletion.** nuget.org gives an owner no way to delete a published version, by
  design, so an unlisted version still restores by exact reference and a pinned `PackageReference`
  keeps building — it is removed from search and the gallery, not from the feed.

  Two deliberate limits. The step is `continue-on-error` and every path in the script exits 0: the
  packages are already pushed by the time it runs, and a tidy-up must never red a released tag. And it
  spends a budget of ~240 calls then stops, because nuget.org rate-limits unlisting to roughly 250
  before a 403 whose retry-after runs to tens of minutes — the remainder is picked up by the next
  release, which supersedes it anyway. The key in `NUGET_API_KEY` needs the **Unlist** scope; a
  push-only key makes the step a no-op with a warning.

  Which versions are superseded is decided by
  [`scripts/lib/unlist_select.py`](../scripts/lib/unlist_select.py) under real semver ordering, table-
  tested in `scripts/tests/unlist-old-versions.test.sh`. Two rules there are load-bearing: nothing
  **newer** than the released version is ever touched, and a **prerelease never retires a stable**
  (by semver `0.21.0` is older than `0.21.1-alpha.0.1`, so without that rule a prerelease tag would
  unlist the current release).

  Candidates come from [`scripts/lib/listed_versions.py`](../scripts/lib/listed_versions.py), which reads
  the **registration** index, not the obvious `v3-flatcontainer/<id>/index.json`. Flat-container reports
  every version ever pushed, unlisted ones included — `rask.native` has all 209 of its versions unlisted
  and flat-container still returns all 209 — so selecting from it would spend the entire quota budget
  re-unlisting finished work and never reach the rest of the backlog. Only the registration index carries
  `listed` per version, and a missing `listed` field means listed.

## CI

- `ci.yml` — the deterministic benchmark byte-gates. **Tests do not run in CI** — the unit/integration
  suite and the E2E suites run locally (see below).
- **Format + unit tests run locally, enforced before commit.** `scripts/run-unit-local.sh` builds the
  solution once, runs the full `dotnet format Rask.slnx --verify-no-changes` (whitespace + style +
  analyzers, one workspace load, ~36s), then every test except the browser E2E. The full pass earns its
  place: import ordering is enforced by `dotnet format` alone — the warnings-as-errors build covers the
  analyzer rules but not the sorting of using directives, which is how a misordered `using` drifted into
  `Rask.Server` unnoticed (#584). Before formatting, the script builds `src/*.Generators` in **Debug**:
  `dotnet format` evaluates the solution in the default configuration, so it resolves the
  `OutputItemType="Analyzer"` references to `bin/Debug/`, and without those DLLs no source generator runs
  — `Routes.*` is never emitted and the routing tests fail to bind with CS1503. That is the real cause of
  the "spurious CS1503" that kept this gate on the whitespace pass alone until #584. The
  `.githooks/pre-commit` hook runs it whenever a commit stages code (enable hooks with
  `git config core.hooksPath .githooks`; bypass with `git commit --no-verify` or `RASK_SKIP_UNIT=1`).
- **E2E runs locally, enforced before push.** The browser-journey E2E
  (`tests/Rask.Examples.E2E.Tests`, Playwright) was moved out of the CI pipeline. Run it with
  `scripts/run-e2e-local.sh`; the `.githooks/pre-push` hook runs it on `git push` (enable hooks
  with `git config core.hooksPath .githooks`; bypass with `git push --no-verify` or `RASK_SKIP_E2E=1`).
  While iterating on **one** journey, narrow the run with `RASK_E2E_FILTER` — the sample publishes still
  happen (they are what the tests boot), but you pay for one journey instead of the whole suite:
  `RASK_E2E_FILTER='FullyQualifiedName~PlaygroundExampleTests' scripts/run-e2e-local.sh`. It says loudly
  that the run was filtered, because a narrowed green is not the gate.

  **Only one browser gate runs at a time.** Two suites on one machine contend for resources, and that
  shows up as a plausible-looking red minutes later with nothing in the log pointing back at it — so the
  gate names the other run (pid, elapsed, worktree) and stops. If you are working across several
  worktrees, this is the thing that stops you diagnosing a failure that was never in your branch.
  `RASK_E2E_ALLOW_CONCURRENT=1` overrides it; treat anything it then reports as suspect until re-run
  alone.

  **What that guard does not cover.** It detects its own kind — a second browser gate. The commoner
  collision is everything else competing with a live suite: a `pre-commit` hook, a plain `dotnet
  build`, a `dotnet publish`, the CLI build gate. None of those is a browser gate, so nothing refuses
  and nothing warns. Two things soften it now, both hints rather than decisions ([#850]):
  `pre-commit` says so before it starts when a browser gate is live (it never refuses — a blocked
  commit is worse than a slow one), and a red suite that finds a heavy build still running names it
  and asks you to re-run alone before investigating. Neither claims your failure is not real; they
  say the run was not clean enough to conclude that it is.

- **Every gate says whether it ran.** The path-filtered gates — CLI build, watch hot-reload, deploy,
  install — used to take a silent branch when nothing in the push matched their paths, printing
  nothing at all. A gate that does not run then looks exactly like one that passed, which is this
  repo's most expensive bug class and the thing [#845] was reported over. Each now prints one
  `… SKIPPED — nothing in this push matches the … paths.` line, so the absence of a gate is visible
  rather than inferred.

- **Hooks in a worktree run the worktree's own copy.** `core.hooksPath` is the relative path
  `.githooks`, and git resolves it against the **pushing worktree's** top level, not the main
  checkout's — so a hook change *is* exercised by the push that introduces it, from a worktree as much
  as from the main clone. Verified on git 2.50.1 by pushing from a linked worktree whose
  `.githooks/pre-push` differed from the main checkout's, from the worktree root and from a
  subdirectory: the worktree's copy ran in both. One caveat worth knowing: if a branch does not
  contain `.githooks/` at all, **no hook runs and nothing says so** — git does not fall back to the
  main checkout's copy.

[#845]: https://github.com/pal-tamas/rask/issues/845
[#850]: https://github.com/pal-tamas/rask/issues/850
- **The payload-bytes gates run locally, enforced before push.** `scripts/run-benchmarks-local.sh`
  checks both wire-byte baselines — the standalone codec numbers and the head-to-head against Blazor —
  byte-for-byte. The numbers are noise-free (no timing: every render emits the same payload shape with
  one value differing), so a change is a real change. `.githooks/pre-push` runs it on every push,
  UNFILTERED unlike the heavier gates below: it costs about a minute, and a hand-listed path filter is
  itself a way for a gate to stop running silently. Bypass with `git push --no-verify` or
  `RASK_SKIP_BENCHMARKS=1`.

  It exists because CI's copy stopped nobody. `ci.yml` runs the same two gates, but `main` has no
  required checks, so the gate rode red through three merges before anyone noticed
  ([#919](https://github.com/pal-tamas/rask/issues/919)).

  **Both gates always run, even when the first fails.** In CI they are two steps in one job, so a
  fail-fast on the first leaves the second unrun — which is how the vs-Blazor baseline stayed broken
  while the standalone one was being fixed, and how a half-fix looked complete. Locally you get both
  answers at once.

  **A regression here means one of two opposite things.** Either the render/diff path got heavier —
  fix the code, do not touch the baseline — or a benchmark scenario's own markup changed, which *does*
  reach a gated number: `AppendRowToList100`'s diff is an `InsertSubtree` whose value is the new row's
  HTML. In that case refresh the baseline in the same commit and say why. The vs-Blazor report tells
  them apart: it records `BlazorBatchBytes` too, and if Blazor's numbers moved by the same amount the
  bytes came from markup both frameworks render, not from anything Rask encodes. Build before
  `--check` — the baseline is read from `bin/`, so `--no-build` compares against a stale copy.
- **The CLI build gate runs locally, enforced before push.** `scripts/run-cli-build-e2e.sh` is the only
  thing proving the code the CLI *writes* actually compiles — every other CLI test asserts on generated
  strings. It packs this commit's Rask packages to a local feed, scaffolds every `rask new` flag
  combination (see the [tutorial](tutorial/00-overview.md) walk-through), then builds each one with
  `-warnaserror`. Because it packs 15 packages and runs several
  full builds it is too slow for the pre-commit loop, so the `.githooks/pre-push` hook runs it instead
  (bypass with `git push --no-verify` or `RASK_SKIP_CLI_BUILD_E2E=1`). The gates are opted into by
  `RASK_CLI_BUILD_E2E=1`, which the script exports; without it every case reports **SKIPPED** rather than
  passing silently, so an un-run gate is always visible in the test output.
- **A red gate names the culprit it actually found.** Both the CLI build gate and the E2E gate build
  browser targets, so both can fail for a reason that has nothing to do with your branch — most often
  `NETSDK1147`, the `wasm-tools` workload resolving as missing because a workload install elsewhere on
  the machine bumped the shared manifests mid-flight (`dotnet workload list` keeps listing it as
  installed throughout, so it will not tell you). They used to report that as *"the code the CLI writes
  doesn't compile"*, which cost two sessions an hour chasing a scaffolder bug that did not exist.
  `scripts/lib/build-failure.sh` now classifies a captured build log by error kind — `code` (`error CS`
  present, your branch), `workload` (`NETSDK1147` and no `CS`), `sdk` (another `NETSDK`), `unknown`
  (neither, so not a compile failure at all) — and the matching explanation is printed once, by the gate
  script when you run it yourself and by `.githooks/pre-push` when the hook is driving, which is how all
  four arms (browser E2E, CLI build, watch, deploy) get the same verdict without saying it twice. `CS`
  wins when both appear: a workload problem does not excuse real compile errors. Only the two machine
  kinds suppress the gate's own advice — a gate that failed at something other than compiling still knows
  what you should do about it. The decision is four rows of bash that had already been wrong once, so it
  has a table test, `scripts/tests/build-failure-kind.test.sh`, run by `run-unit-local.sh` before
  anything else.
- **The deploy gate runs locally, on pushes that touch the deploy path.**
  `scripts/run-deploy-e2e-local.sh` points the real `rask deploy` at a throwaway container standing in for
  a bare VPS — sshd plus its own Docker daemon (`docker:dind`, privileged) — and asserts on what happened
  *on the host*: an image that built over SSH, a container that answers its health check, a blue-green
  swap that retired the old colour, a Caddyfile a real Caddy accepted, and a named volume whose contents
  outlived the container. Every other deploy test is mocked, so this is the only coverage that the deploy
  actually deploys. It needs a `docker` CLI and a daemon that can run a privileged container; it installs
  nothing and never reads or writes your `~/.ssh`. The `.githooks/pre-push` hook runs it only when the
  push changes `DeployCommand`/`Host*`/`SshTarget`/`DockerProbe`/`DeployConfig` or the deploy tests
  (bypass with `RASK_SKIP_DEPLOY_E2E=1`). **Not covered:** real DNS and Let's Encrypt issuance — the gate
  uses a `.test` domain, so ACME never runs.
- **The install gate runs locally, on pushes that touch the public installer.** `rask.sh` and `rask.ps1`
  at the repo root are what [`docs/installation.md`](installation.md) tells people to `curl | sh`, and
  they are published to GitHub Pages by `pages.yml`. Two things cover them.
  `scripts/tests/install-script.test.sh` runs on **every** commit (it is a `scripts/tests/*.test.sh`, so
  `run-unit-local.sh` picks it up): it sources `rask.sh` with `RASK_INSTALL_LIB_ONLY=1` to table-test the
  pure helpers, drives the real `step_path` against a throwaway `HOME`, asserts the file stays POSIX `sh`
  (`dash -n` plus greps for the bashisms `dash` accepts and then dies on), asserts truncation safety by
  *running prefixes of the file* and requiring that none reaches `main`, and checks that the install URL
  is byte-identical in all nine places it is written. `scripts/run-install-e2e-local.sh` is the other
  half, and covers what that one structurally cannot: it runs the working tree's `rask.sh` inside
  containers that genuinely lack a .NET SDK, Node and tools, then asserts a working `rask`, `dotnet-ef`
  and Node ≥ 22.12 on the box afterwards, plus a scaffolded project that builds. It is slow (an SDK
  download per case), so `.githooks/pre-push` runs it only when the push changes `rask.sh`, `rask.ps1` or
  the gate itself (bypass with `RASK_SKIP_INSTALL_E2E=1`). **Not covered:** a real Windows host — case 7
  runs `rask.ps1` under PowerShell on Linux with the Windows-only steps off, so the SDK install and the
  user `PATH` write are unproven there.

  Note that `rask.sh`/`rask.ps1` are also listed explicitly in the **pre-commit** path filter. They sit at
  the repo root, which matched none of that filter's directory prefixes, so before they were added a
  commit touching only the public installer was the one commit that ran neither the formatter nor its own
  test.
- `commitlint.yml` — Conventional Commits check on PRs.
- `nightly.yml` — prerelease publish on `main`.
- `release.yml` — tag-triggered stable publish.
- Dependencies are kept current by `.github/dependabot.yml` (NuGet + Actions, weekly) and the
  `check-nuget-updates` skill.

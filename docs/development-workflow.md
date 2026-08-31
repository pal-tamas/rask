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

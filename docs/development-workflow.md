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
without it being torn down. Run it when you touch the hot-reload coordinator, the scoped-asset
registry, the generated registries, or `rask dev`. CI runs it before a release and on a
`ci/release/**` branch, not on every push (see [CI](#ci)).

There used to be a browser half, `scripts/run-wasm-watch-e2e.sh`, driving a WASM sample under
`dotnet watch`. It is gone with the app it edited, so **Mono applying a metadata delta to a live WASM
runtime is covered by nothing**.

## The definition-of-done gate

Every change passes this gate before it lands on `main` (the `rask-ship` skill). No hook enforces
it: CI runs the format check, the build, the tests and the browser journeys after the push (see
[CI](#ci)), so what you skip here you find out there, on `main`.

1. **Format + analyzers** — `dotnet format Rask.slnx` then `--verify-no-changes`. CI's
   `format src` and `format tests` jobs run the same verify after the push.
2. **Clean build, warnings-as-errors** —
   `dotnet build Rask.slnx -c Release -warnaserror -p:EnforceCodeStyleInBuild=true`.
   Enforced in `Directory.Build.props` (`TreatWarningsAsErrors`, `EnableNETAnalyzers`,
   `EnforceCodeStyleInBuild`), so a plain build enforces it too. See [code-analysis.md](code-analysis.md).
   The same build runs the **public-API gate**: anything public you added, renamed or removed fails
   until it is recorded in `src/<Project>/PublicAPI/<tfm>/PublicAPI.Unshipped.txt`. That diff is the
   API review — read it against [api-style.md](api-style.md) before you commit.
3. **Tests** — unit-test every feature/fix (`tests/Rask.*.Tests`); add E2E only when a unit test
   can't reach the path. **Every `src/Rask.Site` change gets an E2E** journey update
   (`tests/Rask.Site.E2E.Tests`). Inner loop — **build once, then test with `--no-build`** so
   each run doesn't rebuild the whole solution (test execution itself is fast; the build dominates):
   ```bash
   dotnet build Rask.slnx -c Release
   dotnet test Rask.slnx -c Release --no-build --filter "FullyQualifiedName!~Rask.Site.E2E"
   ```
   Narrow further to one project (`dotnet test tests/Rask.Core.Tests --no-build`) or one class
   (`--filter FullyQualifiedName~ATests`) while iterating. The build runs in parallel by default —
   don't add `-m:1` (the former WASM copy-race workaround is fixed at the source in
   `Rask.Spa.Hosting.targets`, which skips a referenced client's nested publish under `RaskWasm=false`).

   `Directory.Build.rsp` turns **MSBuild node reuse off** for every build started from the repository
   root. That is deliberate and load-bearing: the scoped-asset bake is not safe across reused workers
   ([#650](https://github.com/pal-tamas/rask/issues/650)), so with reuse on, publishing two different
   WASM apps in a row fails the second one. It costs about a fifth of a second per incremental
   build. If you are working on `Rask.Wasm.Tasks` itself, note that a reused node also pins the **task**
   assembly — run `dotnet build-server shutdown` before judging any change to it, or you are measuring
   the previous build's DLL.
4. **Benchmarks** — any render/live-runtime hot-path change runs `tests/Rask.Benchmarks`
   before/after and quotes the `Allocated` delta in the commit body.
5. **Docs & the site** — user-facing changes update `src/Rask.Site`, the relevant `docs/*.md`,
   `README.md`, `NUGET.md`, `llms.txt`, and `docs/ai-agents.md`. Add a `CHANGELOG.md`
   `[Unreleased]` entry (Keep a Changelog).
6. **Review** — security, performance, and memory held together with UX; prefer standard .NET
   APIs over hand-rolled code; refactor duplication you touch (the `rask-review` skill).
7. **Land on `main`** — Conventional Commit `type(scope): subject` (enforced by commitlint), then
   merge `origin/main` in and `git push origin HEAD:main`. **Own work never goes through a pull
   request** — PRs are reserved for external contributions, which arrive from forks. `ci.yml` runs
   on the push; a red `main` is fixed forward. See the `land-on-main` skill.

## Versioning & releases

- **Versions come from git tags via MinVer** (`vX.Y.Z`); assemblies carry `AssemblyVersion`,
  `FileVersion`, and `InformationalVersion` automatically.
- **Stable release:** promote `CHANGELOG.md` `[Unreleased]` to a dated section, tag `vX.Y.Z`,
  push — `release.yml` runs every gate, the release-only ones included, and only then packs the
  NuGets and publishes to nuget.org + a GitHub release (the `cut-release` skill). Try those gates
  ahead of the tag on a `ci/release/**` branch.
- **Nightly:** `nightly.yml` runs hourly and takes the newest commit `full` (the whole run) has passed on `main` — it packs the MinVer
  prerelease versions and publishes them to nuget.org (prerelease) and GitHub Packages. A commit
  `ci` did not pass publishes nothing.
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

**Nothing blocks a commit or a push.** The git hooks take seconds and build nothing; the gates run in
GitHub Actions *after* the push, each as its own job. Own work still lands on `main` directly, and
`ci.yml` says afterwards whether it should have. A red `main` is fixed forward.

They used to run in the hooks, under a one-minute budget that only held on an idle machine: with
several worktrees gating at once each run shrank to two cores and took ten minutes and more. CI is
free for a public repository and nobody waits on it.

### What runs where

| When | What |
|---|---|
| `commit-msg` hook | Conventional Commits (commitlint) and the attribution guard. |
| `pre-commit` hook | `scripts/tests/front-doors.test.sh`, only when `README.md` or `NUGET.md` is staged. |
| `pre-push` hook | The attribution guard again, over the commits being pushed. |
| **CI, every push to `main` and every pull request** — `ci.yml`, SCOPED | The gates of the **push** set the change can reach: `build` and `unit` always, `format` when a `.cs` changed, the browser journeys when what they draw is reached, the CLI build and templates when the CLI, a template or a project file changed, a front-end template's own job when its tree changed (all seven when the SPA host, the TypeScript emitter or the scaffolder did). `scripts/lib/affected_gates.py` decides, from the last commit `ci` or `full` passed; a change to the CI itself, a gate script or the package pins runs the whole set. |
| **CI, behind every push to `main`, one run at a time** — `full.yml`; also `ci.yml` on a `ci/**` branch | The whole **push** set, unscoped: build, unit (two shards), format (two halves) (`run-unit-local.sh`), browser E2E in four shards and the Rask.Server journeys (`run-e2e-local.sh`), devtools E2E, browser SQLite E2E, data demo E2E, CLI build (`run-cli-build-e2e.sh`), templates (`run-template-e2e.sh`), each front-end template as a job of its own (`run-template-e2e.sh --front-end=<key>`), the benchmark byte and allocation budgets (`run-benchmarks-local.sh`). |
| **CI, before a release** — `release.yml` on a `v*` tag, and `ci.yml` on a `ci/release/**` branch | The push set plus the **release** set, which needs containers or a real host: watch hot reload (`run-watch-e2e.sh`), deploy, storage providers, installer, providers. |
| **Only when you ask** | BenchmarkDotNet timings, the SQLite load gate (`run-sqlite-load-local.sh`), the Linux dev-host gate (`run-devhost-linux-local.sh`). No hook, no workflow. |

The list of gates lives once, in `.github/workflows/gates.yml`, which `ci.yml`, `full.yml`, `soak.yml`
and `release.yml` all call, so they cannot drift. Jobs do not fail fast: one red gate says nothing about the others, and
every one reports.

Enable the hooks with `git config core.hooksPath .githooks` (the first `dotnet build` does it for
you); bypass one with `--no-verify`.

### A red job

**Every job runs one script from `scripts/`, the same one you run by hand.** The job's last step
is that command; run it in a worktree at that commit:

```bash
scripts/run-unit-local.sh                    # the build, unit and format jobs (RASK_UNIT_PART=build | tests-1/2 | format-src … runs one piece)
scripts/run-e2e-local.sh                     # the "browser E2E" job
scripts/run-template-e2e.sh --front-end=vue  # the "front end vue" job
scripts/run-all-gates.sh --only 'E2E|CLI'    # several, by label
scripts/run-all-gates.sh --list              # every gate, and what it needs
```

On `main` every push gets its own run, started at once and never cancelled, so "which push broke it" has an answer.

### Trying a change first

Push to a `ci/**` branch to run the push set without landing anything; only the newest push to a
branch is kept running. A `ci/release/**` branch runs the release set too — the way to find out
before tagging rather than from a failed release.

```bash
git push origin HEAD:refs/heads/ci/my-change
gh run watch          # pick the run
git push origin --delete ci/my-change
```

### What a red `main` stops

Publishing. `pages.yml` triggers when `full` completes on `main` and does nothing unless it concluded
`success`; `nightly.yml` runs hourly and packs the newest commit `full` passed, once. So neither a
prerelease package nor rask.sh is built from a commit the WHOLE set did not pass — a scoped run's green
publishes nothing. `full` runs behind every push, one run at a time with the newest push waiting, so a
site change is live about twenty minutes after its push and a package within the hour after that.

The price of a scoped push is named here once: a break the scoped run could not see is found by the
next whole run, minutes later, with green pushes on top of it. `full` red and `ci` green means
exactly that. `release.yml`'s `publish` job needs its `gates` job (both sets). A manual
`workflow_dispatch` of `nightly` or `pages` publishes whatever `main` holds, verdict or not.

### The workflows

- `ci.yml` — the gates a change reaches, on every push to `main` and every pull request; the whole
  push set on a `ci/**` branch. By hand: `gh workflow run ci.yml --ref <branch> -f only='CLI build'`
  (`only` is a pattern over job names: `'front end'` is all seven front ends, `'front end vue$'` one).
- `full.yml` — the whole push set against `main`, behind every push (one run at a time, fourteen jobs
  at once so a push's own run finds runners) and hourly as the backstop. Publishing follows it.
- `gates.yml` — the reusable list of gate jobs the others call.
- `soak.yml` — the release set against `main` every three hours, so an image or a download that
  disappears is found within hours and not on the day of a release.
- `commitlint.yml` — Conventional Commits and the attribution guard on PRs: the commits and the PR
  title, which is the squash subject and passes through no local hook.
- `nightly.yml` — prerelease publish from a commit `full` passed.
- `pages.yml` — rask.sh, from a commit `full` passed.
- `release.yml` — tag-triggered: every gate, then the stable publish.
- `upstream.yml` — daily; follows what Rask is generated from. `scripts/upstream/follow.sh` moves the
  MDN snapshot to the latest stable data, records the public surface that moved with it
  (`scripts/public-api/record.py`) and moves the stated Node line to the Active LTS; the result is
  gated and THEN landed on `main`, with nobody watching. It opens an issue only when it needs a
  person: the gates refused what upstream shipped, or Flux UI moved. The Flux lock
  (`tests/Rask.Ui.Tests/Flux/flux.lock.json`) is measured on its runner, so it is relocked there:
  `gh workflow run upstream.yml -f relock=true`.
- `dependabot-merge.yml` — merges a Dependabot pull request once `ci` has passed it. What must not
  move on its own is in `.github/dependabot.yml`'s ignore lists. A bump to a front-end template's
  client is held to that template's `front end <key>` job as well as the `deps` set.

### The gate scripts

True of the scripts wherever they run — a CI job or your terminal.

- **Format + unit tests: `scripts/run-unit-local.sh`.** It builds the solution once, then runs the
  full `dotnet format Rask.slnx --verify-no-changes` (whitespace + style + analyzers) **concurrently
  with** every test except the browser E2E, and reports both statuses: a run that is red for
  formatting still tells you whether your tests pass. The full pass earns its place — import ordering
  is enforced by `dotnet format` alone, not by the warnings-as-errors build (#584). Before formatting
  it builds `src/*.Generators` in **Debug**: `dotnet format` evaluates the solution in the default
  configuration and resolves the analyzer references to `bin/Debug/`, and without those DLLs no
  source generator runs and the routing tests fail to bind with CS1503. The gate's own bash tests
  (`scripts/tests/*.test.sh`) run first, and every run ends with one line per phase saying where the
  time went.
- **`dotnet format` never gets `--no-restore`.** With it,
  `dotnet format Rask.slnx --verify-no-changes` **modified 57 files it was only asked to check**:
  without a restore the workspace cannot resolve the source generators, every generated symbol goes
  missing, the usings look dead, and it writes. Check `git status` after any format run.
- **A hand run can be scoped to what changed.** `RASK_TEST_SCOPE=affected scripts/run-unit-local.sh`
  builds and tests only the projects the staged files can reach (or a range's, with
  `RASK_SCOPE_RANGE=origin/main...HEAD`); `RASK_FORMAT_SCOPE=staged|range` narrows the formatter the
  same way. `scripts/lib/affected_projects.py` walks `ProjectReference`, the `..\…` items a project
  pulls in, and every test's `<RaskTestReads/>` declarations, and answers FULL for anything it cannot
  map precisely (a repo-root import, the solution, a gate script, a hook, `.editorconfig`, a file no
  project owns). The run prints which it chose and why. A project that passed is stamped with the
  tree it passed on (`scripts/lib/gate_stamps.py`), and the next scoped run skips it when nothing in
  reach changed; `RASK_GATE_REUSE=0` runs everything. Unscoped is the default, and it is what CI runs.
- **A test that reads a file from disk declares it.** A contract test on another project's `rask.ts`,
  a walk of `docs/**/*.md`: no reference carries that edge, so the test's csproj names it —
  ```xml
  <ItemGroup Condition="false">
    <RaskTestReads Include="..\..\src\Rask.Server\Resources\**" />
  </ItemGroup>
  ```
  The group is `Condition="false"` so MSBuild never expands the glob (the scoper reads it as text;
  `scripts/tests/affected-projects.test.sh` enforces this). Forget the declaration and a scoped run
  skips the test that pins the file. The gate's own tests declare theirs on a `# gate-inputs:` header
  line, an ERE over repo-relative paths; a change under `scripts/` or `.githooks/` runs all of them.
- **Every script finds Node and opts out of .NET CLI telemetry.** `scripts/lib/node-path.sh` takes the
  newest version under `~/.nvm/versions/node` when `node` is not on PATH (a shell started by an IDE
  or an agent), instead of failing the islands build with RASKISLAND001. `scripts/lib/dotnet-env.sh`
  exports `DOTNET_CLI_TELEMETRY_OPTOUT=1`: the CLI walks and locks its spool under
  `~/.dotnet/TelemetryStorageService` on every exit, and a gate's hundreds of `dotnet` processes
  queue on a large one with the cores idle.
- **The scaffold templates compile in the unit gate.** `src/Rask.Templates` is in no project the
  solution builds, so `tests/Rask.Generators.Tests/TemplatesCompileTests.cs` materialises each
  template the way `rask new` does and compiles the C# in memory with the real generators. Restore,
  the project file, the MSBuild targets and publish stay with `scripts/run-cli-build-e2e.sh`. A using
  that a package's `build/*.props` adds for an app is listed in that test by hand.
- **`run-all-gates.sh` is every gate in one local run.** `--only 'E2E|CLI'` narrows it to the gates
  whose label matches. `--parallel` runs two lanes: the CLI build, template and watch gates pack with
  MinVer on and everything else builds with `MinVerSkip`, so in one tree they recompile each other's
  `obj/Release`; it checks out a second worktree at HEAD for that group and refuses a dirty tree.
- **A red gate names the culprit it actually found.** The CLI build and browser gates build browser
  targets, so they can fail for a reason that has nothing to do with your branch — most often
  `NETSDK1147`, the `wasm-tools` workload resolving as missing. `scripts/lib/build-failure.sh`
  classifies the build log — `code` (`error CS`, your branch), `workload`, `sdk`, `unknown` — and the
  gate prints the matching explanation. `CS` wins when both appear.
- **Attribution trailers are rejected, at both boundaries.** Commit messages carry no
  `Co-authored-by:`, no `Claude-Session:` and no "Generated with …" footer: GitHub's contributor list
  credits co-authors, and taking two such accounts back off cost a rewrite of all 970 commits. The
  rule lives once, in `scripts/lib/attribution.sh`, consulted by `.githooks/commit-msg` and again by
  `.githooks/pre-push` over the commits being pushed — the first only runs once `core.hooksPath` is
  set, which a fresh clone has not done. It is the one check CI cannot take over for own work: by
  the time CI sees the commit it is on `main`. A human `Signed-off-by:` passes. `commitlint.yml`
  makes one allowance the hooks do not: on a pull request **Dependabot opened**, Dependabot's own
  sign-off is dropped before the check.
- **Hooks in a worktree run the worktree's own copy.** `core.hooksPath` is the relative path
  `.githooks`, resolved against the **pushing worktree's** top level. If a branch does not contain
  `.githooks/` at all, **no hook runs and nothing says so**.
- **A public rename is one command.** `scripts/tools/RaskRename` renames a symbol across `Rask.slnx`
  the way an IDE does, then rewrites `Type.Old` in the templates, docs and agent guides.
  `.claude/skills/rename-public-member` is the playbook.

### The gates, one by one

- **Browser E2E — `scripts/run-e2e-local.sh`** (push set). The Playwright journeys in
  `tests/Rask.Site.E2E.Tests` and `tests/Rask.Server.E2E.Tests`. It publishes `src/Rask.Site` and
  builds the leaf test projects — the graph the suite runs, not the solution; that the whole solution
  compiles is the unit gate's claim. It passes `MinVerSkip=true` like the unit gate, so the two do
  not recompile each other's `obj/`; that is safe only while no fixture launches a published host
  out of process. While iterating on one journey, narrow it:
  `RASK_E2E_FILTER='FullyQualifiedName~WasmExampleTests' scripts/run-e2e-local.sh` — it says loudly
  that the run was filtered, because a narrowed green is not the gate.
- **Devtools E2E — `scripts/run-devtools-e2e-local.sh`** (push set). Rask DevTools against real pages
  (`tests/Rask.DevTools.E2E.Tests`): the pill, the dock and every tab. A gate of its own because the
  devtools exist only in a **Debug** build. It needs **node** on `PATH` (a real Lit island is bundled)
  and sets `DOTNET_MODIFIABLE_ASSEMBLIES=debug`; a Release build, or a run without the variable,
  fails naming the script rather than passing on nothing.
- **Browser SQLite E2E — `scripts/run-browser-sqlite-e2e-local.sh`** and **data demo E2E —
  `scripts/run-data-demo-e2e-local.sh`** (push set). The only gates that link `e_sqlite3` natively
  into a WebAssembly bundle, so they need the `wasm-tools` workload and refuse to start without it.
  The first drives full-text search over `tests/Rask.SQLite.Browser.Fixture.Wasm`; the second drives
  `src/Rask.Site.DataDemo`, the notes demo served at `/demos/data/`.
- **CLI build — `scripts/run-cli-build-e2e.sh`** (push set). The only thing proving the code the CLI
  *writes* compiles: it packs this commit's packages to a local feed, scaffolds every `rask new` flag
  combination and the [tutorial](tutorial/00-overview.md) walk-through, and builds each with
  `-warnaserror`. The script exports `RASK_CLI_BUILD_E2E=1`; without it every case reports
  **SKIPPED** rather than passing silently.
- **Templates — `scripts/run-template-e2e.sh`** (push set). Scaffolds `server`, `wasm` and
  `wasm-hosted` through the dispatch `rask new` uses and builds what it wrote. It also scaffolds a server
  app per npm island runtime and runs the real island build (`npm install` and Vite), asserting the
  manifest lists the scaffolded island: a compile-only row cannot see a scaffold that does not bundle. Exports
  `RASK_TEMPLATE_E2E=1`, with the same SKIPPED rule.
- **Front ends — `scripts/run-template-e2e.sh --front-end=<key>`** (push set, one job per template:
  `front end react`, `front end vue`, …). Scaffolds that template with every battery on and runs the one
  command a deploy runs, `dotnet publish`: `npm ci` from the committed lockfile, the typed client written
  into `client/src/rask/` from the host's message records, and the bundle. Then `npm run lint` and
  `npm run format:check`, and the **published** app is started and asked for `/` (the bundle's
  `index.html`, and the script it loads) and for the starter's query, by the wire name the generated
  client carries. No browser. The jobs come from the trees: a template with a `client/package.json` is a
  front end (`--list-front-ends`), so adding one adds its job, and `TemplateTreeContractTests` holds that
  list to the one `rask new` offers. About 2.5 minutes for one on a ten-core machine.
- **Watch hot reload — `scripts/run-watch-e2e.sh`** (release set). See [the inner loop](#the-inner-loop).
- **Deploy — `scripts/run-deploy-e2e-local.sh`** (release set). Points the real `rask deploy` at a
  throwaway privileged container standing in for a bare VPS and asserts on the host: an image built
  over SSH, a container answering its health check, a blue-green swap, a Caddyfile a real Caddy
  accepted, a volume that outlived its container. Every other deploy test is mocked. **Not covered:**
  real DNS and Let's Encrypt — the gate uses a `.test` domain.
- **Installer — `scripts/run-install-e2e-local.sh`** (release set). Runs the working tree's `rask.sh`
  in containers that lack a .NET SDK, Node and tools, then asserts a working `rask`, `dotnet-ef` and
  Node, plus a scaffolded project that builds. Its cheap half, `scripts/tests/install-script.test.sh`,
  is one of the unit gate's own tests: the pure helpers, POSIX `sh`, truncation safety (by running
  prefixes of the file) and the install URL byte-identical everywhere it is written. **Not covered:**
  a real Windows host.
- **Storage providers — `scripts/run-storage-providers-local.sh`** and **providers —
  `scripts/run-providers-local.sh`** (release set). Rask.Storage's S3 and Azure signing against MinIO
  and Azurite, and the database batteries against real servers, all in containers; a provider fact
  whose server is not reachable reports SKIPPED, never PASSED.
- **Linux dev host — `scripts/run-devhost-linux-local.sh`** (on request, `RASK_DEVHOST_E2E=1`, in no
  CI job). Verifies the Linux half of [`https://<name>.test`](cli.md#httpsnametest) in a throwaway
  container where the CA anchors, NSS databases, `/etc/hosts` and the port sysctl are really
  modified, ending with a `curl` TLS handshake with no `--cacert`. **Not covered:** macOS and Windows.
- **Benchmarks — `scripts/run-benchmarks-local.sh`** (CI, every push). Checks both wire-byte baselines —
  the standalone codec and the head-to-head against Blazor — byte-for-byte, holds a live update to its
  allocation budget (`Baselines/allocation-budget.csv`, +5%), and smoke-runs the three
  live-session capacity reports; `session-churn --smoke` asserts that 100 create→dispose cycles leave
  nothing behind. Every check runs even when an earlier one fails. A regression means one of two
  opposite things: the render/diff path got heavier (fix the code, leave the baseline), or a
  scenario's own markup changed (refresh the baseline in the same commit and say why) — the vs-Blazor
  report tells them apart, because Blazor's numbers move by the same amount in the second case.
  Build before `--check`: the baseline is read from `bin/`.

### Sharing one machine

A CI runner has its machine to itself and skips all of this. By hand, several worktrees share one
box, so `scripts/lib/machine-lane.sh` gives it a budget of **10 slots** (the performance cores) that
the unit and browser gates claim against:

| gate | what it does | waits? |
|---|---|---|
| `run-unit-local.sh` | takes whatever is left, floor 2, and **shrinks into it**; prints the size it chose | never |
| a browser gate — build and publish | a partial claim; several worktrees may build at once | never |
| a browser gate — the suite | claims the whole budget; two browser suites never overlap | yes, oldest first |

A unit gate younger than a waiting browser suite is not waited for, so "exclusive" is not "an empty
machine". Contention produces boot timeouts and dead fixtures, never a false assertion pass: **a
green under contention is trustworthy; a red is not** — re-run it alone
(`ps -Ao pid,etime,command | grep -E '[r]un-(e2e|unit)-local'` shows what else is live). Every other
gate, and any plain `dotnet build`, is invisible to the budget; a red browser suite that finds a
heavy build still running names it ([#850](https://github.com/pal-tamas/rask/issues/850)).

| variable | effect |
|---|---|
| `RASK_LANE_SLOTS` | the budget (default `10`). |
| `RASK_LANE_DISABLE=1` | switch the mechanism off: every gate gets its maximum and nothing waits. |
| `RASK_E2E_QUEUE_TIMEOUT` | seconds to wait before giving up (default `5400`); the gate then exits 1 and names what holds the slots. |
| `RASK_E2E_QUEUE_POLL` | seconds between checks (default `20`). |
| `RASK_E2E_QUEUE=0` | do not wait; refuse immediately. |
| `RASK_E2E_ALLOW_CONCURRENT=1` | do not wait; run alongside. Treat a red from that run as suspect until re-run alone. |

## Dependencies

The standing rule is **the latest LTS** — Node, .NET, and the front-end toolchains. A scaffolder is a
recommendation every new project inherits, so a maintenance-only pin hands users a runtime that has
stopped getting security patches.

**What updates itself.** `.github/dependabot.yml` covers NuGet, GitHub Actions, the site's npm
front end and the seven front-end templates' clients (`src/Rask.Templates/*/client`) weekly. The
templates arrive as one pull request for all seven — and a second for majors — and
`dependabot-merge.yml` lands one only when the `front end <key>` job of every template it changes has
passed: that job installs the new lockfile with `npm ci`, builds, lints and serves the scaffold. An
update inside a `package.json` range moves the lockfile alone (`versioning-strategy:
increase-if-necessary`); the ranges are drawn by hand. Families
that must move together are grouped (`microsoft-extensions`, `test-tooling`, `spectre-console`,
`sqlitepclraw`); the three `Microsoft.CodeAnalysis.CSharp*` packages are ignored on purpose, because
an analyzer referencing a newer Roslyn than the running `csc` is CS9057 and raises the compiler floor
for every downstream consumer.

**What does not.** Several pins are invisible to Dependabot because they are not `PackageReference`s:
`RaskEsbuildVersion` and `RaskTsgoVersion` (`Rask.Core.targets`), `RaskTailwindVersion`
(`Rask.Tailwind.props`), the two Node build floors (`RaskExternalMinimumNode` for islands and
`RaskSpaMinimumNode` for front ends — one number, in two props files), the Angular template's own higher
`RaskSpaMinimumNode` in its csproj, the Node scaffold line (`NodeRequirement.ScaffoldLine`) with the
NodeSource line (`setup_NN.x`) in each front-end template's `Dockerfile`, and the npm ranges `rask new --islands` writes, which live in
`src/Rask.Templates/_islands/*/island.json` — not a `package.json`, so Dependabot cannot read them.
The `check-dependency-updates` skill walks all of them.

**What keeps the copies honest.** A version stated twice is a version that can drift, so the pairs that
matter most are asserted by an offline unit test rather than by a comment:

| Test | Holds together |
| --- | --- |
| `NodeRequirementTests` | `ScaffoldLine` vs. `rask.sh`, `rask.ps1`, and `docs/installation.md` (both platform columns, the `≥ NN.NN` sentence, the "Node NN LTS" summary); the CLI's build floor vs. `RaskExternalMinimumNode` and `RaskSpaMinimumNode` |
| `TemplateNodePinTests` | each front-end template's `Dockerfile` vs. `ScaffoldLine`'s major; a client's `engines.node` vs. the build floor; the Angular template's `RaskSpaMinimumNode` vs. the lowest Node the `@angular/cli` in its lockfile accepts; a lockfile beside every client manifest |
| `TailwindVersionPinTests` | the `tailwindcss` range in every template client vs. `RaskTailwindVersion` |
| `PackagePinFamilyTests` | the Spectre and SQLitePCLRaw pairs, the one-version platform stack, and that every SQLite project can still reach the patched SQLitePCLRaw |
| `EfToolProbeTests` | the `dotnet-ef` floor the CLI checks for vs. the EF Core version in `Directory.Packages.props` |
| `ResolveTypeScriptToolTaskTests` | reads `RaskTsgoVersion`/`RaskEsbuildVersion` out of `Rask.Core.targets` instead of restating them |

They need no network and run in the ordinary unit gate — CI's `unit` jobs, on every push and
every pull request — so the gate that runs for a version bump is the one that checks it was complete.

**Not everything is covered, and pretending otherwise is the same bug.** Prose mentions of the Node
line elsewhere — the `22.12` build-floor figures quoted in `docs/islands.md`, and the codename in `NodeRequirement`'s own doc comment — are
still only prose. `scripts/upstream/node-lts.sh` rewrites the stated line and the codename when the Active
LTS moves — the templates' `setup_NN.x` lines with it — and `upstream.yml` lands it; the build floor is
deliberately left alone.

**Re-importing a front-end template is done by hand.** `scripts/refresh-templates.sh` used to re-run each
framework's creator and print the diff against the committed tree. It is not coming back: every file in a
client now carries Rask's own layer (the Tailwind starter, the lint and format configs, the dev proxy,
the battery markers), so that diff is the same wall on every run and a real change in the creator's
output is one line lost in it. Versions are Dependabot's now. For the shape — a new `tsconfig` option, a
creator that changed its linter — run the creator into a scratch directory and read it beside the tree:

```bash
cd "$(mktemp -d)"
npx --yes create-vite@latest client --template preact-ts   # react-ts, vue-ts, solid-ts, svelte-ts, lit-ts
npx --yes @angular/cli@latest new client --directory client --style css --ssr false --skip-git --skip-install
diff -ru --exclude=src --exclude=public --exclude=package-lock.json <repo>/src/Rask.Templates/preact/client client
```

Take what matters into `src/Rask.Templates/<key>/client` by hand, re-lock it there
(`npm install --package-lock-only`), and run `dotnet test tests/Rask.Cli.Tests` and
`scripts/run-template-e2e.sh --front-end=<key>`.

**Landing a Dependabot PR.** `ci.yml` runs the short `deps` set on the pull request (format, unit, CLI build, templates, and the `front end <key>` job of a template whose client it changes — the browser suites run on `main` after the merge), but `main` has no
required checks, so a red run does not disable the merge button: read the run first. Then land it
locally, not from the web UI — check the branch out and push it.

A green `commitlint` check on a Dependabot PR means its title and commits are Conventional and carry
no trailer *other than* Dependabot's own sign-off, which CI lets through on those PRs only. The hooks
do not: `pre-push` refuses the commit until that line is gone, so landing one starts with
`git commit --amend --reset-author` and deleting the `Signed-off-by:` line.

**Vulnerability scanning is manual.** `dotnet list Rask.slnx package --vulnerable --include-transitive`
is the only scan that runs anywhere; no workflow runs it.

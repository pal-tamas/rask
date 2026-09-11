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

`.githooks/pre-push` runs it when a push touches the hot-reload path (bypass with
`RASK_SKIP_WATCH_E2E=1`).

There used to be a browser half, `scripts/run-wasm-watch-e2e.sh`, driving a WASM sample under
`dotnet watch`. It is gone with the app it edited, so **Mono applying a metadata delta to a live WASM
runtime is covered by nothing**. Its last run demonstrated the failure mode it was written to fix: with
its tests deleted, `dotnet test --filter` matched nothing, printed "No test matches the given testcase
filter", exited 0, and the hook announced the gate had passed.

## The definition-of-done gate

Every change passes this gate before it lands on `main` (the `rask-ship` skill):

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
   `Rask.Wasm.Hosting.targets`).

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
   `README.md`, `NUGET.md`, `llms.txt`, and the template `AGENTS.md`. Add a `CHANGELOG.md`
   `[Unreleased]` entry (Keep a Changelog).
6. **Review** — security, performance, and memory held together with UX; prefer standard .NET
   APIs over hand-rolled code; refactor duplication you touch (the `rask-review` skill).
7. **Land on `main`** — Conventional Commit `type(scope): subject` (enforced by commitlint), then
   merge `origin/main` in with `git merge --no-commit` (a *clean* merge auto-commits and runs
   `pre-merge-commit`, a hook this repo does not have, so it lands ungated) and
   `git push origin HEAD:main`. **Own work never goes through a pull request** — PRs are reserved
   for external contributions, which arrive from forks. See the `land-on-main` skill.

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

- **GitHub runs the bare minimum: only what GitHub alone can do.** `commitlint.yml` (PR commits + the
  PR title — the squash subject, which no local hook ever sees), `pages.yml`, `release.yml`, and
  `nightly.yml`'s prerelease publish. There is no `ci.yml`: the benchmark byte-gates moved into
  `.githooks/pre-push` alongside the browser E2E, so **nothing in CI runs a test or a benchmark** —
  your machine is the only thing that will tell you something broke.
- **A commit is scoped; a push is not.** The `pre-commit` hook sets `RASK_TEST_SCOPE=affected`, and the
  gate then builds and tests only the projects the staged change can reach —
  `scripts/lib/affected_projects.py` walks both `ProjectReference` and the source-linked
  `<Compile Include="..\…"/>` edges, and answers FULL for anything it cannot map precisely (a repo-root
  import, the solution, a gate script, a hook, `.editorconfig`, a packaged `build/` import, or a shared
  file owned by no project). A one-component commit costs ~55s instead of ~325s. The run always prints
  which of the two it chose and why. What makes this safe is that **nothing leaves the machine on a
  scoped run**: `.githooks/pre-push` still runs the whole solution, the browser E2E and the benchmark
  gates. A broken commit can exist locally; it cannot be pushed. Run the whole thing by hand any time
  with `scripts/run-unit-local.sh`, which is unscoped by default.
- **Format + unit tests run locally, enforced before commit.** `scripts/run-unit-local.sh` builds the
  solution once, then runs the full `dotnet format Rask.slnx --verify-no-changes` (whitespace + style +
  analyzers, one workspace load) **concurrently with** every test except the browser E2E. The two share
  nothing either of them writes — the formatter restores and reads a Debug workspace, the tests load
  the already-built `bin/Release` — so the only thing that had ever serialised them was the order they
  were written in. Both statuses are collected and both are reported: a run that is red for formatting
  still tells you whether your tests pass, instead of costing a second full gate to find out. The full
  pass earns its
  place: import ordering is enforced by `dotnet format` alone — the warnings-as-errors build covers the
  analyzer rules but not the sorting of using directives, which is how a misordered `using` drifted into
  `Rask.Server` unnoticed (#584). Before formatting, the script builds `src/*.Generators` in **Debug**:
  `dotnet format` evaluates the solution in the default configuration, so it resolves the
  `OutputItemType="Analyzer"` references to `bin/Debug/`, and without those DLLs no source generator runs
  — `Routes.*` is never emitted and the routing tests fail to bind with CS1503. That is the real cause of
  the "spurious CS1503" that kept this gate on the whitespace pass alone until #584. That Debug build
  happens **only on the runs that go on to format**: the formatter is its only consumer, so a commit
  staging no `.cs` at all (a docs page, a workflow, a `.ts` file) no longer pays for three Debug
  compilations and then skips the formatter. The three projects build concurrently — they have no
  `ProjectReference`, so there is no shared output to race over — each pinned to `-m:1` so they cannot
  each claim the box. The gate's own bash tests run concurrently too, for the same reason: ten
  independent scripts that stub `ps`/`pgrep` rather than touching the machine.
  The `.githooks/pre-commit` hook runs it whenever a commit stages code (enable hooks with
  `git config core.hooksPath .githooks`; bypass with `git commit --no-verify` or `RASK_SKIP_UNIT=1`).
- **`dotnet format` never gets `--no-restore`, and that is not an oversight.** Both arms of the gate
  passed it until now, and `dotnet format Rask.slnx --verify-no-changes --no-restore` **modified 57
  files it was only asked to check**, rewriting `using` directives across the repo. Without a restore
  the workspace cannot resolve the source generators; every generated symbol goes missing, the
  remove-unnecessary-imports analysis concludes those usings are dead, and it writes — which
  `--verify-no-changes` did not stop. The restore it now does is already up to date from the build
  above, so this costs seconds. Do not put the flag back to shave them off, and check `git status`
  after any format run: a destructive pass and a clean one differ only in how many files moved.
- **Attribution trailers are rejected, at both boundaries.** Commit messages carry no
  `Co-authored-by:`, no `Claude-Session:` and no "Generated with …" footer. GitHub's contributor list
  credits co-authors as well as authors, so one footer adds an account to the sidebar that only a
  history rewrite removes — two of them (one Claude, one Copilot Autofix) cost a rewrite of all 970
  commits plus a force-push of `main` and 18 release tags. The rule lives once, in
  `scripts/lib/attribution.sh`, and is consulted by **both** hooks: `.githooks/commit-msg` at commit
  time, and `.githooks/pre-push` over the commits actually being pushed. The second is not
  redundant — the first only runs once `core.hooksPath` is set, and a fresh clone or a new worktree
  has not set it, which is exactly how the two trailers got in. A human `Signed-off-by:` passes;
  `scripts/tests/attribution-guard.test.sh` states all 32 cases, both directions.
- **A deletion-only push runs no gate.** `git push origin --delete <branch>` moves no commits and
  changes no tree, so there is nothing for a build or a browser journey to have an opinion about. The
  hook used to run the whole gate on it regardless — every gate below it is phrased as "is this push
  path-relevant", and a deletion matches those filters like anything else. It now returns early when
  **every** ref in the push is a deletion; a push that deletes one branch and updates another still
  gets the full gate.

- **E2E runs locally, enforced before push.** The browser-journey E2E
  (`tests/Rask.Site.E2E.Tests`, Playwright) was moved out of the CI pipeline. Run it with
  `scripts/run-e2e-local.sh`; the `.githooks/pre-push` hook runs it on `git push` (enable hooks
  with `git config core.hooksPath .githooks`; bypass with `git push --no-verify` or `RASK_SKIP_E2E=1`).
  While iterating on **one** journey, narrow the run with `RASK_E2E_FILTER` — the sample publishes still
  happen (they are what the tests boot), but you pay for one journey instead of the whole suite:
  `RASK_E2E_FILTER='FullyQualifiedName~WasmExampleTests' scripts/run-e2e-local.sh`. It says loudly
  that the run was filtered, because a narrowed green is not the gate.

  **It builds the graph the suite runs, not the solution.** This gate used to open with
  `dotnet build Rask.slnx -m:1` — 105 projects, serially, on one core, before a browser opened.
  `Rask.Site.E2E.Tests` has no `ProjectReference` at all (it drives a served bundle over HTTP) and
  every fixture in it boots exactly one app, `src/Rask.Site`; transitively that is 39 projects. The
  other 66 — every unit-test assembly, all three benchmark projects, the CLI — were compiled here and
  never loaded, after `pre-commit` had already built **and run** them on the way in. The gate now
  publishes the site (which bootstraps the MSBuild task assemblies the leaf needs) and then builds the
  one leaf project. What it stops proving is that the whole solution compiles, which is `pre-commit`'s
  job on every code commit; the one thing it built that `pre-commit` does not is the WASM bundle, and
  that is the site publish itself.

  **All three gates now agree on `MinVerSkip=true`.** The unit and benchmark gates already passed it;
  this one did not, and that disagreement was expensive in a way none of them could see. MinVer stamps
  the commit height and SHA into `AssemblyInformationalVersion`, so every project's generated
  `AssemblyInfo.cs` changes on **every commit**, and any project whose version flag differs from the
  last gate to touch `obj/` is recompiled from scratch. The three were rebuilding each other's output
  in a loop. It is safe here because the recorded hazard — a MinVer fallback version breaking a
  published app launched **out-of-process** whose routes live in a separate assembly — cannot arise:
  `ExampleAppFixture`, the only out-of-process host runner, has no derived class left, and the one
  fixture in use serves a published browser-WASM bundle from an in-process static-file host. If an
  out-of-process host fixture is ever reintroduced, the flag has to come back off.

  **The machine has a slot budget, and every gate claims against it.** Several worktrees share one
  box, and the two things that go wrong there pull in opposite directions: too much work at once
  (nothing used to throttle the unit gate — three of them ran together and put 35 MSBuild worker nodes
  on 14 cores, load average 98 at 0.0% idle), and too little (the browser gate used to take the whole
  machine for its *whole run*, including a build that uses one core). Both are fixed by the same
  mechanism, in `scripts/lib/machine-lane.sh`.

  The box publishes **10 slots** — the performance cores, deliberately not all 14, so the efficiency
  cores stay free for your editor and no timing-sensitive test gets scheduled somewhere slower than
  the run that set its timeout. A gate then asks one of two questions:

  | gate | what it does | blocks? |
  |---|---|---|
  | `run-unit-local.sh` | takes whatever is left, floor 2, and **shrinks into it** — `-m` on the build and on `dotnet test` follow the number, and it prints the size it chose | **never** |
  | `run-e2e-local.sh` — preflight, build, publishes | a partial claim; several worktrees may build at once | **never** |
  | `run-e2e-local.sh` — the browser suite | claims the whole budget | **yes** — see the exact guarantee below |

  **What "exclusive" does and does not promise.** Two browser suites never overlap: whichever gate is
  older is waited for, and a younger one finds the budget fully claimed and waits in turn. What it does
  *not* promise is an empty machine. A unit gate that started while the browser gate was still building
  is **younger** than it, so the browser gate does not count it and does not wait for it — that unit
  gate keeps the slots it sized itself to and runs alongside the suite. That is a deliberate
  consequence of the unit gate never blocking a commit, not an oversight: making the suite wait for
  every unit gate as well would let a steady stream of commits starve the browser gate indefinitely,
  since unit gates never queue. If you need a genuinely quiet machine for a suspicious red, check with
  `ps -Ao pid,etime,command | grep -E '[r]un-(e2e|unit)-local'` and re-run alone.

  So a commit is never delayed — `.githooks/pre-commit` decided that a blocked commit costs more than
  a slow one, and that still holds; the gate just gets smaller on a busy box. And a browser gate no
  longer blocks seven worktrees while it builds: on a measured run, 9m30s of its first 10m12s was
  build and publish, roughly a quarter of the ~40m norm, spent holding a machine it was using one core
  of. Only the suite itself is exclusive now, and a gate queued for it has already finished building.

  The queue is ordered by process age: a gate waits only for gates *older* than itself, so the run
  holding the machine is ahead of everyone and each waiter is ahead of the ones that arrived later.
  Exactly one is released at a time, and nothing starves — a gate's age only grows and a new gate
  starts at zero, so nobody can ever be inserted ahead of you. That ordering is load-bearing rather
  than decorative: a waiting gate is itself a gate process, so "wait until no other gate exists" would
  deadlock two waiters against each other, and "start when the holder exits" would release them
  simultaneously into the very contention this prevents.

  Claims are processes, not files (`scripts/lib/lane-claim.sh`) — a file survives `kill -9` and a
  laptop sleep and then wedges every gate on the box until someone works out what to delete, whereas a
  claim whose owner is gone stops counting the moment `ps` forgets it. A gate that is *queued* for the
  browser suite publishes its claim **before** it waits: it has not started `dotnet test` yet, so
  nothing in its process tree would otherwise say what it is about to need, and a junior would start
  work the waiter is about to need the whole machine for.

  | variable | effect |
  |---|---|
  | `RASK_LANE_SLOTS` | the budget (default `10`). |
  | `RASK_LANE_DISABLE=1` | switch the whole mechanism off: every gate gets its maximum and nothing waits. |
  | `RASK_E2E_QUEUE_TIMEOUT` | seconds to wait before giving up (default `5400`, 90m). Past it the gate exits 1 and names what still holds the slots — a run past the ~40m norm is usually wedged, not busy. |
  | `RASK_E2E_QUEUE_POLL` | seconds between checks (default `20`). |
  | `RASK_E2E_QUEUE=0` | do not wait; refuse immediately. |
  | `RASK_E2E_ALLOW_CONCURRENT=1` | do not wait; run alongside. The claim is still published, so others account for the load — but treat anything the run reports as suspect until re-run alone. |

  Worth knowing when you read a red: contention produces boot timeouts and dead fixtures, never a false
  assertion pass. **A green under contention is trustworthy; a red is not.**

  **What the budget does not cover.** Only the two gates above claim against it. The rest —
  `run-benchmarks-local.sh`, `run-cli-build-e2e.sh`, the watch and deploy and install gates — and any
  plain `dotnet build` you run by hand are invisible to it, so the accounting is *incomplete* rather
  than wrong: work exists that nobody counted. A gate that goes unseen costs some over-subscription,
  which is the safe direction; the alternative, a phantom gate, would shrink everyone else for as long
  as it was believed in. Two older hints still soften the uncounted cases ([#850]): `pre-commit` says
  so before it starts when a browser gate is live (it never refuses), and a red suite that finds a
  heavy build still running names it and asks you to re-run alone before investigating. Neither claims
  your failure is not real; they say the run was not clean enough to conclude that it is.

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
- **The benchmark gates run locally, enforced before push.** `scripts/run-benchmarks-local.sh`
  checks both wire-byte baselines — the standalone codec numbers and the head-to-head against Blazor —
  byte-for-byte. The numbers are noise-free (no timing: every render emits the same payload shape with
  one value differing), so a change is a real change. `.githooks/pre-push` runs it on every push,
  UNFILTERED unlike the heavier gates below: it costs about a minute, and a hand-listed path filter is
  itself a way for a gate to stop running silently. Bypass with `git push --no-verify` or
  `RASK_SKIP_BENCHMARKS=1`.

  It exists because CI's copy stopped nobody. A CI job ran the same two gates, but `main` has no
  required checks, so it rode red through three merges before anyone noticed
  ([#919](https://github.com/pal-tamas/rask/issues/919)). That job is gone; this is the only copy.

  **It also smoke-runs the three live-session capacity reports** (`session-footprint`, `session-churn`,
  `session-load`) for about four seconds in total. Two of the three had been dead on startup for four days with
  nothing to notice, because the nightly job that ran them went when the rest of CI did
  ([#922](https://github.com/pal-tamas/rask/issues/922)) — and that outage hid a leak in which every
  page served retained its whole live session. So `session-churn --smoke` does not merely run: it
  **asserts** that 100 create→dispose cycles leave nothing behind. The full reports stay hand-run; run
  them yourself before claiming a capacity number.

  **Every gate always runs, even when an earlier one fails.** In CI they are two steps in one job, so a
  fail-fast on the first leaves the second unrun — which is how the vs-Blazor baseline stayed broken
  while the standalone one was being fixed, how a half-fix looked complete, and how `session-churn`'s
  crash stayed invisible while `session-footprint`'s identical one was being looked at. Locally you get
  every answer at once.

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
- **The template gate builds every template, not the four that used to have one.**
  `scripts/run-template-e2e.sh` scaffolds each of the fifteen templates through the same dispatch
  `rask new` uses and builds what it wrote with `-warnaserror`. Before it existed only `server`, `wasm`
  and `react` were ever scaffolded-and-built, plus `angular` for its Tailwind output — and **no meta
  template was built by anything**: that lane's only gate publishes a hand-written stub csproj against
  stand-in files, so a real Nuxt or SvelteKit app compiling was checked nowhere.

  Two tiers, because the costs differ by two orders of magnitude. The default runs the C# half of all
  fifteen and is what `run-all-gates.sh` includes. `--front-end` additionally runs each client's real
  `npm ci`, `npm run lint`, `npm run format:check` and production build — four to six minutes per
  template on a cold cache, so about an hour for the thirteen, which belongs to a release rather than
  to every run of every gate. The lint run is there rather than in the unit gate for a specific reason:
  a plugin's exported config name differs per plugin and per major, and a wrong one throws at ESLint
  *startup*, which nothing that merely reads the config file can see.

  Opted into by `RASK_TEMPLATE_E2E=1`, which the script exports; without it every case reports
  **SKIPPED** rather than passing silently.
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
- **The Linux dev-host gate is opt-in and runs in a container.** `scripts/run-devhost-linux-local.sh`
  verifies the Linux half of [`https://<name>.test`](cli.md#httpsnametest) against a real Linux machine:
  a throwaway container running as an ordinary user with passwordless sudo, where the CA anchors,
  `certutil`'s NSS databases, `/etc/hosts` and the `ip_unprivileged_port_start` sysctl are all genuinely
  modified. It finishes by having `curl` complete a TLS handshake to `https://appname.test` with **no
  `--cacert`** and nothing told about the authority — only a correctly installed system trust can make
  that succeed. Everything else about the dev host is a pure function or a fake process runner, which
  proves the argv Rask *builds* and nothing about whether a machine ends up trusting anything; this is
  the only coverage that the setup actually sets anything up. It rewrites the machine it runs on, so it
  is gated behind `RASK_DEVHOST_E2E=1` and is never part of a push. It needs a `docker` CLI and a daemon
  that can run a privileged container. **Not covered:** macOS and Windows, whose keychain, UAC and pf
  steps have no equivalent sandbox — those remain exercised by hand.
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
- `lts-watch.yml` — monthly; opens an issue when Node's Active LTS line moves past the one the repo
  states. Not a gate: it cannot go red on a branch and blocks nothing.

## Dependencies

The standing rule is **the latest LTS** — Node, .NET, and the front-end toolchains. A scaffolder is a
recommendation every new project inherits, so a maintenance-only pin hands users a runtime that has
stopped getting security patches.

**What updates itself.** `.github/dependabot.yml` covers NuGet and GitHub Actions weekly. Families
that must move together are grouped (`microsoft-extensions`, `test-tooling`, `spectre-console`,
`sqlitepclraw`); the three `Microsoft.CodeAnalysis.CSharp*` packages are ignored on purpose, because
an analyzer referencing a newer Roslyn than the running `csc` is CS9057 and raises the compiler floor
for every downstream consumer.

**What does not.** Several pins are invisible to Dependabot because they are not `PackageReference`s:
`RaskEsbuildVersion` and `RaskTsgoVersion` (`Rask.Core.targets`), `RaskTailwindVersion`
(`Rask.Tailwind.props`), the Node floors (`RaskSpaMinimumNode`, `RaskExternalMinimumNode`), the Node
scaffold line (`NodeRequirement.ScaffoldLine`), and the npm caret ranges the SPA templates write. The
`check-dependency-updates` skill walks all of them.

**What keeps the copies honest.** A version stated twice is a version that can drift, so the pairs that
matter most are asserted by an offline unit test rather than by a comment:

| Test | Holds together |
| --- | --- |
| `NodeRequirementTests` | `ScaffoldLine` vs. `rask.sh`, `rask.ps1`, and `docs/installation.md` (both platform columns, the `≥ NN.NN` sentence, the "Node NN LTS" summary); and the two build floors, `RaskSpaMinimumNode` vs. `RaskExternalMinimumNode` |
| `PackagePinFamilyTests` | the Spectre and SQLitePCLRaw pairs, the one-version platform stack, and that every SQLite project can still reach the patched SQLitePCLRaw |
| `TailwindVersionPinTests` | `RaskTailwindVersion` vs. the SPA templates' caret range |
| `ProjectGeneratorTests.Wasm_auth_framework_version_matches_the_repo_pin` | the WASM scaffold's framework version vs. `Directory.Packages.props` |
| `TypeScriptCompilesTests`, `ResolveTypeScriptToolTaskTests` | read `RaskTsgoVersion`/`RaskEsbuildVersion` out of `Rask.Core.targets` instead of restating them |

They need no network and run in the ordinary unit gate — which `pre-commit` already triggers for any
`Directory.` path, so the gate that fires for a version bump is the one that checks it was complete.

**Not everything is covered, and pretending otherwise is the same bug.** Prose mentions of the Node
line elsewhere — `docs/spa.md`'s "Active LTS (24 'Krypton')", the `22.12` build-floor figures quoted in
`docs/spa.md` and `docs/islands.md`, and the codename in `NodeRequirement`'s own doc comment — are
still only prose. `lts-watch.yml`'s issue lists the files to change; treat that list, not this table, as
the checklist when the line moves.

**Landing a Dependabot PR.** Merge it locally, not from the web UI. Dependabot authors server-side, so
its PRs never touch `.githooks/`, and `main` has no required checks — a web merge is a version change
that nothing built, formatted or tested. Check the branch out and push it so `pre-commit` and
`pre-push` run. Note that `Directory.Packages.props` is in `pre-push`'s `generator_paths`, so the CLI
build gate runs too.

**Vulnerability scanning is manual.** `dotnet list Rask.slnx package --vulnerable --include-transitive`
is the only scan that runs anywhere; the CI job that used to do it went with `ci.yml` in #923.

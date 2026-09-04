#!/usr/bin/env bash
# Local E2E gate.
#
# The browser-journey E2E (tests/Rask.Examples.E2E.Tests, Playwright) no longer runs in CI — it runs
# here, locally, and the
# pre-push hook (.githooks/pre-push) enforces this browser gate before code leaves the machine.
#
# This mirrors the build-once → publish-samples → run-shards flow the old .github/workflows/e2e.yml
#
# Usage:  scripts/run-e2e-local.sh
# Skip:   RASK_SKIP_E2E=1 (also honoured by the pre-push hook)
set -euo pipefail

if [ "${RASK_SKIP_E2E:-}" = "1" ]; then
  echo "run-e2e-local: RASK_SKIP_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

# How much of this machine may this gate take?
#
# The port collision was fixed in #626, but two suites on one machine still contend for resources, and
# the way that surfaces is the expensive one: a plausible-looking red in one or both runs, minutes after
# the contention, with nothing in the log pointing back at it. It has already cost one unexplained
# timeout across two worktrees that were coordinating and still both believed the machine was idle.
#
# The answer is no longer one all-or-nothing lane held for the whole run. This gate spends most of its
# time building and publishing — measured at 9m30s of a 10m12s run before `dotnet test` even started —
# and that work shares a machine perfectly well; it is the browser suite that cannot. So the budget in
# scripts/lib/machine-lane.sh is claimed in two steps: the build and publishes below run alongside
# whatever else is going on, and rask_e2e_await_slots (further down, just before the suite) is the one
# point that waits. Everything about HOW that waiting is ordered, and why it is decided by process
# rather than by a lockfile, lives in machine-lane.sh and lane-claim.sh rather than being restated here.
#
# The two anchored lines it prints — "run-e2e-local: still queued after …" and "run-e2e-local: refused
# to start …" — are contracts: scripts/lib/build-failure.sh greps for them to tell a busy machine from
# a broken branch, and rewording either silently reclassifies a contended run as a genuine failure.
#
# Sourced unconditionally, though the WAIT itself is skipped under CI: the contention hint on a failing
# suite at the end of this script needs rask_other_heavy_builds either way, and a helper that exists
# only on one branch of an `if` is a "command not found" waiting for the first CI run.
# shellcheck source=lib/e2e-concurrency.sh
. "$root/scripts/lib/e2e-concurrency.sh"
# The slot budget this gate now claims against, rather than taking the whole machine for its whole
# run. Sources e2e-concurrency.sh itself, so the line above is redundant in effect and kept in place
# because the contention hint at the end of this script names that file as where its helper lives.
# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"

# Wait until this machine has room for the browser suite.
#
# MOVED, in the change that split this gate by phase. This block used to run at the top of the script,
# so a gate held the whole machine from its very first line. Measured on a live run: 9m30s of build
# and publish before `dotnet test` appeared at all — roughly a quarter of the ~40m norm, during which
# seven other worktrees were blocked while this one used a SINGLE core (the build below is -m:1, and
# stays that way). Builds are throughput work and tolerate sharing; the browser suite genuinely does
# not. So the build and publish phases now take a partial claim, several worktrees may build at once,
# and only this function serialises. A gate that queues here has already done its building.
#
# The claim is PUBLISHED BEFORE THE WAIT, and that ordering is load-bearing rather than tidy. A gate
# queued here has not started `dotnet test` yet, so nothing in its own process tree says what it is
# about to need; without an explicit claim its juniors would read it as merely building, under-count
# it, and start work it is about to need the whole machine for. See scripts/lib/lane-claim.sh.
#
# Everything #934 established is kept: ordering by process age, the deadline, the poll interval, and
# every override. So are the two ANCHORED output lines below — scripts/lib/build-failure.sh greps for
# them to decide whether a red gate means "your branch is broken" or "your machine was busy", and a
# reworded line there silently reclassifies a contended run as a genuine failure.
rask_e2e_await_slots() {
  # Skipped under CI purely so this can never be the thing that breaks an automated run. Nothing in
  # .github/workflows runs the browser E2E today, so this is insurance against a future parallel path.
  [ -n "${CI:-}" ] && return 0

  slots_needed="$(rask_lane_budget)"
  rask_lane_claim "$slots_needed" test

  rask_lane_fits "$slots_needed" && return 0

  echo "run-e2e-local: this machine's slots are taken, and the browser suite needs all $slots_needed."
  rask_e2e_name_seniors
  echo
  echo "            Two suites on one machine contend for resources. The port collision was fixed in"
  echo "            #626, but contention still surfaces as a plausible-looking red in one or both runs,"
  echo "            minutes later, with nothing in the log pointing back at it. It has already cost one"
  echo "            unexplained timeout between two worktrees that were coordinating and still both"
  echo "            believed the machine was idle."
  echo

  if [ "${RASK_E2E_ALLOW_CONCURRENT:-}" = "1" ]; then
    # Now strictly better than it used to be: the claim above stays published, so this run is at least
    # honest about the load it is adding and everyone else accounts for it. It still buys a result you
    # cannot trust.
    echo "run-e2e-local: RASK_E2E_ALLOW_CONCURRENT=1 — starting alongside it anyway."
    echo "            Treat any failure as suspect until you have re-run it alone."
    return 0
  fi

  queue_deadline_s="${RASK_E2E_QUEUE_TIMEOUT:-5400}"
  queue_poll_s="${RASK_E2E_QUEUE_POLL:-20}"
  queue_waited_s=0

  if [ "${RASK_E2E_QUEUE:-1}" = "0" ]; then
    echo "            Wait for the run above to finish, then push again. To run anyway:"
    echo "                RASK_E2E_ALLOW_CONCURRENT=1 git push        (or set it for this script)"
    echo "            and treat any failure as suspect until you have re-run it alone."
    # A distinct, anchored line for rask_build_failure_kind. It cannot classify on the banner above:
    # that now prints on runs which go on to QUEUE AND SUCCEED, so keying on it would relabel a
    # genuine failure hours later as contention.
    echo "run-e2e-local: refused to start — RASK_E2E_QUEUE=0 and the lane is held."
    exit 1
  fi

  echo "run-e2e-local: queued behind it — waiting up to $((queue_deadline_s / 60))m for the slots."
  echo "            The build and publishes above are already done, so this wait is the suite only."
  echo "            Ctrl-C to give up; RASK_E2E_QUEUE=0 to refuse immediately instead of waiting;"
  echo "            RASK_SKIP_E2E=1 to skip the gate entirely."

  # Recomputed FRESH each poll rather than cached. A waiter holding a stale list would keep waiting
  # for a pid that had already exited and — worse — would miss a gate that started later but outranks
  # it after a tie-break.
  while ! rask_lane_fits "$slots_needed"; do
    if [ "$queue_waited_s" -ge "$queue_deadline_s" ]; then
      echo
      echo "run-e2e-local: still queued after $((queue_waited_s / 60))m — giving up rather than waiting silently."
      echo "            The slots are held by:"
      rask_e2e_name_seniors
      echo "            A gate that has outlived the ~40m norm is usually a wedged run, not a busy"
      echo "            machine — check it before assuming you are merely unlucky."
      exit 1
    fi
    sleep "$queue_poll_s"
    queue_waited_s=$((queue_waited_s + queue_poll_s))
    # One line a minute: enough to show the wait is alive inside a hook printing thousands of build
    # lines, quiet enough not to become the noise it is reporting on.
    if [ "$((queue_waited_s % 60))" -eq 0 ]; then
      echo "run-e2e-local: still queued ($((queue_waited_s / 60))m)…"
    fi
  done

  echo "run-e2e-local: slots free after $((queue_waited_s / 60))m $((queue_waited_s % 60))s — starting the suite."
}

# Printing WHICH run and HOW LONG is the useful half — "there is a conflict" tells you there is a
# problem, `ps -o etime=` is what lets you decide whether to wait for it or investigate your own red.
rask_e2e_name_seniors() {
  rask_lane_senior_gates | while read -r pid slots; do
    # Through the same indirected accessors the ordering uses, not a bare `ps`. Two reasons, and the
    # second is the one that matters: it keeps this testable, and it stops the list going silently
    # empty. A senior can exit between being ranked and being named, and a bare `ps` then prints
    # nothing for it — so a refusal could name NOBODY, which is precisely the unexplained kind this
    # gate has always gone out of its way not to be. The pid is printed either way.
    elapsed="$(rask_e2e_etime_of "$pid")"
    cmd="$(rask_e2e_command_of "$pid" | cut -c1-110)"
    [ -n "$elapsed" ] || elapsed="(exited)"
    echo "               pid $pid, $slots slot(s), running for ${elapsed}: $cmd"
  done
  return 0
}

# RASK_E2E_QUEUE=0 means "give me my push back rather than the wait", so it has to be answered BEFORE
# the build, not after it.
#
# Splitting this gate by phase moved the only slot check to just above the suite — roughly ten minutes
# in, by this file's own measurement. That is right for waiting (the wait is then spent having already
# built) and wrong for refusing: someone who set QUEUE=0 precisely to avoid a delay would have paid the
# entire build first and only then been told no. The refusal is therefore asked twice, once here where
# it can still save the ten minutes, and once below for a lane that fills up while we build.
#
# Same anchored line both times — scripts/lib/build-failure.sh greps for it to tell a busy machine from
# a broken branch.
if [ -z "${CI:-}" ] && [ "${RASK_E2E_QUEUE:-1}" = "0" ] && [ "${RASK_E2E_ALLOW_CONCURRENT:-}" != "1" ]; then
  if ! rask_lane_fits "$(rask_lane_budget)"; then
    echo "run-e2e-local: this machine's slots are taken, and the browser suite needs all $(rask_lane_budget)."
    rask_e2e_name_seniors
    echo "            Wait for the run above to finish, then push again. To run anyway:"
    echo "                RASK_E2E_ALLOW_CONCURRENT=1 git push        (or set it for this script)"
    echo "            and treat any failure as suspect until you have re-run it alone."
    echo "run-e2e-local: refused to start — RASK_E2E_QUEUE=0 and the lane is held."
    exit 1
  fi
fi


# shellcheck source=lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"

# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

# The playground publish below needs the native relink (see the note there), which needs the wasm-tools
# workload + emscripten. Check up front: without it the publish fails several minutes in, with an error
# about a missing runtime pack rather than about a missing workload.
#
# This catches wasm-tools never having been installed. It does NOT catch the case in #718 — a concurrent
# workload install elsewhere on the machine making it transiently unresolvable — because `dotnet workload
# list` keeps listing it as installed throughout. That one surfaces as NETSDK1147 during the build below
# and is classified there.
if ! dotnet workload list 2>/dev/null | grep -q '^wasm-tools'; then
  echo "run-e2e-local: the 'wasm-tools' workload is not installed." >&2
  echo "  The playground publishes with a native relink so its tutorial can run SQLite in the browser." >&2
  echo "  Install it once with:  sudo dotnet workload install wasm-tools" >&2
  exit 1
fi

# WasmBuildNative=false: build every WASM sample against the prebuilt .NET-WASM runtime, the same mode
# the fixtures serve (Site/Playground/Standalone all publish with it below) and the CI unit gate uses.
# Without it, the slnx build compiles Rask.Example.Wasm with the native relink (unset WasmBuildNative →
# InvariantGlobalization forces it) while the other WASM builds stay no-native — two modes writing the
# same obj/, so the fingerprinted _framework assets can drift out of sync with the SRI hashes the boot
# import map pins. The browser then blocks the mismatched asset and the runtime hangs at "Loading… 96%"
# (WasmExampleAppFixture boots this build with `dotnet run --no-build`). One consistent mode = no drift,
# and it skips the slow, flaky relink. Serial (-m:1): the nested WASM publish double-builds Rask.Core.dll.
echo "==> Build the E2E graph once (Release, serial, prebuilt WASM runtime)"
# Teed and classified by error kind: this build is the first thing to fail when the machine cannot build
# browser targets, and reporting that as a broken journey sends people to debug a test that is fine
# (#718). `set -o pipefail` above is what makes the pipeline report dotnet's status rather than tee's.
build_log="$(mktemp -t rask-e2e-build.XXXXXX)"
# rask_lane_release rides along here rather than in a second trap, because a bare `trap ... EXIT`
# REPLACES any handler already installed — a separate release trap added later would silently destroy
# this one and leak the build log, or vice versa. Releasing is harmless when no claim was ever made,
# so this is safe on every exit path including the early ones above.
trap 'rm -f "$build_log"; rask_lane_release' EXIT

build_status=0
dotnet build Rask.slnx -c Release -m:1 -p:WasmBuildNative=false 2>&1 | tee "$build_log" || build_status=$?

if [ "$build_status" -ne 0 ]; then
  # .githooks/pre-push captures this same output and delivers the verdict itself when it is the caller
  # (RASK_GATE_WRAPPED=1) — a direct run gets the explanation here, a wrapped one is not told twice.
  if [ "${RASK_GATE_WRAPPED:-}" != "1" ]; then
    echo >&2
    rask_explain_build_failure \
      "$(rask_build_failure_kind "$build_log")" \
      "E2E gate" \
      "FAILED — the E2E graph does not compile."
  fi
  exit "$build_status"
fi

echo "==> Publish the samples the E2E fixtures boot"
# Server shard boots the *published* host; the WASM static-host shards need their published wwwroot bundle.
dotnet publish samples/Rask.Example.Server -c Release --no-build --no-restore --nologo
# The Shop fixture runs the published app too, so the fixture boots what a deploy would.
dotnet publish samples/Rask.Example.Shop -c Release --no-build --no-restore --nologo
# The playground is the one sample published WITH the native relink, and it has to be: its tutorial track
# runs EF Core against SQLite in the browser, which means linking e_sqlite3 (a static archive) into the
# runtime. Passing -p:WasmBuildNative=false here would silently drop the data packages (see
# RaskPlaygroundData in its csproj) and the tutorial half of PlaygroundExampleTests would fail.
#
# Which makes this project the exact case the note above warns about — one project built two ways into one
# obj/ — so it gets the TFM intermediates cleared first. Without this the scoped-asset bake, staged under
# obj/Release/net10.0-browser/rask-scoped/_rask/a, is left over from the no-native build and does NOT make
# it into the publish: wwwroot/_rask/ is simply absent, the page 404s on the PlaygroundView.js that owns
# mountEditor, the editor never mounts, and every journey dies waiting for a permanently disabled Run
# button. It reads as a hang, names nothing, and reproduces only on some runs — whichever mode wrote obj/
# last. Clearing only obj/Release/net10.0-browser keeps obj/project.assets.json, so the restore below is
# still incremental.
#
# bin/ is cleared alongside obj/. Be clear about what this does and does not do: it is NOT the fix for
# #650. It was added on an A/B that looked decisive at the time (obj alone -> 0 files under
# publish/wwwroot/_rask, obj + bin -> 6) and was then contradicted by three sessions' worth of runs,
# including reproductions on branches that already had it. It is kept only because it is cheap — one
# recompile of a project whose native relink dominates this step anyway — and because the TFM-intermediate
# clear above it is load-bearing for the two-modes-into-one-obj/ reason given there.
#
# #650 itself is fixed, below, by -nodeReuse:false; see the note on that publish for the mechanism. This
# comment used to end by saying the bake wrote zero files for reasons no cleaning could address and that
# the fix was "still open" — true when written, stale since, and directly contradicted by the note 20
# lines down. BakeScopedAssetsTask now also fails the build rather than baking an empty bundle and
# reporting success (see its zero-files Log.LogError), so the silent version of this cannot recur.
#
# Two traps when verifying anything in this area, both of which have already produced false confidence:
# running the gate twice back to back does not distinguish these cases, since both runs do the same thing;
# and `dotnet publish` does not clean its output directory, so _rask left by an earlier good publish makes
# a broken publish look fine — delete the publish dir first.
rm -rf samples/Rask.Example.Playground/obj/Release/net10.0-browser \
       samples/Rask.Example.Playground/bin/Release/net10.0-browser
# It also RE-RESTORES (no --no-restore, unlike the publishes above). RaskPlaygroundData gates the EF Core /
# SQLitePCLRaw PackageReferences, so the package graph differs between the two modes — and the build above
# restored in the other one. MSBuild does not error when a PackageReference appears after restore, it
# silently ignores it: the bundle would compile with RASK_PLAYGROUND_DATA defined (chapters 5-8 unlocked)
# while _framework shipped no EF Core at all, and the E2E would burn its full timeout on a CS0246.
#
# -nodeReuse:false is the actual difference between this publish working and not. BakeScopedAssetsTask
# inspects the built assemblies with Assembly.LoadFrom, and MSBuild reuses worker nodes between builds: a
# publish landing on a node that already loaded an assembly of the same simple name (from the solution
# build above) hits a FileLoadException, skips that assembly, and bakes an empty bundle while reporting
# success. Measured here at 3 failures in 4 consecutive publishes on identical inputs — which is why this
# looked like a stale-output problem for so long, and why neither clean below ever fixed it: the
# conflicting state is in the process, not on disk. A fresh node per publish removes the conflict.
# The task now also fails rather than baking nothing silently, so this is the fix and that is the net.
dotnet publish samples/Rask.Example.Playground -c Release -nodeReuse:false --nologo
dotnet publish samples/Rask.Example.Site -c Release --no-restore -p:WasmBuildNative=false --nologo
# The other exception to WasmBuildNative=false, and the slowest line here (an emscripten relink, minutes
# not seconds): this sample runs SQLite in the browser, and SQLite is a native library. Skipping the
# relink produces a bundle that boots and then fails on every database call, so the flag must NOT be
# passed. BrowserJobsWasmAppFixture checks the published output and says so if it was.
#
# Which makes it the same two-modes-into-one-obj/ case as the playground above, so it gets the same
# treatment. It has no scoped assets today, so the missing bake that bites the playground cannot bite it
# yet — which is exactly why this is worth doing now rather than after someone adds a scoped .css to the
# sample and spends an afternoon on a 404 that names nothing. Unlike the playground it keeps --no-restore:
# nothing here gates a PackageReference on the build mode, so the graph is identical either way.
rm -rf samples/Rask.Example.Wasm.Jobs/obj/Release/net10.0-browser
dotnet publish samples/Rask.Example.Wasm.Jobs -c Release --no-restore --nologo

# This used to shell out to `pwsh <path>/playwright.ps1 install chromium`, and skipped itself whenever
# pwsh was missing. On Linux that is the common case, not the edge one — PowerShell is in neither
# Fedora's nor Debian's repositories — so the gate printed a skip notice into thousands of build lines
# and the suite then failed with a missing-browser error that named the browsers rather than the skip.
#
# The ps1 was never doing any work: six lines that load Microsoft.Playwright.dll and call its Main. The
# package ships the real driver alongside it, so rask_playwright_driver finds that and we run it
# directly. See scripts/lib/playwright.sh for why `npx playwright install` is not an equivalent shortcut.
echo "==> Ensure Playwright browsers are installed"
# Resolve in the condition, install in the body — deliberately, not stylistically. `set -e` is suspended
# inside an `if` condition, so running the install there would swallow a failed download into the else
# branch and let the gate continue having installed nothing. Only the lookup belongs in the condition,
# where "not found" genuinely is not fatal; the install then runs under `set -e` and stops the gate.
if pw_driver="$(rask_playwright_driver tests/Rask.Examples.E2E.Tests/bin/Release)"; then
  pw_node="$(printf '%s\n' "$pw_driver" | sed -n 1p)"
  pw_cli="$(printf '%s\n' "$pw_driver" | sed -n 2p)"
  "$pw_node" "$pw_cli" install chromium
else
  echo "   (skipped auto-install: no bundled Playwright driver under"
  echo "    tests/Rask.Examples.E2E.Tests/bin/Release — build the E2E project once, then:"
  echo "      scripts/playwright.sh install chromium)"
fi

# RASK_E2E_FILTER narrows the run while you are iterating on ONE journey. The publishes above still
# happen — they are what makes the bundles the tests boot — but a single journey then costs one run
# instead of the whole suite. Unset (the gate's own case) it runs everything, which is the only
# setting the pre-push hook should ever use.
#
#   RASK_E2E_FILTER='FullyQualifiedName~PlaygroundExampleTests' scripts/run-e2e-local.sh
# The one point in this gate that serialises. Everything above — the preflight, the solution build,
# the eight sample publishes — ran with a partial claim alongside whatever else the machine was doing.
rask_e2e_await_slots

e2e_filter="${RASK_E2E_FILTER:-FullyQualifiedName~Rask.Examples.E2E.Tests}"
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> Browser journey E2E (FILTERED: $e2e_filter)"
  echo "    Not the full gate. Clear RASK_E2E_FILTER before trusting a green run."
else
  echo "==> Browser journey E2E (Rask.Examples.E2E.Tests)"
fi
# Not bare, and `set -e` is why this needs saying: this used to be the script's last statement, so a
# red suite propagated dotnet's exit code with no layer above it — unlike the build step, which is
# explained. That is where #850's cost actually sits. The suite is timing-sensitive; under a competing
# build it fails in ways indistinguishable from real bugs, and the hours go on treating a load flake
# as a defect. Naming the suspicion at the moment of failure is the cheapest thing that attacks that,
# and it adds a line rather than making a decision, so it has no false-positive cost.
set +e
dotnet test tests/Rask.Examples.E2E.Tests/bin/Release/net10.0/Rask.Examples.E2E.Tests.dll \
  --filter "$e2e_filter" \
  --logger "console;verbosity=normal"
e2e_status=$?
set -e

if [ "$e2e_status" -ne 0 ]; then
  # Sampled AFTER the failure rather than during it. A competing build lasts minutes, so it is very
  # likely still there; a sampler running alongside the suite would be more machinery and would itself
  # be load the gate does not need.
  competing="$(rask_other_heavy_builds | tr '\n' ' ')"

  if [ -n "${competing// /}" ]; then
    {
      echo
      echo "run-e2e-local: a heavy build was running on this machine during this suite."
      for pid in $competing; do
        elapsed="$(ps -o etime= -p "$pid" 2>/dev/null | tr -d ' ')"
        cmd="$(ps -o command= -p "$pid" 2>/dev/null | cut -c1-120)"
        [ -n "$elapsed" ] && echo "               pid $pid, running for ${elapsed}: $cmd"
      done
      echo
      echo "            These journeys and the WebSocket tests are timing-sensitive, and contention"
      echo "            reads as a plausible failure with nothing in the log pointing back at it."
      echo "            RE-RUN THIS SUITE ALONE before investigating the failure above — it may be"
      echo "            real, and this line does not claim otherwise. It only says the run was not"
      echo "            clean enough to conclude that it is."
    } >&2
  fi

  exit "$e2e_status"
fi

echo
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> Browser E2E passed — FILTERED run ($e2e_filter), not the full gate."
else
  echo "==> Browser E2E passed."
fi

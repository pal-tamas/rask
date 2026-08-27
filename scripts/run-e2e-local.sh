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

# Is another copy of this gate already running?
#
# The port collision was fixed in #626, but two suites on one machine still contend for resources, and
# the way that surfaces is the expensive one: a plausible-looking red in one or both runs, minutes after
# the contention, with nothing in the log pointing back at it. It has already cost one unexplained
# timeout across two worktrees that were coordinating and still both believed the machine was idle.
#
# REFUSE by default, with an override. This runs from .githooks/pre-push, which prints thousands of
# build lines, and a warning in the middle of that is a warning nobody reads — which would leave the
# contention to be discovered later as a red that looks real. Refusing also matches how every other
# opt-out here already works (RASK_SKIP_E2E, RASK_SKIP_CLI_BUILD_E2E, RASK_SKIP_WATCH_E2E), so it needs
# no new convention.
#
# The argument against — that a blocked push with a false positive becomes "I cannot push and I do not
# know why" — is real, and is answered in two places rather than by warning instead. The match is
# decided on the EXECUTABLE POSITION in the other process's argv (rask_is_e2e_gate_command), because a
# bare name matched a shell whose argv merely mentioned the script the first time this was tested — and
# an anchor on the scripts/ path, which was the first fix, then silently missed the relative invocation
# the docs actually tell you to use. And the refusal prints the offending pid, its elapsed time, its full
# command line, and the exact variable that overrides it, so it can never be the unexplained kind.
#
# Detected by PROCESS, never by a lockfile: a lockfile survives kill -9 and a laptop sleep and then
# wedges the gate until someone works out what to delete. Process detection is self-healing — a run
# someone Ctrl-C'd leaves nothing behind. This repo has enough gates that failed quietly without adding
# one that fails loudly at the worst moment.
#
# Printing WHICH run and HOW LONG is the useful half — "there is a conflict" tells you there is a
# problem, `ps -o etime=` is what lets you decide whether to wait for it or investigate your own red.
#
# Skipped under CI purely so this can never be the thing that breaks an automated run. Nothing in
# .github/workflows runs the browser E2E today — release.yml only packs, ci.yml runs the benchmark
# gates — so this is insurance against a future parallel path, not protection of a known one.
if [ -z "${CI:-}" ]; then
  # shellcheck source=lib/e2e-concurrency.sh
  . "$root/scripts/lib/e2e-concurrency.sh"
  others="$(rask_other_e2e_runs | tr '\n' ' ')"

  if [ -n "${others// /}" ]; then
    echo "run-e2e-local: another browser E2E gate is already running on this machine."
    for pid in $others; do
      elapsed="$(ps -o etime= -p "$pid" 2>/dev/null | tr -d ' ')"
      cmd="$(ps -o command= -p "$pid" 2>/dev/null | cut -c1-120)"
      [ -n "$elapsed" ] && echo "               pid $pid, running for ${elapsed}: $cmd"
    done
    echo
    echo "            Two suites on one machine contend for resources. The port collision was fixed in"
    echo "            #626, but contention still surfaces as a plausible-looking red in one or both runs,"
    echo "            minutes later, with nothing in the log pointing back at it. It has already cost one"
    echo "            unexplained timeout between two worktrees that were coordinating and still both"
    echo "            believed the machine was idle."
    echo
    echo "            Wait for the run above to finish, then push again. To run anyway:"
    echo "                RASK_E2E_ALLOW_CONCURRENT=1 git push        (or set it for this script)"
    echo "            and treat any failure as suspect until you have re-run it alone."
    if [ "${RASK_E2E_ALLOW_CONCURRENT:-}" != "1" ]; then
      exit 1
    fi
    echo "run-e2e-local: RASK_E2E_ALLOW_CONCURRENT=1 — starting alongside it anyway."
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
trap 'rm -f "$build_log"' EXIT

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
# The Shop fixture runs the published app too: an in-repo `dotnet run` never materialises the
# _content/Rask.Bootstrap static assets its shell links.
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
e2e_filter="${RASK_E2E_FILTER:-FullyQualifiedName~Rask.Examples.E2E.Tests}"
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> Browser journey E2E (FILTERED: $e2e_filter)"
  echo "    Not the full gate. Clear RASK_E2E_FILTER before trusting a green run."
else
  echo "==> Browser journey E2E (Rask.Examples.E2E.Tests)"
fi
dotnet test tests/Rask.Examples.E2E.Tests/bin/Release/net10.0/Rask.Examples.E2E.Tests.dll \
  --filter "$e2e_filter" \
  --logger "console;verbosity=normal"

echo
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> Browser E2E passed — FILTERED run ($e2e_filter), not the full gate."
else
  echo "==> Browser E2E passed."
fi

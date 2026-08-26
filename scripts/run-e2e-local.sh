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

# shellcheck source=lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"

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

echo "==> Ensure Playwright browsers are installed"
pw="$(find tests/Rask.Examples.E2E.Tests/bin/Release -name 'playwright.ps1' 2>/dev/null | head -1)"
if [ -n "$pw" ] && command -v pwsh >/dev/null 2>&1; then
  pwsh "$pw" install chromium
else
  echo "   (skipped auto-install: pwsh or playwright.ps1 not found — install browsers manually if the run"
  echo "    fails with a missing-browser error: 'pwsh <path>/playwright.ps1 install chromium')"
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

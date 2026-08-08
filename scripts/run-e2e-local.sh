#!/usr/bin/env bash
# Local E2E gate.
#
# The browser-journey E2E (tests/Rask.Examples.E2E.Tests, Playwright) and the on-device native E2E
# (tests/Rask.Native.Appium.Tests, Appium) no longer run in CI — they run here, locally, and the
# pre-push hook (.githooks/pre-push) enforces this browser gate before code leaves the machine.
#
# This mirrors the build-once → publish-samples → run-shards flow the old .github/workflows/e2e.yml
# used. The on-device native Appium suite needs a booted emulator/simulator + an Appium server, so it
# can't run unattended here; run it manually per docs/native.md.
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

# The playground publish below needs the native relink (see the note there), which needs the wasm-tools
# workload + emscripten. Check up front: without it the publish fails several minutes in, with an error
# about a missing runtime pack rather than about a missing workload.
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
dotnet build Rask.slnx -c Release -m:1 -p:WasmBuildNative=false

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
# bin/ is cleared as well as obj/, because clearing only the intermediates was measured to be
# insufficient: on one machine, an A/B inside this gate gave 0 files under publish/wwwroot/_rask with obj
# alone and 6 with obj + bin, the one-line change being the only difference. On another machine the same
# sequence — solution build, clear obj, publish — reproduces cleanly and ships its files every time, so
# whatever decides it is not in this script.
#
# The mechanism is therefore NOT known. The first explanation (a stale no-native bin/ letting the publish
# treat the compile as up to date, so the bake never re-runs) is contradicted by those clean runs, where
# the bake re-ran and published normally; it is recorded here only so nobody re-derives and re-believes
# it. The clean is a deliberate superset justified by cost — one recompile of a project whose native
# relink dominates this step regardless — not by a claim either measurement supports.
#
# Worth knowing when verifying any fix here: running the gate twice back to back does NOT distinguish
# these, since both runs clear the same things and leave the same things stale. Nor does inspecting
# publish/wwwroot after a passing run — `dotnet publish` does not clean its output directory, so _rask
# left by an earlier good publish makes a broken publish look fine. Delete the publish dir first.
# _RaskVerifyPublishedScopedAssets (Rask.Wasm.targets) turns the whole class into a build error if it
# recurs, which is the durable half of this. See #650.
rm -rf samples/Rask.Example.Playground/obj/Release/net10.0-browser \
       samples/Rask.Example.Playground/bin/Release/net10.0-browser
# It also RE-RESTORES (no --no-restore, unlike the publishes above). RaskPlaygroundData gates the EF Core /
# SQLitePCLRaw PackageReferences, so the package graph differs between the two modes — and the build above
# restored in the other one. MSBuild does not error when a PackageReference appears after restore, it
# silently ignores it: the bundle would compile with RASK_PLAYGROUND_DATA defined (chapters 5-8 unlocked)
# while _framework shipped no EF Core at all, and the E2E would burn its full timeout on a CS0246.
dotnet publish samples/Rask.Example.Playground -c Release --nologo
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

echo "==> Browser journey E2E (Rask.Examples.E2E.Tests)"
dotnet test tests/Rask.Examples.E2E.Tests/bin/Release/net10.0/Rask.Examples.E2E.Tests.dll \
  --filter "FullyQualifiedName~Rask.Examples.E2E.Tests" \
  --logger "console;verbosity=normal"

echo
echo "==> Browser E2E passed."
echo "    Reminder: the on-device native E2E (Rask.Native.Appium.Tests) is NOT part of this gate — it needs"
echo "    a booted emulator/simulator + Appium. Run it before shipping native changes (see docs/native.md)."

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
dotnet publish samples/Rask.Example.Playground -c Release --no-restore -p:WasmBuildNative=false --nologo
dotnet publish samples/Rask.Example.Site -c Release --no-restore -p:WasmBuildNative=false --nologo

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

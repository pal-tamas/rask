#!/usr/bin/env bash
# The browser host's own journeys in a real Chromium, by hand: tests/Rask.Wasm.Fixture.Wasm — a page drawn for the
# signed-in user, an API of its own server, a sign-in page — served from an ASP.NET host inside the test process that
# issues the cookie (tests/Rask.Wasm.E2E.Tests). Two tabs of one browser: a sign-out in one, a refused API call in the
# other.
#
#   scripts/run-wasm-e2e-local.sh
#   RASK_E2E_FILTER='FullyQualifiedName~SignInEndedElsewhereTests' scripts/run-wasm-e2e-local.sh
#
# A gate of its own because nothing else publishes the fixture, and the site gate's static host has no server to sign
# anybody in. Listed in scripts/run-all-gates.sh.
#
# Waits for the machine exactly as the browser gate does (scripts/lib/e2e-admission.sh, under its own name), and the
# same overrides apply: RASK_E2E_QUEUE=0, RASK_E2E_ALLOW_CONCURRENT=1, RASK_E2E_QUEUE_TIMEOUT, RASK_SKIP_E2E=1.
set -euo pipefail

if [ "${RASK_SKIP_E2E:-}" = "1" ]; then
  echo "run-wasm-e2e-local: RASK_SKIP_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
# shellcheck source=lib/dotnet-env.sh
. "$root/scripts/lib/dotnet-env.sh"
cd "$root"

export RASK_E2E_GATE=run-wasm-e2e-local

# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"
# shellcheck source=lib/e2e-admission.sh
. "$root/scripts/lib/e2e-admission.sh"
# shellcheck source=lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"
# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

rask_e2e_refuse_before_build

build_log="$(mktemp -t rask-wasm-e2e-build.XXXXXX)"
trap 'rm -f "$build_log"; rask_lane_release' EXIT

project=tests/Rask.Wasm.E2E.Tests
# Release for both, because the host serves the publish of the configuration its own assembly was built in.
# WasmBuildNative=false for the reason run-e2e-local.sh gives: one WASM build mode, so the fingerprinted runtime assets
# and the boot import map's hashes cannot drift apart in a shared obj/. Serial, because a WASM publish builds Rask.Core
# twice. MinVerSkip=true like every other gate, and no analyzers like every other browser gate: the unit gate's `build`
# part runs them (run-e2e-local.sh).
echo "==> Publish the WASM fixture the journeys serve (Release)"
build_status=0
dotnet publish tests/Rask.Wasm.Fixture.Wasm/Rask.Wasm.Fixture.Wasm.csproj -c Release -m:"${RASK_BUILD_SLOTS:-1}" \
  -p:WasmBuildNative=false -p:MinVerSkip=true -p:RunAnalyzers=false --nologo 2>&1 \
  | tee "$build_log" || build_status=$?

if [ "$build_status" -eq 0 ]; then
  echo "==> Build the browser-journey project (Release)"
  # The literal .csproj path, which E2EGateCoverageTests looks for to know this gate runs the suite.
  dotnet build tests/Rask.Wasm.E2E.Tests/Rask.Wasm.E2E.Tests.csproj -c Release -p:MinVerSkip=true -p:RunAnalyzers=false --nologo 2>&1 \
    | tee -a "$build_log" || build_status=$?
fi

if [ "$build_status" -ne 0 ]; then
  if [ "${RASK_GATE_WRAPPED:-}" != "1" ]; then
    echo >&2
    rask_explain_build_failure \
      "$(rask_build_failure_kind "$build_log")" \
      "WASM E2E gate" \
      "FAILED — the WASM E2E graph does not compile."
  fi
  exit "$build_status"
fi

echo "==> Ensure Playwright browsers are installed"
if pw_driver="$(rask_playwright_driver "$project/bin/Release")"; then
  pw_node="$(printf '%s\n' "$pw_driver" | sed -n 1p)"
  pw_cli="$(printf '%s\n' "$pw_driver" | sed -n 2p)"
  "$pw_node" "$pw_cli" install chromium
else
  echo "   (skipped auto-install: no bundled Playwright driver under $project/bin/Release)"
fi

rask_e2e_await_slots

filter="${RASK_E2E_FILTER:-FullyQualifiedName~Rask.Wasm.E2E.Tests}"
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> WASM browser journeys (FILTERED: $filter)"
  echo "    Not the full gate. Clear RASK_E2E_FILTER before trusting a green run."
else
  echo "==> WASM browser journeys (Rask.Wasm.E2E.Tests)"
fi
dotnet test "$project/bin/Release/net11.0/Rask.Wasm.E2E.Tests.dll" --filter "$filter" --logger "console;verbosity=normal"

echo
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> WASM browser E2E passed — FILTERED run ($filter), not the full gate."
else
  echo "==> WASM browser E2E passed."
fi

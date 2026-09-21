#!/usr/bin/env bash
# Browser SQLite in a real Chromium, by hand: full-text search (HasFullTextSearch / Search / FullText.Highlight)
# on a database inside WebAssembly (#1129).
#
#   scripts/run-browser-sqlite-e2e-local.sh
#
# A gate of its own because it is the only one that links native code: e_sqlite3 is linked into the WASM bundle
# (no -p:WasmBuildNative=false), which needs the wasm-tools workload. Every other browser gate publishes with
# WasmBuildNative=false and so never proves the SQLite build a browser app ships. Listed in scripts/run-all-gates.sh.
#
# Waits for the machine exactly as the browser gate does (scripts/lib/e2e-admission.sh, under its own name), and the
# same overrides apply: RASK_E2E_QUEUE=0, RASK_E2E_ALLOW_CONCURRENT=1, RASK_E2E_QUEUE_TIMEOUT, RASK_SKIP_E2E=1.
set -euo pipefail

if [ "${RASK_SKIP_E2E:-}" = "1" ]; then
  echo "run-browser-sqlite-e2e-local: RASK_SKIP_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

export RASK_E2E_GATE=run-browser-sqlite-e2e-local

# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"
# shellcheck source=lib/e2e-admission.sh
. "$root/scripts/lib/e2e-admission.sh"
# shellcheck source=lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"
# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

if ! dotnet workload list 2>/dev/null | grep -q '^wasm-tools'; then
  echo "run-browser-sqlite-e2e-local: the wasm-tools workload is not installed, and without it e_sqlite3 is not" >&2
  echo "                              linked into the bundle. Install it: dotnet workload install wasm-tools" >&2
  exit 1
fi

rask_e2e_refuse_before_build

build_log="$(mktemp -t rask-browser-sqlite-e2e-build.XXXXXX)"
trap 'rm -f "$build_log"; rask_lane_release' EXIT

project=tests/Rask.SQLite.Browser.E2E.Tests
# Release for both, because the static host serves the publish of the configuration its own assembly was built in.
# Serial, because a WASM publish builds Rask.Core twice. MinVerSkip=true like every other gate.
echo "==> Publish the full-text fixture (Release, native-linked)"
build_status=0
dotnet publish tests/Rask.SQLite.Browser.Fixture.Wasm/Rask.SQLite.Browser.Fixture.Wasm.csproj -c Release -m:1 \
  -p:MinVerSkip=true --nologo 2>&1 | tee "$build_log" || build_status=$?

if [ "$build_status" -eq 0 ]; then
  echo "==> Build the browser-journey project (Release)"
  dotnet build tests/Rask.SQLite.Browser.E2E.Tests/Rask.SQLite.Browser.E2E.Tests.csproj -c Release -p:MinVerSkip=true --nologo 2>&1 \
    | tee -a "$build_log" || build_status=$?
fi

if [ "$build_status" -ne 0 ]; then
  if [ "${RASK_GATE_WRAPPED:-}" != "1" ]; then
    echo >&2
    rask_explain_build_failure \
      "$(rask_build_failure_kind "$build_log")" \
      "browser SQLite E2E gate" \
      "FAILED — the browser SQLite E2E graph does not compile."
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

echo "==> Browser SQLite journeys (Rask.SQLite.Browser.E2E.Tests)"
dotnet test "$project/bin/Release/net10.0/Rask.SQLite.Browser.E2E.Tests.dll" --logger "console;verbosity=normal"

echo
echo "==> Browser SQLite E2E passed."

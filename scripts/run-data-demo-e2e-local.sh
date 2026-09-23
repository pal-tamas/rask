#!/usr/bin/env bash
# The rask.sh data demo in a real Chromium, by hand: src/Rask.Site.DataDemo — a Rask.Data aggregate in SQLite inside
# the tab, listed through Rask.Query, searched with FTS5 and kept across reloads — published exactly as pages.yml
# publishes it (Release, native-linked, <base href="/demos/data/">) and served under /demos/data/.
#
#   scripts/run-data-demo-e2e-local.sh
#
# A gate of its own for the same reason as scripts/run-browser-sqlite-e2e-local.sh: it links e_sqlite3 natively into
# the WASM bundle (no -p:WasmBuildNative=false), which needs the wasm-tools workload. Listed in scripts/run-all-gates.sh.
#
# Waits for the machine exactly as the browser gate does (scripts/lib/e2e-admission.sh, under its own name), and the
# same overrides apply: RASK_E2E_QUEUE=0, RASK_E2E_ALLOW_CONCURRENT=1, RASK_E2E_QUEUE_TIMEOUT, RASK_SKIP_E2E=1.
set -euo pipefail

if [ "${RASK_SKIP_E2E:-}" = "1" ]; then
  echo "run-data-demo-e2e-local: RASK_SKIP_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

export RASK_E2E_GATE=run-data-demo-e2e-local

# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"
# shellcheck source=lib/e2e-admission.sh
. "$root/scripts/lib/e2e-admission.sh"
# shellcheck source=lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"
# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

if ! dotnet workload list 2>/dev/null | grep -q '^wasm-tools'; then
  echo "run-data-demo-e2e-local: the wasm-tools workload is not installed, and without it e_sqlite3 is not linked" >&2
  echo "                         into the bundle. Install it: dotnet workload install wasm-tools" >&2
  exit 1
fi

rask_e2e_refuse_before_build

build_log="$(mktemp -t rask-data-demo-e2e-build.XXXXXX)"
trap 'rm -f "$build_log"; rask_lane_release' EXIT

project=tests/Rask.Site.DataDemo.E2E.Tests
# Release for both, because the static host serves the publish of the configuration its own assembly was built in.
# Serial, because a WASM publish builds Rask.Core twice. MinVerSkip=true like every other gate.
echo "==> Publish the data demo (Release, native-linked, under /demos/data/)"
build_status=0
dotnet publish src/Rask.Site.DataDemo/Rask.Site.DataDemo.csproj -c Release -m:1 \
  -p:MinVerSkip=true --nologo 2>&1 | tee "$build_log" || build_status=$?

if [ "$build_status" -eq 0 ]; then
  echo "==> Build the browser-journey project (Release)"
  dotnet build tests/Rask.Site.DataDemo.E2E.Tests/Rask.Site.DataDemo.E2E.Tests.csproj -c Release -p:MinVerSkip=true --nologo 2>&1 \
    | tee -a "$build_log" || build_status=$?
fi

if [ "$build_status" -ne 0 ]; then
  if [ "${RASK_GATE_WRAPPED:-}" != "1" ]; then
    echo >&2
    rask_explain_build_failure \
      "$(rask_build_failure_kind "$build_log")" \
      "data demo E2E gate" \
      "FAILED — the data demo E2E graph does not compile."
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

echo "==> Data demo journeys (Rask.Site.DataDemo.E2E.Tests)"
dotnet test "$project/bin/Release/net10.0/Rask.Site.DataDemo.E2E.Tests.dll" --logger "console;verbosity=normal"

echo
echo "==> Data demo E2E passed."

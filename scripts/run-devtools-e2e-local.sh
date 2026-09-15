#!/usr/bin/env bash
# Rask DevTools in a real Chromium, by hand: the pill, the panel and every tab, against real pages.
#
#   scripts/run-devtools-e2e-local.sh
#   RASK_E2E_FILTER='FullyQualifiedName~ServerTreeJourneyTests' scripts/run-devtools-e2e-local.sh
#
# A gate of its own rather than a step in scripts/run-e2e-local.sh, because the devtools exist only in a Debug
# build and that gate builds Release: folding this in would add a Debug build of the graph to a gate that already
# spends most of its ten minutes building. Listed in scripts/run-all-gates.sh.
#
# Waits for the machine exactly as the browser gate does (scripts/lib/e2e-admission.sh, under its own name), and the
# same overrides apply: RASK_E2E_QUEUE=0, RASK_E2E_ALLOW_CONCURRENT=1, RASK_E2E_QUEUE_TIMEOUT, RASK_SKIP_E2E=1.
set -euo pipefail

if [ "${RASK_SKIP_E2E:-}" = "1" ]; then
  echo "run-devtools-e2e-local: RASK_SKIP_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

export RASK_E2E_GATE=run-devtools-e2e-local

# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"
# shellcheck source=lib/e2e-admission.sh
. "$root/scripts/lib/e2e-admission.sh"
# shellcheck source=lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"
# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

rask_e2e_refuse_before_build

build_log="$(mktemp -t rask-devtools-e2e-build.XXXXXX)"
trap 'rm -f "$build_log"; rask_lane_release' EXIT

project=tests/Rask.DevTools.E2E.Tests
# Debug, because that is the only build the devtools are in. MinVerSkip=true like every other gate, so this build
# and theirs share obj/ instead of recompiling each other's graph on every commit; the journeys host their app in
# this test process, so no version identity ever has to resolve.
echo "==> Build the devtools browser-journey project (Debug)"
build_status=0
dotnet build tests/Rask.DevTools.E2E.Tests/Rask.DevTools.E2E.Tests.csproj -c Debug -p:MinVerSkip=true --nologo 2>&1 \
  | tee "$build_log" || build_status=$?

if [ "$build_status" -ne 0 ]; then
  if [ "${RASK_GATE_WRAPPED:-}" != "1" ]; then
    echo >&2
    rask_explain_build_failure \
      "$(rask_build_failure_kind "$build_log")" \
      "devtools E2E gate" \
      "FAILED — the devtools E2E graph does not compile."
  fi
  exit "$build_status"
fi

echo "==> Ensure Playwright browsers are installed"
if pw_driver="$(rask_playwright_driver "$project/bin/Debug")"; then
  pw_node="$(printf '%s\n' "$pw_driver" | sed -n 1p)"
  pw_cli="$(printf '%s\n' "$pw_driver" | sed -n 2p)"
  "$pw_node" "$pw_cli" install chromium
else
  echo "   (skipped auto-install: no bundled Playwright driver under $project/bin/Debug)"
fi

rask_e2e_await_slots

filter="${RASK_E2E_FILTER:-FullyQualifiedName~Rask.DevTools.E2E.Tests}"
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> DevTools browser journeys (FILTERED: $filter)"
  echo "    Not the full gate. Clear RASK_E2E_FILTER before trusting a green run."
else
  echo "==> DevTools browser journeys (Rask.DevTools.E2E.Tests)"
fi

# DOTNET_MODIFIABLE_ASSEMBLIES=debug is what a `dotnet watch` session sets, and the only way the runtime draws its
# own dev-error overlay, which the Errors journey opens the panel from. It has to be in the test process's
# environment when that process starts; the journey checks and names this script if it is not.
set +e
DOTNET_MODIFIABLE_ASSEMBLIES=debug dotnet test "$project/bin/Debug/net10.0/Rask.DevTools.E2E.Tests.dll" \
  --filter "$filter" \
  --logger "console;verbosity=normal"
status=$?
set -e

if [ "$status" -ne 0 ]; then
  competing="$(rask_other_heavy_builds | tr '\n' ' ')"
  if [ -n "${competing// /}" ]; then
    {
      echo
      echo "run-devtools-e2e-local: a heavy build was running on this machine during this suite."
      echo "            These journeys are timing-sensitive; RE-RUN THIS SUITE ALONE before investigating the"
      echo "            failure above. It may be real — this line only says the run was not clean enough to tell."
    } >&2
  fi
  exit "$status"
fi

echo
if [ -n "${RASK_E2E_FILTER:-}" ]; then
  echo "==> DevTools browser E2E passed — FILTERED run ($filter), not the full gate."
else
  echo "==> DevTools browser E2E passed."
fi

#!/usr/bin/env bash
# Local unit/integration gate.
#
# The unit + integration suite no longer runs in CI — it runs here, locally, and the pre-commit hook
# (.githooks/pre-commit) enforces it before a code commit. Mirrors the old ci.yml `unit` job: build the
# solution once (no WASM bundle — no unit test consumes it), then run every test EXCEPT the browser E2E
# (those are their own local gate — see scripts/run-e2e-local.sh).
#
# Usage:  scripts/run-unit-local.sh
# Skip:   RASK_SKIP_UNIT=1 (also honoured by the pre-commit hook)
set -euo pipefail

if [ "${RASK_SKIP_UNIT:-}" = "1" ]; then
  echo "run-unit-local: RASK_SKIP_UNIT=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

echo "==> Build (Release, no WASM bundle: -p:RaskWasm=false -p:WasmBuildNative=false)"
dotnet build Rask.slnx -c Release -p:RaskWasm=false -p:WasmBuildNative=false -p:MinVerSkip=true

echo "==> Unit & integration tests (excludes the browser E2E)"
dotnet test Rask.slnx -c Release --no-build \
  --filter "FullyQualifiedName!~Rask.Examples.E2E" \
  --logger "console;verbosity=normal"

echo "==> Unit gate passed."

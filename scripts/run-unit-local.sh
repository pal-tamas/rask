#!/usr/bin/env bash
# Local format + unit/integration gate.
#
# Formatting and the unit suite no longer run in CI — they run here, locally, and the pre-commit hook
# (.githooks/pre-commit) enforces this before a code commit. Steps: build once, run the whitespace
# formatter, then run every test EXCEPT the browser E2E (that's its own gate — see run-e2e-local.sh).
#
# Why the WHITESPACE formatter and not full `dotnet format`: full format's style/analyzer passes compile
# the whole solution through their own Roslyn workspace, which runs the `Routes.*` source generator
# differently than `dotnet build` and spuriously reports CS1503 in the routing tests (the real compiler
# builds them clean). The whitespace pass is compile-independent, so it's reliable AND still catches
# indentation / spacing / final-newline violations. Run full `dotnet format Rask.slnx` before a PR for the
# style pass (the build here is warnings-as-errors, so error-severity analyzer rules are already enforced).
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

echo "==> Build once (Release, no WASM bundle: -p:RaskWasm=false -p:WasmBuildNative=false)"
dotnet build Rask.slnx -c Release -p:RaskWasm=false -p:WasmBuildNative=false -p:MinVerSkip=true

echo "==> Formatting check (dotnet format whitespace --verify-no-changes)"
dotnet format whitespace Rask.slnx --verify-no-changes --no-restore

echo "==> Unit & integration tests (excludes the browser E2E)"
dotnet test Rask.slnx -c Release --no-build \
  --filter "FullyQualifiedName!~Rask.Examples.E2E" \
  --logger "console;verbosity=normal"

echo "==> Format + unit gate passed."

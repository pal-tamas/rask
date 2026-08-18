#!/usr/bin/env bash
# Local format + unit/integration gate.
#
# Formatting and the unit suite no longer run in CI — they run here, locally, and the pre-commit hook
# (.githooks/pre-commit) enforces this before a code commit. Steps: build once, run the FULL formatter
# (whitespace + style + analyzers), then run every test EXCEPT the browser E2E (that's its own gate —
# see run-e2e-local.sh).
#
# Why the FULL pass: import ordering is caught by nothing else. The build is warnings-as-errors with
# EnforceCodeStyleInBuild on, but IDE0055 only reports whitespace computed by the Formatter — sorting
# using directives is OrganizeImportsService, a separate mechanism that only `dotnet format` runs. So a
# misordered using drifts in silently (it did: see #584). The full pass is one workspace load, ~36s.
#
# This gate ran `dotnet format whitespace` only until #584, because the style/analyzer passes reported
# CS1503 in the routing tests. That was never spurious, and it is the reason for the Debug step below:
# `dotnet format` evaluates the solution in the DEFAULT configuration (Debug), so it resolves the
# OutputItemType="Analyzer" project references to src/*.Generators/bin/DEBUG/. This gate builds Release,
# so on a machine that has never built Debug those DLLs do not exist — Roslyn then loads no generator,
# `Rask.Core.Routing.Generated.Route<T>()` is never emitted, and every call site fails to bind with
# CS1503 ("cannot convert from '?[]'"). Building the generators in Debug costs ~2s and makes the pass
# deterministic; it is also why the failure looked machine-dependent, since a stale Debug DLL from an
# earlier build hides it.
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

# Cheap and first: the gates' own shared logic. rask_build_failure_kind decides whether a red gate tells
# you your branch is broken or your machine is busy, and it is plain bash, so nothing else would catch a
# regression in it.
echo "==> Gate script tests"
for t in scripts/tests/*.test.sh; do
  [ -e "$t" ] || continue
  bash "$t"
done

echo "==> Build once (Release, no WASM bundle: -p:RaskWasm=false -p:WasmBuildNative=false)"
dotnet build Rask.slnx -c Release -p:RaskWasm=false -p:WasmBuildNative=false -p:MinVerSkip=true

echo "==> Source generators in Debug (dotnet format resolves analyzers from the default configuration)"
for proj in src/*.Generators/*.csproj; do
  dotnet build "$proj" -c Debug --nologo -v quiet
done

echo "==> Formatting check (dotnet format --verify-no-changes: whitespace + style + analyzers)"
dotnet format Rask.slnx --verify-no-changes --no-restore

echo "==> Unit & integration tests (excludes the browser E2E)"
dotnet test Rask.slnx -c Release --no-build \
  --filter "FullyQualifiedName!~Rask.Examples.E2E" \
  --logger "console;verbosity=normal"

echo "==> Format + unit gate passed."

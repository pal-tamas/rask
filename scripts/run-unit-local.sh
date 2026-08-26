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

# The generated TypeScript is checked by tsc, which needs node. The rest of this gate deliberately does
# not (the build passes -p:RaskSpaBuild=false), so the toolchain is provisioned here when it can be and
# the check is excluded — LOUDLY — when it cannot. What must not happen is the third option: a test that
# quietly reports success without ever running a type-checker, which is how a generator emitting
# malformed TypeScript would ship green.
#
# Cached under artifacts/, so the install is a one-off rather than a per-run download.
# The generated TypeScript is compiled by tsgo, which the test fetches through npx at a pinned version
# (npx caches it, so only the first run downloads). The rest of this gate deliberately needs no node —
# the build passes -p:RaskSpaBuild=false — so when npx is absent the check is excluded here, LOUDLY.
# What must not happen is the third option: a test that quietly reports success without ever having run
# a type-checker, which is how a generator emitting malformed TypeScript would ship green.
tsc_filter=""
if ! command -v npx >/dev/null 2>&1; then
  echo "run-unit-local: WARNING — npx is not on PATH, so the generated TypeScript was NOT type-checked." >&2
  echo "  Install Node.js to run that check, or run it directly with:" >&2
  echo "    RASK_TSC=<path-to>/tsgo dotnet test tests/Rask.Spa.Tasks.Tests --filter TypeScriptCompiles" >&2
  tsc_filter="&FullyQualifiedName!~TypeScriptCompiles"
fi

echo "==> Unit & integration tests (excludes the browser E2E)"
# --blame-crash: when a test host dies below the managed layer, the run reports "Test host process
# crashed" with no exception, no stack and not even the name of the test that was running — and since
# the solution runs ~40 assemblies at once, the last lines of console output belong to whichever OTHER
# assembly happened to be writing, which is how #769 spent an investigation on Rask.Server.Tests over a
# crash in an assembly that reported more tests than Rask.Server.Tests has. Blame writes a per-host
# sequence file naming the test in flight and collects a dump, so the next occurrence is diagnosable
# instead of merely observed. It costs nothing on a green run.
dotnet test Rask.slnx -c Release --no-build \
  --filter "FullyQualifiedName!~Rask.Examples.E2E$tsc_filter" \
  --blame-crash \
  --results-directory "$root/artifacts/test-blame" \
  --logger "console;verbosity=normal"

echo "==> Format + unit gate passed."

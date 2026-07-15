#!/usr/bin/env bash
# The SQLite load gate: run it for any change under src/Rask.SQLite* (the rask-ship skill does this for you).
#
# It is deliberately NOT part of scripts/run-unit-local.sh: that one is the pre-commit hook, and adding ~2
# minutes to every commit is how a gate ends up permanently RASK_SKIP'd.
#
# The gate asserts invariants and same-run ratios, never absolute milliseconds — see LoadGate.cs for why, and
# for what it does and does not catch.
set -euo pipefail

if [[ "${RASK_SKIP_SQLITE_LOAD:-0}" == "1" ]]; then
  echo "run-sqlite-load-local: skipped (RASK_SKIP_SQLITE_LOAD=1)."
  exit 0
fi

cd "$(git rev-parse --show-toplevel)"

PROJECT=benchmarks/Rask.Benchmarks.Sqlite/Rask.Benchmarks.Sqlite.csproj

echo "==> Build (Release; the harness is meaningless in Debug)"
dotnet build "$PROJECT" -c Release -p:MinVerSkip=true

echo "==> Gate (Tier 1 invariants + the Tier 2 checks that need a real box)"
dotnet run -c Release --project "$PROJECT" -p:MinVerSkip=true --no-build -- check "$@"

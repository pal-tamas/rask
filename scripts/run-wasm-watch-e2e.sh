#!/usr/bin/env bash
# WASM hot-reload gate — does an edit to a C# component reach a running browser app?
#
# The rest of the WASM channel is covered cheaply: StaticWebAssetsManifestFileProviderTests for serving
# the build bundle, WasmHotReloadBridgeTests for the announcement, HotReloadClientContractTests for the
# shared indicator, DevCommandTests for the dev-bundle switch. This is the one hop none of those can
# reach — Mono applying a metadata delta to a live WASM runtime — so it needs a real browser and a real
# `dotnet watch` session.
#
# Opt-in because it runs a full WASM client build before it can assert anything (minutes), and because
# it writes a probe source file into samples/Rask.Example.Wasm for the duration of the run.
#
# If the edit never applies, check the PATH first: `dotnet watch` computes an empty hot-reload delta,
# silently, when the project path traverses a symlink. See #536.
#
# Usage:  scripts/run-wasm-watch-e2e.sh
# Skip:   RASK_SKIP_WASM_WATCH_E2E=1
set -euo pipefail

if [ "${RASK_SKIP_WASM_WATCH_E2E:-}" = "1" ]; then
  echo "run-wasm-watch-e2e: RASK_SKIP_WASM_WATCH_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

export RASK_WASM_WATCH_E2E=1

echo "==> Build the E2E test project"
dotnet build tests/Rask.Examples.E2E.Tests/Rask.Examples.E2E.Tests.csproj -c Release -m:1

echo "==> WASM hot-reload gate (real dotnet watch + a real browser)"
dotnet test tests/Rask.Examples.E2E.Tests/Rask.Examples.E2E.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~WasmWatchHotReloadTests" \
  --logger "console;verbosity=normal"

echo
echo "==> WASM hot-reload gate passed."

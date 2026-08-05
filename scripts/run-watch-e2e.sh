#!/usr/bin/env bash
# Local watch hot-reload gate — does an edit actually reach a running app?
#
# Every other hot-reload test drives the coordinator in-process, with the assemblies and the "generated"
# registry classes faked. These are the only tests that scaffold a real app, run it under a real
# `dotnet watch`, edit a source file on disk, and assert the change reached the open live session over
# its WebSocket — the whole loop, including that the session was never torn down, and that the
# "hot reload applied" frame actually arrives.
#
# They are opt-in because they pack this commit's packages, build the generated app, and run several
# watch sessions with 2-minute ceilings — far too slow for the pre-commit inner loop.
#
# If an edit ever stops applying here, check the PATH before anything else: `dotnet watch` computes an
# empty Edit-and-Continue delta, with no error, when the project path traverses a symlink (macOS temp is
# /var/folders/… and /var → /private/var). The harness resolves it via RealPath; that one character of
# difference is what kept two of these red for months. See #536 and the class remarks.
#
# Usage:  scripts/run-watch-e2e.sh
# Verbose watch diagnostics: RASK_WATCH_E2E_VERBOSE=1 scripts/run-watch-e2e.sh
# Skip:   RASK_SKIP_WATCH_E2E=1
set -euo pipefail

if [ "${RASK_SKIP_WATCH_E2E:-}" = "1" ]; then
  echo "run-watch-e2e: RASK_SKIP_WATCH_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

# Shares CliBuildE2E's local feed, so it needs that gate's switch on too.
export RASK_CLI_BUILD_E2E=1
export RASK_WATCH_E2E=1

echo "==> Build the test project (MinVer must stamp a real version — the feed is read off the nupkg name)"
dotnet build tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj -c Release -m:1

echo "==> Watch hot-reload gate (real dotnet watch + a real live session)"
dotnet test tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~WatchHotReloadE2ETests" \
  --logger "console;verbosity=normal"

echo
echo "==> Watch hot-reload gate passed."

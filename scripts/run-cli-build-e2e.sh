#!/usr/bin/env bash
# Local CLI build gate — does the code the CLI writes actually compile?
#
# Every other CLI test asserts on generated *strings*. These are the only tests that pack this commit's
# Rask packages to a local feed, drop a generated project on disk, restore it against that feed, and run
# a real `dotnet build -warnaserror` over the result. They cover every `rask new` flag combination, a
# multi-entity `rask generate feature`, and the whole docs/tutorial walk-through (chapters 1-8).
#
# They are opt-in because they pack 15 packages and run several full builds — far too slow for the
# pre-commit inner loop. The pre-push hook (.githooks/pre-push) runs them, so a scaffolding break is
# caught before it leaves the machine rather than in a beginner's terminal.
#
# Usage:  scripts/run-cli-build-e2e.sh
# Skip:   RASK_SKIP_CLI_BUILD_E2E=1 (also honoured by the pre-push hook)
set -euo pipefail

if [ "${RASK_SKIP_CLI_BUILD_E2E:-}" = "1" ]; then
  echo "run-cli-build-e2e: RASK_SKIP_CLI_BUILD_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

# The gates read this to decide whether to run; without it every case reports SKIPPED.
export RASK_CLI_BUILD_E2E=1

echo "==> Build the CLI test project (Release)"
# MinVerSkip is deliberately NOT set: the gate packs the Rask packages and reads the packed version off
# the nupkg filename, so MinVer must stamp a real version here.
dotnet build tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj -c Release -m:1

echo "==> CLI build gates (scaffold output + tutorial walk-through must compile)"
# Serial (-m:1 above, and one pack at a time inside the fixture): the gates share a single packed feed,
# built lazily on first use.
dotnet test tests/Rask.Cli.Tests/Rask.Cli.Tests.csproj -c Release --no-build \
  --filter "FullyQualifiedName~BuildE2ETests|FullyQualifiedName~TutorialWalkthroughE2ETests" \
  --logger "console;verbosity=normal"

echo
echo "==> CLI build gate passed."

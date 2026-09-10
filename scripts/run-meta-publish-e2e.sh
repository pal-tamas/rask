#!/usr/bin/env bash
# The meta-framework publish gate: a real `dotnet publish` per case, asserting that each supported
# front end's build output lands beside the published app.
#
# This is the only thing proving that `Rask.Meta.Hosting`'s targets actually run node and copy what
# node produced. Every other test in that area asserts on strings.
#
# It used to have no gate and no guard at all: MetaPublishBuildE2ETests lived inside
# tests/Rask.Meta.Hosting.Tests with plain [Fact]s, so it ran inside the ORDINARY UNIT GATE on every
# commit — 14.9s of `dotnet publish` in a suite whose next-slowest assembly is under a second, and a
# hard dependency on node for a gate that is supposed to be node-light. Splitting it into
# tests/Rask.Meta.Hosting.E2E.Tests made it selectable; RASK_META_PUBLISH_E2E makes it opt-in; this
# script is what runs it.
#
# Usage: scripts/run-meta-publish-e2e.sh
# Skip:  RASK_SKIP_META_PUBLISH_E2E=1
set -euo pipefail

if [ "${RASK_SKIP_META_PUBLISH_E2E:-}" = "1" ]; then
  echo "meta publish gate: RASK_SKIP_META_PUBLISH_E2E=1 — skipping."
  exit 0
fi

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

# node is a hard requirement, and saying so beats a publish that fails three targets deep. A gate whose
# first question is "is the tooling here?" must ANSWER it rather than skip quietly — a skip is how a
# gate stops running without anybody noticing.
if ! command -v node >/dev/null 2>&1; then
  echo "meta publish gate: no 'node' on PATH. This gate publishes a real Nuxt/Next front end." >&2
  echo "                  Install Node (Active LTS), or skip with RASK_SKIP_META_PUBLISH_E2E=1." >&2
  exit 1
fi

echo "==> Meta-framework publish gate (tests/Rask.Meta.Hosting.E2E.Tests)"

# -m:1 and a serial run: each case shells out to `dotnet publish`, which takes the machine on its own.
RASK_META_PUBLISH_E2E=1 \
dotnet test tests/Rask.Meta.Hosting.E2E.Tests/Rask.Meta.Hosting.E2E.Tests.csproj \
  -c Release -m:1 \
  --logger "console;verbosity=normal"

echo "==> Meta-framework publish gate passed."

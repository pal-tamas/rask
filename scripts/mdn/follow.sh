#!/usr/bin/env bash
# The MDN follower: refresh src/Rask.Core/Dom/mdn.snapshot.json to MDN's latest stable data, then record
# the public surface that moved with it — Core first, because Rask.Web and Rask.Wasm generate from the same
# snapshot and reference it. Nothing moved: nothing is built.
#
# Usage:  scripts/mdn/follow.sh   (scripts/upstream/follow.sh mdn runs it)
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

snapshot=src/Rask.Core/Dom/mdn.snapshot.json
scripts/mdn/refresh.sh
if ! git diff --quiet -- "$snapshot"; then
  scripts/public-api/record.py src/Rask.Core src/Rask.Web src/Rask.Wasm
fi

#!/usr/bin/env bash
# Bring everything Rask is generated from to its upstream's latest stable release, in the working tree.
#
#   MDN        src/Rask.Core/Dom/mdn.snapshot.json — elements, web APIs, keywords, keys, ARIA
#              (scripts/mdn/refresh.sh), then the public surface that moved with it
#   Node LTS   the line the installers and the docs state (scripts/upstream/node-lts.sh)
#
# It commits nothing: `git status` afterwards is the report. .github/workflows/upstream.yml runs this
# every day, gates the result and lands it; run it by hand to see what tomorrow's run would do.
# Flux UI is followed by scripts/flux/sync.mjs, which needs a browser and a person: a moved look is
# matched by rewriting a component, not by regenerating one.
#
# Serial: both steps write the one tree, and the surface is recorded from a build of the new snapshot.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

snapshot=src/Rask.Core/Dom/mdn.snapshot.json

scripts/mdn/refresh.sh
if ! git diff --quiet -- "$snapshot"; then
  # Core first: Rask.Web and Rask.Wasm generate from the same snapshot and reference it.
  scripts/public-api/record.py src/Rask.Core src/Rask.Web src/Rask.Wasm
fi

scripts/upstream/node-lts.sh

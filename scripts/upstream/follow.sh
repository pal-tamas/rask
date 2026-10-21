#!/usr/bin/env bash
# Bring everything Rask is generated from to its upstream's latest stable release, in the working tree.
#
#   MDN        src/Rask.Core/Dom/mdn.snapshot.json — elements, web APIs, keywords, keys, ARIA
#              (scripts/mdn/refresh.sh), then the public surface that moved with it
#   Node LTS   the line the installers and the docs state, and the npm it bundles (scripts/upstream/node-lts.sh)
#   images     the Docker image tags named in code and scripts: curl, alpine, postgres (images.py)
#   Litestream RaskLitestreamVersion, its per-platform SHA-256s and the template images (litestream.py)
#   Chrome     Rask.Pdf's pinned Chrome for Testing headless shell and its per-platform SHA-256s (chrome.py)
#   compilers  esbuild, tsgo (typescript 7), typescript 6.x and Tailwind, with their digests (tools.py)
#   fixtures   the adapter tests' frameworks: preact, react, vue, solid, svelte, angular, happy-dom (fixtures.py)
#   actions    the majors a scaffolded deploy workflow uses (actions.py)
#
# Dependabot keeps what it can read (.github/dependabot.yml): NuGet, the npm lockfiles, base images, actions.
# It commits nothing: `git status` afterwards is the report. .github/workflows/upstream.yml runs this
# every day, gates the result and lands it; run it by hand to see what tomorrow's run would do.
# Flux UI is followed by scripts/flux/sync.mjs, which needs a browser and a person: a moved look is
# matched by rewriting a component, not by regenerating one. Its lock is measured on the CI runner.
#
# Serial: every step writes the one tree, and the surface is recorded from a build of the new snapshot.
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
scripts/upstream/images.py
scripts/upstream/litestream.py
scripts/upstream/chrome.py
scripts/upstream/tools.py
scripts/upstream/fixtures.py
scripts/upstream/actions.py

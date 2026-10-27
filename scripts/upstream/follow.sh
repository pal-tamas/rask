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
#   front ends the npm releases `rask new` chooses from when the registry does not answer (front-ends.py)
#   CLDR       what Ui.DatetimePicker's button writes between a date and a time, by locale (cldr.mjs)
#
# Dependabot keeps what it can read (.github/dependabot.yml): NuGet, the npm lockfiles, base images, actions.
# It commits nothing: `git status` afterwards is the report. .github/workflows/upstream.yml runs this
# every day, ONE follower per job, and gates and lands each one's commit on its own — a release that
# breaks a gate holds back that source only. Run it by hand to see what tomorrow's run would do.
# Flux UI is followed by scripts/flux/sync.mjs, which needs a browser and a person: a moved look is
# matched by rewriting a component, not by regenerating one. Its lock is measured on the CI runner.
#
# Usage:  scripts/upstream/follow.sh              # every follower, in this tree
#         scripts/upstream/follow.sh mdn cldr     # these only
#         scripts/upstream/follow.sh --list       # the names upstream.yml makes a job of
#
# Serial: every step writes the one tree, and the surface is recorded from a build of the new snapshot.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

followers=(mdn node-lts images litestream chrome tools fixtures actions front-ends cldr)

follow() {
  case "$1" in
    mdn)
      local snapshot=src/Rask.Core/Dom/mdn.snapshot.json
      scripts/mdn/refresh.sh
      if ! git diff --quiet -- "$snapshot"; then
        # Core first: Rask.Web and Rask.Wasm generate from the same snapshot and reference it.
        scripts/public-api/record.py src/Rask.Core src/Rask.Web src/Rask.Wasm
      fi
      ;;
    node-lts)   scripts/upstream/node-lts.sh ;;
    images)     scripts/upstream/images.py ;;
    litestream) scripts/upstream/litestream.py ;;
    chrome)     scripts/upstream/chrome.py ;;
    tools)      scripts/upstream/tools.py ;;
    fixtures)   scripts/upstream/fixtures.py ;;
    actions)    scripts/upstream/actions.py ;;
    front-ends) scripts/upstream/front-ends.py ;;
    cldr)       node scripts/upstream/cldr.mjs ;;
    *) echo "follow.sh: no follower '$1' (follow.sh --list names them)" >&2; exit 2 ;;
  esac
}

if [ "${1:-}" = "--list" ]; then
  printf '%s\n' "${followers[@]}"
  exit 0
fi

for name in "${@:-${followers[@]}}"; do
  follow "$name"
done

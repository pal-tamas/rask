#!/usr/bin/env bash
# Refresh Rask's element surface from MDN: install the LATEST @webref/elements, @webref/idl and
# @mdn/browser-compat-data, and rewrite src/Rask.Core/Dom/mdn.snapshot.json. Commit the diff: the build
# never touches the network, and the generator builds every element type from this file.
# Needs the latest LTS Node (https://nodejs.org/en/about/previous-releases).
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/rask-mdn.XXXXXX")"
trap 'rm -rf "$work"' EXIT

(cd "$work" && npm init -y >/dev/null && npm install --silent --no-audit --no-fund \
  @webref/elements@latest @webref/idl@latest @mdn/browser-compat-data@latest webidl2@latest)
node "$root/scripts/mdn/refresh.mjs" "$work" "$root/src/Rask.Core/Dom/mdn.snapshot.json"

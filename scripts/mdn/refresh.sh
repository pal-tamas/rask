#!/usr/bin/env bash
# Refresh Rask's element surface from MDN: install @webref/elements, @webref/idl and
# @mdn/browser-compat-data and rewrite src/Rask.Core/Dom/mdn.snapshot.json. Commit the diff.
#
# Versions default to each package's `latest` dist-tag (its stable release) and webref's `curated` head. The Rask.Core
# build runs this with exact pins when it sees a newer release (src/Rask.Core/Dom/Rask.Dom.targets):
#   RASK_MDN_BCD, RASK_MDN_IDL, RASK_MDN_ELEMENTS  npm versions (stable releases)
#   RASK_MDN_WEBIDL2                                the IDL parser's version
#   RASK_MDN_WEBREF                                 webref commit for the spec's attribute index
# --ignore-scripts: a build runs this unattended, so no package install script ever executes.
# Needs the latest LTS Node (https://nodejs.org/en/about/previous-releases).
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
work="$(mktemp -d "${TMPDIR:-/tmp}/rask-mdn.XXXXXX")"
trap 'rm -rf "$work"' EXIT

(cd "$work" && npm init -y >/dev/null && npm install --silent --no-audit --no-fund --ignore-scripts \
  "@webref/elements@${RASK_MDN_ELEMENTS:-latest}" "@webref/idl@${RASK_MDN_IDL:-latest}" \
  "@mdn/browser-compat-data@${RASK_MDN_BCD:-latest}" "webidl2@${RASK_MDN_WEBIDL2:-latest}")
node "$root/scripts/mdn/refresh.mjs" "$work" "$root/src/Rask.Core/Dom/mdn.snapshot.json"

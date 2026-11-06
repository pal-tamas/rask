#!/usr/bin/env bash
# Refresh Rask's element surface from MDN: @webref/elements, @webref/idl, @webref/css and @mdn/browser-compat-data,
# UI Events' key and code tables and the WAI-ARIA spec, read by scripts/mdn/refresh.py into
# src/Rask.Core/Dom/mdn.snapshot.json. Commit the diff.
#
# Python 3 alone: each package is its registry tarball, checked against the registry's integrity and read in
# memory, so nothing is installed and no package script runs. The pins (RASK_MDN_BCD, RASK_MDN_WEBREF, …) are
# listed in refresh.py.
set -euo pipefail
exec python3 "$(dirname "$0")/refresh.py" "$@"

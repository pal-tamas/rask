#!/usr/bin/env bash
# Tests for scripts/lib/node-path.sh against a fake nvm root, so nothing here depends on what this
# machine has installed.
#
# Usage:  scripts/tests/node-path.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/node-path.sh
. "$root/scripts/lib/node-path.sh"

tmp="$(mktemp -d -t rask-node-path-test.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

failures=0

# check <name> <expected> <actual>
check() {
  if [ "$2" = "$3" ]; then
    printf '  ok   %s\n' "$1"
  else
    printf '  FAIL %s -> %s (expected %s)\n' "$1" "$3" "$2" >&2
    failures=$((failures + 1))
  fi
}

install_node() {
  mkdir -p "$tmp/nvm/versions/node/$1/bin"
  printf '#!/bin/sh\necho %s\n' "$1" > "$tmp/nvm/versions/node/$1/bin/node"
  chmod +x "$tmp/nvm/versions/node/$1/bin/node"
}

echo "==> node-path"

check "no nvm root gives nothing" "" "$(rask_newest_nvm_node "$tmp/absent")"

# 24.9 against 24.20 is the pair a plain text sort gets backwards.
install_node v16.20.2
install_node v24.9.0
install_node v24.20.0
mkdir -p "$tmp/nvm/versions/node/v99.0.0/bin"   # a version directory with no node in it

check "the newest version that holds a node wins, numerically" "$tmp/nvm/versions/node/v24.20.0/bin" "$(rask_newest_nvm_node "$tmp/nvm")"

found="$(PATH=/usr/bin:/bin RASK_NVM_DIR="$tmp/nvm" bash -c ". '$root/scripts/lib/node-path.sh'; rask_ensure_node >/dev/null; node")"
check "a shell without node gets the nvm one" "v24.20.0" "$found"

kept="$(PATH="$tmp/nvm/versions/node/v16.20.2/bin:/usr/bin:/bin" RASK_NVM_DIR="$tmp/nvm" bash -c ". '$root/scripts/lib/node-path.sh'; rask_ensure_node; node")"
check "a node already on PATH is left alone" "v16.20.2" "$kept"

[ "$failures" -eq 0 ] || exit 1
echo "node-path: all checks passed."

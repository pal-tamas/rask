#!/usr/bin/env bash
# Table test for rask_playwright_driver (scripts/lib/playwright.sh).
#
# This resolver decides whether the browser gate installs anything at all. Its failure mode is the quiet
# one: return nothing, the caller prints a skip notice into thousands of build lines, and the suite dies
# minutes later with a missing-browser error that points at the browsers rather than at the resolver.
# That is precisely what the pwsh check it replaced used to do on any Linux box without PowerShell.
#
# So both directions are stated here rather than left to whatever layout someone happened to build:
# a find that is too loose would match a stale nested copy, and one that is too strict would miss the
# rid directory, which is named for the build host and differs on every machine this runs on.
#
# Usage:  scripts/tests/playwright.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

failures=0
checked=0

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# fixture <dir> <rid> — lay down a build output that looks like Microsoft.Playwright's.
fixture() {
  mkdir -p "$tmp/$1/.playwright/package" "$tmp/$1/.playwright/node/$2"
  : > "$tmp/$1/.playwright/package/cli.js"
  : > "$tmp/$1/.playwright/node/$2/node"
  chmod +x "$tmp/$1/.playwright/node/$2/node"
}

# assert_ok <name> <search-root> <expected-node-suffix> <expected-cli-suffix>
assert_ok() {
  name="$1"
  out="$(rask_playwright_driver "$tmp/$2" || true)"
  got_node="$(printf '%s\n' "$out" | sed -n 1p)"
  got_cli="$(printf '%s\n' "$out" | sed -n 2p)"
  checked=$((checked + 1))

  case "$got_node" in *"$3") node_ok=1 ;; *) node_ok=0 ;; esac
  case "$got_cli"  in *"$4") cli_ok=1  ;; *) cli_ok=0  ;; esac

  if [ "$node_ok" = 1 ] && [ "$cli_ok" = 1 ]; then
    printf '  ok   %-52s -> %s\n' "$name" "${got_node##*/.playwright/}"
  else
    printf '  FAIL %-52s -> node[%s] cli[%s]\n' "$name" "$got_node" "$got_cli" >&2
    failures=$((failures + 1))
  fi
}

# assert_absent <name> <search-root> — resolver must fail, and print nothing.
assert_absent() {
  name="$1"
  checked=$((checked + 1))

  if out="$(rask_playwright_driver "$tmp/$2")"; then
    printf '  FAIL %-52s -> resolved [%s], expected failure\n' "$name" "$(printf '%s' "$out" | tr '\n' ' ')" >&2
    failures=$((failures + 1))
  elif [ -n "$out" ]; then
    printf '  FAIL %-52s -> failed but printed [%s]\n' "$name" "$out" >&2
    failures=$((failures + 1))
  else
    printf '  ok   %-52s -> not found\n' "$name"
  fi
}

echo "==> rask_playwright_driver"

# The three rid directories this actually runs on. The rid is never assumed, so each is stated.
fixture linux-guest   linux-arm64
assert_ok "linux-arm64 (the UTM guest)"        linux-guest   "/node/linux-arm64/node"  "/package/cli.js"

fixture mac-laptop    darwin-arm64
assert_ok "darwin-arm64 (an Apple laptop)"     mac-laptop    "/node/darwin-arm64/node" "/package/cli.js"

fixture ci-box        linux-x64
assert_ok "linux-x64 (a CI box)"               ci-box        "/node/linux-x64/node"    "/package/cli.js"

# Nested under a TFM directory, which is where it really lives: bin/Release/net10.0/.playwright/...
mkdir -p "$tmp/nested/bin/Release/net10.0"
fixture "nested/bin/Release/net10.0" linux-arm64
assert_ok "nested under bin/Release/net10.0"   nested        "/node/linux-arm64/node"  "/package/cli.js"

# Negative cases. Each one used to be a silent skip.
assert_absent "search root does not exist"     no-such-dir

mkdir -p "$tmp/empty"
assert_absent "build output with no .playwright" empty

mkdir -p "$tmp/no-node/.playwright/package"
: > "$tmp/no-node/.playwright/package/cli.js"
assert_absent "cli.js present but no node driver" no-node

mkdir -p "$tmp/no-cli/.playwright/node/linux-arm64"
: > "$tmp/no-cli/.playwright/node/linux-arm64/node"
assert_absent "node driver present but no cli.js" no-cli

echo "==> the resolved driver is runnable"

# The two lines are a node binary and a cli.js in that order, and callers invoke them as `$node $cli`.
# A shell script standing in for the node binary proves the pair comes back the right way round — a
# resolver that swapped them would still pass every assertion above.
mkdir -p "$tmp/runnable/.playwright/package" "$tmp/runnable/.playwright/node/linux-arm64"
: > "$tmp/runnable/.playwright/package/cli.js"
printf '#!/bin/sh\necho "ARGS:$*"\n' > "$tmp/runnable/.playwright/node/linux-arm64/node"
chmod +x "$tmp/runnable/.playwright/node/linux-arm64/node"

checked=$((checked + 1))
driver="$(rask_playwright_driver "$tmp/runnable")"
got="$("$(printf '%s\n' "$driver" | sed -n 1p)" "$(printf '%s\n' "$driver" | sed -n 2p)" install chromium)"
case "$got" in
  *"cli.js install chromium")
    printf '  ok   %-52s -> %s\n' "node first, cli.js second, args appended" "ARGS:...${got##*/}" ;;
  *)
    printf '  FAIL %-52s -> [%s]\n' "node first, cli.js second, args appended" "$got" >&2
    failures=$((failures + 1)) ;;
esac

if [ "$failures" -ne 0 ]; then
  printf '\n%d of %d checks failed\n' "$failures" "$checked" >&2
  exit 1
fi
printf '\n%d checks passed\n' "$checked"

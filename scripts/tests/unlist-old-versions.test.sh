#!/usr/bin/env bash
# Table test for scripts/lib/unlist_select.py -- which versions a release supersedes.
#
# This function decides what gets unlisted from nuget.org on every release, and the two ways it can be
# wrong are not symmetric. Leaving an old alpha listed is untidy; unlisting a version NEWER than the one
# being released takes the current package off the gallery. Lexical string ordering gets exactly that
# wrong (alpha.0.9 vs alpha.0.10, and a prerelease vs its stable), so every ordering rule is pinned here
# rather than left to the cases someone happened to try.
#
# Usage:  scripts/tests/unlist-old-versions.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
select_py="$root/scripts/lib/unlist_select.py"

failures=0
checked=0

# assert <name> <released> <versions-newline-separated> <expected-newline-separated>
assert() {
  local name="$1" released="$2" versions="$3" expected="$4"
  local actual
  # Normalise through the same pipeline on both sides, and collapse whitespace, so that "no versions"
  # compares equal whether it arrives as an empty string or a bare newline.
  actual="$(printf '%s\n' "$versions" | python3 "$select_py" "$released" | sort | xargs)"
  expected="$(printf '%s\n' "$expected" | sort | xargs)"
  checked=$((checked + 1))
  if [ "$actual" = "$expected" ]; then
    printf '  ok   %s\n' "$name"
  else
    printf '  FAIL %s\n       got:      %s\n       expected: %s\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

echo "==> unlist_select: which versions a release supersedes"

assert "prior stables and alphas both go" \
  "0.21.0" \
  "0.19.0
0.20.0
0.20.1-alpha.0.3
0.21.0" \
  "0.19.0
0.20.0
0.20.1-alpha.0.3"

assert "the released version is never unlisted" \
  "0.21.0" \
  "0.21.0" \
  ""

# The dangerous direction: a rerun, or a nightly published after the tag, must survive.
assert "nothing newer is touched" \
  "0.20.0" \
  "0.19.0
0.20.0
0.20.1-alpha.0.1
0.21.0" \
  "0.19.0"

# Lexically "0.20.1-alpha.0.9" > "0.20.1-alpha.0.10"; numerically it is not.
assert "numeric prerelease identifiers compare numerically" \
  "0.20.1-alpha.0.10" \
  "0.20.1-alpha.0.2
0.20.1-alpha.0.9
0.20.1-alpha.0.10
0.20.1-alpha.0.11" \
  "0.20.1-alpha.0.2
0.20.1-alpha.0.9"

# A prerelease ranks below the stable of the same core, so releasing the stable clears its own alphas.
assert "a stable supersedes its own prereleases" \
  "0.21.0" \
  "0.21.0-alpha.0.1
0.21.0-alpha.0.2
0.21.0" \
  "0.21.0-alpha.0.1
0.21.0-alpha.0.2"

# ...and releasing an alpha must NOT unlist the stable it has not reached yet.
assert "an alpha does not supersede a later stable" \
  "0.21.1-alpha.0.1" \
  "0.21.0
0.21.1-alpha.0.1" \
  ""

assert "build metadata is ignored, not treated as a prerelease" \
  "0.21.0+abc" \
  "0.20.0
0.21.0+abc" \
  "0.20.0"

echo
echo "==> listed_versions: which versions are still listed"

listed_py="$root/scripts/lib/listed_versions.py"

# assert_listed <name> <registration-json> <expected-versions-space-separated>
assert_listed() {
  local name="$1" reg="$2" expected="$3"
  local actual
  actual="$(printf '%s' "$reg" | python3 "$listed_py" --parse | sort | xargs)"
  expected="$(printf '%s\n' "$expected" | tr ' ' '\n' | sort | xargs)"
  checked=$((checked + 1))
  if [ "$actual" = "$expected" ]; then
    printf '  ok   %s\n' "$name"
  else
    printf '  FAIL %s\n       got:      %s\n       expected: %s\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

# The whole point of this module: an unlisted version must NOT come back as work to do. Reading the
# flat-container index instead returns every version ever pushed, which spends the entire unlist quota
# re-doing finished work -- measured on rask.native, 209 unlisted and still 209 reported.
assert_listed "unlisted versions are excluded" \
  '{"items":[{"items":[
     {"catalogEntry":{"version":"0.1.0","listed":false}},
     {"catalogEntry":{"version":"0.2.0","listed":true}}
   ]}]}' \
  "0.2.0"

# Older catalog entries simply omit the field; nuget.org treats that as listed, so we must too --
# defaulting it to false would silently skip real work and report success.
assert_listed "a missing listed field means listed" \
  '{"items":[{"items":[{"catalogEntry":{"version":"0.3.0"}}]}]}' \
  "0.3.0"

assert_listed "everything unlisted yields nothing to do" \
  '{"items":[{"items":[
     {"catalogEntry":{"version":"0.1.0","listed":false}},
     {"catalogEntry":{"version":"0.2.0","listed":false}}
   ]}]}' \
  ""

assert_listed "multiple pages are all walked" \
  '{"items":[
     {"items":[{"catalogEntry":{"version":"0.1.0","listed":true}}]},
     {"items":[{"catalogEntry":{"version":"0.2.0","listed":true}}]}
   ]}' \
  "0.1.0 0.2.0"

echo
if [ "$failures" -ne 0 ]; then
  printf '==> unlist_select: %d/%d FAILED\n' "$failures" "$checked" >&2
  exit 1
fi
printf '==> unlist_select: %d/%d passed\n' "$checked" "$checked"

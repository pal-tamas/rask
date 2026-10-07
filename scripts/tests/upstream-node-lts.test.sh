#!/usr/bin/env bash
# gate-inputs: rask\.(sh|ps1)$|docs/installation\.md$|src/Rask\.Cli/NodeRequirement\.cs$|src/Rask\.Templates/[^/]+/Dockerfile$
# scripts/upstream/node-lts.sh, run in a copy of the files it rewrites against a saved nodejs.org index:
# a newer LTS major moves every stated copy at once, and the same line or a non-LTS release moves nothing.
#
# This edit lands with nobody reading it, once a year. A copy it misses is a gate failure at best and an
# installer recommending a retired Node at worst.
#
# Usage:  scripts/tests/upstream-node-lts.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-node-lts.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

files=(src/Rask.Cli/NodeRequirement.cs rask.sh rask.ps1 docs/installation.md scripts/upstream/node-lts.sh
       src/Rask.Templates/react/Dockerfile src/Rask.Templates/server/Dockerfile)
fresh() {
  rm -rf "$tmp/tree"
  for f in "${files[@]}"; do mkdir -p "$tmp/tree/$(dirname "$f")"; cp "$root/$f" "$tmp/tree/$f"; done
}

stated="$(sed -n 's/.*ScaffoldLine = new(\([0-9]*\), *\([0-9]*\), *\([0-9]*\)).*/\1.\2.\3/p' "$root/src/Rask.Cli/NodeRequirement.cs")"
major="${stated%%.*}"
next=$((major + 2))

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}
count() { (grep -rF --exclude=node-lts.sh -- "$1" "$tmp/tree" || true) | wc -l | tr -d ' '; }

echo "==> upstream node-lts"

fresh
printf '[{"version":"v%s.3.0","lts":false},{"version":"v%s.11.2","lts":"Neon"},{"version":"v%s","lts":"Old"}]' \
  $((next + 1)) "$next" "$stated" > "$tmp/moved.json"
"$tmp/tree/scripts/upstream/node-lts.sh" "$tmp/moved.json" >/dev/null
check "no copy of the old version is left"        0 "$(count "$stated")"
check "ScaffoldLine is the new LTS"               1 "$(count "ScaffoldLine = new($next, 11, 2)")"
check "the doc comment names the new line"        1 "$(count "$next is \"Neon\"")"
check "both installers ask for it"                2 "$( (grep -lF -- "$next.11.2" "$tmp/tree/rask.sh" "$tmp/tree/rask.ps1" || true) | wc -l | tr -d ' ')"
check "the docs stop naming the old line"         0 "$(count "Node $major LTS")"
check "the docs' shorthand floor moved too"       0 "$(count "≥ ${stated%.*}")"
check "a front end's image installs the new line" 1 "$(count "setup_$next.x")"
check "...and no image is left on the old one"    0 "$(count "setup_$major.x")"
check "an image with no Node is left alone"       "" "$(cmp -s "$tmp/tree/src/Rask.Templates/server/Dockerfile" "$root/src/Rask.Templates/server/Dockerfile" || echo changed)"

fresh
printf '[{"version":"v%s.3.0","lts":false},{"version":"v%s.99.0","lts":"Same"}]' $((major + 1)) "$major" > "$tmp/same.json"
"$tmp/tree/scripts/upstream/node-lts.sh" "$tmp/same.json" >/dev/null
check "a newer release on the same line changes nothing" "" \
  "$(for f in "${files[@]}"; do cmp -s "$tmp/tree/$f" "$root/$f" || echo "$f"; done)"

[ "$failures" -eq 0 ] || { echo "upstream node-lts: $failures failed" >&2; exit 1; }
echo "upstream node-lts: ok"

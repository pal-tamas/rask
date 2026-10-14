#!/usr/bin/env bash
# gate-inputs: rask\.(sh|ps1)$|docs/installation\.md$|src/Rask\.Cli/NodeRequirement\.cs$|src/Rask\.Templates/[^/]+/Dockerfile$|src/Rask\.Templates/[^/]+/client/package\.json$|Rask\.(Spa\.Hosting|External)\.props$|scripts/upstream/node-lts\.sh$
# scripts/upstream/node-lts.sh, run in a copy of the files it rewrites against a saved nodejs.org index:
# a new LTS major moves every stated copy and the floor at once, a newer release on the same line moves the
# newest-release copies only, and a release that is not LTS moves nothing.
#
# This edit lands with nobody reading it. A copy it misses is a gate failure at best and an installer
# recommending a retired Node at worst.
#
# Usage:  scripts/tests/upstream-node-lts.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-node-lts.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

files=(src/Rask.Cli/NodeRequirement.cs rask.sh rask.ps1 docs/installation.md scripts/upstream/node-lts.sh
       src/Rask.Templates/react/Dockerfile src/Rask.Templates/server/Dockerfile src/Rask.Templates/react/client/package.json
       src/Rask.Templates/angular/client/package.json
       src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.props src/Rask.External/build/Rask.External.props)
fresh() {
  rm -rf "$tmp/tree"
  for f in "${files[@]}"; do mkdir -p "$tmp/tree/$(dirname "$f")"; cp "$root/$f" "$tmp/tree/$f"; done
}
read_version() { sed -n "s/.*$1 = new(\([0-9]*\), *\([0-9]*\), *\([0-9]*\)).*/\1.\2.\3/p" "$root/src/Rask.Cli/NodeRequirement.cs"; }

stated="$(read_version ScaffoldLine)"
floor="$(read_version BuildFloor)"
major="${stated%%.*}"
next=$((major + 2))

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}
count() { (grep -rF --exclude=node-lts.sh -- "$1" "$tmp/tree" || true) | wc -l | tr -d ' '; }

echo "==> upstream node-lts"

fresh
printf '[{"version":"v%s.3.0","lts":false},{"version":"v%s.11.2","lts":"Neon","npm":"99.1.0"},{"version":"v%s.9.0","lts":"Neon"},{"version":"v%s.0.0","lts":false},{"version":"v%s","lts":"Old"}]' \
  $((next + 1)) "$next" "$next" "$next" "$stated" > "$tmp/moved.json"
"$tmp/tree/scripts/upstream/node-lts.sh" "$tmp/moved.json" >/dev/null
check "no copy of the old version is left"        0 "$(count "$stated")"
check "ScaffoldLine is the newest LTS release"    1 "$(count "ScaffoldLine = new($next, 11, 2)")"
check "the doc comment names the new line"        1 "$(count "$next is \"Neon\"")"
check "both installers ask for it"                2 "$( (grep -lF -- "$next.11.2" "$tmp/tree/rask.sh" "$tmp/tree/rask.ps1" || true) | wc -l | tr -d ' ')"
check "the docs stop naming the old line"         0 "$(count "Node $major LTS")"
check "the docs' shorthand floor moved too"       0 "$(count "≥ ${stated%.*}")"
check "a front end's image installs the new line" 1 "$(count "setup_$next.x")"
check "...and no image is left on the old one"    0 "$(count "setup_$major.x")"
check "an image with no Node is left alone"       "" "$(cmp -s "$tmp/tree/src/Rask.Templates/server/Dockerfile" "$root/src/Rask.Templates/server/Dockerfile" || echo changed)"
check "@types/node follows the new line"          1 "$(count "\"@types/node\": \"^$next.0.0\"")"
check "Angular names the npm that release bundles"  1 "$(count "\"packageManager\": \"npm@99.1.0\"")"
check "the floor is the line's first LTS release" 1 "$(count "BuildFloor = new($next, 9, 0)")"
check "...and both build props enforce it"        2 "$(count ">$next.9.0</")"

fresh
printf '[{"version":"v%s.3.0","lts":false},{"version":"v%s.99.0","lts":"Same"},{"version":"v%s","lts":"Same"}]' \
  $((major + 1)) "$major" "$floor" > "$tmp/same.json"
"$tmp/tree/scripts/upstream/node-lts.sh" "$tmp/same.json" >/dev/null
check "a newer release on the line moves ScaffoldLine" 1 "$(count "ScaffoldLine = new($major, 99, 0)")"
check "...and the installers"                      2 "$( (grep -lF -- "$major.99.0" "$tmp/tree/rask.sh" "$tmp/tree/rask.ps1" || true) | wc -l | tr -d ' ')"
check "...but not the line, the images or the floor" "" \
  "$(for f in src/Rask.Templates/react/Dockerfile src/Rask.Templates/react/client/package.json src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.props src/Rask.External/build/Rask.External.props; do cmp -s "$tmp/tree/$f" "$root/$f" || echo "$f"; done)"
check "...and the codename is untouched"          0 "$( (grep -cF '"Same"' "$tmp/tree/src/Rask.Cli/NodeRequirement.cs" || true) | tr -d ' ')"

fresh
printf '[{"version":"v%s.0.0","lts":false},{"version":"v%s","lts":"Same"},{"version":"v%s","lts":"Same"}]' \
  $((major + 1)) "$stated" "$floor" > "$tmp/none.json"
"$tmp/tree/scripts/upstream/node-lts.sh" "$tmp/none.json" >/dev/null
check "a release that is not LTS changes nothing" "" \
  "$(for f in "${files[@]}"; do cmp -s "$tmp/tree/$f" "$root/$f" || echo "$f"; done)"

[ "$failures" -eq 0 ] || { echo "upstream node-lts: $failures failed" >&2; exit 1; }
echo "upstream node-lts: ok"

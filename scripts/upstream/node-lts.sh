#!/usr/bin/env bash
# Keep every Node the repo states on nodejs.org's Active LTS line: its newest release, and its first as the floor.
#
#   ScaffoldLine (src/Rask.Cli/NodeRequirement.cs) — the newest LTS release, what the installers put on a machine
#                and what every front-end version `rask new` chooses must run on. rask.sh, rask.ps1 and
#                docs/installation.md repeat it; on a new major the templates' Dockerfiles (setup_NN.x) and
#                @types/node follow the line too.
#   BuildFloor   — the first release of that line: the floor a build enforces (RaskSpaMinimumNode,
#                RaskExternalMinimumNode). Rask builds on the latest LTS and nothing older.
#
# NodeRequirementTests and TemplateNodePinTests hold every copy to these two. This rewrites them all in
# one pass, so the gates judge a consistent tree.
#
# Usage:  scripts/upstream/node-lts.sh            (reads nodejs.org)
#         scripts/upstream/node-lts.sh index.json (reads a saved index — the self-test does)
# Exit:   0 always when it could tell; the diff is the report. 1 when the repo changed shape.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

# The installer's own LTS reader and version comparison, rather than a second copy of them.
RASK_INSTALL_LIB_ONLY=1 . ./rask.sh

if [ $# -gt 0 ]; then
  index="$(cat "$1")"
elif ! index="$(curl -fsS --max-time 60 https://nodejs.org/dist/index.json)"; then
  echo "node lts: nodejs.org did not answer; leaving the line as it is." >&2
  exit 0
fi

requirement=src/Rask.Cli/NodeRequirement.cs
read_version() { sed -n "s/.*$1 = new(\([0-9]*\), *\([0-9]*\), *\([0-9]*\)).*/\1.\2.\3/p" "$requirement"; }

# `|| true`: the reader ends in `head -n 1`, which closes the pipe early by design.
latest="$(printf '%s' "$index" | rask_node_lts_version || true)"
stated="$(read_version ScaffoldLine)"
floor="$(read_version BuildFloor)"
[ -n "$stated" ] && [ -n "$floor" ] || { echo "node lts: could not read ScaffoldLine/BuildFloor out of $requirement" >&2; exit 1; }
[ -n "$latest" ] || { echo "node lts: no LTS version in the index; leaving the line as it is." >&2; exit 0; }

releases="$(printf '%s' "$index" | tr '{' '\n')"
codename="$(printf '%s\n' "$releases" | grep -F "\"version\":\"v$latest\"" | sed -n 's/.*"lts":"\([A-Za-z]*\)".*/\1/p' | head -n 1 || true)"
# The index is newest first, so the line's first LTS release is its last entry carrying the codename.
first="$(printf '%s\n' "$releases" | grep -F "\"lts\":\"$codename\"" | tail -n 1 | sed -n 's/.*"version":"v\([0-9.]*\)".*/\1/p' || true)"

moved=""

if [ "$latest" != "$stated" ] && rask_version_ge "$latest" "$stated"; then
  export OLD="$stated" NEW="$latest" CODENAME="$codename" SINCE="$(date -u +%Y-%m)"
  perl -pi -e '
    my ($o, $n) = ($ENV{OLD}, $ENV{NEW});
    my ($om, $omi) = split /\./, $o;
    my ($nm, $nmi, $np) = split /\./, $n;
    s/ScaffoldLine = new\(\d+, *\d+, *\d+\)/ScaffoldLine = new($nm, $nmi, $np)/;
    if ($nm != $om) {
      s/\b$om is "\w+", Active LTS since [\d-]+/$nm is "$ENV{CODENAME}", Active LTS since $ENV{SINCE}/;
      s/Node $om LTS/Node $nm LTS/g;
    }
    s/\Q$o\E/$n/g;
    s/≥ \Q$om.$omi\E\b/≥ $nm.$nmi/g;
  ' "$requirement" rask.sh rask.ps1 docs/installation.md

  if [ "${latest%%.*}" != "${stated%%.*}" ]; then
    new_major="${latest%%.*}"
    # The front-end templates' images install the same line from NodeSource (setup_NN.x), and the
    # templates type against it (@types/node); TemplateNodePinTests holds both to ScaffoldLine's major.
    images="$(grep -lE 'setup_[0-9]+\.x' src/Rask.Templates/*/Dockerfile 2>/dev/null || true)"
    manifests="$(grep -lE '"@types/node"' src/Rask.Templates/*/client/package.json 2>/dev/null || true)"
    # shellcheck disable=SC2086  # one path per line, none with a space in it
    [ -z "$images" ] || NEW_MAJOR="$new_major" perl -pi -e 's/setup_\d+\.x/setup_$ENV{NEW_MAJOR}.x/g' $images
    # shellcheck disable=SC2086
    [ -z "$manifests" ] || NEW_MAJOR="$new_major" perl -pi -e 's/("\@types\/node"\s*:\s*)"\^[0-9.]+"/$1"^$ENV{NEW_MAJOR}.0.0"/g' $manifests
  fi
  moved="$stated -> $latest ($codename)"
fi

if [ -n "$first" ] && [ "$first" != "$floor" ] && rask_version_ge "$first" "$floor"; then
  export FLOOR="$first"
  perl -pi -e 'my ($a, $b, $c) = split /\./, $ENV{FLOOR}; s/BuildFloor = new\(\d+, *\d+, *\d+\)/BuildFloor = new($a, $b, $c)/' "$requirement"
  perl -pi -e 's/(<RaskSpaMinimumNode Condition="[^"]*">)[0-9.]+(<)/${1}$ENV{FLOOR}$2/' src/Rask.Spa.Hosting/build/Rask.Spa.Hosting.props
  perl -pi -e 's/(<RaskExternalMinimumNode Condition="[^"]*">)[0-9.]+(<)/${1}$ENV{FLOOR}$2/' src/Rask.External/build/Rask.External.props
  moved="${moved:+$moved; }floor $floor -> $first"
fi

echo "node lts: ${moved:-$stated is the newest release of the Active LTS line, $floor its first (nodejs.org: $latest)}."

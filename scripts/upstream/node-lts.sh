#!/usr/bin/env bash
# Move the Node line the repo states to nodejs.org's Active LTS, when that has moved to a newer major.
#
# ScaffoldLine (src/Rask.Cli/NodeRequirement.cs) is the source of truth; the installers,
# docs/installation.md and the front-end templates' Dockerfiles repeat it, and NodeRequirementTests
# and TemplateNodePinTests hold them to it. This rewrites every copy
# in one pass, so the gates judge a consistent tree. The build floor (RaskExternalMinimumNode) is a
# different number on purpose and is not touched.
#
# Usage:  scripts/upstream/node-lts.sh            (reads nodejs.org)
#         scripts/upstream/node-lts.sh index.json (reads a saved index — the self-test does)
# Exit:   0 always when it could tell; the diff is the report. 1 when the repo changed shape.
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

# The installer's own LTS reader, rather than a second copy of it.
RASK_INSTALL_LIB_ONLY=1 . ./rask.sh

if [ $# -gt 0 ]; then
  index="$(cat "$1")"
elif ! index="$(curl -fsS --max-time 60 https://nodejs.org/dist/index.json)"; then
  echo "node lts: nodejs.org did not answer; leaving the line as it is." >&2
  exit 0
fi

# `|| true`: the reader ends in `head -n 1`, which closes the pipe early by design.
latest="$(printf '%s' "$index" | rask_node_lts_version || true)"
stated="$(sed -n 's/.*ScaffoldLine = new(\([0-9]*\), *\([0-9]*\), *\([0-9]*\)).*/\1.\2.\3/p' src/Rask.Cli/NodeRequirement.cs)"
[ -n "$stated" ] || { echo "node lts: could not read ScaffoldLine out of src/Rask.Cli/NodeRequirement.cs" >&2; exit 1; }
[ -n "$latest" ] || { echo "node lts: no LTS version in the index; leaving the line as it is." >&2; exit 0; }

if [ "${latest%%.*}" -le "${stated%%.*}" ]; then
  echo "node lts: $stated is on the Active LTS line (nodejs.org: $latest)."
  exit 0
fi

codename="$(printf '%s' "$index" | tr '{' '\n' | grep '"lts":"' | head -n 1 | sed -n 's/.*"lts":"\([A-Za-z]*\)".*/\1/p' || true)"
export OLD="$stated" NEW="$latest" CODENAME="$codename" SINCE="$(date -u +%Y-%m)"

perl -pi -e '
  my ($o, $n) = ($ENV{OLD}, $ENV{NEW});
  my ($om, $omi) = split /\./, $o;
  my ($nm, $nmi, $np) = split /\./, $n;
  s/ScaffoldLine = new\(\d+, *\d+, *\d+\)/ScaffoldLine = new($nm, $nmi, $np)/;
  s/\b$om is "\w+", Active LTS since [\d-]+/$nm is "$ENV{CODENAME}", Active LTS since $ENV{SINCE}/;
  s/\Q$o\E/$n/g;
  s/≥ \Q$om.$omi\E\b/≥ $nm.$nmi/g;
  s/Node $om LTS/Node $nm LTS/g;
' src/Rask.Cli/NodeRequirement.cs rask.sh rask.ps1 docs/installation.md

# The front-end templates' images install the same line from NodeSource (setup_NN.x), and
# TemplateNodePinTests holds them to ScaffoldLine's major.
images="$(grep -lE 'setup_[0-9]+\.x' src/Rask.Templates/*/Dockerfile 2>/dev/null || true)"
if [ -n "$images" ]; then
  # shellcheck disable=SC2086  # one path per line, none with a space in it
  NEW_MAJOR="${latest%%.*}" perl -pi -e 's/setup_\d+\.x/setup_$ENV{NEW_MAJOR}.x/g' $images
fi

echo "node lts: $stated -> $latest ($codename)"

#!/usr/bin/env bash
# The commit the rolling `nightly` tag can point at, for a nightly packed from <sha>.
#
# GitHub refuses the workflow token (it can never hold the `workflows` permission) a new ref on a commit that
# itself changes a file under .github/workflows/ — `gh release create --target <sha>` then fails with
# "HTTP 403: Resource not accessible by integration", even though the commit is already on main. Seen
# 2026-11-03 (nightly run 41608821292, both attempts): 3c0e28e54, a merge that changed eight workflows, was
# refused; 5e1c1eb7f, a merge that changed none, was tagged an hour earlier by the same step. The release
# had already been deleted, so no nightly was listed until the next one.
#
# So the tag goes on the newest commit of <sha>'s first-parent line whose .github/workflows tree is the
# same as each of its parents' — <sha> itself, unless it changed a workflow. The release's title names
# <sha> either way, and that is what scripts/lib/nightly_pick.sh reads. Prints nothing when no such commit
# is found in the last 200; the caller then tags <sha> and lets GitHub decide.
#
# Usage:  scripts/lib/nightly_anchor.sh <sha>   (needs the history)
set -euo pipefail

sha="${1:?usage: nightly_anchor.sh <sha>}"
tree() { git rev-parse -q --verify "$1:.github/workflows" 2>/dev/null || echo none; }

for commit in $(git rev-list --first-parent --max-count=200 "$sha"); do
  own="$(tree "$commit")"
  same=1
  for parent in $(git rev-list --parents -n 1 "$commit" | cut -d' ' -f2-); do
    if [ "$(tree "$parent")" != "$own" ]; then
      same=0
      break
    fi
  done
  if [ "$same" = 1 ]; then
    echo "$commit"
    exit 0
  fi
done

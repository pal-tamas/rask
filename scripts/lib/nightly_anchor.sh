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
# --like <ref> picks instead the newest commit of that line whose .github/workflows tree EQUALS <ref>'s (the
# default branch as it is now). Both refusals of 2026-11-04 (#1275: nightly runs 41790959318 and 41803112719)
# tagged an anchor below <sha> whose workflows differed from main's, while every tag on a commit whose
# workflows matched main's was accepted — so GitHub appears to judge a new ref by its workflows against the
# default branch. The release step tries this candidate first and probes each before it deletes anything.
#
# Usage:  scripts/lib/nightly_anchor.sh <sha> [--like <ref>]   (needs the history)
set -euo pipefail

sha="${1:?usage: nightly_anchor.sh <sha> [--like <ref>]}"
tree() { git rev-parse -q --verify "$1:.github/workflows" 2>/dev/null || echo none; }

if [ "${2:-}" = "--like" ]; then
  want="$(tree "${3:?usage: nightly_anchor.sh <sha> --like <ref>}")"
  for commit in $(git rev-list --first-parent --max-count=200 "$sha"); do
    if [ "$(tree "$commit")" = "$want" ]; then
      echo "$commit"
      exit 0
    fi
  done
  exit 0
fi

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

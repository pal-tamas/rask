#!/usr/bin/env bash
# Where a scoped run measures its change from: the newest commit a green run passed that HEAD descends from.
#
# Each argument is a workflow's run listing, as the REST API names it ("ci.yml/runs?branch=main&event=push");
# the newest green run of each whose commit is an ancestor of HEAD is a candidate, and the newest candidate
# wins. Prints nothing when there is none — the caller then runs the whole set. A green commit HEAD does not
# descend from (a rewritten main, a branch made before it) is no base at all, so a branch cut from an older
# main is measured from the newest green commit it does contain, not sent to the whole set.
#
# Usage:  scripts/lib/green_base.sh <runs query>...   (needs GITHUB_REPOSITORY, gh and the history)
set -euo pipefail

base=""
for query in "$@"; do
  greens="$(gh api "repos/$GITHUB_REPOSITORY/actions/workflows/$query&status=success&per_page=30" \
    --jq '.workflow_runs[].head_sha' 2>/dev/null || true)"
  for green in $greens; do
    git merge-base --is-ancestor "$green" HEAD 2>/dev/null || continue
    if [ -z "$base" ] || git merge-base --is-ancestor "$base" "$green"; then base="$green"; fi
    break   # newest first: this workflow's first ancestor is its newest
  done
done
printf '%s\n' "$base"

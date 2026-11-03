#!/usr/bin/env bash
# Where a scoped ci run measures its change from. Prints a commit, or nothing — the caller then runs the whole set.
#
#   main <this run id>   the head of main's newest EARLIER push run that started its gates, green or red,
#                        that HEAD descends from. Every push's run checks its own change, and only it: a red
#                        run named its push and is fixed forward, so the next one does not run that red set
#                        again on top of its own. A run still waiting for its first gate is no base: it is
#                        cancelled by this one (scripts/lib/cancel_superseded.sh), and this one's scope holds
#                        its change. A run that passed with no gate to run counts; a cancelled one does not.
#   branch               `git merge-base origin/main HEAD`: a ci/** branch is measured by its OWN commits. Until
#                        2026-11-03 it was measured from main's last green commit, so main's churn — every
#                        CI file a push to main touched since — made every branch the whole set.
#
# Until 2026-11-03 main was measured from its last GREEN commit (this workflow's or full.yml's), so one red
# push ran its red set again under every push until it was fixed. Restore at 1.0: measure main from its last
# green push run again (docs/development-workflow.md, "Loosened until 1.0").
#
# Usage:  scripts/lib/green_base.sh main <this run id>   (needs GITHUB_REPOSITORY, gh and the history)
#         scripts/lib/green_base.sh branch               (needs origin/main fetched)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=SCRIPTDIR/ci_runs.sh
. "$here/ci_runs.sh"

case "${1:-}" in
  branch)
    git merge-base origin/main HEAD 2>/dev/null || true
    ;;
  main)
    self="$2"
    runs="$(gh api "repos/$GITHUB_REPOSITORY/actions/workflows/ci.yml/runs?branch=main&event=push&per_page=30" \
      --jq ".workflow_runs[] | select(.id < $self) | \"\(.id) \(.head_sha) \(.conclusion // \"\")\"" 2>/dev/null || true)"
    while read -r id sha conclusion; do
      [ -n "$id" ] || continue
      git merge-base --is-ancestor "$sha" HEAD 2>/dev/null || continue
      case "$conclusion" in
        success) ;;
        cancelled | skipped) continue ;;
        *) gates_started "$id" || continue ;;
      esac
      printf '%s\n' "$sha"
      exit 0
    done <<<"$runs"
    ;;
  *)
    echo "usage: green_base.sh main <this run id> | branch" >&2
    exit 2
    ;;
esac

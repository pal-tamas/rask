#!/usr/bin/env bash
# On main, a newer ci run makes an older one that has not started a gate redundant: cancel it.
#
# Every push gets its own run, so a red one names its push. A run still waiting for its first gate when a
# newer push's run starts names nothing yet, and the newer run's scope already holds its change: it is
# measured from the newest earlier run that STARTED its gates (scripts/lib/green_base.sh), never from the
# waiting one. Runs that have started a gate are left to finish, so a verdict being worked on is never
# thrown away. (full.yml had a half here too until 2026-11-03, when it stopped running behind every push.)
#
# Manual runs are never touched: they ask their own question. Cancelling is best effort; a run that slips
# through just runs.
#
# Usage:  scripts/lib/cancel_superseded.sh <this ci run id>   (needs GITHUB_REPOSITORY and gh, actions: write)
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=SCRIPTDIR/ci_runs.sh
. "$here/ci_runs.sh"

self="$1"
repo="$GITHUB_REPOSITORY"
active='.status == "queued" or .status == "in_progress" or .status == "waiting" or .status == "pending" or .status == "requested"'

# cancel_older <runs query> <newest id>: every active run of the listing older than <newest> with no gate started.
cancel_older() {
  local id
  for id in $(gh api "repos/$repo/actions/workflows/$1&per_page=50" \
                --jq ".workflow_runs[] | select(.id < $2 and .event != \"workflow_dispatch\") | select($active) | .id"); do
    if gates_started "$id"; then
      echo "supersede: run $id has started its gates — left to finish"
    else
      echo "supersede: run $id has not started a gate and run $2 covers it — cancelling"
      gh run cancel "$id" --repo "$repo" >/dev/null 2>&1 || echo "supersede: could not cancel $id"
    fi
  done
}

cancel_older "ci.yml/runs?branch=main&event=push" "$self"

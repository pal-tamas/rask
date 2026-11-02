#!/usr/bin/env bash
# On main, a newer run makes an older one that has not started a gate redundant: cancel it.
#
#   ci    every push gets its own run, so a red one names its push. A run still waiting for its first gate
#         when a newer push's run starts names nothing yet, and the newer run's scope already holds its
#         change: both are measured from the last GREEN commit, which the waiting one's push is not. Runs
#         that have started a gate are left to finish, so a verdict being worked on is never thrown away.
#   full  one run at a time, with the newest waiting. A run holding that one place while every gate of it
#         still waits for a runner kept the newer run (more commits, the same gates) waiting for nothing
#         (2026-11-02, 70 minutes). Only when a newer run waits behind it.
#
# Manual runs are never touched: they ask their own question. Cancelling is best effort; a run that slips
# through just runs.
#
# Usage:  scripts/lib/cancel_superseded.sh <this ci run id>   (needs GITHUB_REPOSITORY and gh, actions: write)
set -euo pipefail

self="$1"
repo="$GITHUB_REPOSITORY"
active='.status == "queued" or .status == "in_progress" or .status == "waiting" or .status == "pending" or .status == "requested"'

# started <run id> -> true when a gate job of the run (not the plan) is running or has run.
started() {
  [ "$(gh api --paginate "repos/$repo/actions/runs/$1/jobs?per_page=100" --jq '[.jobs[]
      | select((.name | startswith("gates / ")) and .name != "gates / plan")
      | select(.status == "in_progress" or (.status == "completed" and .conclusion != "skipped"))] | length' \
    | awk '{ n += $1 } END { print n + 0 }')" -gt 0 ]
}

# cancel_older <runs query> <newest id>: every active run of the listing older than <newest> with no gate started.
cancel_older() {
  local id
  for id in $(gh api "repos/$repo/actions/workflows/$1&per_page=50" \
                --jq ".workflow_runs[] | select(.id < $2 and .event != \"workflow_dispatch\") | select($active) | .id"); do
    if started "$id"; then
      echo "supersede: run $id has started its gates — left to finish"
    else
      echo "supersede: run $id has not started a gate and run $2 covers it — cancelling"
      gh run cancel "$id" --repo "$repo" >/dev/null 2>&1 || echo "supersede: could not cancel $id"
    fi
  done
}

cancel_older "ci.yml/runs?branch=main&event=push" "$self"

newest_full="$(gh api "repos/$repo/actions/workflows/full.yml/runs?branch=main&per_page=50" \
  --jq "[.workflow_runs[] | select(.event != \"workflow_dispatch\") | select($active) | .id] | max // empty")"
[ -z "$newest_full" ] || cancel_older "full.yml/runs?branch=main" "$newest_full"

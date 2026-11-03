#!/usr/bin/env bash
# What scripts/lib/green_base.sh and scripts/lib/cancel_superseded.sh both ask of a ci run. Sourced, not run.
# Needs gh and $GITHUB_REPOSITORY.

# gates_started <run id> -> true when a gate job of the run (not the plan) is running or has run.
gates_started() {
  [ "$(gh api --paginate "repos/$GITHUB_REPOSITORY/actions/runs/$1/jobs?per_page=100" --jq '[.jobs[]
      | select((.name | startswith("gates / ")) and .name != "gates / plan")
      | select(.status == "in_progress" or (.status == "completed" and .conclusion != "skipped"))] | length' \
    | awk '{ n += $1 } END { print n + 0 }')" -gt 0 ]
}

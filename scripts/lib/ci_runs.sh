#!/usr/bin/env bash
# What scripts/lib/green_base.sh, scripts/lib/cancel_superseded.sh, scripts/lib/nightly_pick.sh and pages.yml
# ask of a ci run. Sourced, not run. Needs gh, jq and $GITHUB_REPOSITORY.

# main's VERDICT runs of ci: a push to main, or a dispatch of the push set on main, which ci.yml's run-name
# titles "main verdict <sha>". A commit a workflow lands on main with its own token starts no push run
# (GitHub starts no workflow for what that token does, save a dispatch), so the workflow dispatches one of
# these instead — dependabot-merge.yml, upstream-follow.yml, upstream.yml (#1341); a person's
# `gh workflow run ci.yml --ref main` is one too. A manual run narrowed by `only` or to another set asks its
# own question, and is none. A jq predicate over one run of a listing; nightly.yml, pages.yml and
# upstream.yml spell the same rule as a workflow expression (scripts/tests/ci-runs.test.sh holds them together).
# shellcheck disable=SC2034  # read by the scripts that source this file
MAIN_VERDICT='(.event == "push" or (.event == "workflow_dispatch" and ((.display_title // "") | startswith("main verdict "))))'

# main_verdict <event> <display title> -> true when a ci run with them is main's verdict (MAIN_VERDICT).
main_verdict() {
  [ "$(jq -n --arg event "$1" --arg display_title "$2" "\$ARGS.named | $MAIN_VERDICT")" = true ]
}

# gates_failed <run id> -> true when a gate job of the run failed: a run that ended `cancelled` with one is
# a RED run that gates.yml's fail-fast step cut short, not one a newer push superseded.
gates_failed() {
  [ "$(gh api --paginate "repos/$GITHUB_REPOSITORY/actions/runs/$1/jobs?per_page=100" --jq '[.jobs[]
      | select((.name | startswith("gates / ")) and .conclusion == "failure")] | length' \
    | awk '{ n += $1 } END { print n + 0 }')" -gt 0 ]
}

# gates_started <run id> -> true when a gate job of the run (not the plan) is running or has run.
gates_started() {
  [ "$(gh api --paginate "repos/$GITHUB_REPOSITORY/actions/runs/$1/jobs?per_page=100" --jq '[.jobs[]
      | select((.name | startswith("gates / ")) and .name != "gates / plan")
      | select(.status == "in_progress" or (.status == "completed" and .conclusion != "skipped"))] | length' \
    | awk '{ n += $1 } END { print n + 0 }')" -gt 0 ]
}

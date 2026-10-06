#!/usr/bin/env bash
# Open the issue with this title, or reopen and update the one that already has it.
#
# The upstream workflow's only way of asking for a person: it is silent while it can land what moved,
# and says so here when it cannot. One thread per title, so a problem that lasts a week is one issue.
#
# Usage:  scripts/upstream/issue.sh "<title>" <body-file>      (needs GH_TOKEN with issues: write)
set -euo pipefail

title="$1"
body="$2"

# Captured on its own line: piped straight into jq, a failed listing would read as "no such issue"
# and open a duplicate every day.
listed="$(gh issue list --state all --limit 100 --search "in:title \"$title\"" --json number,title)"
existing="$(jq -r --arg title "$title" '[.[] | select(.title == $title)][0].number // empty' <<<"$listed")"

if [ -n "$existing" ]; then
  gh issue reopen "$existing" >/dev/null 2>&1 || true
  gh issue edit "$existing" --body-file "$body"
else
  gh issue create --title "$title" --body-file "$body"
fi

#!/usr/bin/env bash
# Did the gates refuse a follower's commit for something main already has?
#
# A follower's commit is gated on top of main, so a gate main is red on is red under it too — and that red
# is whoever broke main's to fix, not upstream's. It is main's when EVERY gate that failed in this run (the
# jobs named "<prefix>... gates / <gate>") also failed in main's newest decided full.yml run. Then the
# follower waits for main rather than asking a person; anything else is upstream's, and opens its issue.
#
# Usage:  scripts/upstream/main-already-red.sh <run-id> "<job-name prefix>"   (needs GH_TOKEN, GITHUB_REPOSITORY)
# Exit:   0 main's · 1 upstream's (or nothing to compare)
set -euo pipefail

run="$1"
prefix="$2"
repo="$GITHUB_REPOSITORY"
gate='sub(".*gates / "; "")'

ours="$(gh api --paginate "repos/$repo/actions/runs/$run/jobs?per_page=100" \
  --jq ".jobs[] | select(.conclusion == \"failure\" and (.name | startswith(\"$prefix\")) and (.name | contains(\"gates / \"))) | .name | $gate" | sort -u)"
if [ -z "$ours" ]; then
  echo "main-already-red: no gate failed under '$prefix' — the red is the follower's own."
  exit 1
fi

decided="$(gh api "repos/$repo/actions/workflows/full.yml/runs?branch=main&status=completed&per_page=20" \
  --jq '[.workflow_runs[] | select(.conclusion == "success" or .conclusion == "failure")][0] | "\(.id) \(.conclusion)"')"
read -r main conclusion <<<"$decided"
if [ "$conclusion" != "failure" ]; then
  echo "main-already-red: main's last full run ($main) is ${conclusion:-missing} — the red is upstream's."
  exit 1
fi

mains="$(gh api --paginate "repos/$repo/actions/runs/$main/jobs?per_page=100" \
  --jq ".jobs[] | select(.conclusion == \"failure\") | .name | $gate" | sort -u)"
upstreams="$(comm -23 <(printf '%s\n' "$ours") <(printf '%s\n' "$mains"))"
if [ -n "$upstreams" ]; then
  printf 'main-already-red: red here but not on main (full run %s):\n%s\n' "$main" "$(sed 's/^/  /' <<<"$upstreams")"
  exit 1
fi

printf 'main-already-red: every red gate is red on main too (full run %s):\n%s\n' "$main" "$(sed 's/^/  /' <<<"$ours")"

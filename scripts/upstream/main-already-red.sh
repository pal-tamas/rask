#!/usr/bin/env bash
# Did the gates refuse a follower's commit for something main already has?
#
# A follower's commit is gated on top of main, so a gate main is red on is red under it too — and that red
# is whoever broke main's to fix, not upstream's. It is main's when EVERY gate that failed in this run (the
# jobs named "<prefix>... gates / <gate>") is red on main: red in main's newest decided ci.yml push run, or
# red in main's newest decided full.yml run and not passed by a ci.yml push run since. main's ci is scoped
# (scripts/lib/affected_gates.py) and runs only the gates a push can reach, so a gate it skipped — the CLI
# build, the templates — is red on main only as full.yml saw it (2026-11-09: main's ci green, its full red
# on both, and tools and intl-tel-input each opened an issue for main's red).
# Then the follower waits for main rather than asking a person; anything else is upstream's, and opens its issue.
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

# "<id> <conclusion> <created_at>" of a workflow's newest run on main that passed or failed, or nothing.
newest() {
  gh api "repos/$repo/actions/workflows/$1/runs?branch=main$2&status=completed&per_page=20" \
    --jq '[.workflow_runs[] | select(.conclusion == "success" or .conclusion == "failure")][0] // empty | "\(.id) \(.conclusion) \(.created_at)"'
}
# The gates a run's jobs ended with this conclusion.
gates() {
  gh api --paginate "repos/$repo/actions/runs/$1/jobs?per_page=100" \
    --jq ".jobs[] | select(.conclusion == \"$2\") | .name | $gate" | sort -u
}

ci="" ci_conclusion="" ci_at=""
read -r ci ci_conclusion ci_at <<<"$(newest ci.yml '&event=push')" || true
full="" full_conclusion="" full_at=""
read -r full full_conclusion full_at <<<"$(newest full.yml '')" || true

mains=""
if [ "$ci_conclusion" = failure ]; then
  mains="$(gates "$ci" failure)"
fi
if [ "$full_conclusion" = failure ]; then
  stale=""
  # ISO 8601 timestamps order as strings: a ci run created after full's has seen a newer main.
  if [ -n "$ci" ] && [[ "$ci_at" > "$full_at" ]]; then
    stale="$(gates "$ci" success)"
  fi
  mains="$(printf '%s\n%s\n' "$mains" "$(comm -23 <(gates "$full" failure) <(printf '%s\n' "$stale" | sort -u))")"
fi
mains="$(printf '%s\n' "$mains" | sed '/^$/d' | sort -u)"

if [ -z "$mains" ]; then
  echo "main-already-red: main is green in its last ci (${ci:-none}) and full (${full:-none}) runs — the red is upstream's."
  exit 1
fi

upstreams="$(comm -23 <(printf '%s\n' "$ours") <(printf '%s\n' "$mains"))"
if [ -n "$upstreams" ]; then
  printf 'main-already-red: red here but not on main (ci run %s, full run %s):\n%s\n' "${ci:-none}" "${full:-none}" "$(sed 's/^/  /' <<<"$upstreams")"
  exit 1
fi

printf 'main-already-red: every red gate is red on main too (ci run %s, full run %s):\n%s\n' "${ci:-none}" "${full:-none}" "$(sed 's/^/  /' <<<"$ours")"

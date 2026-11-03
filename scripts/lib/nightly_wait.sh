#!/usr/bin/env bash
# Waits for a ci run's `build` job (the build and the unit tests) and says whether its commit may be published.
# nightly.yml's `prepare` job runs it after the pack, so the pack and the build overlap.
#
#   go=true    the build job passed — the rest of the run may still be going (restore at 1.0: wait for the
#              whole run; docs/development-workflow.md, "Loosened until 1.0")
#   go=true    the run passed with no build job: a scoped run that reached no code
#   go=false   the build job failed or was cancelled, or the run ended without one and did not pass, or the
#              wait ran out
#
# Writes go= to $GITHUB_OUTPUT and why to stdout and the run page. Always exits 0 on a verdict: a red build
# is ci's to report, not the nightly's.
#
# Usage:  scripts/lib/nightly_wait.sh <ci run id>   (needs gh and GITHUB_REPOSITORY)
#         RASK_NIGHTLY_WAIT (seconds, default 3600) and RASK_NIGHTLY_POLL (default 20) bound it.
set -euo pipefail

run="${1:?usage: nightly_wait.sh <ci run id>}"
repo="$GITHUB_REPOSITORY"
deadline=$((SECONDS + ${RASK_NIGHTLY_WAIT:-3600}))

verdict() {
  echo "$2"
  [ -z "${GITHUB_STEP_SUMMARY:-}" ] || echo "$2" >> "$GITHUB_STEP_SUMMARY"
  [ -z "${GITHUB_OUTPUT:-}" ] || echo "go=$1" >> "$GITHUB_OUTPUT"
  exit 0
}

while :; do
  # The latest attempt's jobs: a re-run of a red build that passes counts.
  build="$(gh api --paginate "repos/$repo/actions/runs/$run/jobs?per_page=100" \
    --jq '.jobs[] | select(.name == "gates / build") | "\(.status) \(.conclusion // "")"' 2>/dev/null | head -1 || true)"
  case "$build" in
    "completed success") verdict true "ci run $run: the build job passed." ;;
    completed*) verdict false "Nothing published: the build job of ci run $run ended ${build#completed }." ;;
    "")
      read -r status conclusion <<<"$(gh api "repos/$repo/actions/runs/$run" \
        --jq '"\(.status) \(.conclusion // "")"' 2>/dev/null || true)" || true
      if [ "${status:-}" = "completed" ]; then
        [ "${conclusion:-}" = "success" ] && verdict true "ci run $run passed, with no build job to wait for."
        verdict false "Nothing published: ci run $run ended ${conclusion:-unknown}, with no build job."
      fi
      ;;
  esac
  [ "$SECONDS" -lt "$deadline" ] || verdict false "Nothing published: ci run $run's build job did not finish in time."
  sleep "${RASK_NIGHTLY_POLL:-20}"
done

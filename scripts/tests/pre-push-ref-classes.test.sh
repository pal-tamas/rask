#!/usr/bin/env bash
# Table test for how .githooks/pre-push CLASSIFIES the refs git hands it.
#
# git feeds the hook "<local ref> <local sha> <remote ref> <remote sha>" per ref, on stdin. Three
# classes reach it, and the hook has to tell them apart because two of them skip ~40 minutes of gates:
#
#   1. NO LINES AT ALL — git already decided to send nothing. Either the push is up to date, or it is
#      a non-fast-forward git is about to reject client-side. Nothing transfers, so nothing is gated.
#   2. every line a DELETION (local sha all zeroes) — `git push origin --delete <branch>`. No commit
#      reaches the remote, so there is nothing a build or a browser journey could have an opinion on.
#   3. anything else — real content, which gets the full gate.
#
# Classes 1 and 2 were reported as the same thing (#1047): `pushes_content` stays 0 for both, because
# an empty stdin leaves the loop body unrun exactly as an all-deletions stdin does. A rejected push
# therefore printed "every ref in this push is a DELETION … Skipping the gates" while deleting
# nothing.
#
# That is worth a test rather than a fix alone, because the wrong message is actively dangerous in
# sequence: the reader's next move after a rejection is `git fetch && git merge origin/main`, and a
# CLEAN merge auto-commits WITHOUT the pre-commit gate (git runs `pre-merge-commit`, which this
# repository does not have). So "Skipping the gates" sat in front of a sequence in which nothing had
# been gated, reading as though gating had been unnecessary.
#
# Class 3 is driven with every RASK_SKIP_* lever set, so this test asserts the hook REACHES its gates
# without paying for them. What is pinned is the classification, not the gates themselves.
#
# Usage:  scripts/tests/pre-push-ref-classes.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

hook="$root/.githooks/pre-push"
zero="0000000000000000000000000000000000000000"
head_sha="$(git rev-parse HEAD)"

failures=0

# Run the hook with $1 on stdin and its gate skipped, capturing whatever it says.
#
# RASK_SKIP_UNIT is the ONLY lever that matters now, and forgetting it is not a slow test — it is a
# fork bomb. The hook's gate is scripts/run-unit-local.sh, which runs every scripts/tests/*.test.sh,
# which includes THIS FILE, which drives the hook again. Observed: ten minutes of exponentially
# multiplying bash processes before it was killed by hand.
#
# The hook also refuses to re-enter itself (RASK_PRE_PUSH_ACTIVE), so this is now belt and braces —
# but the skip is what keeps the test fast and honest about only pinning classification.
run_hook() {
  printf '%s' "$1" | \
    RASK_SKIP_UNIT=1 RASK_PRE_PUSH_ACTIVE= \
    "$hook" origin https://github.com/pal-tamas/rask.git 2>&1 || true
}

expect_says() {
  local label="$1" stdin="$2" needle="$3"
  local out
  out="$(run_hook "$stdin")"
  if printf '%s' "$out" | grep -qF "$needle"; then
    printf '  ok   %s\n' "$label"
  else
    printf '  FAIL %s\n       expected to see: %s\n       got: %s\n' "$label" "$needle" "$out"
    failures=$((failures + 1))
  fi
}

expect_silent_on() {
  local label="$1" stdin="$2" needle="$3"
  local out
  out="$(run_hook "$stdin")"
  if printf '%s' "$out" | grep -qF "$needle"; then
    printf '  FAIL %s\n       must NOT say: %s\n       got: %s\n' "$label" "$needle" "$out"
    failures=$((failures + 1))
  else
    printf '  ok   %s\n' "$label"
  fi
}

echo "pre-push ref classification:"

# 1. No ref lines. The regression this file exists for.
expect_says "no refs says so, and not 'DELETION'" "" "git is sending no refs"
expect_silent_on "no refs does not claim a deletion" "" "every ref in this push is a DELETION"

# The message has to be honest about what has NOT happened, because the natural next step after a
# rejection (fetch + a clean merge) is itself ungated.
expect_says "no refs warns that nothing was gated" "" "NOTHING has been gated"

# 2. A real deletion still reads as a deletion.
deletion="refs/heads/gone $zero refs/heads/gone $head_sha
"
expect_says "a deletion still says DELETION" "$deletion" "every ref in this push is a DELETION"
expect_silent_on "a deletion does not claim empty refs" "$deletion" "git is sending no refs"

# 3. Real content skips neither branch — it must reach the gates. Both levers are asserted, because
#    the bug was one classification swallowing the other.
content="refs/heads/main $head_sha refs/heads/main $zero
"
expect_silent_on "content is not called a deletion" "$content" "every ref in this push is a DELETION"
expect_silent_on "content is not called empty" "$content" "git is sending no refs"

if [ "$failures" -ne 0 ]; then
  printf '\npre-push-ref-classes: %d assertion(s) failed\n' "$failures" >&2
  exit 1
fi

echo "pre-push-ref-classes: all assertions passed"

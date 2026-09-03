#!/usr/bin/env bash
# Table test for run-e2e-local.sh's rask_e2e_await_slots — the one point in the browser gate that
# decides whether a push proceeds, waits, or stops.
#
# scripts/tests/machine-lane.test.sh covers the arithmetic underneath this. What it cannot cover is the
# WIRING: which branch a given machine state and set of overrides actually reaches, and whether the two
# output lines scripts/lib/build-failure.sh greps for still come out spelled the way it expects. That
# last one is worth a test on its own, because getting it wrong is invisible — nothing fails, a
# contended run is simply relabelled as a genuine failure and someone spends an afternoon on a red that
# was never in their branch.
#
# The function is LIFTED out of the gate rather than sourced, because sourcing run-e2e-local.sh would
# run it. The lift is checked below, so a rename cannot leave this file passing vacuously.
#
# Usage:  scripts/tests/e2e-await-slots.test.sh   (run by scripts/run-unit-local.sh)
set -uo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"
# shellcheck source=../lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"

gate="$root/scripts/run-e2e-local.sh"
lifted="$(sed -n '/^rask_e2e_await_slots() {/,/^}/p' "$gate")"
lifted_seniors="$(sed -n '/^rask_e2e_name_seniors() {/,/^}/p' "$gate")"

if [ -z "$lifted" ] || [ -z "$lifted_seniors" ]; then
  echo "e2e-await-slots: could not lift the wait functions out of $gate — were they renamed?" >&2
  echo "                 This test would otherwise pass without testing anything." >&2
  exit 1
fi
eval "$lifted"
eval "$lifted_seniors"

# Gate DETECTION is stubbed throughout, and it has to be: this box really does run several gates at
# once, and an exclusive claim equals the whole budget, so it is admissible only when there are no
# seniors at all. That is the design rather than something a test can wish away.
#
# Note RASK_LANE_PGREP_OVERRIDE="" does NOT mean "no gates" — the library tests it with -n, so an empty
# value falls through to a real pgrep, deliberately (an unset override must never blind a gate). With
# the command stub on, pgrep may still return real pids and none of them resolves to a gate, which is
# what gives a genuinely empty machine.
#
# The CLAIM side is real: rask_lane_claim starts an actual scripts/lib/lane-claim.sh child here.
export RASK_E2E_COMMAND_STUB=1
export RASK_LANE_SLOTS=10
eval "export RASK_E2E_ETIME_$$=00:01"

failures=0
checked=0

assert_eq() {
  name="$1"
  actual="$2"
  expected="$3"
  checked=$((checked + 1))
  if [ "$actual" = "$expected" ]; then
    printf '  ok   %-58s -> [%s]\n' "$name" "$actual"
  else
    printf '  FAIL %-58s -> [%s] (expected [%s])\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

assert_says() {
  name="$1"
  haystack="$2"
  needle="$3"
  checked=$((checked + 1))
  if printf '%s' "$haystack" | grep -q -- "$needle"; then
    printf '  ok   %s\n' "$name"
  else
    printf '  FAIL %s (no match for: %s)\n' "$name" "$needle" >&2
    failures=$((failures + 1))
  fi
}

echo "==> rask_e2e_await_slots"

rask_e2e_await_slots >/dev/null 2>&1
assert_eq "an empty machine admits immediately" "$?" "0"
rask_lane_release

# Under CI nothing must wait and nothing must be published — this gate has never run there, and a
# scheduling mechanism is not allowed to be the thing that breaks a future automated path.
CI=1 rask_e2e_await_slots >/dev/null 2>&1
assert_eq "CI returns without waiting" "$?" "0"
assert_eq "CI publishes no claim" "${RASK_LANE_MARKER_PID:-none}" "none"

# One senior holding the entire budget: an exclusive claim cannot fit beside it.
export RASK_E2E_CMD_31000="bash /repo/scripts/run-e2e-local.sh"
export RASK_E2E_ETIME_31000="59:00"
export RASK_LANE_CHILDREN_31000="31001"
export RASK_E2E_CMD_31001="bash /repo/scripts/lib/lane-claim.sh 31000 10 test"

RASK_LANE_COMMAND_STUB=1 RASK_LANE_DISABLE=1 RASK_LANE_PGREP_OVERRIDE="31000" \
  rask_e2e_await_slots >/dev/null 2>&1
assert_eq "RASK_LANE_DISABLE admits even when held" "$?" "0"

out="$(RASK_LANE_COMMAND_STUB=1 RASK_LANE_PGREP_OVERRIDE="31000" RASK_E2E_QUEUE=0 \
        rask_e2e_await_slots 2>&1)"
rc=$?
rask_lane_release
assert_eq "RASK_E2E_QUEUE=0 refuses rather than waiting" "$rc" "1"
# Anchored at column 0 and word-for-word: rask_build_failure_kind greps for exactly this.
assert_says "the refusal prints the anchored line" "$out" '^run-e2e-local: refused to start'
# A refusal that names nobody is the unexplained kind this gate has always avoided being.
assert_says "the refusal names the senior and its slots" "$out" 'pid 31000, 10 slot'

out="$(RASK_LANE_COMMAND_STUB=1 RASK_LANE_PGREP_OVERRIDE="31000" RASK_E2E_ALLOW_CONCURRENT=1 \
        rask_e2e_await_slots 2>&1)"
rc=$?
rask_lane_release
assert_eq "RASK_E2E_ALLOW_CONCURRENT proceeds" "$rc" "0"
assert_says "and says the result is suspect" "$out" 'starting alongside it anyway'

echo "==> the classifier contract"

# The whole point of the anchored lines. If either is reworded, a contended run stops being reported as
# 'busy' and starts reading as a real failure, whose advice — look for a failing assertion, a timeout,
# a host that exited early — is actively wrong about a suite that never started.
log="$(mktemp -t e2e-await-slots.XXXXXX)"
printf '%s\n' "run-e2e-local: refused to start — RASK_E2E_QUEUE=0 and the lane is held." > "$log"
assert_eq "a refusal classifies as busy" "$(rask_build_failure_kind "$log")" "busy"
printf '%s\n' "run-e2e-local: still queued after 90m — giving up rather than waiting silently." > "$log"
assert_eq "a queue timeout classifies as busy" "$(rask_build_failure_kind "$log")" "busy"
rm -f "$log"

echo
if [ "$failures" -ne 0 ]; then
  echo "e2e-await-slots: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "e2e-await-slots: $checked checks passed."

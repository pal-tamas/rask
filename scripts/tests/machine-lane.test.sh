#!/usr/bin/env bash
# Table test for scripts/lib/machine-lane.sh.
#
# This decides how large every gate on the machine is allowed to be, so both directions cost real
# money and neither is self-announcing. Handing out too much reproduces the bug the library exists to
# fix -- 35 MSBuild worker nodes on 14 cores, and a suite whose reds cannot be trusted. Handing out
# too little makes every gate on a quiet box crawl for no reason, which is how a gate ends up
# permanently RASK_SKIP'd. Neither shows up as a failure; both show up as "the machine feels wrong".
# So every row is stated here rather than left to whatever someone happened to try, the same way
# e2e-concurrency.test.sh and build-failure-kind.test.sh state theirs.
#
# Usage:  scripts/tests/machine-lane.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"

# Both stubs on: no real `ps` or `pgrep` is consulted anywhere below, so the table is the whole world
# and the test cannot be perturbed by whatever else this machine happens to be running -- which
# matters unusually much here, because the thing under test is a reading of that machine. Nothing in
# this file spawns a process.
export RASK_E2E_COMMAND_STUB=1
export RASK_LANE_COMMAND_STUB=1
export RASK_LANE_SLOTS=10

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

echo "==> rask_lane_script_of (argv position, never substring)"

assert_eq "the documented invocation, relative" \
  "$(rask_lane_script_of 'bash scripts/run-e2e-local.sh')" "run-e2e-local.sh"
assert_eq "absolute, as .githooks/pre-push execs it" \
  "$(rask_lane_script_of '/bin/bash /repo/scripts/run-unit-local.sh')" "run-unit-local.sh"
assert_eq "the unit gate after its re-exec" \
  "$(rask_lane_script_of '/bin/bash /repo/scripts/run-unit-local.sh --lane-slots 4')" "run-unit-local.sh"
assert_eq "under bash -x, plausible while debugging a gate" \
  "$(rask_lane_script_of 'bash -x scripts/run-e2e-local.sh')" "run-e2e-local.sh"
assert_eq "a claim marker" \
  "$(rask_lane_script_of 'bash /repo/scripts/lib/lane-claim.sh 400 10 test')" "lane-claim.sh"
# The false positives that made the sibling file exist. An editor and a log are not gates, and
# counting them would shrink every real gate for as long as the editor stayed open.
assert_eq "an editor holding the file open" \
  "$(rask_lane_script_of 'vim scripts/run-e2e-local.sh')" ""
assert_eq "a tee of the gate's own log" \
  "$(rask_lane_script_of 'tee /var/folders/T/rask-pre-push-9562/run-e2e-local.log')" ""
assert_eq "a wrapper shell whose -c text merely names it" \
  "$(rask_lane_script_of "/bin/zsh -c source /home/u/snap.sh && bash /tmp/x/run-e2e-local.sh")" ""
assert_eq "an unrelated process" \
  "$(rask_lane_script_of 'dotnet build Rask.slnx -c Release')" ""

echo "==> rask_lane_declared_cost_of (a claim is read from the marker's argv)"

RASK_LANE_CHILDREN_400="401"
RASK_E2E_CMD_401="bash /repo/scripts/lib/lane-claim.sh 400 10 test"
assert_eq "an exclusive claim" "$(rask_lane_declared_cost_of 400)" "10"

RASK_LANE_CHILDREN_402="403"
RASK_E2E_CMD_403="/bin/bash /r/.claude/worktrees/a/scripts/lib/lane-claim.sh 402 4 build"
assert_eq "a partial claim from another worktree" "$(rask_lane_declared_cost_of 402)" "4"

# A marker naming somebody else must never be credited here, or one gate's claim would be counted
# twice and a second gate would be shrunk for a claim it does not hold.
RASK_LANE_CHILDREN_404="405"
RASK_E2E_CMD_405="bash /repo/scripts/lib/lane-claim.sh 999 10 test"
assert_eq "a marker owned by a different gate is ignored" "$(rask_lane_declared_cost_of 404)" ""

RASK_LANE_CHILDREN_406="407"
RASK_E2E_CMD_407="bash /repo/scripts/lib/lane-claim.sh 406 four test"
assert_eq "a non-numeric cost is ignored" "$(rask_lane_declared_cost_of 406)" ""

# The phase that used to hold the whole lane for nothing: nine and a half minutes of build and
# publish before the suite starts at all. No marker, so the gate is on its phase default.
RASK_LANE_CHILDREN_410="411"
RASK_E2E_CMD_411="dotnet publish samples/Rask.Example.Playground -c Release -nodeReuse:false"
assert_eq "a building gate declares nothing" "$(rask_lane_declared_cost_of 410)" ""

assert_eq "a gate with no children declares nothing" "$(rask_lane_declared_cost_of 430)" ""

echo "==> rask_lane_cost_of"

RASK_E2E_CMD_400="bash /repo/scripts/run-e2e-local.sh"
assert_eq "a declared claim wins over the phase default" "$(rask_lane_cost_of 400)" "10"

RASK_E2E_CMD_410="bash /repo/scripts/run-e2e-local.sh"
assert_eq "a building gate claims a share, not the box" "$(rask_lane_cost_of 410)" "4"

RASK_E2E_CMD_440="bash /repo/scripts/run-unit-local.sh --lane-slots 3"
assert_eq "a unit gate reports the size it chose" "$(rask_lane_cost_of 440)" "3"

# The few milliseconds between launch and the re-exec. Counted high, because a gate about to get
# large is the direction that over-subscribes.
RASK_E2E_CMD_450="bash /repo/scripts/run-unit-local.sh"
assert_eq "a unit gate before its re-exec counts as its max" "$(rask_lane_cost_of 450)" "8"

RASK_E2E_CMD_460="bash /repo/scripts/run-unit-local.sh --lane-slots banana"
assert_eq "a malformed slot count falls back to the max" "$(rask_lane_cost_of 460)" "8"

RASK_E2E_CMD_470="dotnet build Rask.slnx"
assert_eq "a non-gate claims nothing" "$(rask_lane_cost_of 470)" "0"

echo "==> rask_lane_claimed_by_seniors (only OLDER gates count -- the no-deadlock rule)"

# self = pid 100, aged 60s.
RASK_E2E_ETIME_100="01:00"

RASK_E2E_CMD_500="bash /repo/scripts/run-unit-local.sh --lane-slots 4"
RASK_E2E_ETIME_500="05:00"
assert_eq "one older unit gate" \
  "$(RASK_LANE_PGREP_OVERRIDE='500' rask_lane_claimed_by_seniors 100 999)" "4"

# A younger gate is invisible to us and we are visible to it. That asymmetry is the whole reason two
# waiters cannot hang against each other.
RASK_E2E_CMD_510="bash /repo/scripts/run-unit-local.sh --lane-slots 4"
RASK_E2E_ETIME_510="00:10"
assert_eq "a younger gate is not counted" \
  "$(RASK_LANE_PGREP_OVERRIDE='510' rask_lane_claimed_by_seniors 100 999)" "0"

assert_eq "older and younger together, only the older counts" \
  "$(RASK_LANE_PGREP_OVERRIDE='500 510' rask_lane_claimed_by_seniors 100 999)" "4"

# Same whole second -- plausible when a merge train fires several pushes at once. Broken on pid so
# both sides compute the same order and neither concludes it outranks the other.
RASK_E2E_CMD_50="bash /repo/scripts/run-unit-local.sh --lane-slots 5"
RASK_E2E_ETIME_50="01:00"
assert_eq "an equal-aged gate with a lower pid outranks us" \
  "$(RASK_LANE_PGREP_OVERRIDE='50' rask_lane_claimed_by_seniors 100 999)" "5"

RASK_E2E_CMD_150="bash /repo/scripts/run-unit-local.sh --lane-slots 5"
RASK_E2E_ETIME_150="01:00"
assert_eq "an equal-aged gate with a higher pid does not" \
  "$(RASK_LANE_PGREP_OVERRIDE='150' rask_lane_claimed_by_seniors 100 999)" "0"

assert_eq "self and parent are never counted against us" \
  "$(RASK_LANE_PGREP_OVERRIDE='100 999' rask_lane_claimed_by_seniors 100 999)" "0"

# A pid that has already exited: ps prints nothing, so it is not a gate and claims nothing. This is
# exactly the case a lockfile gets wrong, wedging the machine until someone works out what to delete.
assert_eq "a pid that has already exited claims nothing" \
  "$(RASK_LANE_PGREP_OVERRIDE='777' rask_lane_claimed_by_seniors 100 999)" "0"

# A senior that is QUEUED for the browser suite has published its claim but has not started
# `dotnet test` yet. It must already count for the full amount, or a junior slips in and starts work
# the senior is about to need the whole machine for.
RASK_E2E_CMD_520="bash /repo/scripts/run-e2e-local.sh"
RASK_E2E_ETIME_520="09:00"
RASK_LANE_CHILDREN_520="521"
RASK_E2E_CMD_521="bash /repo/scripts/lib/lane-claim.sh 520 10 test"
assert_eq "a senior queued for the browser suite counts in full" \
  "$(RASK_LANE_PGREP_OVERRIDE='520' rask_lane_claimed_by_seniors 100 999)" "10"

echo "==> rask_lane_headroom"

assert_eq "an empty machine offers the whole budget" \
  "$(RASK_LANE_PGREP_OVERRIDE='' rask_lane_headroom 100 999)" "10"
assert_eq "one older build-phase gate leaves six" \
  "$(RASK_LANE_PGREP_OVERRIDE='500' rask_lane_headroom 100 999)" "6"

# Over-commitment is reachable: an E2E gate declares the whole budget on top of a unit gate that is
# already running. Clamped at zero, because a negative would propagate into `-m:-3`.
assert_eq "an over-committed box floors at zero, never negative" \
  "$(RASK_LANE_PGREP_OVERRIDE='500 520' rask_lane_headroom 100 999)" "0"

echo "==> rask_lane_fit (answers now; never waits, never returns zero)"

assert_eq "a quiet machine, clamped to the caller's max" \
  "$(RASK_LANE_PGREP_OVERRIDE='' rask_lane_fit 2 8 100 999)" "8"
assert_eq "a busy machine hands back what is left" \
  "$(RASK_LANE_PGREP_OVERRIDE='500' rask_lane_fit 2 8 100 999)" "6"

# The row this file exists for. -m:0 means "use every core", so a zero here would make the BUSIEST
# machine produce the LEAST bounded run -- silently, and in exactly the circumstances where that does
# the most damage.
assert_eq "a saturated machine returns the floor, never zero" \
  "$(RASK_LANE_PGREP_OVERRIDE='500 520' rask_lane_fit 2 8 100 999)" "2"
assert_eq "the floor is honoured even when asked for one slot" \
  "$(RASK_LANE_PGREP_OVERRIDE='500 520' rask_lane_fit 1 8 100 999)" "1"

# The global off switch. A new machine-wide gate needs one, and it is the first thing anyone will
# reach for when it misbehaves.
assert_eq "RASK_LANE_DISABLE hands back the caller's max" \
  "$(RASK_LANE_DISABLE=1 RASK_LANE_PGREP_OVERRIDE='500 520' rask_lane_fit 2 8 100 999)" "8"

echo "==> rask_lane_fits (the blocking caller's predicate)"

if RASK_LANE_PGREP_OVERRIDE='' rask_lane_fits 10 100 999; then r=yes; else r=no; fi
assert_eq "an exclusive claim fits on an empty machine" "$r" "yes"

if RASK_LANE_PGREP_OVERRIDE='500' rask_lane_fits 10 100 999; then r=yes; else r=no; fi
assert_eq "an exclusive claim does not fit beside a senior" "$r" "no"

if RASK_LANE_PGREP_OVERRIDE='500' rask_lane_fits 6 100 999; then r=yes; else r=no; fi
assert_eq "exactly the remaining headroom fits" "$r" "yes"

if RASK_LANE_PGREP_OVERRIDE='500' rask_lane_fits 7 100 999; then r=yes; else r=no; fi
assert_eq "one slot more than the headroom does not" "$r" "no"

# Only juniors are present, so an exclusive claim is admissible: this is the FIFO guarantee, and the
# reason a full-budget claim can ever be granted at all on a machine that is never idle.
if RASK_LANE_PGREP_OVERRIDE='510' rask_lane_fits 10 100 999; then r=yes; else r=no; fi
assert_eq "juniors never block an exclusive claim" "$r" "yes"

# A cost above the budget is clamped, not refused. Without this, lowering RASK_LANE_SLOTS while some
# caller still asks for 10 would block every browser suite on the machine forever, saying nothing.
if RASK_LANE_SLOTS=6 RASK_LANE_PGREP_OVERRIDE='' rask_lane_fits 10 100 999; then r=yes; else r=no; fi
assert_eq "a cost above the budget is clamped, not wedged" "$r" "yes"

if RASK_LANE_DISABLE=1 RASK_LANE_PGREP_OVERRIDE='500 520' rask_lane_fits 10 100 999; then r=yes; else r=no; fi
assert_eq "RASK_LANE_DISABLE admits everything" "$r" "yes"

echo "==> rask_lane_senior_gates (the FIFO release order)"

# These rows moved here from e2e-concurrency.test.sh with rask_e2e_lane_holders, which they used to
# cover. The rule is unchanged and so are the cases; only the implementation they exercise is new.
#
# The failure this ordering exists to prevent: a WAITING gate is itself a gate process, so it is
# indistinguishable from the run holding the machine. Two waiters that each "wait until no other gate
# exists" deadlock; two that each proceed when the holder exits start simultaneously, which is the
# contention the whole mechanism was written to stop. Waiting only for OLDER gates orders them
# without shared state, so exactly one is released at a time.
seniors() {
  name="$1"
  expected="$2"
  self="$3"
  actual="$(rask_lane_senior_gates "$self" 0 | awk '{print $1}' | tr '\n' ' ' | sed 's/ *$//')"
  checked=$((checked + 1))
  if [ "$actual" = "$expected" ]; then
    printf '  ok   %-58s -> [%s]\n' "$name" "$actual"
  else
    printf '  FAIL %-58s -> [%s] (expected [%s])\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

# A holder running 5m, and us just started: we wait for it.
export RASK_LANE_PGREP_OVERRIDE="400"
export RASK_E2E_CMD_400="bash /repo/scripts/run-e2e-local.sh"
export RASK_E2E_ETIME_400="05:00"
export RASK_E2E_ETIME_500="00:02"
seniors "a younger gate waits for the holder" "400" 500

# The holder exits; we are alone. Our turn.
export RASK_LANE_PGREP_OVERRIDE=""
seniors "nothing older means it is our turn" "" 500

# Two waiters behind one holder. The OLDER waiter (500, 2m) waits only for the holder; the YOUNGER
# waiter (600, 10s) waits for both. So when the holder exits exactly one is released.
export RASK_LANE_PGREP_OVERRIDE="400 500 600"
export RASK_E2E_CMD_500="bash /repo/scripts/run-e2e-local.sh"
export RASK_E2E_CMD_600="bash /repo/scripts/run-e2e-local.sh"
export RASK_E2E_ETIME_500="02:00"
export RASK_E2E_ETIME_600="00:10"
seniors "senior waiter waits only for the holder" "400" 500
seniors "junior waiter waits for holder and senior" "400 500" 600

# Holder gone: the senior is released, the junior still waits for the senior. This is the row that
# would fail if the ordering were "wait until no other gate exists" (both hang) or "proceed when the
# holder exits" (both start at once).
export RASK_LANE_PGREP_OVERRIDE="500 600"
seniors "senior is released when the holder exits" "" 500
seniors "junior still waits for the senior" "500" 600

# A tie on the whole second is plausible when a merge train fires several pushes at once. It breaks on
# pid so the order is TOTAL — without a tiebreak each would see the other as not-older and both start.
export RASK_LANE_PGREP_OVERRIDE="700"
export RASK_E2E_CMD_700="bash /repo/scripts/run-e2e-local.sh"
export RASK_E2E_ETIME_700="01:00"
export RASK_E2E_ETIME_800="01:00"
seniors "equal age: lower pid wins" "700" 800
export RASK_E2E_ETIME_650="01:00"
seniors "equal age: higher pid does not block us" "" 650

# A process that merely mentions a gate is not one, so the ordering inherits the argv-position rule
# rather than re-deriving it — an editor open on the script must not stall a push for 90 minutes.
export RASK_LANE_PGREP_OVERRIDE="900"
export RASK_E2E_CMD_900="vim /repo/scripts/run-e2e-local.sh"
export RASK_E2E_ETIME_900="30:00"
seniors "an editor is not a senior" "" 500

# Mixed kinds: the budget is machine-wide, so a unit gate and a browser gate queue in ONE order. This
# is the case the old per-script lane could not express at all, and the reason three unit gates could
# pile onto a live browser suite without anything noticing.
export RASK_LANE_PGREP_OVERRIDE="400 910"
export RASK_E2E_CMD_910="bash /repo/scripts/run-unit-local.sh --lane-slots 4"
export RASK_E2E_ETIME_910="03:00"
seniors "unit and browser gates share one order" "400 910" 500

unset RASK_LANE_PGREP_OVERRIDE

echo
if [ "$failures" -ne 0 ]; then
  echo "machine-lane.test.sh: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "machine-lane.test.sh: $checked checks passed."

#!/usr/bin/env bash
# Table test for rask_other_e2e_runs (scripts/lib/e2e-concurrency.sh).
#
# This predicate decides whether a push is allowed to proceed, so both directions cost something real:
# a miss lets two browser suites contend and produces a red that looks like a bug and is not, while a
# false positive blocks a push for a process that is not a gate at all. The second is not hypothetical —
# the first live run of this check reported three conflicts where there was one, having matched a shell
# whose argv merely mentioned the script and its own test harness. Every row below is stated rather than
# left to the cases someone happened to try, same as build-failure-kind.test.sh.
#
# Usage:  scripts/tests/e2e-concurrency.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/e2e-concurrency.sh
. "$root/scripts/lib/e2e-concurrency.sh"

export RASK_E2E_COMMAND_STUB=1

failures=0
checked=0

# assert <name> <expected-pids> — candidates and their commands come from RASK_E2E_* set by the caller.
assert() {
  name="$1"
  expected="$2"

  actual="$(rask_other_e2e_runs 100 200 | tr '\n' ' ' | sed 's/ *$//')"
  checked=$((checked + 1))

  if [ "$actual" = "$expected" ]; then
    printf '  ok   %-56s -> [%s]\n' "$name" "$actual"
  else
    printf '  FAIL %-56s -> [%s] (expected [%s])\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

echo "==> rask_other_e2e_runs"

# A real gate run: bash executing <repo>/scripts/run-e2e-local.sh.
RASK_E2E_PGREP_OVERRIDE="300" \
  RASK_E2E_CMD_300="bash /Users/x/RiderProjects/Rask/.claude/worktrees/a/scripts/run-e2e-local.sh" \
  assert "a real gate run in another worktree" "300"

# Our own pid, and the shell that launched us, are never a conflict with ourselves.
RASK_E2E_PGREP_OVERRIDE="100 200" \
  RASK_E2E_CMD_100="bash /repo/scripts/run-e2e-local.sh" \
  RASK_E2E_CMD_200="bash /repo/scripts/run-e2e-local.sh" \
  assert "self and parent are excluded" ""

# The false positive that made this file exist: a wrapper shell whose command line contains the path
# without executing it. pgrep -f matches it; the scripts/ anchor must not.
RASK_E2E_PGREP_OVERRIDE="301" \
  RASK_E2E_CMD_301="/bin/zsh -c source /home/u/.claude/snapshot.sh && bash /tmp/fake/run-e2e-local.sh" \
  assert "a wrapper shell mentioning a non-scripts path" ""

# tee of the gate's log carries the name too, and is not a second gate.
RASK_E2E_PGREP_OVERRIDE="302" \
  RASK_E2E_CMD_302="tee /var/folders/T/rask-pre-push-9562/run-e2e-local.log" \
  assert "the tee of a gate's log is not a gate" ""

# A dead pid: ps prints nothing. Process detection is self-healing precisely here — this is the case a
# lockfile would get wrong, wedging the gate until someone deleted the file by hand.
RASK_E2E_PGREP_OVERRIDE="303" \
  assert "a pid that has already exited" ""

# Several at once, mixed with noise: report every genuine one, because "how long has it been going"
# is the thing that decides whether to wait, and that needs all of them.
RASK_E2E_PGREP_OVERRIDE="304 305 306" \
  RASK_E2E_CMD_304="bash /repo/a/scripts/run-e2e-local.sh" \
  RASK_E2E_CMD_305="grep -rn run-e2e-local.sh /repo" \
  RASK_E2E_CMD_306="bash /repo/b/scripts/run-e2e-local.sh" \
  assert "two real runs alongside an unrelated grep" "304 306"

# --- invocation forms -------------------------------------------------------------------------
#
# The predicate decides on argv POSITION, not substring, and these are the rows that pin it. An earlier
# version anchored on the '/scripts/' path segment: it got the editor and the tee right and silently
# missed `bash scripts/run-e2e-local.sh`, which is the form CLAUDE.md and CONTRIBUTING.md document for
# running the gate by hand. A guard blind to the manual runs that prompted it is worse than none,
# because it looks correct.
echo
echo "==> rask_is_e2e_gate_command"

form() {
  expected="$1"
  cmd="$2"
  checked=$((checked + 1))
  if rask_is_e2e_gate_command "$cmd"; then actual="gate"; else actual="not"; fi
  if [ "$actual" = "$expected" ]; then
    printf '  ok   %-52s -> %s\n' "$cmd" "$actual"
  else
    printf '  FAIL %-52s -> %s (expected %s)\n' "$cmd" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

form gate "bash scripts/run-e2e-local.sh"
form gate "scripts/run-e2e-local.sh"
form gate "bash ./scripts/run-e2e-local.sh"
form gate "/bin/bash /repo/.claude/worktrees/a/scripts/run-e2e-local.sh"
form gate "run-e2e-local.sh"

# Shell options are walked past, not treated as the end of the road. Running the gate under -x is a
# plausible thing to do while debugging the gate, and an earlier version bailed on the first option and
# made every one of these invisible.
form gate "bash -x scripts/run-e2e-local.sh"
form gate "bash -eu ./scripts/run-e2e-local.sh"

form not  "vim scripts/run-e2e-local.sh"
form not  "grep -rn run-e2e-local.sh /repo"
form not  "tee /var/folders/T/rask-pre-push-9562/run-e2e-local.log"
# -c stays excluded, including combined forms: the argument is script text, and the child it spawns is
# matched on its own pid. This is the distinction the option-walk above must not erase.
form not  "/bin/zsh -c source /home/u/.claude/snap.sh && bash /tmp/fake/run-e2e-local.sh"
form not  "sh -lc bash /tmp/fake/run-e2e-local.sh"
form not  "bash -x -c bash /tmp/fake/run-e2e-local.sh"
form not  "less /repo/scripts/run-e2e-local.sh"
form not  ""

echo
echo "==> rask_other_heavy_builds"

# The #850 half: the competitor that is NOT a browser gate. This one only ever adds a line to a failure
# that already happened, so the cost of being wrong is asymmetric — a miss loses a hint, a false
# positive prints a sentence. It still has to tell a real build from `dotnet --version`, or the hint
# fires on every red run and stops being read.
heavy() {
  name="$1"
  expected="$2"
  shift 2

  actual="$(rask_other_heavy_builds 100 | tr '\n' ' ' | sed 's/ *$//')"
  checked=$((checked + 1))

  if [ "$actual" = "$expected" ]; then
    printf '  ok   %-56s -> [%s]\n' "$name" "$actual"
  else
    printf '  FAIL %-56s -> [%s] (expected [%s])\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

export RASK_HEAVY_PGREP_OVERRIDE="301"
RASK_E2E_CMD_301="dotnet build Rask.slnx -c Release" heavy "a solution build" "301"

RASK_E2E_CMD_301="dotnet test Rask.slnx --no-build" heavy "a test run" "301"
RASK_E2E_CMD_301="dotnet publish samples/Rask.Example.Wasm -c Release" heavy "a publish" "301"
RASK_E2E_CMD_301="dotnet msbuild probe.csproj -getItem:Watch" heavy "an msbuild evaluation" "301"
RASK_E2E_CMD_301="dotnet pack src/Rask.Core" heavy "a pack" "301"

# The cheap verbs. A dev server, a migration and a version query are not contention worth naming, and
# reporting them would train people to ignore the line that matters.
RASK_E2E_CMD_301="dotnet run --project samples/Rask.Example.Server" heavy "a dev server is not contention" ""
RASK_E2E_CMD_301="dotnet ef migrations add Init" heavy "a migration is not contention" ""
RASK_E2E_CMD_301="dotnet --version" heavy "a version query is not contention" ""
RASK_E2E_CMD_301="dotnet tool install -g dotnet-ef" heavy "a tool install is not contention" ""

# One build is ONE competitor. MSBuild spawns a worker node per core, and naming each would report a
# single `dotnet build` as eight machines fighting for the box.
RASK_E2E_CMD_301="dotnet /usr/share/dotnet/sdk/10.0.100/MSBuild.dll -nodemode:1 -nologo" \
  heavy "an MSBuild worker node is not a separate build" ""

# Self is excluded, or the gate reports itself as its own competitor on every failure.
export RASK_HEAVY_PGREP_OVERRIDE="100"
RASK_E2E_CMD_100="dotnet test Rask.slnx" heavy "self is excluded" ""

export RASK_HEAVY_PGREP_OVERRIDE="302"
RASK_E2E_CMD_302="" heavy "a pid that has already exited" ""

unset RASK_HEAVY_PGREP_OVERRIDE

echo
if [ "$failures" -ne 0 ]; then
  echo "e2e-concurrency: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "e2e-concurrency: $checked checks passed."

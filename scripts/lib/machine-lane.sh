#!/usr/bin/env bash
# How much of this machine may a gate take right now?
#
# The problem this solves is NOT the one scripts/lib/e2e-concurrency.sh solves. That file answers
# "is another browser gate running?" and serialises the browser suite against itself. It is right
# about that, and it stays. What it cannot see is the commoner and more expensive collision (#850):
# several worktrees on one box each running the FULL unit gate, none of them bounded. Measured here
# with three of them live: 35 MSBuild worker nodes on 14 cores, load average 98, 0.0% idle, 41 of
# 48 GB resident. Nothing refused, nothing warned, and every timing-sensitive test in all three runs
# became untrustworthy -- this repo's rule is that a green under contention is trustworthy and a red
# is not, so each of those runs cost a re-run at minimum.
#
# So the unit is no longer "the lane" (one holder, everyone else waits) but SLOTS. The box publishes
# a budget, each gate declares what it is taking, and a gate asks one of two questions:
#
#   rask_lane_fit  -- "how big may I be?"   Answers immediately. NEVER blocks. Used by the unit gate,
#                     which runs from .githooks/pre-commit -- and that hook decided deliberately (see
#                     its comment) that a blocked commit costs more than a slow one. Shrinking is how
#                     a gate honours a busy machine without ever making someone wait for it.
#
#   rask_lane_fits -- "does this much fit?"  The predicate a blocking caller polls. Used only where
#                     the work genuinely cannot share the box: the browser suite, whose journeys and
#                     WebSocket timing tests fail under load in ways indistinguishable from real bugs.
#
# Both are computed from the same headroom function, over the ordering rule this file inherited from
# e2e-concurrency.sh's rask_e2e_lane_holders and then replaced (that function is gone; two copies of
# an ordering rule that must agree is a bug waiting for the day they stop agreeing).
#
# THE RULE: you count only gates OLDER than yourself. It is what makes waiting safe rather than
# deadlocking, and the argument is worth keeping in full because it is not obvious and it is the
# thing most likely to be "simplified" away later.
#
# A gate that is waiting is itself a gate process, so detection alone cannot tell a waiter from the
# run that holds the machine, and two waiters therefore see each other. If both wait until nobody
# else is present, they hang. If both instead start the moment the holder exits, they start
# simultaneously -- which is exactly the contention the whole mechanism exists to prevent. A naive
# queue converts one honest refusal into either a deadlock or the very bug it replaced.
#
# Resolved by AGE, which totally orders the contenders with no shared state: you wait only for gates
# older than you. The holder is older than every waiter, so everyone waits for it; among waiters each
# waits for all of its seniors, so when the holder exits exactly one waiter -- the oldest -- is
# released, and the others keep waiting because that one is still older than they are. FIFO, and fair.
# A gate's age only grows and a new gate starts at zero, so nobody can ever be INSERTED ahead of you:
# the set you are waiting for only shrinks, which is why nothing starves.
#
# Ties (the same whole second, plausible when a merge train fires several pushes at once) break on
# numeric pid, purely to make the order total. Any deterministic tiebreak would do; what matters is
# that both sides compute the same one, so two processes never each conclude they outrank the other.
#
# Everything here is decided BY PROCESS, never a lockfile -- see scripts/lib/lane-claim.sh for why a
# claim is itself a process, and why inferring a gate's phase from its own children is not enough.

# shellcheck source=e2e-concurrency.sh
. "${BASH_SOURCE%/*}/e2e-concurrency.sh"

# The budget, in slots. One slot is meant to be one core's worth of work.
#
# Defaults to 10 on a 14-core box, and the missing 4 are deliberate rather than rounding. This
# machine is 10 performance + 4 efficiency cores, and handing out the efficiency cores is how a
# timing-sensitive test ends up scheduled somewhere far slower than the run that set its timeout --
# which is a false red, the exact currency this whole file exists to stop spending. The remainder
# also keeps an editor and a browser responsive while a gate runs, so nobody is tempted to skip it.
rask_lane_budget() {
  printf '%s' "${RASK_LANE_SLOTS:-10}"
}

# What a gate in its build/publish phase claims. Not the whole box: builds are throughput work, they
# tolerate sharing, and the entire point of splitting the E2E gate by phase is that several worktrees
# may build at once. Before that split, a gate held the whole machine from its first line -- measured
# here at 9m30s of build and publish before `dotnet test` appeared at all, roughly a quarter of the
# documented ~40m norm, during which seven other worktrees were blocked while the holder used a
# single core (the build is -m:1, and stays that way -- see run-e2e-local.sh).
rask_lane_build_cost() {
  printf '%s' "${RASK_LANE_BUILD_COST:-4}"
}

# The largest a unit gate will ever size itself.
rask_lane_unit_max() {
  printf '%s' "${RASK_LANE_UNIT_MAX:-8}"
}

# Which script is this command line EXECUTING, if any? Prints a basename, or nothing.
#
# Generalises rask_is_e2e_gate_command to the scripts this file cares about, and keeps the thing that
# made that function correct: the decision is made on ARGV POSITION, never a substring. The comment
# there records what substring matching did in practice -- it reported three conflicts where there
# was one, having matched an editor holding the file open and a `tee` of the gate's own log. Neither
# is a gate, and counting them would shrink every real gate on the box for as long as the editor
# stayed open.
#
# The shell-option walk and the -c arm are lifted from that function deliberately rather than
# reimplemented; see its comments for why -c must bail (the argument is script TEXT, and the child
# that actually runs the script is matched on its own pid) and for the known, written-down
# limitations (a repo path containing a space; `bash --rcfile x script`). Both fail as a MISSED
# detection, never a wrong one -- an unseen gate costs some over-subscription, while a phantom gate
# would shrink everyone else for as long as it was believed in.
rask_lane_script_of() {
  [ -n "${1:-}" ] || return 0

  set -f
  # shellcheck disable=SC2086
  set -- $1
  set +f

  first="${1:-}"

  case "${first##*/}" in
    bash | sh | zsh | dash | ksh)
      shift
      while [ $# -gt 0 ]; do
        case "$1" in
          -*c*) return 0 ;;
          -*) shift ;;
          *) break ;;
        esac
      done
      target="${1:-}"
      ;;
    *) target="$first" ;;
  esac

  case "$target" in
    */run-e2e-local.sh | run-e2e-local.sh) printf 'run-e2e-local.sh' ;;
    */run-unit-local.sh | run-unit-local.sh) printf 'run-unit-local.sh' ;;
    */lane-claim.sh | lane-claim.sh) printf 'lane-claim.sh' ;;
  esac
}

# Direct children of a pid. Indirected so the tests can stub it without spawning anything.
rask_lane_children_of() {
  if [ -n "${RASK_LANE_COMMAND_STUB:-}" ]; then
    eval "printf '%s' \"\${RASK_LANE_CHILDREN_$1:-}\""
    return 0
  fi
  pgrep -P "$1" 2>/dev/null || true
}

# The slot count a gate has explicitly claimed via a lane-claim.sh child, if it has one.
# Prints the cost, or nothing when the gate is running on its phase default.
#
# The claim is read from the marker's ARGV rather than from anything the gate itself could be asked,
# because the reader is in another worktree and `ps` is all it has. The owner pid in argv is checked
# against the gate we are asking about, so one gate's marker can never be credited to another.
rask_lane_declared_cost_of() {
  gate_pid="${1:-}"
  [ -n "$gate_pid" ] || return 0

  for child in $(rask_lane_children_of "$gate_pid"); do
    cmd="$(rask_e2e_command_of "$child")"
    [ "$(rask_lane_script_of "$cmd")" = "lane-claim.sh" ] || continue

    set -f
    # shellcheck disable=SC2086
    set -- $cmd
    set +f
    # Walk to the script path, then read <owner> <cost> after it.
    while [ $# -gt 0 ]; do
      case "$(rask_lane_script_of "$1")" in
        lane-claim.sh) break ;;
      esac
      shift
    done
    owner="${2:-}"
    cost="${3:-}"

    [ "$owner" = "$gate_pid" ] || continue
    case "$cost" in
      "" | *[!0-9]*) continue ;;
    esac
    printf '%s' "$cost"
    return 0
  done
  return 0
}

# What is this gate currently taking? Prints a slot count.
#
# Order matters: an explicit claim wins over the phase default, because the claim is what a gate
# publishes BEFORE it waits. A gate queued for the browser suite has not started `dotnet test` yet
# and would otherwise still read as merely building -- so its juniors would under-count it and start
# work it is about to need the whole machine for.
#
# The unit gate needs no marker: it never changes its claim, so it carries the number in its own argv
# (`--lane-slots N`), put there by re-execing itself once it has chosen a size. Argv, not an
# environment variable, precisely so this function can read it back out of `ps`. A unit gate seen
# WITHOUT the flag is in the few milliseconds before that re-exec; it is counted at the maximum it
# could pick, because under-counting a gate that is about to get large over-subscribes the box.
rask_lane_cost_of() {
  pid="${1:-}"
  cmd="$(rask_e2e_command_of "$pid")"
  script="$(rask_lane_script_of "$cmd")"

  declared="$(rask_lane_declared_cost_of "$pid")"
  if [ -n "$declared" ]; then
    printf '%s' "$declared"
    return 0
  fi

  case "$script" in
    run-e2e-local.sh) rask_lane_build_cost ;;
    run-unit-local.sh)
      case "$cmd" in
        *--lane-slots\ *)
          slots="${cmd#*--lane-slots }"
          slots="${slots%% *}"
          case "$slots" in
            "" | *[!0-9]*) rask_lane_unit_max ;;
            *) printf '%s' "$slots" ;;
          esac
          ;;
        *) rask_lane_unit_max ;;
      esac
      ;;
    *) printf '0' ;;
  esac
}

# Every OTHER live gate on this machine, of any kind. Prints pids, one per line.
rask_lane_other_gates() {
  self_pid="${1:-$$}"
  parent_pid="${2:-$PPID}"

  candidates="$(
    if [ -n "${RASK_LANE_PGREP_OVERRIDE:-}" ]; then
      printf '%s\n' $RASK_LANE_PGREP_OVERRIDE
    else
      pgrep -f 'run-(e2e|unit)-local\.sh' 2>/dev/null || true
    fi
  )"

  for pid in $candidates; do
    [ "$pid" = "$self_pid" ] && continue
    [ "$pid" = "$parent_pid" ] && continue
    case "$(rask_lane_script_of "$(rask_e2e_command_of "$pid")")" in
      run-e2e-local.sh | run-unit-local.sh) printf '%s\n' "$pid" ;;
    esac
  done
  return 0
}

# The gates that outrank us, for the message a waiting gate prints. One "pid cost" per line.
#
# Kept separate from the arithmetic below rather than folded into it, because naming WHO is holding
# the machine is the half that actually helps: "there is contention" says there is a problem, while a
# pid and an elapsed time is what lets someone decide whether to wait for it or go and look at it.
rask_lane_senior_gates() {
  self_pid="${1:-$$}"
  parent_pid="${2:-$PPID}"

  self_age="$(rask_etime_seconds "$(rask_e2e_etime_of "$self_pid")")"

  for pid in $(rask_lane_other_gates "$self_pid" "$parent_pid"); do
    age="$(rask_etime_seconds "$(rask_e2e_etime_of "$pid")")"
    if [ "$age" -gt "$self_age" ] || { [ "$age" -eq "$self_age" ] && [ "$pid" -lt "$self_pid" ]; }; then
      printf '%s %s\n' "$pid" "$(rask_lane_cost_of "$pid")"
    fi
  done
  return 0
}

# Slots claimed by gates that outrank us. See the header: only OLDER gates count, ties on pid.
#
# The age is the GATE's, never its marker's. A marker is re-spawned when a claim changes, so ordering
# on marker age would make a gate surrender its seniority every time it declared a new phase -- it
# would go to the back of the queue for doing work, which is a starvation generator. Ordering on the
# owner means a gate never loses its place.
rask_lane_claimed_by_seniors() {
  self_pid="${1:-$$}"
  parent_pid="${2:-$PPID}"

  self_age="$(rask_etime_seconds "$(rask_e2e_etime_of "$self_pid")")"
  total=0

  for pid in $(rask_lane_other_gates "$self_pid" "$parent_pid"); do
    age="$(rask_etime_seconds "$(rask_e2e_etime_of "$pid")")"
    senior=0
    if [ "$age" -gt "$self_age" ]; then
      senior=1
    elif [ "$age" -eq "$self_age" ] && [ "$pid" -lt "$self_pid" ]; then
      senior=1
    fi
    if [ "$senior" = "1" ]; then
      total=$((total + $(rask_lane_cost_of "$pid")))
    fi
  done

  printf '%s' "$total"
}

# Slots left for us. Never negative: seniors can over-commit the box between two polls -- an E2E gate
# declaring its exclusive claim on top of a unit gate that is already running -- and a negative
# headroom would propagate straight into `-m:-3`, which MSBuild rejects.
rask_lane_headroom() {
  headroom=$(( $(rask_lane_budget) - $(rask_lane_claimed_by_seniors "${1:-$$}" "${2:-$PPID}") ))
  if [ "$headroom" -lt 0 ]; then
    headroom=0
  fi
  printf '%s' "$headroom"
}

# "How big may I be?" -- answers now, never waits.
#
# The floor is load-bearing, and is why this takes a minimum at all. A gate handed 0 slots would pass
# `-m:0` to MSBuild, which means "use every core" -- so the BUSIEST possible machine would produce
# the LEAST bounded run, silently, in exactly the circumstances where that does the most damage.
# Clamped low rather than refused: a slow gate is a working gate, and this is what lets the unit gate
# keep .githooks/pre-commit's promise never to block a commit.
rask_lane_fit() {
  min="${1:-2}"
  max="${2:-8}"

  if [ "${RASK_LANE_DISABLE:-}" = "1" ]; then
    printf '%s' "$max"
    return 0
  fi

  fit="$(rask_lane_headroom "${3:-$$}" "${4:-$PPID}")"

  if [ "$fit" -lt "$min" ]; then
    fit="$min"
  fi
  if [ "$fit" -gt "$max" ]; then
    fit="$max"
  fi
  printf '%s' "$fit"
}

# "Does this much fit right now?" -- the predicate a blocking caller polls. The wait LOOP lives in the
# calling script, not here, so the waiting message can name what is being waited for; same split as
# e2e-concurrency.sh, where the predicate is testable and run-e2e-local.sh owns the queue.
#
# A cost larger than the whole budget is clamped rather than refused. Without that, setting
# RASK_LANE_SLOTS=6 while some caller still asks for 10 would block every browser suite on the
# machine forever, and nothing would say why.
rask_lane_fits() {
  cost="${1:-1}"

  [ "${RASK_LANE_DISABLE:-}" = "1" ] && return 0

  budget="$(rask_lane_budget)"
  [ "$cost" -gt "$budget" ] && cost="$budget"

  [ "$(rask_lane_headroom "${2:-$$}" "${3:-$PPID}")" -ge "$cost" ]
}

# Publish a claim: start a lane-claim.sh child advertising <cost> for <phase>, replacing any claim
# this process already had. Returns immediately -- claiming is not waiting.
#
# The redirections are not cosmetic. .githooks/pre-push runs the gate as `... | tee "$gate_log"`, and
# a background child that inherits stdout holds the pipe's write end open, so `tee` cannot exit while
# the marker lives. Without </dev/null and the output redirects, a marker outliving its gate by even
# a few seconds would hang the push.
rask_lane_claim() {
  cost="${1:-1}"
  phase="${2:-work}"

  [ "${RASK_LANE_DISABLE:-}" = "1" ] && return 0

  rask_lane_release

  bash "${BASH_SOURCE%/*}/lane-claim.sh" "$$" "$cost" "$phase" \
    >/dev/null 2>&1 </dev/null &
  RASK_LANE_MARKER_PID=$!
}

# Drop this process's claim. Safe to call when there is none, and safe to call twice.
rask_lane_release() {
  [ -n "${RASK_LANE_MARKER_PID:-}" ] || return 0
  kill "$RASK_LANE_MARKER_PID" 2>/dev/null || true
  wait "$RASK_LANE_MARKER_PID" 2>/dev/null || true
  RASK_LANE_MARKER_PID=""
}

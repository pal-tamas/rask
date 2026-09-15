# shellcheck shell=bash
# The admission of a browser suite: whether it may start now, waits, or refuses — shared by the browser gates.
#
# Lived inline in scripts/run-e2e-local.sh until the devtools gate (scripts/run-devtools-e2e-local.sh) needed the
# same wait. Both suites need the whole machine for the same reason, and a second copy of this is the kind that
# drifts: one gate learning a new override, or a reworded anchored line, while the other keeps the old.
#
# RASK_E2E_GATE names the gate in every line printed (default run-e2e-local, so that gate's output is unchanged).
# The two anchored lines — "<gate>: still queued after …" and "<gate>: refused to start …" — are contracts:
# scripts/lib/build-failure.sh greps for them to tell a busy machine from a broken branch.
#
# Needs scripts/lib/machine-lane.sh sourced first. Tested by scripts/tests/e2e-await-slots.test.sh.

# Wait until this machine has room for the browser suite.
#
# MOVED, in the change that split this gate by phase. This block used to run at the top of the script,
# so a gate held the whole machine from its very first line. Measured on a live run: 9m30s of build
# and publish before `dotnet test` appeared at all — roughly a quarter of the ~40m norm, during which
# seven other worktrees were blocked while this one used a SINGLE core (the build below is -m:1, and
# stays that way). Builds are throughput work and tolerate sharing; the browser suite genuinely does
# not. So the build and publish phases now take a partial claim, several worktrees may build at once,
# and only this function serialises. A gate that queues here has already done its building.
#
# The claim is PUBLISHED BEFORE THE WAIT, and that ordering is load-bearing rather than tidy. A gate
# queued here has not started `dotnet test` yet, so nothing in its own process tree says what it is
# about to need; without an explicit claim its juniors would read it as merely building, under-count
# it, and start work it is about to need the whole machine for. See scripts/lib/lane-claim.sh.
#
# Everything #934 established is kept: ordering by process age, the deadline, the poll interval, and
# every override. So are the two ANCHORED output lines below — scripts/lib/build-failure.sh greps for
# them to decide whether a red gate means "your branch is broken" or "your machine was busy", and a
# reworded line there silently reclassifies a contended run as a genuine failure.
rask_e2e_await_slots() {
  # Skipped under CI purely so this can never be the thing that breaks an automated run. Nothing in
  # .github/workflows runs the browser E2E today, so this is insurance against a future parallel path.
  [ -n "${CI:-}" ] && return 0

  slots_needed="$(rask_lane_budget)"
  rask_lane_claim "$slots_needed" test

  rask_e2e_machine_admits "$slots_needed" && return 0

  if ! rask_lane_fits "$slots_needed"; then
    echo "${RASK_E2E_GATE:-run-e2e-local}: this machine's slots are taken, and the browser suite needs all $slots_needed."
    rask_e2e_name_seniors
    echo
    echo "            Two suites on one machine contend for resources. The port collision was fixed in"
    echo "            #626, but contention still surfaces as a plausible-looking red in one or both runs,"
    echo "            minutes later, with nothing in the log pointing back at it. It has already cost one"
    echo "            unexplained timeout between two worktrees that were coordinating and still both"
    echo "            believed the machine was idle."
    echo
  else
    rask_e2e_describe_load
  fi

  if [ "${RASK_E2E_ALLOW_CONCURRENT:-}" = "1" ]; then
    # Now strictly better than it used to be: the claim above stays published, so this run is at least
    # honest about the load it is adding and everyone else accounts for it. It still buys a result you
    # cannot trust.
    echo "${RASK_E2E_GATE:-run-e2e-local}: RASK_E2E_ALLOW_CONCURRENT=1 — starting alongside it anyway."
    echo "            Treat any failure as suspect until you have re-run it alone."
    return 0
  fi

  queue_deadline_s="${RASK_E2E_QUEUE_TIMEOUT:-5400}"
  queue_poll_s="${RASK_E2E_QUEUE_POLL:-20}"
  queue_waited_s=0

  if [ "${RASK_E2E_QUEUE:-1}" = "0" ]; then
    echo "            Wait for the run above to finish, then push again. To run anyway:"
    echo "                RASK_E2E_ALLOW_CONCURRENT=1 git push        (or set it for this script)"
    echo "            and treat any failure as suspect until you have re-run it alone."
    # A distinct, anchored line for rask_build_failure_kind. It cannot classify on the banner above:
    # that now prints on runs which go on to QUEUE AND SUCCEED, so keying on it would relabel a
    # genuine failure hours later as contention.
    echo "${RASK_E2E_GATE:-run-e2e-local}: refused to start — RASK_E2E_QUEUE=0 and the lane is held."
    exit 1
  fi

  echo "${RASK_E2E_GATE:-run-e2e-local}: queued behind it — waiting up to $((queue_deadline_s / 60))m for the slots."
  echo "            The build and publishes above are already done, so this wait is the suite only."
  echo "            Ctrl-C to give up; RASK_E2E_QUEUE=0 to refuse immediately instead of waiting;"
  echo "            RASK_SKIP_E2E=1 to skip the gate entirely."

  # Recomputed FRESH each poll rather than cached. A waiter holding a stale list would keep waiting
  # for a pid that had already exited and — worse — would miss a gate that started later but outranks
  # it after a tie-break.
  while ! rask_e2e_machine_admits "$slots_needed"; do
    if [ "$queue_waited_s" -ge "$queue_deadline_s" ]; then
      echo
      echo "${RASK_E2E_GATE:-run-e2e-local}: still queued after $((queue_waited_s / 60))m — giving up rather than waiting silently."
      echo "            Load average $(rask_lane_load_average) on $(rask_lane_cpu_count) CPUs (ceiling ${RASK_E2E_MAX_LOAD_PER_CPU:-8} per CPU)."
      echo "            The slots are held by:"
      rask_e2e_name_seniors
      echo "            A gate that has outlived the ~40m norm is usually a wedged run, not a busy"
      echo "            machine — check it before assuming you are merely unlucky."
      exit 1
    fi
    sleep "$queue_poll_s"
    queue_waited_s=$((queue_waited_s + queue_poll_s))
    # One line a minute: enough to show the wait is alive inside a hook printing thousands of build
    # lines, quiet enough not to become the noise it is reporting on.
    if [ "$((queue_waited_s % 60))" -eq 0 ]; then
      echo "${RASK_E2E_GATE:-run-e2e-local}: still queued ($((queue_waited_s / 60))m)…"
    fi
  done

  echo "${RASK_E2E_GATE:-run-e2e-local}: slots free after $((queue_waited_s / 60))m $((queue_waited_s % 60))s — starting the suite."
}

# Whether the browser suite may start now: its slots are free AND the machine is calm enough (#1099). The slots are
# this repo's own bookkeeping; the load average is everything else the machine is doing, which the slots cannot see.
rask_e2e_machine_admits() {
  rask_lane_fits "$1" && rask_lane_load_ok
}

# Said when the slots are free and the load is what holds the suite back, so a wait never goes unexplained.
rask_e2e_describe_load() {
  echo "${RASK_E2E_GATE:-run-e2e-local}: the slots are free, but this machine's load average is $(rask_lane_load_average) on $(rask_lane_cpu_count) CPUs."
  echo
  echo "            A browser suite started on a machine this busy boots its WebAssembly pages past their"
  echo "            budget and reports the timeouts as failures, in whichever tests happen to be running"
  echo "            (#1099). RASK_E2E_MAX_LOAD_PER_CPU (default 8) sets the ceiling; 0 turns the check off."
  echo
  return 0
}

# Printing WHICH run and HOW LONG is the useful half — "there is a conflict" tells you there is a
# problem, `ps -o etime=` is what lets you decide whether to wait for it or investigate your own red.
rask_e2e_name_seniors() {
  rask_lane_senior_gates | while read -r pid slots; do
    # Through the same indirected accessors the ordering uses, not a bare `ps`. Two reasons, and the
    # second is the one that matters: it keeps this testable, and it stops the list going silently
    # empty. A senior can exit between being ranked and being named, and a bare `ps` then prints
    # nothing for it — so a refusal could name NOBODY, which is precisely the unexplained kind this
    # gate has always gone out of its way not to be. The pid is printed either way.
    elapsed="$(rask_e2e_etime_of "$pid")"
    cmd="$(rask_e2e_command_of "$pid" | cut -c1-110)"
    [ -n "$elapsed" ] || elapsed="(exited)"
    echo "               pid $pid, $slots slot(s), running for ${elapsed}: $cmd"
  done
  return 0
}

# RASK_E2E_QUEUE=0 means "give me my push back rather than the wait", so it has to be answered BEFORE
# the build, not after it.
#
# Splitting this gate by phase moved the only slot check to just above the suite — roughly ten minutes
# in, by this file's own measurement. That is right for waiting (the wait is then spent having already
# built) and wrong for refusing: someone who set QUEUE=0 precisely to avoid a delay would have paid the
# entire build first and only then been told no. The refusal is therefore asked twice, once here where
# it can still save the ten minutes, and once below for a lane that fills up while we build.
#
# Same anchored line both times — scripts/lib/build-failure.sh greps for it to tell a busy machine from
# a broken branch.
rask_e2e_refuse_before_build() {
  if [ -z "${CI:-}" ] && [ "${RASK_E2E_QUEUE:-1}" = "0" ] && [ "${RASK_E2E_ALLOW_CONCURRENT:-}" != "1" ]; then
    if ! rask_e2e_machine_admits "$(rask_lane_budget)"; then
      if rask_lane_fits "$(rask_lane_budget)"; then
        rask_e2e_describe_load
      else
        echo "${RASK_E2E_GATE:-run-e2e-local}: this machine's slots are taken, and the browser suite needs all $(rask_lane_budget)."
      fi
      rask_e2e_name_seniors
      echo "            Wait for the run above to finish, then push again. To run anyway:"
      echo "                RASK_E2E_ALLOW_CONCURRENT=1 git push        (or set it for this script)"
      echo "            and treat any failure as suspect until you have re-run it alone."
      echo "${RASK_E2E_GATE:-run-e2e-local}: refused to start — RASK_E2E_QUEUE=0 and the lane is held."
      exit 1
    fi
  fi
  return 0
}

#!/usr/bin/env bash
# Is another browser E2E gate already running on this machine?
#
# Lives here rather than inline in run-e2e-local.sh so it can be tested (scripts/tests/e2e-concurrency.test.sh) —
# same split as scripts/lib/build-failure.sh, and for the same reason: a predicate that decides whether a
# push is allowed to proceed is exactly the kind of thing that is quietly wrong until it costs someone
# an afternoon.

# Prints the pids of OTHER live gate runs, one per line. Empty output means the machine is ours.
#
# Two decisions here, both learned the hard way:
#
#   * The match is ANCHORED on the scripts/ path segment. A bare `pgrep -f run-e2e-local.sh` also matches
#     any shell whose argv merely MENTIONS the script -- a `sh -c` wrapper, a `tee` of its log, an editor.
#     The first live test of this check reported three "conflicts" where there was one, two of them its
#     own test harness. A real run is always <repo>/scripts/run-e2e-local.sh.
#
#   * By PROCESS, never a lockfile. A lockfile survives kill -9 and a laptop sleep and then wedges the
#     gate until someone works out what to delete; a Ctrl-C'd run leaves no process behind, so this is
#     self-healing. RASK_E2E_PGREP_OVERRIDE exists only so the test can inject a candidate list.
rask_other_e2e_runs() {
  self_pid="${1:-$$}"
  parent_pid="${2:-$PPID}"

  candidates="$(
    if [ -n "${RASK_E2E_PGREP_OVERRIDE:-}" ]; then
      printf '%s\n' $RASK_E2E_PGREP_OVERRIDE
    else
      pgrep -f 'run-e2e-local\.sh' 2>/dev/null || true
    fi
  )"

  for pid in $candidates; do
    [ "$pid" = "$self_pid" ] && continue
    [ "$pid" = "$parent_pid" ] && continue
    if rask_is_e2e_gate_command "$(rask_e2e_command_of "$pid")"; then
      printf '%s\n' "$pid"
    fi
  done
}

# Is this command line a process actually RUNNING the gate, as opposed to one that merely mentions it?
#
# Decided on the EXECUTABLE POSITION rather than by substring, which is what makes both directions come
# out right:
#
#   bash scripts/run-e2e-local.sh          gate     <- the documented invocation; relative, no ./
#   scripts/run-e2e-local.sh               gate
#   /bin/bash /repo/scripts/...sh          gate     <- what .githooks/pre-push execs
#   vim scripts/run-e2e-local.sh           NOT      <- pgrep -f matches it; an editor is not a gate
#   grep -rn run-e2e-local.sh /repo        NOT
#   tee /tmp/.../run-e2e-local.log         NOT      <- the gate's own log, not a second gate
#   zsh -c '... && bash /tmp/x/...sh'      NOT      <- a wrapper; the child it spawns is matched on its own
#
# An earlier version anchored on the '/scripts/' path segment instead. That got the editor and the tee
# right but silently missed `bash scripts/run-e2e-local.sh` -- no leading slash -- which is exactly the
# form CLAUDE.md and .github/CONTRIBUTING.md document for running the gate by hand. It would have been blind to
# the manual runs that prompted the check, while looking correct. Substring matching cannot separate
# "is running this" from "names this"; argv position can.
rask_is_e2e_gate_command() {
  [ -n "${1:-}" ] || return 1

  # Word-split the command line, with globbing off so a stray * in an argument cannot expand.
  #
  # Known limitation: a repo path containing a SPACE defeats the split, and `ps -o command=` gives no way
  # to recover the boundary. Recovering it properly means reading argv from /proc or lsof, which is a lot
  # of machinery for a case this repo does not have — so it is written down rather than handled, and the
  # failure is a missed detection (a run that proceeds), never a wrong refusal.
  set -f
  # shellcheck disable=SC2086
  set -- $1
  set +f

  first="${1:-}"
  second="${2:-}"

  case "${first##*/}" in
    bash | sh | zsh | dash | ksh)
      # Walk past the shell's own options to the script path. Bailing on the FIRST option instead --
      # which is what this did at first -- is correct for -c and wrong for every other flag: it made
      # `bash -x scripts/run-e2e-local.sh` invisible, and running the gate under -x is a plausible thing
      # to do while debugging the gate. A guard clause right for its motivating case and over-broad for
      # its siblings is the same mistake as the path anchor it replaced.
      shift
      while [ $# -gt 0 ]; do
        case "$1" in
          # -c (and combined forms like -lc): the argument is script TEXT, not a path. It can end with
          # the gate's path without running it; the child process that does is matched on its own pid.
          #
          # Matches ANY option containing a c, deliberately and approximately. It over-matches --norc,
          # and `bash --rcfile x <script>` is missed for a second, unrelated reason: --rcfile consumes
          # an argument, so the walk below takes `x` for the script path. Narrowing this arm to
          # single-dash clusters would fix --norc and leave --rcfile broken -- a rule that reads as
          # exhaustive while still missing a case, which is worse than one that admits it. Handling
          # both needs a table of which options take arguments, for invocations nobody uses on this
          # gate. Both failures are missed detections, never wrong refusals.
          -*c*) return 1 ;;
          -*) shift ;;
          *) break ;;
        esac
      done
      target="${1:-}"
      ;;
    *) target="$first" ;;
  esac

  case "$target" in
    */run-e2e-local.sh | run-e2e-local.sh) return 0 ;;
  esac
  return 1
}

# Elapsed seconds for a pid, from `ps -o etime=`.
#
# macOS ps has no `etimes` -- the Linux keyword that would hand us seconds directly errors here with
# "keyword not found" -- so the formatted field is parsed instead. The format is [[dd-]hh:]mm:ss and
# all three widths occur in practice: a gate seconds after launch prints "07", a normal run "12:34",
# a run that outlived a laptop sleep "01-00:39:09".
#
# Anything unparseable returns 0, i.e. "infinitely young". That is the safe direction: a process whose
# age cannot be read never outranks anyone, so a ps that fails can delay a run but can never be the
# reason two suites start at once.
rask_etime_seconds() {
  etime="${1:-}"
  [ -n "$etime" ] || { printf '0'; return; }

  days=0
  case "$etime" in
    *-*)
      days="${etime%%-*}"
      etime="${etime#*-}"
      ;;
  esac

  case "$etime" in
    *:*:*)
      hours="${etime%%:*}"
      rest="${etime#*:}"
      mins="${rest%%:*}"
      secs="${rest#*:}"
      ;;
    *:*)
      hours=0
      mins="${etime%%:*}"
      secs="${etime#*:}"
      ;;
    *)
      hours=0
      mins=0
      secs="$etime"
      ;;
  esac

  # Reject non-numeric parts, then strip leading zeros: $(( 08 )) is an octal error, and "08" is what
  # ps prints for the first minute of every run.
  for _field in days hours mins secs; do
    eval "_n=\$$_field"
    case "$_n" in
      *[!0-9]* | "") printf '0'; return ;;
    esac
    _n="${_n#"${_n%%[!0]*}"}"
    [ -n "$_n" ] || _n=0
    eval "$_field=\$_n"
  done

  printf '%s' "$((days * 86400 + hours * 3600 + mins * 60 + secs))"
}

# Indirected so the test can stub it without spawning real processes.
rask_e2e_etime_of() {
  if [ -n "${RASK_E2E_COMMAND_STUB:-}" ]; then
    eval "printf '%s' \"\${RASK_E2E_ETIME_$1:-}\""
    return
  fi
  ps -o etime= -p "$1" 2>/dev/null | tr -d ' '
}

# rask_e2e_lane_holders lived here until the gates moved to a slot budget. It ordered browser
# gates against each other by process age; scripts/lib/machine-lane.sh now does that for every
# gate and every phase, and carries the age-ordering argument in full. Two copies of an ordering
# rule that must agree is a bug waiting for the day they stop agreeing, so there is one.

# Indirected so the test can stub it without spawning real processes.
rask_e2e_command_of() {
  if [ -n "${RASK_E2E_COMMAND_STUB:-}" ]; then
    eval "printf '%s' \"\${RASK_E2E_CMD_$1:-}\""
    return
  fi
  ps -o command= -p "$1" 2>/dev/null
}

# Is some OTHER heavy build competing with this suite? Prints their pids, one per line.
#
# This is the collision the guard above does NOT catch, and the one #850 is about. That guard detects
# its own kind — a second browser gate — and refuses. The expensive case is everything else: a
# pre-commit hook, a plain `dotnet build`, a `dotnet publish`, the CLI build gate. None of those is a
# browser gate, so nothing refuses, nothing warns, and the contention is silent. The browser journeys
# and the WebSocket tests are timing-sensitive; under load they fail in ways that look exactly like
# real bugs, minutes after the contention, with nothing in the log pointing back at it.
#
# Deliberately a HINT and never a decision. `dotnet` runs for dozens of legitimate reasons, most of
# them cheap, and refusing on any of them would block far more than it protects — so this is consulted
# only to add a line to a failure that has ALREADY happened. That is why it can afford to be
# approximate where the refuse-or-proceed guard cannot, and why a false positive costs a sentence
# rather than a blocked push.
#
# Only the verbs that cost minutes and saturate cores: `dotnet run`, `dotnet ef`, `dotnet tool` and a
# bare `dotnet --version` are not contention worth naming, and a language server certainly is not.
rask_other_heavy_builds() {
  self_pid="${1:-$$}"

  candidates="$(
    if [ -n "${RASK_HEAVY_PGREP_OVERRIDE:-}" ]; then
      printf '%s\n' $RASK_HEAVY_PGREP_OVERRIDE
    else
      pgrep -f 'dotnet' 2>/dev/null || true
    fi
  )"

  for pid in $candidates; do
    [ "$pid" = "$self_pid" ] && continue

    cmd="$(rask_e2e_command_of "$pid")"
    [ -n "$cmd" ] || continue

    # An MSBuild worker node belongs to a build already counted, so naming every node would report
    # one build as eight competitors.
    case "$cmd" in
      *nodemode*) continue ;;
    esac

    case "$cmd" in
      *"dotnet build"*|*"dotnet test"*|*"dotnet publish"*|*"dotnet msbuild"*|*"dotnet pack"*)
        printf '%s\n' "$pid"
        ;;
    esac
  done
}

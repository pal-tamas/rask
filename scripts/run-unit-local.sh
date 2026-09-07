#!/usr/bin/env bash
# Local format + unit/integration gate.
#
# Formatting and the unit suite no longer run in CI — they run here, locally, and the pre-commit hook
# (.githooks/pre-commit) enforces this before a code commit. Steps: build once, run the FULL formatter
# (whitespace + style + analyzers), then run every test EXCEPT the browser E2E (that's its own gate —
# see run-e2e-local.sh).
#
# Why the FULL pass: import ordering is caught by nothing else. The build is warnings-as-errors with
# EnforceCodeStyleInBuild on, but IDE0055 only reports whitespace computed by the Formatter — sorting
# using directives is OrganizeImportsService, a separate mechanism that only `dotnet format` runs. So a
# misordered using drifts in silently (it did: see #584). The full pass is one workspace load, ~36s.
#
# This gate ran `dotnet format whitespace` only until #584, because the style/analyzer passes reported
# CS1503 in the routing tests. That was never spurious, and it is the reason for the Debug step below:
# `dotnet format` evaluates the solution in the DEFAULT configuration (Debug), so it resolves the
# OutputItemType="Analyzer" project references to src/*.Generators/bin/DEBUG/. This gate builds Release,
# so on a machine that has never built Debug those DLLs do not exist — Roslyn then loads no generator,
# `Rask.Core.Routing.Generated.Route<T>()` is never emitted, and every call site fails to bind with
# CS1503 ("cannot convert from '?[]'"). Building the generators in Debug costs ~2s and makes the pass
# deterministic; it is also why the failure looked machine-dependent, since a stale Debug DLL from an
# earlier build hides it.
#
# Usage:  scripts/run-unit-local.sh
# Skip:   RASK_SKIP_UNIT=1 (also honoured by the pre-commit hook)
set -euo pipefail

if [ "${RASK_SKIP_UNIT:-}" = "1" ]; then
  echo "run-unit-local: RASK_SKIP_UNIT=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

# Sourced at the TOP, not inside the failure branch below where the contention hint used to reach for
# it. A helper that only exists on one branch of an `if` is the shape run-e2e-local.sh argues against:
# it is loaded exactly when things are already going wrong, which is the worst moment to discover that
# the load itself is broken.
# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"

# How much of this machine may we take?
#
# This gate NEVER WAITS, and that is a deliberate inheritance from .githooks/pre-commit, which runs it
# on every commit and decided that a blocked commit costs more than a slow one -- "the person
# committing is usually not the person who can decide to wait". So instead of queueing, it asks how
# much room is left and SHRINKS INTO IT. On an idle box that is the full eight slots and nothing has
# changed; on a box with two other gates running it is two, and the commit still starts immediately.
#
# The count goes into ARGV via a one-time re-exec rather than into the environment, because the whole
# point is that a gate in ANOTHER worktree can read it back out of `ps` and account for it. An
# environment variable is invisible there. The re-exec happens before any work, so nothing is
# repeated; the guard is the presence of the flag itself.
case "${1:-}" in
  --lane-slots)
    # ${2:?} rather than $2: with `set -u` a bare $2 aborts with "unbound variable" and no hint, and
    # this is the one flag the script now advertises in its own argv.
    lane_slots="${2:?run-unit-local: --lane-slots needs a number}"
    shift 2
    ;;
  *)
    # An ABSOLUTE path, not "$0". `cd "$root"` above has already moved us, so a relative $0 — which is
    # what `bash ../scripts/run-unit-local.sh` from a subdirectory gives — no longer resolves, and a
    # failed exec kills the shell outright rather than falling through. Verified: it dies with
    # "No such file or directory" and the gate never runs.
    #
    # The ceiling comes from rask_lane_unit_max, because that is the number every OTHER gate credits a
    # unit gate with before this re-exec lands. Hardcoding 8 here meant RASK_LANE_UNIT_MAX=4 would make
    # readers account for 4 while this took 8 — over-subscribing the box silently, which is the
    # direction the library says is unsafe.
    exec "$root/scripts/run-unit-local.sh" --lane-slots "$(rask_lane_fit 2 "$(rask_lane_unit_max)")" "$@"
    ;;
esac

echo "run-unit-local: taking $lane_slots of $(rask_lane_budget) slots on this machine."

# Cheap and first: the gates' own shared logic. rask_build_failure_kind decides whether a red gate tells
# you your branch is broken or your machine is busy, and it is plain bash, so nothing else would catch a
# regression in it.
echo "==> Gate script tests"
for t in scripts/tests/*.test.sh; do
  [ -e "$t" ] || continue
  bash "$t"
done

echo "==> Build once (Release; no WASM bundle, no sample front ends)"
# -m:$lane_slots is the lever that actually bounds this gate. Left at MSBuild's default it takes every
# logical core, and three of these running from three worktrees is how the machine reached 35 worker
# nodes on 14 cores, load average 98, 0.0% idle -- at which point every timing-sensitive test in all
# three runs was untrustworthy. This was the only gate in the repo without an -m cap; the others all
# pass -m:1.
# RaskMetaBuild / RaskSpaBuild off: this gate runs UNIT tests, and not one of them exercises a
# meta framework's or a SPA's compiled front end — the browser E2E gate builds those, which is where
# a broken Nuxt config should surface. Left on, `dotnet build Rask.slnx` runs npm plus a PRODUCTION
# front-end build for each of the six Rask.Example.Meta.* samples.
#
# Measured on this machine rather than assumed, because the saving is much smaller than it looks:
# warm, 12.7s -> 10.7s for the whole solution; with one sample's front end invalidated, 4.5s -> 1.4s
# for that project. It is minutes only on a genuinely cold tree. The gate's real cost is elsewhere —
# the test run is ~146s and `dotnet format --verify-no-changes` ~57s, together about 90% of a warm
# run — so do not read this line as the thing that makes the gate fast.
dotnet build Rask.slnx -c Release -m:"$lane_slots" \
  -p:RaskWasm=false -p:WasmBuildNative=false -p:MinVerSkip=true \
  -p:RaskMetaBuild=false -p:RaskSpaBuild=false

echo "==> Source generators in Debug (dotnet format resolves analyzers from the default configuration)"
for proj in src/*.Generators/*.csproj; do
  dotnet build "$proj" -c Debug --nologo -v quiet
done

# Scoped to the files being committed when the caller says so (the pre-commit hook does), and the whole
# solution otherwise. Measured: 59s full, 30s scoped — the remaining 30s is solution load, which no
# scoping avoids.
#
# Sound rather than merely cheaper: dotnet format decides per DOCUMENT, so a file it is not shown is a
# file it would have had nothing to say about. And a tree cannot contain an unformatted committed file
# for this to miss, because that file would have had to pass this same gate on its own way in.
#
# The standalone gate keeps the full pass on purpose. It is the definition-of-done run, invoked with no
# commit in view, and "everything is formatted" is exactly the claim it exists to make.
format_scope=""
if [ "${RASK_FORMAT_SCOPE:-}" = "staged" ]; then
  # -z/-d so a path with a space or a newline in it cannot split into two arguments.
  staged_cs="$(git diff --cached --name-only --diff-filter=ACMR -z | tr '\0' '\n' | grep -E '\.cs$' || true)"

  if [ -n "$staged_cs" ]; then
    echo "==> Formatting check (staged .cs files only — RASK_FORMAT_SCOPE=staged)"
    # shellcheck disable=SC2086
    printf '%s\n' "$staged_cs" | tr '\n' ' ' | xargs dotnet format Rask.slnx --verify-no-changes --no-restore --include
    format_scope="done"
  else
    echo "==> Formatting check skipped — RASK_FORMAT_SCOPE=staged and no .cs staged."
    format_scope="done"
  fi
fi

if [ -z "$format_scope" ]; then
  echo "==> Formatting check (dotnet format --verify-no-changes: whitespace + style + analyzers)"
  dotnet format Rask.slnx --verify-no-changes --no-restore
fi

# The generated TypeScript is compiled by tsgo, which the test fetches itself as a checksum-verified
# binary at a pinned version, cached per user. So the type CHECK needs no node and always runs. Nothing
# to exclude any more: it used to be skipped when npx was absent, and a check whose first question is
# "is the tooling here?" is one that eventually answers no and stops running — which is how a generator
# emitting malformed TypeScript would ship green.
#
# The GATE still needs node, for the islands. RaskExternalBuild is left ON deliberately — the showcase
# carries a package.json, so the solution build runs npm and Vite for it, and that IS covered by unit
# tests. RaskSpaBuild and RaskMetaBuild are turned off on the build line above; see the note there.
#
# This comment used to claim the opposite — "the build passes -p:RaskSpaBuild=false", a flag no script
# in this repo has ever passed (#1012). Anyone reading it would have concluded the unit gate was
# node-light and cheap. It is neither, and the stale sentence is exactly what would have stopped someone
# noticing that the gate's cost changed when the meta samples landed. Whether the unit gate SHOULD build
# sample front ends is a separate, open question; this comment's job is only to describe what it does.
tsc_filter=""

echo "==> Unit & integration tests (excludes the browser E2E)"
# --blame-crash: when a test host dies below the managed layer, the run reports "Test host process
# crashed" with no exception, no stack and not even the name of the test that was running — and since
# the solution runs ~40 assemblies at once, the last lines of console output belong to whichever OTHER
# assembly happened to be writing, which is how #769 spent an investigation on Rask.Server.Tests over a
# crash in an assembly that reported more tests than Rask.Server.Tests has. Blame writes a per-host
# sequence file naming the test in flight and collects a dump, so the next occurrence is diagnosable
# instead of merely observed. It costs nothing on a green run.
set +e
# The FULL slot count, not half of it. The halving assumed (assemblies in flight) x (2 threads each)
# would square the budget; measured back-to-back on this 14-core box, that reasoning cost more than it
# saved — the same suite runs in 284s at -m:4 and 128s at -m:8, a 2.2x difference on the phase that is
# about two thirds of a warm gate.
#
# The oversubscription the halving feared is real but bounded: xUnit threads are mostly blocked on I/O
# and on each other, not saturating a core apiece. Checked for the failure mode that would matter —
# timing-sensitive tests going red under load is this repository's most expensive kind of noise — with
# repeat full runs at the higher count, both green.
#
# If timing flakes do start tracking this, halve it back rather than chasing the individual tests: a
# suite that only passes at low parallelism is telling you something, and it is cheaper to believe it.
test_slots="$lane_slots"
[ "$test_slots" -lt 1 ] && test_slots=1

dotnet test Rask.slnx -c Release --no-build -m:"$test_slots" \
  --filter "FullyQualifiedName!~Rask.Examples.E2E$tsc_filter" \
  --blame-crash \
  --results-directory "$root/artifacts/test-blame" \
  --logger "console;verbosity=normal"
unit_status=$?
set -e

# The same hint the browser gate prints, for the same reason. This suite has WebSocket and timing
# tests of its own, and #850 records one being blamed for a change that could not have caused it —
# the real cause was a browser gate holding the machine while this ran.
if [ "$unit_status" -ne 0 ]; then
  # shellcheck source=lib/e2e-concurrency.sh
  . "$root/scripts/lib/e2e-concurrency.sh"
  browser_gates="$(rask_other_e2e_runs | tr '\n' ' ')"

  if [ -n "${browser_gates// /}" ]; then
    {
      echo
      echo "run-unit-local: a browser E2E gate was running on this machine during this run."
      for pid in $browser_gates; do
        elapsed="$(ps -o etime= -p "$pid" 2>/dev/null | tr -d ' ')"
        cmd="$(ps -o command= -p "$pid" 2>/dev/null | cut -c1-120)"
        [ -n "$elapsed" ] && echo "                pid $pid, running for ${elapsed}: $cmd"
      done
      echo
      echo "             Re-run alone before investigating. A timing-sensitive failure here under a"
      echo "             live browser suite has already cost one investigation into a test the change"
      echo "             under review could not reach."
    } >&2
  fi

  exit "$unit_status"
fi

echo "==> Format + unit gate passed."

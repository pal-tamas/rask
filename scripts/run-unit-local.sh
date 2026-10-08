#!/usr/bin/env bash
# The format + unit/integration gate.
#
# CI runs this script as its build, unit and format jobs (RASK_UNIT_PART) on every push (.github/workflows/gates.yml), and it
# runs the same way by hand. No git hook runs it. Steps: build once, run the FULL formatter
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
# Skip:   RASK_SKIP_UNIT=1
set -euo pipefail

if [ "${RASK_SKIP_UNIT:-}" = "1" ]; then
  echo "run-unit-local: RASK_SKIP_UNIT=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
# shellcheck source=lib/node-path.sh
. "$root/scripts/lib/node-path.sh"
rask_ensure_node
# shellcheck source=lib/dotnet-env.sh
. "$root/scripts/lib/dotnet-env.sh"
cd "$root"

# Sourced at the TOP, not inside the failure branch below where the contention hint used to reach for
# it. A helper that only exists on one branch of an `if` is the shape run-e2e-local.sh argues against:
# it is loaded exactly when things are already going wrong, which is the worst moment to discover that
# the load itself is broken.
# shellcheck source=lib/machine-lane.sh
. "$root/scripts/lib/machine-lane.sh"

# How much of this machine may we take?
#
# This gate NEVER WAITS: a run that starts small costs less than one that queues behind every other
# worktree. So instead of queueing, it asks how much room is left and SHRINKS INTO IT. On an idle box
# that is the full eight slots; on a box with two other gates running it is two, and the run still
# starts immediately. (CI passes --lane-slots itself: nothing else is running on the runner.)
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

# --- Where the time goes --------------------------------------------------------------------------
#
# Printed on EVERY exit, red ones included: a gate that cannot say which phase spent its time gets
# "optimised" by guesswork. rask_phase closes the phase that just ended; the
# formatter and the scoped test projects run alongside other work, so they report their own seconds
# and are listed apart rather than added to a total they do not extend.
phase_table=""
phase_mark=$SECONDS
alongside_table=""
rask_phase() {
  phase_table="$phase_table$(printf '    %4ds  %s' "$((SECONDS - phase_mark))" "$1")
"
  phase_mark=$SECONDS
}
rask_alongside() {
  alongside_table="$alongside_table$(printf '    %4ds  %s' "$1" "$2")
"
}
rask_print_phases() {
  [ -n "$phase_table" ] || return 0
  echo "==> Where the time went (${SECONDS}s in all)"
  printf '%s' "$phase_table"
  if [ -n "$alongside_table" ]; then
    echo "    of which, running alongside each other (slowest first):"
    printf '%s' "$alongside_table" | sort -rn | head -12
  fi
}
trap rask_print_phases EXIT

# --- Scope: which projects can this change actually reach? --------------------------------------
#
# Opt-in: RASK_TEST_SCOPE=affected narrows a hand run to the projects the change can reach. Unset — the
# default, and what CI runs — it does the whole solution, which is what makes narrowing safe: nothing
# is published on the strength of a scoped run alone.
#
# scripts/lib/affected_projects.py owns the graph, follows both ProjectReference and the
# source-linked <Compile Include="..\..."/> edges this repo uses, and answers FULL for anything it
# cannot map precisely — a repo-root import, a gate script, a shared file that belongs to no project.
# The reason is always PRINTED: a gate that silently narrows itself is the failure mode this
# repository has paid for most often, so a scoped run says what it scoped to and a full run says why
# it could not.
#
# Two ways of asking "what changed":
#   * by default, the STAGED files.
#   * with RASK_SCOPE_RANGE set to a git range (origin/main...HEAD), everything that range changed, as
#     one unit. Scoping a branch to only its tip commit would be unsound: a file changed in an earlier
#     commit of the same range would go untested.
scope_projects=""
if [ "${RASK_TEST_SCOPE:-}" = "affected" ]; then
  if [ -n "${RASK_SCOPE_RANGE:-}" ]; then
    scope_changed="$(git diff --name-only --diff-filter=ACMR "$RASK_SCOPE_RANGE" | grep . || true)"
    scope_source="the push range $RASK_SCOPE_RANGE"
  else
    scope_changed="$(git diff --cached --name-only --diff-filter=ACMR -z | tr '\0' '\n' | grep . || true)"
    scope_source="the staged change"
  fi

  if [ -z "$scope_changed" ]; then
    echo "==> Scope: nothing changed in $scope_source — running the full solution."
  else
    scope_out="$(printf '%s\n' "$scope_changed" | python3 "$root/scripts/lib/affected_projects.py" "$root")"

    case "$scope_out" in
      FULL*)
        echo "==> Scope: FULL solution — $(printf '%s' "$scope_out" | head -1 | cut -f2-)"
        ;;
      "")
        echo "==> Scope: the graph named no projects — running the full solution."
        ;;
      *)
        scope_projects="$scope_out"
        echo "==> Scope: $(printf '%s\n' "$scope_projects" | grep -c .) project(s) reachable from the staged change:"
        printf '        %s\n' $scope_projects
        ;;
    esac
  fi
fi

# RASK_UNIT_PART cuts this gate into pieces CI runs on separate machines (.github/workflows/gates.yml);
# a hand run leaves it unset and gets everything.
#
#   build          the gate script tests and the warnings-as-errors build, analyzers on — nothing else
#   tests          a build without analyzers, then the tests
#   tests-K/N      the same build, then shard K of N of the test projects (dealt round-robin from a
#                  sorted list, so a new project joins a shard by existing)
#   format         a build without analyzers, then the formatter. A scoped CI run calls it with
#                  RASK_FORMAT_SCOPE=range: the changed .cs files only, over a build of the projects
#                  the change reaches
#   format-src     a build without analyzers, then the formatter over src/
#   format-tests   a build without analyzers, then the formatter over tests/
#
# Every piece builds, because the tests need the assemblies and the formatter resolves types the build
# generates (the site's scoped-TypeScript types; without them it reports CS0246 for code that
# compiles). Only `build` pays for the analyzers. The formatter is the slowest single step and decides
# per document, so its two halves cover exactly what the whole does. A test shard builds the WHOLE
# solution and narrows only what it runs: narrowing the build as well left tests that read a build
# product of a project they do not reference (rask.wasm.js) with nothing to read.
unit_part="${RASK_UNIT_PART:-}"
format_include=""
test_shard=""
case "$unit_part" in
  ""|build|tests|format) ;;
  tests-[0-9]*/[0-9]*)
    test_shard="${unit_part#tests-}"
    unit_part="tests"
    ;;
  format-src) format_include="src/" ;;
  format-tests) format_include="tests/" ;;
  *)
    echo "run-unit-local: RASK_UNIT_PART must be build, tests, tests-K/N, format, format-src or format-tests, not '$unit_part'." >&2
    exit 1
    ;;
esac

# With RASK_TEST_SCOPE=affected as well (a scoped CI run), the scope narrows what a part RUNS and what
# `build` compiles. A tests part still builds the whole solution, for the reason above, so its scope is
# set aside here and applied after the build.
#
# A format part keeps the narrowed build when the formatter is narrowed to the same change: it decides
# per document, and every project a changed document compiles against is in the affected build, being
# what the projects that changed depend on. The whole-solution build was 198 s of a 292 s job that
# formatted two files. Asked for every document, the formatter still gets the whole solution.
tests_scope=""
skip_tests=0
format_follows_scope=0
case "${RASK_FORMAT_SCOPE:-}" in
  range)  [ -n "${RASK_SCOPE_RANGE:-}" ] && format_follows_scope=1 ;;
  staged) [ -z "${RASK_SCOPE_RANGE:-}" ] && format_follows_scope=1 ;;
esac
case "$unit_part" in
  ""|build) ;;
  format)
    [ "$format_follows_scope" -eq 1 ] || scope_projects=""
    ;;
  *)
    tests_scope="$scope_projects"
    scope_projects=""
    ;;
esac

rask_phase "scope"

# Cheap and first: the gates' own shared logic. rask_build_failure_kind decides whether a red gate tells
# you your branch is broken or your machine is busy, and it is plain bash, so nothing else would catch a
# regression in it.
#
# Run CONCURRENTLY, with ONE exception below. They are independent bash scripts that stub `ps`/`pgrep`
# rather than touching the machine, and the slowest (machine-lane, 54 cases) sets the floor for all of
# them either way. `wait -n` is deliberately not used: it needs bash 4.3+, and macOS still ships bash
# 3.2 as /bin/bash, so this collects statuses by pid instead.
gate_tests_failed=0

# Which of them this change can reach. Each test is about the script it sits beside, so scripts/ and
# .githooks/ run all of them; a test whose subject lives elsewhere names it on a `# gate-inputs:` line
# (an ERE over repo-relative paths) -- the public-API prober's is src/Rask.Cache and the MSBuild
# imports. An unscoped or FULL run executes every one. This was the costliest step a narrow commit
# paid for: ~45 s, nearly all of it the prober's four builds, on changes that could not affect it.
rask_gate_test_applies() {
  case "$unit_part" in ""|build) ;; *) return 1 ;; esac   # the `build` part runs these
  [ -z "$scope_projects" ] && return 0
  inputs="$(sed -n 's/^# gate-inputs: //p' "$1" | head -1)"
  printf '%s\n' "$scope_changed" | grep -E "^(scripts/|\.githooks/)${inputs:+|$inputs}" >/dev/null
}

# The exception, and the reason the blanket "no shared state" this comment used to claim is not true:
# public-api-gate proves the analyzer by writing src/Rask.Cache/__PublicApiGateProbe.cs into the REAL
# worktree and briefly moving that project's PublicAPI baselines aside (it restores both on exit, so
# nothing is left behind). attribution-guard ends by asserting the working tree is exactly where it
# was. Run concurrently, the guard sees the prober's mutations and fails with "a git env var leaked
# into the temp repo" — naming a cause that is not the one, on a run where nothing is wrong.
#
# It is the GUARD that runs alone rather than the prober: the guard is pure bash and costs about a
# second, while the prober is four builds of Rask.Cache and is precisely what the concurrency is for.
serial_test="scripts/tests/attribution-guard.test.sh"
if [ -e "$serial_test" ] && rask_gate_test_applies "$serial_test"; then
  echo "==> Gate script test (alone: it asserts the working tree is untouched)"
  serial_log="${TMPDIR:-/tmp}/rask-gate-test-$$-attribution-guard.log"
  if bash "$serial_test" >"$serial_log" 2>&1; then
    cat "$serial_log"
  else
    cat "$serial_log" >&2
    echo "run-unit-local: gate script test FAILED: $serial_test" >&2
    gate_tests_failed=1
  fi
fi

echo "==> Gate script tests (concurrent)"
gate_test_pids=""
gate_test_names=""
for t in scripts/tests/*.test.sh; do
  [ -e "$t" ] || continue
  if [ "$t" = "$serial_test" ]; then continue; fi   # already run, alone, above
  if ! rask_gate_test_applies "$t"; then
    echo "    skipped $t -- nothing it covers changed"
    continue
  fi
  bash "$t" >"${TMPDIR:-/tmp}/rask-gate-test-$$-$(basename "$t" .test.sh).log" 2>&1 &
  gate_test_pids="$gate_test_pids $!"
  gate_test_names="$gate_test_names $t"
done

# shellcheck disable=SC2086  # deliberate word split: the names line up with the pids collected above
set -- $gate_test_names
for pid in $gate_test_pids; do
  gate_test_name="$1"; shift
  gate_test_log="${TMPDIR:-/tmp}/rask-gate-test-$$-$(basename "$gate_test_name" .test.sh).log"
  # Output is printed either way. A gate test that passes silently is one nobody notices has stopped
  # asserting anything, which is the failure this repository keeps paying for.
  if wait "$pid"; then
    cat "$gate_test_log"
  else
    cat "$gate_test_log" >&2
    echo "run-unit-local: gate script test FAILED: $gate_test_name" >&2
    gate_tests_failed=1
  fi
  rm -f "$gate_test_log"
done
rask_phase "gate script tests"
[ "$gate_tests_failed" -eq 0 ] || exit 1

# Only `build` pays for the analyzers: it runs them over this same source on another machine at the
# same moment, and the tests and the formatter need the assemblies, not a second verdict. Source
# generators are not analyzers and still run.
build_analyzers=""
case "$unit_part" in ""|build) ;; *) build_analyzers="-p:RunAnalyzers=false" ;; esac

if [ -n "$scope_projects" ]; then
  # ONE MSBuild invocation over the affected set, not a loop of `dotnet build` per project. The set
  # has edges inside it, and two concurrent builds of a project that both depend on a third race on
  # that third one's obj/ and bin/. The traversal file lives in TMPDIR precisely so it does NOT pick
  # up the repo's Directory.Build.props — each csproj still imports its own chain from its own
  # location, which is the only chain that should apply to it.
  scope_proj="${TMPDIR:-/tmp}/rask-affected-$$.proj"
  {
    echo '<Project DefaultTargets="Build">'
    echo '  <ItemGroup>'
    printf '%s\n' $scope_projects | while read -r p; do
      [ -n "$p" ] && echo "    <ScopedProject Include=\"$root/$p\" />"
    done
    echo '  </ItemGroup>'
    echo '  <Target Name="Build">'
    # The session id is what `dotnet build -restore` passes too. Without a global property that differs,
    # Build reuses the evaluation Restore made BEFORE it wrote the NuGet imports, so a never-restored test
    # project (any fresh worktree) builds without Microsoft.NET.Test.Sdk's targets: no runtimeconfig, no
    # testhost, and the run aborts with "Could not find testhost". Measured: 22 files out -> 81.
    echo '    <MSBuild Projects="@(ScopedProject)" Targets="Restore" Properties="MSBuildRestoreSessionId=$([System.Guid]::NewGuid())" />'
    echo '    <MSBuild Projects="@(ScopedProject)" Targets="Build" BuildInParallel="true" />'
    echo '  </Target>'
    echo '</Project>'
  } >"$scope_proj"

  echo "==> Build (Release; affected projects only)"
  # shellcheck disable=SC2086  # empty unless set above
  dotnet build "$scope_proj" -c Release -m:"$lane_slots" \
    -p:RaskWasm=false -p:WasmBuildNative=false -p:MinVerSkip=true \
    -p:RaskSpaBuild=false $build_analyzers
  rm -f "$scope_proj"
else

echo "==> Build once (Release; no WASM bundle)"
# -m:$lane_slots is the lever that actually bounds this gate. Left at MSBuild's default it takes every
# logical core, and three of these running from three worktrees is how the machine reached 35 worker
# nodes on 14 cores, load average 98, 0.0% idle -- at which point every timing-sensitive test in all
# three runs was untrustworthy. This was the only gate in the repo without an -m cap; the others all
# pass -m:1.
# RaskWasm / RaskSpaBuild off: this gate runs UNIT tests, and not one of them needs a published
# WebAssembly bundle — the browser E2E gate publishes those. The gate's real cost is elsewhere — the test
# run and `dotnet format --verify-no-changes` are about 90% of a warm run.
# shellcheck disable=SC2086  # empty unless set above
dotnet build Rask.slnx -c Release -m:"$lane_slots" \
  -p:RaskWasm=false -p:WasmBuildNative=false -p:MinVerSkip=true \
  -p:RaskSpaBuild=false $build_analyzers

fi
rask_phase "build (Release)"

# A tests part: the build above was the whole solution; from here on only this shard's projects run,
# and of those only the ones the scope reaches.
if [ "$unit_part" = "tests" ] && { [ -n "$test_shard" ] || [ -n "$tests_scope" ]; }; then
  part_tests="$(ls tests/*/*.csproj | grep -E '\.Tests/[^/]+\.csproj$' | grep -v '\.E2E\.Tests/' | LC_ALL=C sort)"
  if [ -n "$test_shard" ]; then
    part_tests="$(printf '%s\n' "$part_tests" | awk -v k="${test_shard%/*}" -v n="${test_shard#*/}" 'NR % n == k % n')"
  fi
  if [ -n "$tests_scope" ]; then
    part_tests="$(printf '%s\n' "$part_tests" | grep -xF "$tests_scope" || true)"
  fi
  if [ -z "$part_tests" ]; then
    skip_tests=1
    echo "==> Tests: the change reaches no test project in this part."
  else
    scope_projects="$part_tests"
    scope_changed="${scope_changed:-}"
    echo "==> Tests in this part: $(printf '%s\n' "$scope_projects" | grep -c .) project(s)"
  fi
fi

# Built ONLY on the paths that go on to run `dotnet format`, which is the only consumer: the formatter
# evaluates the solution in the DEFAULT configuration (Debug) and resolves the OutputItemType="Analyzer"
# project references from src/*.Generators/bin/DEBUG/, while this gate builds Release. See the header.
# Rask.Dom.Tasks too: Rask.Core loads its MDN emitter from bin/$(Configuration), and without it the
# workspace has no element types, so every chain that uses one fails to bind and RASK095 fires falsely.
#
# CONCURRENT, and safe to be: these projects have no ProjectReference at all, so there is no shared
# output for two builds to race over — they share only source-linked .cs files, which are read and
# never written. Each is pinned to -m:1 so three concurrent invocations cannot each claim the whole
# box; same reasoning as the -m cap on the solution build above, applied to parallelism that is ours
# rather than MSBuild's.
rask_build_debug_generators() {
  echo "==> Source generators + DOM emitter in Debug (dotnet format evaluates the default configuration)"
  gen_pids=""
  for proj in src/*.Generators/*.csproj src/Rask.Dom.Tasks/Rask.Dom.Tasks.csproj; do
    [ -e "$proj" ] || continue
    dotnet build "$proj" -c Debug -m:1 --nologo -v quiet &
    gen_pids="$gen_pids $!"
  done
  gen_failed=0
  for pid in $gen_pids; do
    wait "$pid" || gen_failed=1
  done
  if [ "$gen_failed" -ne 0 ]; then
    echo "run-unit-local: a Debug generator build FAILED — see above." >&2
    exit 1
  fi
}

# Scoped to the files being committed when the caller says so (RASK_FORMAT_SCOPE=staged|range), and the
# whole solution otherwise. Measured: 59s full, 30s scoped — the remaining 30s is solution load, which no
# scoping avoids.
#
# Sound rather than merely cheaper: dotnet format decides per DOCUMENT, so a file it is not shown is a
# file it would have had nothing to say about. And a tree cannot contain an unformatted committed file
# for this to miss, because that file would have had to pass this same gate on its own way in.
#
# The standalone gate keeps the full pass on purpose. It is the definition-of-done run, invoked with no
# commit in view, and "everything is formatted" is exactly the claim it exists to make.
#
# DECIDED BEFORE rask_build_debug_generators runs, which is the only reason that build is now
# conditional. A commit that stages no .cs at all — a docs page, a workflow, a .ts file — was paying
# for three Debug compilations whose entire purpose is to make `dotnet format` resolve its analyzers,
# and then skipping the formatter.
#
# NEITHER ARM PASSES --no-restore, and that is a correctness fix, not a missing optimisation. Both did
# until now, and `dotnet format Rask.slnx --verify-no-changes --no-restore` MODIFIED 57 files it was
# only asked to check, rewriting `using` directives across the repo. Without a restore the workspace
# cannot resolve the source generators; every generated symbol goes missing, the
# remove-unnecessary-imports analysis concludes those usings are dead, and it WRITES — which
# --verify-no-changes did not stop. The restore it now does is up to date from the solution build
# above, so this costs seconds and buys back a verify that cannot rewrite the tree it is verifying.
# Do not put the flag back to shave them off. (Also: check `git status` after any format run — a
# destructive pass and a clean one differ only in how many files moved, not in the exit code.)
# Run CONCURRENTLY with the test run below, rather than ahead of it.
#
# The two share nothing that either one writes. The Release solution build above has already produced
# everything `dotnet test --no-build` will load, and it loads it out of bin/Release; the formatter
# neither builds nor writes there — it restores (which it must; see the note above about what happens
# without it) and loads a Debug workspace to READ. So the only thing serialising them was the order
# they happened to be written in, and it cost the gate the whole formatter pass — measured at 57 s for
# the full tree, 30 s scoped to a commit — on top of a test run that leaves this 14-core box 90% idle.
#
# Both statuses are collected and BOTH are reported. Fail-fast on the formatter would be the cheaper
# shape, but it means a run that is red for formatting tells you nothing about whether your tests pass,
# so the next iteration is a second full gate to find out. One run now answers both questions.
#
# Output goes to a log and is replayed AFTER the tests, never interleaved: two concurrent dotnet
# processes writing the same terminal is how a real failure ends up spliced through 40 assemblies of
# test output and read as noise. $$ keeps it distinct from a gate running in another worktree, which
# shares this TMPDIR.
format_log="${TMPDIR:-/tmp}/rask-format-$$.log"
format_pid=""
format_label=""

rask_start_format() {
  case "$unit_part" in
    build|tests)
      echo "==> Formatting check: not in the '$unit_part' part — the format parts run it."
      return 0
      ;;
  esac
  # `range` is `staged` for commits that already exist: the files are the ones RASK_SCOPE_RANGE
  # changed. Same soundness argument as above, over the same range the tests are scoped to.
  if [ "${RASK_FORMAT_SCOPE:-}" = "staged" ] || { [ "${RASK_FORMAT_SCOPE:-}" = "range" ] && [ -n "${RASK_SCOPE_RANGE:-}" ]; }; then
    if [ "$RASK_FORMAT_SCOPE" = "range" ]; then
      staged_cs="$(git diff --name-only --diff-filter=ACMR "$RASK_SCOPE_RANGE" | grep -E '\.cs$' || true)"
    else
      # -z/-d so a path with a space or a newline in it cannot split into two arguments.
      staged_cs="$(git diff --cached --name-only --diff-filter=ACMR -z | tr '\0' '\n' | grep -E '\.cs$' || true)"
    fi

    if [ -z "$staged_cs" ]; then
      echo "==> Formatting check skipped — RASK_FORMAT_SCOPE=$RASK_FORMAT_SCOPE and no .cs changed."
      echo "    (and with it the Debug generator build, which exists only to serve the formatter)"
      return 0
    fi

    rask_build_debug_generators
    format_label="Formatting check (changed .cs files only — RASK_FORMAT_SCOPE=$RASK_FORMAT_SCOPE)"
    echo "==> $format_label — running alongside the tests"
    (
      set +e   # the seconds are written on a red run too
      format_began=$SECONDS
      # shellcheck disable=SC2086
      printf '%s\n' "$staged_cs" | tr '\n' ' ' | xargs dotnet format Rask.slnx --verify-no-changes --include
      format_exit=$?
      echo "$((SECONDS - format_began))" >"$format_log.secs"
      exit "$format_exit"
    ) >"$format_log" 2>&1 &
    format_pid=$!
    return 0
  fi

  rask_build_debug_generators
  format_label="Formatting check (dotnet format --verify-no-changes: whitespace + style + analyzers)"
  echo "==> $format_label — running alongside the tests"
  (
    set +e   # the seconds are written on a red run too
    format_began=$SECONDS
    # shellcheck disable=SC2086  # empty on a whole run, one path in a format part
    dotnet format Rask.slnx --verify-no-changes ${format_include:+--include $format_include}
    format_exit=$?
    echo "$((SECONDS - format_began))" >"$format_log.secs"
    exit "$format_exit"
  ) >"$format_log" 2>&1 &
  format_pid=$!
}

rask_start_format
rask_phase "generators in Debug (for the formatter)"

# The generated TypeScript is compiled by tsgo, which the test fetches itself as a checksum-verified
# binary at a pinned version, cached per user. So the type CHECK needs no node and always runs. Nothing
# to exclude any more: it used to be skipped when npx was absent, and a check whose first question is
# "is the tooling here?" is one that eventually answers no and stops running — which is how a generator
# emitting malformed TypeScript would ship green.
#
# The GATE still needs node, for the islands. RaskExternalBuild is left ON deliberately — the showcase
# carries a package.json, so the solution build runs npm and Vite for it, and that IS covered by unit
# tests.
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

# It stays `dotnet test Rask.slnx`, and NOT the 51 built DLLs handed to one vstest run. That looks like
# the obvious next saving — the browser gate already invokes its assembly directly, and a solution run
# re-evaluates 105 projects to discover 51 test assemblies — so here is the measurement, to stop it
# being tried a third time.
#
# It is SLOWER: 146s for the DLL list against 129s for the solution, same 51 assemblies, same box,
# back to back. MSBuild running eight test projects in parallel, each in its own testhost, beats one
# vstest process scheduling 51 assemblies at RunConfiguration.MaxCpuCount=8.
#
# And it is WRONG, which matters more. `MetadataUpdater.IsSupported` is a per-PROCESS feature switch
# read from the assembly's own runtimeconfig.json, and two projects set MetadataUpdaterSupport=true
# precisely because the SDK turns it off in Release — tests/Rask.Server.HotReload.Tests and
# tests/Rask.Wasm.Tests. A solution run gives each project its own testhost and so its own
# runtimeconfig; one vstest invocation over many DLLs shares testhosts, and the switch then belongs to
# whichever assembly booted the host. Six hot-reload tests failed, including the two named
# `The_feature_switch_is_on_in_this_assembly` — guards that exist so those files cannot pass vacuously
# with the switch off. They did their job on this experiment.

if [ -n "$unit_part" ] && [ "$unit_part" != "tests" ]; then
  echo "==> Tests: not in the '$unit_part' part — the 'tests' part runs them."
  unit_status=0
elif [ "$skip_tests" -eq 1 ]; then
  unit_status=0
elif [ -n "$scope_projects" ]; then
  # The affected TEST projects, handed to the same traversal shape as the build. VSTest is invoked
  # per project so each assembly keeps its own testhost and therefore its own runtimeconfig.json —
  # the MetadataUpdaterSupport point above applies here exactly as it does to the solution run, so
  # this must never collapse into one vstest invocation over several DLLs.
  # `*.Tests` selects the test projects; `*.E2E.Tests` is then taken back out, because that suffix
  # matches BOTH and an end-to-end suite is not part of a commit-time gate. The benchmarks need no
  # exclusion of their own — tests/Rask.Benchmarks* does not end in `.Tests` at all.
  scope_tests="$(printf '%s\n' $scope_projects | grep -E '\.Tests/[^/]+\.csproj$' | grep -v '\.E2E\.Tests/' || true)"

  if [ -z "$scope_tests" ]; then
    echo "==> No test project is reachable from the staged change — nothing to run."
    unit_status=0
  else
    echo "==> Unit & integration tests ($(printf '%s\n' "$scope_tests" | grep -c .) affected assembly/assemblies)"
    unit_status=0

    # A project that already passed on a tree nothing has reached it from since is not run again.
    # A second scoped run minutes later on the same tree would otherwise repeat the first in full.
    # scripts/lib/gate_stamps.py asks the scoper, so "reached" means exactly what it means for
    # the scope above; anything it cannot narrow is run. RASK_GATE_REUSE=0 runs everything.
    gate_tree=""
    gate_salt="$(dotnet --version 2>/dev/null)|Release"
    if [ "${RASK_GATE_REUSE:-1}" != "0" ]; then
      gate_tree="$(python3 "$root/scripts/lib/gate_stamps.py" tree "$root" 2>/dev/null || true)"
    fi
    if [ -n "$gate_tree" ]; then
      scope_reused="$(printf '%s\n' $scope_tests | python3 "$root/scripts/lib/gate_stamps.py" reuse "$root" "$gate_tree" "$gate_salt" || true)"
      if [ -n "$scope_reused" ]; then
        echo "==> Reused $(printf '%s\n' "$scope_reused" | grep -c .) pass(es) recorded on an unchanged tree (RASK_GATE_REUSE=0 to run them):"
        printf '        %s\n' $scope_reused
        scope_tests="$(printf '%s\n' $scope_tests | grep -vxF "$scope_reused" || true)"
      fi
    fi

    # All at once, and safe to be: --no-build means nothing here writes to bin/ or obj/, so the only
    # thing these share is the machine. Each
    # keeps its own `dotnet test` and so its own testhost and runtimeconfig — see the
    # MetadataUpdaterSupport note above.
    scope_pids=""
    scope_logs=""
    scope_ran=""
    for tp in $scope_tests; do
      scope_log="${TMPDIR:-/tmp}/rask-scoped-test-$$-$(basename "$(dirname "$tp")").log"

      (
        test_began=$SECONDS
        dotnet test "$root/$tp" -c Release --no-build -m:1 \
          --blame-crash \
          --results-directory "$root/artifacts/test-blame" \
          --logger "console;verbosity=normal"
        test_exit=$?
        echo "$((SECONDS - test_began))" >"$scope_log.secs"
        exit "$test_exit"
      ) >"$scope_log" 2>&1 &
      scope_pids="$scope_pids $!"
      scope_logs="$scope_logs $scope_log"
      scope_ran="$scope_ran $tp"
    done

    # Output is captured per assembly and replayed in order rather than interleaved.
    scope_passed=""
    # shellcheck disable=SC2086  # deliberate word split: the logs and projects line up with the pids above
    set -- $scope_logs
    for tp in $scope_ran; do
      scope_log="$1"; shift
      # shellcheck disable=SC2086
      scope_pid="$(printf '%s\n' $scope_pids | sed -n 1p)"
      scope_pids="$(printf '%s\n' $scope_pids | sed 1d | tr '\n' ' ')"
      if wait "$scope_pid"; then
        scope_passed="$scope_passed $tp"
      else
        unit_status=$?
      fi
      cat "$scope_log"
      scope_name="$(basename "$(dirname "$tp")")"
      scope_secs="$(cat "$scope_log.secs" 2>/dev/null || echo 0)"
      rask_alongside "$scope_secs" "$scope_name"
      rm -f "$scope_log" "$scope_log.secs"
    done

    if [ -n "$gate_tree" ] && [ -n "$scope_passed" ]; then
      # shellcheck disable=SC2086
      printf '%s\n' $scope_passed | python3 "$root/scripts/lib/gate_stamps.py" record "$root" "$gate_tree" "$gate_salt"
    fi
  fi
else
  # Excluded by the PROJECT-SHAPED suffix, not by one suite's name. Every end-to-end suite lives in a
  # `*.E2E.Tests` project and therefore a `*.E2E.Tests.*` namespace, so this one pattern covers all of
  # them — Rask.Site.E2E.Tests, Rask.Cli.E2E.Tests — and covers the next one without anybody
  # remembering to widen it. Naming a single suite here is how a newly-added E2E project silently
  # starts running inside the unit gate — which once cost it 14.9s of `dotnet publish` on every commit.
  dotnet test Rask.slnx -c Release --no-build -m:"$test_slots" \
    --filter "FullyQualifiedName!~.E2E.Tests.$tsc_filter" \
    --blame-crash \
    --results-directory "$root/artifacts/test-blame" \
    --logger "console;verbosity=normal"
  unit_status=$?
fi
rask_phase "tests"

# Collected before anything can exit, so the formatter's verdict is never lost to an early `exit` on
# the test status. A gate that starts a check and then leaves without reading it is a gate that has
# silently stopped running, which is the failure this repository keeps paying for.
format_status=0
if [ -n "$format_pid" ]; then
  wait "$format_pid" || format_status=$?
  rask_alongside "$(cat "$format_log.secs" 2>/dev/null || echo 0)" "dotnet format"
  rm -f "$format_log.secs"
  rask_phase "formatter, after the tests finished"
fi
set -e

if [ -n "$format_pid" ]; then
  if [ "$format_status" -eq 0 ]; then
    echo "==> $format_label passed."
    rm -f "$format_log"
  else
    {
      echo
      echo "run-unit-local: $format_label FAILED."
      echo
      cat "$format_log"
      echo
      echo "             Fix with 'dotnet format Rask.slnx' and restage. Note that a destructive"
      echo "             format pass and a clean one differ only in how many files moved, not in the"
      echo "             exit code — check 'git status' afterwards."
    } >&2
    rm -f "$format_log"
  fi
fi

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

# Reached only when the tests are green, so a formatting failure is the reason and says so on its own.
if [ "$format_status" -ne 0 ]; then
  echo "run-unit-local: the tests passed but the formatting check did not — see above." >&2
  exit "$format_status"
fi

echo "==> Format + unit gate passed."

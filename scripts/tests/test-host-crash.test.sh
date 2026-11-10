#!/usr/bin/env bash
# The gate's test runner must never call a run green when a test host died or tests went missing (#1308).
#
# A test that overflowed the stack made `dotnet test tests/Rask.Data.Tests` print `[FATAL ERROR]` and then
# `Passed! ... 707` — of 903. VSTest's exit code was 1, which only a reader who looked at it saw. Since then
# every gate runs tests through rask_dotnet_test (scripts/lib/dotnet-test.sh), which also reads the TRX files
# back through scripts/lib/test_verdict.py. This proves both halves:
#
#   1. a REAL suite (scripts/tests/fixtures/HostCrash.Tests, built as every tests/ suite is) whose one test
#      kills its host each way a host dies — stack overflow, FailFast, Environment.Exit(0), an exception on a
#      background thread, a hang — fails rask_dotnet_test, AND fails the verdict on its own, so the run is
#      red even where dotnet test's exit code is lost;
#   2. the verdict's rows on hand-written TRX files, for the cases a real host cannot be made to produce on
#      demand: fewer results than discovered with nothing else wrong, a missing discovery count, and the rest.
#
# It also pins the filter syntax: under VSTest, `--filter FullyQualifiedName~X` narrows a run, while
# xUnit's own `-- --filter-class X` is read as a RunSettings argument and silently runs everything.
#
# gate-inputs: tests/Directory\.Build\.props|^Directory\.Build\.(props|targets)$|^Directory\.Packages\.props$|^global\.json$
set -uo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
fixture="$root/scripts/tests/fixtures/HostCrash.Tests/HostCrash.Tests.csproj"
verdict="$root/scripts/lib/test_verdict.py"
# shellcheck source=../lib/dotnet-test.sh
. "$root/scripts/lib/dotnet-test.sh"

tmp="$(mktemp -d -t rask-host-crash-test.XXXXXX)"
# A hang timeout kills the testhost but, on macOS, leaves xUnit's test process behind; the fixture's hang
# is bounded, and this ends it sooner. The path is this checkout's own, so no other run is touched.
trap 'pkill -f "$(dirname "$fixture")/bin/" 2>/dev/null; rm -rf "$tmp"' EXIT

failures=0
checked=0

ok()   { checked=$((checked + 1)); printf '  ok   %s\n' "$1"; }
fail() { checked=$((checked + 1)); failures=$((failures + 1)); printf '  FAIL %s\n' "$1" >&2; }

# ---------------------------------------------------------------------------------------------------------
echo "==> test_verdict.py on hand-written TRX files"

# trx <name> <outcome> <results> <stdout> [runinfo-outcome runinfo-text]
trx() {
  local dir="$tmp/trx/$1"
  mkdir -p "$dir"
  local infos=""
  if [ -n "${5:-}" ]; then
    infos="<RunInfos><RunInfo computerName=\"x\" outcome=\"$5\" timestamp=\"2026-01-01T00:00:00\"><Text>$6</Text></RunInfo></RunInfos>"
  fi
  cat >"$dir/results.trx" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="1" name="x" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <ResultSummary outcome="$2">
    <Counters total="$3" executed="$3" passed="$3" failed="0" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" />
    <Output><StdOut>$4</StdOut></Output>
    $infos
  </ResultSummary>
</TestRun>
EOF
  printf '%s' "$dir"
}

# verdict_row <name> <expected 0|1> <dir> [verdict args...]
verdict_row() {
  local name="$1" expected="$2" dir="$3"; shift 3
  local actual=0
  python3 "$verdict" "$dir" "$@" >"$tmp/verdict.out" 2>&1 || actual=1
  if [ "$actual" = "$expected" ]; then
    ok "$name -> exit $actual"
  else
    fail "$name -> exit $actual (expected $expected)"
    sed 's/^/         /' "$tmp/verdict.out" >&2
  fi
}

found="[xUnit.net 00:00:00.31]   Discovered:  A.Tests (7 test cases to be run)"

verdict_row "every discovered test reported" 0 "$(trx all Completed 7 "$found")"
verdict_row "a theory reported more results than test cases" 0 "$(trx theory Completed 9 "$found")"
verdict_row "fewer results than discovered, nothing else wrong (the silent case)" 1 "$(trx short Completed 5 "$found")"
verdict_row "no discovery count to check against" 1 "$(trx uncounted Completed 7 "no diagnostic line")"
verdict_row "the run's outcome is Aborted" 1 "$(trx aborted Aborted 7 "$found")"
verdict_row "a FATAL ERROR logged, every result in" 1 \
  "$(trx fatal Completed 7 "$found" Error "[xUnit.net 00:01:00.72]     [FATAL ERROR] Xunit.Sdk.TestPipelineException")"
verdict_row "a crashed host logged only as a warning" 1 \
  "$(trx warned Warning 7 "$found" Warning "Test host process crashed")"
verdict_row "a harmless warning (a filter that matched nothing)" 0 \
  "$(trx nomatch Warning 7 "$found" Warning "No test matches the given testcase filter")"
verdict_row "an assembly filtered out on purpose, exempt from the count" 0 \
  "$(trx exempt Warning 0 "[xUnit.net 00:00:00.31]   Discovered:  A.E2E.Tests (40 test cases to be run)")" \
  --count-exempt '\.E2E\.Tests$'
verdict_row "the exemption does not reach another assembly" 1 \
  "$(trx notexempt Completed 0 "$found")" --count-exempt '\.E2E\.Tests$'
mkdir -p "$tmp/trx/empty"
verdict_row "a run that wrote no TRX file at all" 1 "$tmp/trx/empty"
mkdir -p "$tmp/trx/cut"
printf '<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSum' \
  >"$tmp/trx/cut/results.trx"
verdict_row "a TRX file cut off mid-write" 1 "$tmp/trx/cut"

# ---------------------------------------------------------------------------------------------------------
echo "==> A real suite whose host dies, run as the gate runs it"

if ! dotnet build "$fixture" -c Release -m:2 -nodeReuse:false -v q >"$tmp/build.log" 2>&1; then
  cat "$tmp/build.log" >&2
  echo "test-host-crash: the fixture did not build." >&2
  exit 1
fi

# Mini dumps, not full ones. The hang row's timeout is short enough to take seconds, not the gate's 15
# minutes; every other row's is longer than the minute the adapter waits on a dead test process, so
# those rows prove the crash path itself rather than the hang timeout cutting it short.
export RASK_TEST_DUMP_TYPE=mini

modes="none overflow failfast exit0 thread hang"
pids=""
# Side by side: --no-build means none of them writes to bin/ or obj/, and each has its own results
# directory. Serially the four crash rows alone would take four minutes — the adapter waits about a
# minute for a dead test process before it reports it.
for mode in $modes; do
  (
    status=0
    hang_timeout=5m
    [ "$mode" = hang ] && hang_timeout=15s
    HOST_CRASH_MODE="$mode" RASK_TEST_HANG_TIMEOUT="$hang_timeout" rask_dotnet_test "$tmp/run-$mode" -- "$fixture" -c Release --no-build -m:1 \
      >"$tmp/run-$mode.log" 2>&1 || status=$?
    echo "$status" >"$tmp/run-$mode.status"
  ) &
  pids="$pids $!"
done
# shellcheck disable=SC2086  # deliberate word split: one pid per word
wait $pids

for mode in $modes; do
  status="$(cat "$tmp/run-$mode.status" 2>/dev/null || echo missing)"
  verdict_status=0
  python3 "$verdict" "$tmp/run-$mode" >"$tmp/verdict-$mode.out" 2>&1 || verdict_status=1

  if [ "$mode" = none ]; then
    if [ "$status" = 0 ] && [ "$verdict_status" = 0 ]; then
      ok "a suite whose host lives passes (exit 0, verdict 0)"
    else
      fail "a suite whose host lives -> exit $status, verdict $verdict_status (expected 0 and 0)"
      tail -40 "$tmp/run-$mode.log" | sed 's/^/         /' >&2
    fi
  elif [ "$status" != 0 ] && [ "$status" != missing ] && [ "$verdict_status" = 1 ]; then
    ok "host killed by '$mode' fails the run (exit $status) and the verdict on its own"
  else
    fail "host killed by '$mode' -> exit $status, verdict $verdict_status (expected non-zero and 1)"
    grep -v '^   at ' "$tmp/run-$mode.log" | tail -40 | sed 's/^/         /' >&2
    sed 's/^/         /' "$tmp/verdict-$mode.out" >&2
  fi
done

# ---------------------------------------------------------------------------------------------------------
echo "==> The filter syntax a gate must use"

results_of() { grep -o 'total="[0-9]*"' "$1"/*/*.trx 2>/dev/null | head -1 | tr -dc '0-9'; }

status=0
rask_dotnet_test "$tmp/filtered" --count-exempt '.' -- "$fixture" -c Release --no-build -m:1 \
  --filter "FullyQualifiedName~SurvivorTests" >"$tmp/filtered.log" 2>&1 || status=$?
if [ "$status" = 0 ] && [ "$(results_of "$tmp/filtered")" = 3 ]; then
  ok "--filter FullyQualifiedName~SurvivorTests runs the three survivors only"
else
  fail "--filter FullyQualifiedName~SurvivorTests -> exit $status, $(results_of "$tmp/filtered") result(s) (expected 0, 3)"
fi

# The trap, kept visible: VSTest takes everything after `--` as RunSettings, so xUnit's native switch
# narrows nothing. Should this row ever start failing, the switch works and docs/development-workflow.md
# can say so.
dotnet test "$fixture" -c Release --no-build -m:1 --results-directory "$tmp/filter-class/HostCrash.Tests" \
  --logger "trx;LogFileName=results.trx" -- --filter-class HostCrash.Tests.SurvivorTests >"$tmp/filter-class.log" 2>&1
if [ "$(results_of "$tmp/filter-class")" = 4 ]; then
  ok "-- --filter-class is ignored under VSTest (all 4 ran) — use --filter"
else
  fail "-- --filter-class -> $(results_of "$tmp/filter-class") result(s) (expected the unfiltered 4)"
fi

echo
if [ "$failures" -ne 0 ]; then
  echo "test-host-crash: $failures of $checked check(s) failed." >&2
  exit 1
fi
echo "test-host-crash: all $checked checks passed."

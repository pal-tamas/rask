# shellcheck shell=bash
# The ONE way a gate runs `dotnet test` over test projects, and what it takes to call that run green.
#
#   rask_dotnet_test <results-dir> [--count-exempt ERE] -- <dotnet test arguments...>
#
# Returns dotnet test's own status when it failed, and 1 when it passed but scripts/lib/test_verdict.py
# finds the TRX files short: a host that died (stack overflow, FailFast, Environment.Exit), an aborted
# run, or fewer results than xUnit discovered test cases (#1308, where `Passed! ... 707` was printed
# for a suite of 903). Each test project writes its TRX under <results-dir>/<project>/ through
# RaskTestResults (tests/Directory.Build.props), so projects run side by side never share a file;
# <results-dir> is emptied first, so a stale file from an earlier run can never stand in for this one.
#
# --blame-crash names the test in flight when a host dies below the managed layer. --blame-hang-timeout
# turns a hung test into a failure that names it within minutes (5, mini dumps): no unit test runs that
# long, and one that legitimately does is one to split. xUnit.LongRunningTestSeconds prints every test
# still running after a minute, so a hang the blame collector cannot see (no test in flight) is named
# too. The whole run is bounded as well (scripts/lib/test_watch.py, RASK_TEST_RUN_TIMEOUT, 22 minutes):
# until 2026-11-25 a run the 15-minute hang timeout did not end ate the CI job's 40 minutes, and GitHub
# keeps no log of a job its timeout cancelled. RASK_TEST_HANG_TIMEOUT and RASK_TEST_DUMP_TYPE override
# both, as the gate's own self-test does (scripts/tests/test-host-crash.test.sh).
#
# FAIL FAST: the first failing test (or dead host) stops the run, after the few seconds its message and
# stack take to print — nobody waits for the rest of a run that is already red. RASK_TEST_FAIL_FAST=0
# runs everything, to see every failure at once.
#
# NOT for an E2E gate that runs a built DLL: `-p:` reaches MSBuild only, and those gates name their
# filter explicitly, which the discovery count does not see.

rask_dotnet_test() {
  local results="$1"; shift
  local exempt=""
  if [ "${1:-}" = "--count-exempt" ]; then
    exempt="$2"; shift 2
  fi
  [ "${1:-}" = "--" ] && shift

  local lib
  lib="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

  rm -rf "$results"
  mkdir -p "$results"

  local dump="${RASK_TEST_DUMP_TYPE:-mini}"
  local watch=(--deadline "$((${RASK_TEST_RUN_TIMEOUT:-22} * 60))")
  [ "${RASK_TEST_FAIL_FAST:-1}" = "0" ] || watch+=(--fail-fast)

  local status=0
  # The inline RunSettings after `--` must stay LAST: everything after it is a RunSettings key=value,
  # which is also why `-- --filter-class X` is silently ignored. xUnit.DiagnosticMessages makes the
  # adapter print the discovery count the verdict checks against, and the long-running lines.
  python3 "$lib/test_watch.py" "${watch[@]}" -- dotnet test "$@" \
    --blame-crash \
    --blame-crash-dump-type "$dump" \
    --blame-hang-timeout "${RASK_TEST_HANG_TIMEOUT:-5m}" \
    --blame-hang-dump-type "$dump" \
    -p:RaskTestResults="$results" \
    --logger "console;verbosity=normal" \
    --logger "trx;LogFileName=results.trx" \
    -- xUnit.DiagnosticMessages=true xUnit.LongRunningTestSeconds=60 || status=$?

  if [ -n "$exempt" ]; then
    python3 "$lib/test_verdict.py" "$results" --count-exempt "$exempt" || [ "$status" -ne 0 ] || status=1
  else
    python3 "$lib/test_verdict.py" "$results" || [ "$status" -ne 0 ] || status=1
  fi
  return "$status"
}

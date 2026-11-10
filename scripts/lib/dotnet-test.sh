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
# turns a hung test into a failure that names it, well inside the job's own timeout; a test that
# legitimately runs this long is one to split. RASK_TEST_HANG_TIMEOUT and RASK_TEST_DUMP_TYPE override
# both for the gate's own self-test (scripts/tests/test-host-crash.test.sh), which must not wait 15
# minutes or write a full dump per case.
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

  local dump_args=()
  if [ -n "${RASK_TEST_DUMP_TYPE:-}" ]; then
    dump_args=(--blame-crash-dump-type "$RASK_TEST_DUMP_TYPE" --blame-hang-dump-type "$RASK_TEST_DUMP_TYPE")
  fi

  local status=0
  # The inline `-- xUnit.DiagnosticMessages=true` RunSettings must stay LAST: everything after `--`
  # is a RunSettings key=value, which is also why `-- --filter-class X` is silently ignored. It makes
  # the adapter print the discovery count the verdict checks against.
  dotnet test "$@" \
    --blame-crash \
    --blame-hang-timeout "${RASK_TEST_HANG_TIMEOUT:-15m}" \
    ${dump_args[@]+"${dump_args[@]}"} \
    -p:RaskTestResults="$results" \
    --logger "console;verbosity=normal" \
    --logger "trx;LogFileName=results.trx" \
    -- xUnit.DiagnosticMessages=true || status=$?

  if [ -n "$exempt" ]; then
    python3 "$lib/test_verdict.py" "$results" --count-exempt "$exempt" || [ "$status" -ne 0 ] || status=1
  else
    python3 "$lib/test_verdict.py" "$results" || [ "$status" -ne 0 ] || status=1
  fi
  return "$status"
}

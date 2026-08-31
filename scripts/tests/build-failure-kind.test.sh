#!/usr/bin/env bash
# Table test for rask_build_failure_kind (scripts/lib/build-failure.sh).
#
# This is three predicates over two counts, and it decides whether a red gate tells you your branch is
# broken or your machine is busy. Getting it wrong is exactly the failure #718 reported — an hour lost to
# a scaffolder bug that did not exist — so every row is stated rather than left to the two cases someone
# happened to try. Same reasoning as BakeScopedAssetsTask.IsNodeReuseBakeFailure's table test (#690),
# which was also a handful of booleans that had already been wrong once with nothing objecting.
#
# Usage:  scripts/tests/build-failure-kind.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/build-failure.sh
. "$root/scripts/lib/build-failure.sh"

tmp="$(mktemp -d -t rask-build-failure-test.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

failures=0
checked=0

# assert <name> <expected-kind> <log-contents>
assert() {
  local name="$1" expected="$2" contents="$3"
  local log="$tmp/$(echo "$name" | tr -c 'A-Za-z0-9' '_').log"

  printf '%s' "$contents" > "$log"

  local actual
  actual="$(rask_build_failure_kind "$log")"
  checked=$((checked + 1))

  if [ "$actual" = "$expected" ]; then
    printf '  ok   %-52s -> %s\n' "$name" "$actual"
  else
    printf '  FAIL %-52s -> %s (expected %s)\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

echo "==> rask_build_failure_kind"

# The refusal, which used to classify as `unknown` — so the hook printed "look for a failing assertion,
# a timeout, or a host that exited early" directly beneath the guard's own "wait for the run above to
# finish", about a suite that had not started. Advice pointing at a test output that does not exist.
assert "the concurrency guard's refusal" busy \
"pre-push: running the local E2E gate (browser journeys).
run-e2e-local: another browser E2E gate is already running on this machine.
               pid 22845, running for 00:07: bash /repo/scripts/run-e2e-local.sh
"

# It must NOT fire on a log that merely quotes the phrase — a test asserting on the guard's own wording
# would otherwise make every red run look like contention.
assert "a test that quotes the refusal is not busy" unknown \
"  Failed AGuardTest.It_says_another_browser_E2E_gate_is_already_running [12 ms]
  Assert.Contains() Failure
"

# A real compile error still wins over a refusal line: if it got far enough to fail compiling, it ran.
assert "compile errors beat a quoted refusal" code \
"run-e2e-local: another browser E2E gate is already running on this machine.
/repo/src/A.cs(1,1): error CS1002: ; expected
"

# The case the gate always got right, and must keep getting right.
assert "compile errors alone" code \
  '/src/App/Program.cs(12,9): error CS0246: The type or namespace name '\''Foo'\'' could not be found
/src/App/Program.cs(14,5): error CS1002: ; expected
'

# The case from #718, in the shape it actually arrived: many NETSDK1147, not one CS.
assert "NETSDK1147 alone is the machine, not the branch" workload \
  '/proj/App.csproj : error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools
/proj/Other.csproj : error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools
'

# Both present: a workload problem does not excuse real compile errors, so the branch is still blamed.
assert "CS wins when both appear" code \
  '/proj/App.csproj : error NETSDK1147: To build this project, the following workloads must be installed: wasm-tools
/src/App/Program.cs(3,1): error CS0103: The name '\''Bar'\'' does not exist in the current context
'

# A different SDK failure is still not the branch, but it is not the wasm-tools story either.
assert "other NETSDK errors report as an SDK problem" sdk \
  '/proj/App.csproj : error NETSDK1045: The current .NET SDK does not support targeting .NET 99.0.
'

# A failing assertion is not a compile failure, and saying "it does not compile" would be wrong here too.
assert "a failing test is neither" unknown \
  'Failed Rask.Cli.Tests.CliBuildE2ETests.Scaffold_Compiles
  Assert.Equal() Failure: Values differ
Total tests: 27   Passed: 26   Failed: 1
'

# A build that failed before emitting any diagnostic (killed host, timeout, crashed node).
assert "empty log" unknown ''

# MSBuild writes warnings with the same shape; a warning must never be read as a failure kind.
assert "warnings only are not errors" unknown \
  '/proj/App.csproj : warning NETSDK1137: It is no longer necessary to use Microsoft.NET.Sdk.Web
/src/App/Program.cs(5,9): warning CS0168: The variable '\''e'\'' is declared but never used
'

# A path that does not exist must not crash the gate mid-failure.
echo "==> missing log file"
checked=$((checked + 1))
missing="$(rask_build_failure_kind "$tmp/does-not-exist.log")"
if [ "$missing" = "unknown" ]; then
  printf '  ok   %-52s -> %s\n' "missing log file" "$missing"
else
  printf '  FAIL %-52s -> %s (expected unknown)\n' "missing log file" "$missing" >&2
  failures=$((failures + 1))
fi

# No argument at all, same reason.
checked=$((checked + 1))
none="$(rask_build_failure_kind)"
if [ "$none" = "unknown" ]; then
  printf '  ok   %-52s -> %s\n' "no argument" "$none"
else
  printf '  FAIL %-52s -> %s (expected unknown)\n' "no argument" "$none" >&2
  failures=$((failures + 1))
fi

# The classification only matters through what it makes the gate SAY. The gates hand
# rask_explain_build_failure two messages — one for a broken branch, one for a gate that failed at
# something other than compiling — and the whole point of #718 is that neither reaches the operator when
# the machine is what broke. That is an easy thing to undo while tidying the wording, so it is asserted.
echo
echo "==> rask_explain_build_failure"

# assert_says <name> <kind> <yes|no: expect the code message> <yes|no: expect the other message>
assert_says() {
  local name="$1" kind="$2" want_code="$3" want_other="$4"
  local out
  out="$(rask_explain_build_failure "$kind" "gate" "CODE-MESSAGE" "OTHER-MESSAGE" 2>&1 || true)"

  local ok=1
  case "$want_code" in
    yes) case "$out" in *CODE-MESSAGE*) ;; *) ok=0 ;; esac ;;
    no)  case "$out" in *CODE-MESSAGE*) ok=0 ;; esac ;;
  esac
  case "$want_other" in
    yes) case "$out" in *OTHER-MESSAGE*) ;; *) ok=0 ;; esac ;;
    no)  case "$out" in *OTHER-MESSAGE*) ok=0 ;; esac ;;
  esac

  checked=$((checked + 1))
  if [ "$ok" -eq 1 ]; then
    printf '  ok   %-52s\n' "$name"
  else
    printf '  FAIL %-52s\n%s\n' "$name" "$out" >&2
    failures=$((failures + 1))
  fi
}

assert_says "busy says nothing ran, not that a test failed"  busy no no
assert_says "code says the branch is broken"          code     yes no
assert_says "workload blames neither the branch nor the gate" workload no no
assert_says "sdk blames neither the branch nor the gate"      sdk      no no
assert_says "unknown says what the gate does, not that it compiled" unknown no yes

echo
if [ "$failures" -ne 0 ]; then
  echo "build-failure-kind: $failures of $checked checks FAILED." >&2
  exit 1
fi

echo "build-failure-kind: $checked checks passed."

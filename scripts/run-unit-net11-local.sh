#!/usr/bin/env bash
# The unit suite on .NET 11: the same gate as scripts/run-unit-local.sh, with every test project built and
# run for net11.0 instead of the primary net10.0.
#
# Every shipped package builds for both versions (RaskNetTargets in Directory.Build.props), so the compile,
# the analyzers and the public-API gate already cover net11.0 on every commit. What only a RUN can show is
# behaviour: a test that passes on the .NET 10 runtime and fails on 11. That run rebuilds the whole test
# graph for another framework, which the hooks' one-minute budget cannot carry, so it lives here and in
# scripts/run-all-gates.sh.
#
# HOW: test projects name their framework as $(RaskTestTarget) (tests/Directory.Build.props), and MSBuild
# reads an environment variable of that name as the property. Exporting it therefore reaches every
# `dotnet build` and `dotnet test` run-unit-local.sh makes without a second copy of that script's logic —
# and the gate still appears in `ps` as run-unit-local.sh, which is the only way scripts/lib/machine-lane.sh
# can count its slots. A script of its own would run invisible to every other gate on the box. A global
# -p:TargetFramework would not work either: it flows into the netstandard2.0 generators and tasks too.
#
# NOT VACUOUS: a run that quietly stayed on net10.0 would be green and prove nothing, so every test
# assembly's summary line is read back — each must say (net11.0), none may say (net10.0).
#
# Cost worth knowing: the test projects' restore graphs are left on net11.0, so the next ordinary unit gate
# restores them again.
#
# Usage: scripts/run-unit-net11-local.sh
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
log="$(mktemp "${TMPDIR:-/tmp}/rask-unit-net11.XXXXXX")"
trap 'rm -f "$log"' EXIT

# Command-scoped, never `export`. An exported RaskTestTarget outlives this gate in the same shell, and the two
# sibling gates hard-code the primary framework's output path — scripts/run-benchmarks-local.sh (bin/Release/net10.0)
# and scripts/run-e2e-local.sh (bin/Release/net10.0) — so the build would write net11.0/ while they ran whatever
# net10 binary an earlier run left behind: green, and measuring the wrong build.
echo "run-unit-net11-local: running the unit gate with RaskTestTarget=net11.0"
RaskTestTarget=net11.0 bash "$root/scripts/run-unit-local.sh" 2>&1 | tee "$log"
status=${PIPESTATUS[0]}

# Read BEFORE deciding on the status, so a failing run still reports which framework it failed on. Counted from
# VSTest's per-assembly header — `Test run for …/X.Tests.dll (.NETCoreApp,Version=v11.0)` — which every assembly
# prints whatever logger the gate passes. The `Passed! … X.dll (net11.0)` summary line is NOT printed under the
# gate's `console;verbosity=normal` logger: matching it reported zero assemblies for a run of fifty-nine.
# Unanchored, because the log keeps the console's colour escapes.
on_11="$(grep -cE 'Test run for .*\.dll \(\.NETCoreApp,Version=v11\.0\)' "$log" || true)"
on_10="$(grep -cE 'Test run for .*\.dll \(\.NETCoreApp,Version=v10\.0\)' "$log" || true)"

if [ "$status" -ne 0 ]; then
  echo "run-unit-net11-local: the unit gate failed on .NET 11 (exit $status; $on_11 assemblies reported on net11.0)." >&2
  exit "$status"
fi

if [ "$on_10" -ne 0 ]; then
  echo "run-unit-net11-local: $on_10 test assembly/assemblies ran on net10.0 — RaskTestTarget did not reach them, so this run did not test .NET 11." >&2
  exit 1
fi

# A full run reports about fifty test assemblies. The floor leaves room for a few being retired and is far
# above the zero a run that tested nothing would print.
if [ "$on_11" -lt 40 ]; then
  echo "run-unit-net11-local: only $on_11 test assembly/assemblies reported running on net11.0, where a full run reports about fifty. It did not test what it claims." >&2
  exit 1
fi

echo "run-unit-net11-local: $on_11 test assemblies passed on .NET 11."

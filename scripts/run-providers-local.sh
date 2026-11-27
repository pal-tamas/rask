#!/usr/bin/env bash
# Provider gate — the provider suites (tests/Rask.Providers.E2E.Tests) against real SQL Server and PostgreSQL servers.
#
# Everything else about the database runs on SQLite. What only a server can prove is what Rask writes or translates
# per provider: collations, row and named locks, sequences, the DDL the conventions write, decimal scale, constraint
# violations, the jobs claim, session settings, bulk insert, and migrations' provider SQL.
#
# The servers are the suite's own: Testcontainers starts one SQL Server and two PostgreSQL servers (the newest and the
# floor) per run, on first use, and stops them at the end (tests/Rask.Providers.E2E.Tests/ProviderServer.cs). In CI
# (CI / GITHUB_ACTIONS set) that just happens; on a dev machine the server facts report SKIPPED unless
# RASK_PROVIDERS_LOCAL=1 says Docker may be used, so this runs the SQLite legs alone there by default. There is no
# hand-started server to point a leg at: a container or nothing. SQL Server's image is amd64-only (it segfaults under
# emulation on Apple Silicon).
#
# Run by CI on every push that reaches the data stack (.github/workflows/gates.yml, "providers") and in the daily
# full run; registered in scripts/run-all-gates.sh.
#
# Usage:  scripts/run-providers-local.sh
#         RASK_PROVIDERS_LOCAL=1 scripts/run-providers-local.sh    # start the containers here too (needs Docker)
# Skip:   RASK_SKIP_PROVIDERS_E2E=1
set -euo pipefail

if [ "${RASK_SKIP_PROVIDERS_E2E:-}" = "1" ]; then
  echo "run-providers-local: RASK_SKIP_PROVIDERS_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
# shellcheck source=lib/dotnet-env.sh
. "$root/scripts/lib/dotnet-env.sh"
cd "$root"

# Three processes, not one: EF Core refuses a process its twenty-first internal service provider
# (ManyServiceProvidersCreatedWarning), each distinct options shape is one, and SQLite, PostgreSQL and SQL Server
# together are past twenty. So the SQL Server tests (a SqlServer class, or a sql_server fact) run in a process of their
# own that may use only SQL Server, and everything else in one that may use only PostgreSQL (RASK_PROVIDERS_SERVERS) —
# twice: on the newest PostgreSQL, and on the oldest Rask supports (RASK_PROVIDERS_POSTGRES=floor, docs/data.md).
# The suite is built once; the three legs then run --no-build on that output side by side, each against its own
# container, each into a log of its own printed once all are done. A failing leg never stops another: every server's
# conformance is reported on every run, and the gate fails at the end if any leg did.
# The split is by NAME, so a fact that is not SQL Server's never says "sql_server" (not even "postgresql_server").
suite=tests/Rask.Providers.E2E.Tests/Rask.Providers.E2E.Tests.csproj
sql_server_tests="FullyQualifiedName~SqlServer|FullyQualifiedName~sql_server"
other_tests="FullyQualifiedName!~SqlServer&FullyQualifiedName!~sql_server"

echo "==> Building the provider suite"
dotnet build "$suite" -c Release

logs="$(mktemp -d -t rask-providers.XXXXXX)"
trap 'rm -rf "$logs"' EXIT
pids=()
names=()
# `leg NAME FILTER VAR=VALUE…` starts one process of the suite in the background, its output in $logs/NAME.
leg() {
  local name="$1" filter="$2"
  shift 2
  env "$@" dotnet test "$suite" -c Release --no-build --filter "$filter" --logger "console;verbosity=normal" \
    >"$logs/$name" 2>&1 &
  pids+=("$!")
  names+=("$name")
}
leg "SQLite, PostgreSQL (newest)" "$other_tests" RASK_PROVIDERS_SERVERS=postgres RASK_PROVIDERS_POSTGRES=newest
leg "SQLite, PostgreSQL (floor)" "$other_tests" RASK_PROVIDERS_SERVERS=postgres RASK_PROVIDERS_POSTGRES=floor
leg "SQL Server" "$sql_server_tests" RASK_PROVIDERS_SERVERS=sqlserver

failed=()
for i in "${!pids[@]}"; do
  wait "${pids[$i]}" || failed+=("${names[$i]}")
  echo "==> Provider tests: ${names[$i]}"
  cat "$logs/${names[$i]}"
done
if [ "${#failed[@]}" -gt 0 ]; then
  printf 'run-providers-local: failed: %s\n' "${failed[@]}" >&2
  exit 1
fi

if [ -n "${CI:-}${GITHUB_ACTIONS:-}" ] || [ "${RASK_PROVIDERS_LOCAL:-}" = "1" ]; then
  echo "==> Provider gate passed (SQLite, the newest PostgreSQL and its floor, and SQL Server where this host runs it)."
else
  echo "==> SQLite legs passed. The SQL Server and PostgreSQL legs were SKIPPED: they run in CI on Testcontainers;"
  echo "    set RASK_PROVIDERS_LOCAL=1 to run them here (needs Docker)."
fi

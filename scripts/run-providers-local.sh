#!/usr/bin/env bash
# Provider gate — the provider suites (tests/Rask.Providers.E2E.Tests) against real SQL Server and PostgreSQL servers.
#
# Everything else about the database runs on SQLite. What only a server can prove is what Rask writes or translates
# per provider: collations, row and named locks, sequences, the DDL the conventions write, decimal scale, constraint
# violations, the jobs claim, session settings, bulk insert, and migrations' provider SQL.
#
# The servers are the suite's own: Testcontainers starts one SQL Server and one PostgreSQL per run, on first use, and
# stops them at the end (tests/Rask.Providers.E2E.Tests/ProviderServer.cs). In CI (CI / GITHUB_ACTIONS set) that just
# happens; on a dev machine the server facts report SKIPPED unless RASK_PROVIDERS_LOCAL=1 says Docker may be used, so
# this runs the SQLite legs alone there by default. There is no hand-started server to point a leg at: a container or
# nothing. SQL Server's image is amd64-only (it segfaults under emulation on Apple Silicon).
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

# Two processes, not one: EF Core refuses a process its twenty-first internal service provider
# (ManyServiceProvidersCreatedWarning), each distinct options shape is one, and SQLite, PostgreSQL and SQL Server
# together are past twenty. So the SQL Server tests (a SqlServer class, or a sql_server fact) run in a process of their
# own that may use only SQL Server, and everything else in one that may use only PostgreSQL (RASK_PROVIDERS_SERVERS).
# Serial, because the second process would fight the first over obj/ — it runs --no-build on the first's output.
# The split is by NAME, so a fact that is not SQL Server's never says "sql_server" (not even "postgresql_server").
suite=tests/Rask.Providers.E2E.Tests/Rask.Providers.E2E.Tests.csproj
sql_server_tests="FullyQualifiedName~SqlServer|FullyQualifiedName~sql_server"
other_tests="FullyQualifiedName!~SqlServer&FullyQualifiedName!~sql_server"

echo "==> Provider tests: SQLite, PostgreSQL"
RASK_PROVIDERS_SERVERS=postgres dotnet test "$suite" -c Release --filter "$other_tests" \
  --logger "console;verbosity=normal"

echo "==> Provider tests: SQL Server"
RASK_PROVIDERS_SERVERS=sqlserver dotnet test "$suite" -c Release --no-build --filter "$sql_server_tests" \
  --logger "console;verbosity=normal"

if [ -n "${CI:-}${GITHUB_ACTIONS:-}" ] || [ "${RASK_PROVIDERS_LOCAL:-}" = "1" ]; then
  echo "==> Provider gate passed (SQLite, PostgreSQL and SQL Server where this host runs it)."
else
  echo "==> SQLite legs passed. The SQL Server and PostgreSQL legs were SKIPPED: they run in CI on Testcontainers;"
  echo "    set RASK_PROVIDERS_LOCAL=1 to run them here (needs Docker)."
fi

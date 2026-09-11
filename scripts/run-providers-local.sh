#!/usr/bin/env bash
# Provider gate — do Rask's batteries hold on a real client-server database?
#
# Everything else about the database runs on SQLite. What only a server can prove is the provider half:
# that the jobs claim's UPDATE re-evaluates its predicate against the row version the winner committed, that
# the session settings survive the pool's reset, that bulk insert spells its SQL the provider's way, and that
# the cache's insert-then-update survives a server that aborts a transaction at its first error.
#
# Deliberately NOT part of scripts/run-unit-local.sh: that one is both git hooks, and requiring a Docker
# daemon on every commit is how a gate ends up permanently skipped. The suite is a *.E2E.Tests project, so the
# unit gate excludes it by name; this script is what runs it. Registered in scripts/run-all-gates.sh.
#
# Requirements: a `docker` CLI and a running daemon. Every fact whose server is not reachable reports SKIPPED,
# never PASSED.
#
# Usage:  scripts/run-providers-local.sh
# Skip:   RASK_SKIP_PROVIDERS_E2E=1
set -euo pipefail

if [ "${RASK_SKIP_PROVIDERS_E2E:-}" = "1" ]; then
  echo "run-providers-local: RASK_SKIP_PROVIDERS_E2E=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

if ! docker version >/dev/null 2>&1; then
  echo "run-providers-local: no usable Docker daemon — the provider gate needs one to start its servers." >&2
  echo "                     Start Docker, or set RASK_SKIP_PROVIDERS_E2E=1 to bypass." >&2
  exit 1
fi

pg_container="rask-providers-pg-$$"
# Empty lets Docker pick a free host port, so two worktrees can run the gate at once. Set it to pin one.
pg_port="${RASK_PG_PORT:-}"
pg_password="rask-test"

mssql_container="rask-providers-mssql-$$"
mssql_password="Rask-test-1234"

cleanup() {
  docker rm -f "$pg_container" >/dev/null 2>&1 || true
  docker rm -f "$mssql_container" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Starting PostgreSQL 17 ($pg_container)"
docker run -d --rm --name "$pg_container" \
  -e POSTGRES_PASSWORD="$pg_password" \
  -e POSTGRES_DB=rask \
  -p "127.0.0.1:$pg_port:5432" \
  postgres:17-alpine >/dev/null

pg_port="$(docker port "$pg_container" 5432/tcp | head -n 1 | sed 's/.*://')"
echo "    listening on 127.0.0.1:$pg_port"

echo "==> Waiting for it to accept connections"
pg_ready=0
for _ in $(seq 1 60); do
  # Over TCP, not the Unix socket: the image's first-boot init server answers on the socket and then stops to
  # restart as the real one, so a socket probe can pass moments before connections are refused.
  if docker exec "$pg_container" pg_isready -h 127.0.0.1 -U postgres -d rask >/dev/null 2>&1; then
    pg_ready=1
    break
  fi
  sleep 1
done

if [ "$pg_ready" = "1" ]; then
  export RASK_PG_TEST_DB="Host=127.0.0.1;Port=$pg_port;Database=rask;Username=postgres;Password=$pg_password"
else
  # Left unset: the PostgreSQL facts then report SKIPPED rather than failing, and the summary below says so.
  # A gate that goes red because the host cannot run an engine teaches people to ignore it.
  echo "    WARNING: PostgreSQL did not become ready in 60s — its tests will report SKIPPED, not pass." >&2
  docker logs "$pg_container" 2>&1 | tail -5 >&2 || true
fi

# SQL Server. Microsoft publishes mssql/server for amd64 only, and on Apple Silicon it SEGFAULTS under emulation
# (exit 139 from launch_sqlservr.sh, confirmed again 2026-09-11). So it is started only on an amd64 host; anywhere
# else, point RASK_MSSQL_TEST_DB at a server you have, or accept that SQL Server is not proven by this run — which
# the summary says out loud rather than counting as a pass.
mssql_state="not-proven"
arch="$(uname -m)"
if [ -n "${RASK_MSSQL_TEST_DB:-}" ]; then
  echo "==> SQL Server: using RASK_MSSQL_TEST_DB from the environment"
  mssql_state="external"
elif [ "$arch" = "x86_64" ] || [ "$arch" = "amd64" ]; then
  echo "==> Starting SQL Server 2022 ($mssql_container)"
  docker run -d --name "$mssql_container" \
    -e ACCEPT_EULA=Y \
    -e "MSSQL_SA_PASSWORD=$mssql_password" \
    -p "127.0.0.1::1433" \
    mcr.microsoft.com/mssql/server:2022-latest >/dev/null
  mssql_port="$(docker port "$mssql_container" 1433/tcp | head -n 1 | sed 's/.*://')"

  echo "==> Waiting for it to accept logins (127.0.0.1:$mssql_port)"
  mssql_state="failed"
  for _ in $(seq 1 90); do
    # A real login, not the "ready for client connections" log line. That line is written before master and msdb
    # finish recovery and the first-start upgrade scripts run, and a login then fails with 18456 (script upgrade
    # mode), which no retry strategy treats as transient. Matching the log with `grep -q` under pipefail also
    # misreads a match as a miss whenever `docker logs` takes the SIGPIPE.
    if docker exec "$mssql_container" /opt/mssql-tools18/bin/sqlcmd -S 127.0.0.1 -U sa -P "$mssql_password" -C -b -Q "SELECT 1" >/dev/null 2>&1; then
      mssql_state="started"
      break
    fi
    sleep 2
  done

  if [ "$mssql_state" = "started" ]; then
    export RASK_MSSQL_TEST_DB="Server=127.0.0.1,$mssql_port;Database=master;User Id=sa;Password=$mssql_password;TrustServerCertificate=true"
  else
    echo "    WARNING: SQL Server did not become ready — its tests will report SKIPPED." >&2
    docker logs "$mssql_container" 2>&1 | tail -5 >&2 || true
  fi
else
  echo "==> SQL Server: NOT STARTED on this $arch host (the amd64 image segfaults under emulation)."
  echo "    Its tests report SKIPPED. Run this gate on amd64, or set RASK_MSSQL_TEST_DB, to prove it."
fi

echo "==> Provider tests"
dotnet test tests/Rask.Providers.E2E.Tests/Rask.Providers.E2E.Tests.csproj -c Release \
  --logger "console;verbosity=normal"

if [ "$pg_ready" != "1" ]; then
  echo "run-providers-local: FINISHED WITH SKIPS — PostgreSQL never became ready, so nothing was proven." >&2
  exit 1
fi

if [ "$mssql_state" = "failed" ]; then
  echo "run-providers-local: FINISHED WITH SKIPS — SQL Server was started here and never became ready." >&2
  exit 1
fi

if [ "$mssql_state" = "not-proven" ]; then
  echo "==> Provider gate passed for PostgreSQL. SQL Server was NOT proven on this host."
else
  echo "==> Provider gate passed (PostgreSQL and SQL Server)."
fi

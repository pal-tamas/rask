#!/usr/bin/env bash
# The provider gate: run it for any change to the jobs/mail/outbox claim, or to provider registration
# (the rask-ship skill does this for you).
#
# It is deliberately NOT part of scripts/run-unit-local.sh: that one is the pre-commit hook, and requiring a
# Docker daemon on every commit is how a gate ends up permanently RASK_SKIP'd — the same reasoning as
# run-sqlite-load-local.sh.
#
# What it proves that nothing else can: the claim's portability rests on PostgreSQL re-evaluating an UPDATE
# predicate against the row version the winner committed. That is a claim about the server, and only a real
# server can settle it. Everything else about leasing is covered deterministically on SQLite in
# tests/Rask.Jobs.Tests/JobLeaseTests.cs.
set -euo pipefail

if [[ "${RASK_SKIP_PROVIDERS:-0}" == "1" ]]; then
  echo "run-providers-local: skipped (RASK_SKIP_PROVIDERS=1)."
  exit 0
fi

if ! docker version >/dev/null 2>&1; then
  echo "run-providers-local: no usable Docker daemon — the provider gate needs one to start PostgreSQL." >&2
  echo "                     Start Docker, or set RASK_SKIP_PROVIDERS=1 to skip deliberately." >&2
  exit 1
fi

CONTAINER="rask-providers-pg-$$"
PORT="${RASK_PG_PORT:-55432}"
PASSWORD="rask-test"

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
  docker rm -f "${MSSQL_CONTAINER:-}" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "==> Starting PostgreSQL ($CONTAINER on :$PORT)"
docker run -d --rm --name "$CONTAINER" \
  -e POSTGRES_PASSWORD="$PASSWORD" \
  -e POSTGRES_DB=rask \
  -p "$PORT:5432" \
  postgres:17-alpine >/dev/null

echo "==> Waiting for it to accept connections"
for _ in $(seq 1 60); do
  if docker exec "$CONTAINER" pg_isready -U postgres -d rask >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

if ! docker exec "$CONTAINER" pg_isready -U postgres -d rask >/dev/null 2>&1; then
  echo "run-providers-local: PostgreSQL did not become ready in 60s." >&2
  docker logs "$CONTAINER" >&2 || true
  exit 1
fi

export RASK_PG_TEST_DB="Host=localhost;Port=$PORT;Database=rask;Username=postgres;Password=$PASSWORD"

# SQL Server. Microsoft publishes mssql/server for amd64 only, and it SEGFAULTS under emulation on Apple
# Silicon (exit 139 from launch_sqlservr.sh), so on arm64 the gate falls back to Azure SQL Edge — the same
# engine, a feature subset, and enough to settle how an UPDATE predicate is re-evaluated under contention.
# That fallback is a real difference, not a formality: full SQL Server coverage needs an amd64 host.
#
# Azure SQL Edge is retired by Microsoft; if it stops being pullable, run this gate on amd64 instead of
# reaching for another substitute.
MSSQL_PORT="${RASK_MSSQL_PORT:-51433}"
MSSQL_PASSWORD="Rask-test-1234"
MSSQL_CONTAINER="rask-providers-mssql-$$"

if [[ "$(uname -m)" == "arm64" || "$(uname -m)" == "aarch64" ]]; then
  MSSQL_IMAGE="mcr.microsoft.com/azure-sql-edge:latest"
  MSSQL_EULA_ENV="ACCEPT_EULA=1"
  echo "==> Starting Azure SQL Edge ($MSSQL_CONTAINER on :$MSSQL_PORT)"
  echo "    NOTE: arm64 host — this is the SQL Server engine, not the full SQL Server image."
else
  MSSQL_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
  MSSQL_EULA_ENV="ACCEPT_EULA=Y"
  echo "==> Starting SQL Server ($MSSQL_CONTAINER on :$MSSQL_PORT)"
fi

docker run -d --rm --name "$MSSQL_CONTAINER" \
  -e "$MSSQL_EULA_ENV" \
  -e "MSSQL_SA_PASSWORD=$MSSQL_PASSWORD" \
  -p "$MSSQL_PORT:1433" \
  "$MSSQL_IMAGE" >/dev/null

echo "==> Waiting for it to accept connections"
# Probed from the host over TCP rather than with sqlcmd in the container: Azure SQL Edge ships no
# mssql-tools, so an exec-based probe silently never succeeds and the tests quietly skip.
MSSQL_READY=0
for _ in $(seq 1 120); do
  if (exec 3<>"/dev/tcp/127.0.0.1/$MSSQL_PORT") 2>/dev/null; then
    exec 3<&- 3>&-
    MSSQL_READY=1
    break
  fi
  sleep 2
done

if [[ "$MSSQL_READY" == "1" ]]; then
  # The listener accepts before the engine finishes recovery; the tests create their own database, so give
  # it a moment rather than racing the first login.
  sleep 10
  export RASK_MSSQL_TEST_DB="Server=localhost,$MSSQL_PORT;Database=master;User Id=sa;Password=$MSSQL_PASSWORD;TrustServerCertificate=true"
else
  # Left unset: the SQL Server facts then report SKIPPED rather than failing, which is honest — a gate that
  # goes red because the host cannot run an engine teaches people to ignore it.
  echo "    WARNING: SQL Server did not become ready — its tests will report SKIPPED, not pass." >&2
  docker logs "$MSSQL_CONTAINER" 2>&1 | tail -5 >&2 || true
fi

echo "==> Provider tests"
dotnet test tests/Rask.Providers.Tests/Rask.Providers.Tests.csproj -c Release \
  --logger "console;verbosity=normal"

echo "==> Provider gate passed."

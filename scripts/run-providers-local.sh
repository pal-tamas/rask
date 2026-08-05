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

echo "==> Provider tests"
dotnet test tests/Rask.Providers.Tests/Rask.Providers.Tests.csproj -c Release \
  --logger "console;verbosity=normal"

echo "==> Provider gate passed."

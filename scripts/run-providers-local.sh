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

cleanup() {
  docker rm -f "$pg_container" >/dev/null 2>&1 || true
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

echo "==> Provider tests"
dotnet test tests/Rask.Providers.E2E.Tests/Rask.Providers.E2E.Tests.csproj -c Release \
  --logger "console;verbosity=normal"

if [ "$pg_ready" != "1" ]; then
  echo "run-providers-local: FINISHED WITH SKIPS — PostgreSQL never became ready, so nothing was proven." >&2
  exit 1
fi

echo "==> Provider gate passed."

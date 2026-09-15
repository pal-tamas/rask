#!/usr/bin/env bash
# Bulk insert on a client-server database, measured at a realistic round-trip time (#1063).
#
# A PostgreSQL container on the same machine answers in microseconds, which hides the one cost that decides the
# write shape on a real server: round trips. This starts PostgreSQL 17 with `tc netem` delaying the container's
# egress, then runs PostgresBulkInsertBenchmarks against it. Benchmarks run only on request, never in a hook.
#
# Requirements: a `docker` CLI and a running daemon (the container needs NET_ADMIN for tc).
#
# Usage:  scripts/run-bulk-insert-benchmarks-local.sh [extra BenchmarkDotNet args]
#         RASK_PG_NETEM_DELAY=5ms scripts/run-bulk-insert-benchmarks-local.sh   (default 1ms, a same-region server)
#         RASK_BENCH_FILTER='*PostgresBulkInsertBenchmarks.DbBatch*' scripts/run-bulk-insert-benchmarks-local.sh --iterationCount 15
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

delay="${RASK_PG_NETEM_DELAY:-1ms}"
container="rask-bulk-bench-pg-$$"
password="rask-bench"

if ! docker version >/dev/null 2>&1; then
  echo "run-bulk-insert-benchmarks-local: no usable Docker daemon." >&2
  exit 1
fi

trap 'docker rm -f "$container" >/dev/null 2>&1 || true' EXIT

echo "==> Starting PostgreSQL 17 ($container) with ${delay} of egress delay"
docker run -d --rm --name "$container" --cap-add NET_ADMIN \
  -e POSTGRES_PASSWORD="$password" -e POSTGRES_DB=rask \
  -p "127.0.0.1::5432" postgres:17-alpine >/dev/null
port="$(docker port "$container" 5432/tcp | head -n 1 | sed 's/.*://')"

ready=0
for _ in $(seq 1 60); do
  if docker exec "$container" pg_isready -h 127.0.0.1 -U postgres -d rask >/dev/null 2>&1; then
    ready=1
    break
  fi
  sleep 1
done
if [ "$ready" != "1" ]; then
  echo "PostgreSQL did not become ready in 60s." >&2
  exit 1
fi

docker exec "$container" sh -c "apk add --no-cache iproute2 >/dev/null && tc qdisc add dev eth0 root netem delay $delay"
echo "    listening on 127.0.0.1:$port, netem: $(docker exec "$container" tc qdisc show dev eth0 | head -n 1)"

export RASK_PG_BENCH_DB="Host=127.0.0.1;Port=$port;Database=rask;Username=postgres;Password=$password"
dotnet run -c Release --project tests/Rask.Benchmarks/Rask.Benchmarks.csproj -- \
  --filter "${RASK_BENCH_FILTER:-*PostgresBulkInsert*}" --artifacts artifacts/bench/postgres-bulk-insert "$@"

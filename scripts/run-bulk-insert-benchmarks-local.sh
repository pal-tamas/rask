#!/usr/bin/env bash
# Bulk insert on a client-server database, measured at a realistic round-trip time (#1063).
#
# A PostgreSQL container on the same machine answers in microseconds, which hides the one cost that decides the
# write shape on a real server: round trips. The benchmark project's `postgres-bulk-insert` subcommand
# (tests/Rask.Benchmarks/PostgresBulkInsertReport.cs) starts PostgreSQL on Testcontainers with `tc netem` delaying the
# container's egress, runs PostgresBulkInsertBenchmarks against it and stops it. Benchmarks run only on request, never
# in a hook. Like the provider suite, it starts a container only in CI or where RASK_PROVIDERS_LOCAL=1 says Docker
# may be used; there is no hand-started server to point it at.
#
# Usage:  RASK_PROVIDERS_LOCAL=1 scripts/run-bulk-insert-benchmarks-local.sh [extra BenchmarkDotNet args]
#         RASK_PG_NETEM_DELAY=5ms ...   (default 1ms, a same-region server)
#         RASK_BENCH_FILTER='*PostgresBulkInsertBenchmarks.DbBatch*' ... --iterationCount 15
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=lib/dotnet-env.sh
. "$root/scripts/lib/dotnet-env.sh"
cd "$root"

dotnet run -c Release --project tests/Rask.Benchmarks/Rask.Benchmarks.csproj -- postgres-bulk-insert \
  --filter "${RASK_BENCH_FILTER:-*PostgresBulkInsert*}" --artifacts artifacts/bench/postgres-bulk-insert "$@"

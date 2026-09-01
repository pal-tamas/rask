#!/usr/bin/env bash
# Local payload-bytes gate — did this change move the wire bytes?
#
# Wire-bytes-per-update for a fixed set of scenarios is noise-free (no timing: every render emits the
# same payload shape with one small value differing), so the numbers are compared byte-for-byte against
# the committed baselines and a regression fails the push. TWO baselines are gated, and both matter:
#
#   benchmarks/Rask.Benchmarks/Baselines/payload-bytes.csv                  (standalone codec)
#   benchmarks/Rask.Benchmarks.VsBlazor/Baselines/vs-blazor-payload-bytes.csv  (head-to-head)
#
# This is the ONLY place they run. A CI job used to duplicate them, but nothing on main was a required
# check, so its answer stopped nobody — the gate rode red through three merges before anyone noticed
# (#919). It runs here now, the same way the browser E2E and the CLI build gate do.
#
# UNCONDITIONAL, deliberately. Every other gate here is path-filtered because it costs minutes; this one
# costs about one, and a hand-listed filter is itself a way for a gate to stop running without saying so
# — see the note above `generator_paths` in .githooks/pre-push, where exactly that happened.
#
# Usage:  scripts/run-benchmarks-local.sh
# Skip:   RASK_SKIP_BENCHMARKS=1 (also honoured by the pre-push hook)
set -euo pipefail

if [ "${RASK_SKIP_BENCHMARKS:-}" = "1" ]; then
  echo "run-benchmarks-local: RASK_SKIP_BENCHMARKS=1 — skipping."
  exit 0
fi

root="$(git rev-parse --show-toplevel)"
cd "$root"

standalone="benchmarks/Rask.Benchmarks/Rask.Benchmarks.csproj"
vsblazor="benchmarks/Rask.Benchmarks.VsBlazor/Rask.Benchmarks.VsBlazor.csproj"

# BUILD FIRST, and never --no-build on its own. `--check` reads the baseline from
# AppContext.BaseDirectory — the copy under bin/ — not from the source tree. Editing the CSV and
# re-running with --no-build compares against the STALE copy and reports the old failure, which reads
# exactly like the fix not working. Verified the confusing way, while refreshing these baselines.
echo "==> Building the benchmark projects (Release)"
dotnet build "$standalone" -c Release -p:MinVerSkip=true
dotnet build "$vsblazor" -c Release -p:MinVerSkip=true

# Both gates run even when the first one fails, and that is the whole point of the `||` bookkeeping
# rather than plain `set -e`. The CI job they came from ran them as two steps, so a fail-fast on the
# first left the second UNRUN — which is how the vs-Blazor baseline stayed broken while the standalone
# one was being fixed, and how the fix looked complete when it was half of one (#921).
status=0

echo
echo "==> Payload-bytes gate (standalone codec)"
dotnet run -c Release --project benchmarks/Rask.Benchmarks --no-build -- payload-bytes --check || status=1

echo
echo "==> Payload-bytes gate (vs Blazor)"
dotnet run -c Release --project benchmarks/Rask.Benchmarks.VsBlazor --no-build -- payload-bytes --check || status=1

if [ "$status" -ne 0 ]; then
  cat >&2 <<'EOF'

  One or both payload-bytes gates regressed. Two things it can mean, and they want opposite fixes:

  A RENDER OR DIFF CHANGE made the wire heavier. That is the regression this gate exists to catch —
  fix the code, do not touch the baseline.

  A SCENARIO'S OWN MARKUP changed, so the numbers legitimately moved. The gated diff bytes are NOT
  immune to that: AppendRowToList100's diff is an InsertSubtree whose value IS the new row's HTML, so
  editing a benchmark component lands in a gated number. Refresh the baseline in the same commit, and
  say why:

    dotnet build benchmarks/Rask.Benchmarks/Rask.Benchmarks.csproj -c Release -p:MinVerSkip=true
    dotnet run -c Release --project benchmarks/Rask.Benchmarks --no-build -- payload-bytes \
      > benchmarks/Rask.Benchmarks/Baselines/payload-bytes.csv

  Telling them apart: the vs-Blazor report also records BlazorBatchBytes. If Blazor's numbers moved by
  the same amount, the bytes came from markup both frameworks render, not from anything Rask encodes.
EOF
  exit 1
fi

echo
echo "==> Payload-bytes gates passed (both baselines)."

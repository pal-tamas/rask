#!/usr/bin/env bash
# Local benchmark gates — did this change move the wire bytes, or start retaining sessions?
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

# The live-session capacity reports, smoke-sized. They answer "how many sessions fit in a box", they
# are documented in docs/scaling.md and docs/configuration.md as the way to size a host — and until
# now NOTHING ran them. The nightly job that did was deleted when GitHub was cut back to publishing,
# and the two of them then died on startup for four days, unnoticed, because a host service AddRask
# had started to require was missing from the benchmark harness (#922).
#
# What that outage hid is the reason these are here rather than left as hand-run tools: every page
# served was retaining its whole live session — ~1.1 MB a time, for the life of the process. The
# session-churn smoke ASSERTS that residue, so the number a host actually pays is gated and not just
# printed. About four seconds for all three; the full reports stay hand-run. (session-load is the one
# with a real cost — left as a sweep its smoke was ~13s of every push, three pages of warmup-plus-
# measure for an answer nothing reads, so --smoke runs ONE page and skips the JIT throwaway.)
#
# Run SEPARATELY, each with its own `|| status=1`. Ganged into one `run:` block, the first one to
# abort left the rest UNRUN — which is precisely how session-churn's identical failure stayed
# invisible while session-footprint's was being looked at (#922), and how the vs-Blazor baseline
# stayed broken through the fix for the standalone one (#921).
echo
echo "==> Live-session capacity: session-footprint (smoke)"
dotnet run -c Release --project benchmarks/Rask.Benchmarks --no-build -- session-footprint --smoke || status=1

echo
echo "==> Live-session capacity: session-churn (smoke, asserts nothing survives teardown)"
dotnet run -c Release --project benchmarks/Rask.Benchmarks --no-build -- session-churn --smoke || status=1

echo
echo "==> Live-session capacity: session-load (smoke, real Kestrel + real sockets)"
dotnet run -c Release --project benchmarks/Rask.Benchmarks --no-build -- session-load --smoke \
  >/dev/null || status=1

if [ "$status" -ne 0 ]; then
  cat >&2 <<'EOF'

  A benchmark gate failed. Read the section above that went red — they fail for different reasons.

  A CAPACITY SMOKE. session-churn reports the bytes a disposed session leaves behind: anything above
  the budget means a session is outliving its own teardown, and the full report (`-- session-churn`)
  prints the per-batch curve. session-footprint and session-load only have to RUN; if one threw, the
  harness has lost a host service the framework now needs — fix the framework, not the harness, since
  whatever a bare container is missing an unusual host is missing too.

  A PAYLOAD-BYTES GATE. Two things it can mean, and they want opposite fixes:

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
echo "==> Benchmark gates passed (both payload-bytes baselines + the capacity smokes)."

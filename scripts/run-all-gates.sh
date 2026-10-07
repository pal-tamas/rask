#!/usr/bin/env bash
# Every gate, in one local run.
#
# CI runs these as separate jobs (.github/workflows/gates.yml): unit + format, the browser suites, the
# CLI build and the template gate on every push, the container and real-host ones before a release.
# This script is the same list on your own machine, for when you want the answer before pushing
# rather than after — the browser suite in particular has caught most of the false greens in this
# repository's casebook. Keep the two lists in step.
#
# BENCHMARKS ARE NOT HERE, and that is deliberate rather than an omission. They run when you ask for
# them and at no other time — no hook, no CI workflow, and not this script either:
#
#     scripts/run-benchmarks-local.sh
#
# They are the one gate whose cost is wall-clock rather than correctness, and a timing measurement
# nobody asked for is a red result everyone learns to ignore.
#
# Everything is opt-in per gate, so this runs the FULL set and does not stop at the first failure —
# you want the whole picture from one long run, not one answer at a time. The exit code is non-zero if
# any gate failed, and the summary at the end names which.
#
# TWO LANES with --parallel. The gates fall into two groups that cannot share a tree: the CLI build,
# template and watch gates pack with MinVer ON, everything else builds with MinVerSkip, and the two
# rewrite each other's obj/Release (run-e2e-local.sh documents the recompile-from-scratch that costs).
# --parallel gives the packing group a second worktree at the same commit and runs it beside the
# rest, each lane in its own order. It refuses a dirty tree, because the second worktree would be
# testing the commit and not what is on disk.
#
# Usage: scripts/run-all-gates.sh
#        scripts/run-all-gates.sh --list              # what it would run, and skip
#        scripts/run-all-gates.sh --only 'E2E|CLI'    # just the gates whose label matches (ERE)
#        scripts/run-all-gates.sh --parallel          # the packing gates in a second worktree
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=lib/node-path.sh
. "$root/scripts/lib/node-path.sh"
rask_ensure_node
# shellcheck source=lib/dotnet-env.sh
. "$root/scripts/lib/dotnet-env.sh"
cd "$root"

# label · script · the env it needs to actually do anything (empty = runs unconditionally)
gates=(
  "unit + format|scripts/run-unit-local.sh|"
  "browser E2E|scripts/run-e2e-local.sh|"
  # The devtools in a real browser. A gate of its own because they exist only in a Debug build.
  "devtools E2E|scripts/run-devtools-e2e-local.sh|"
  # Browser SQLite in a real browser. A gate of its own because it is the only one that links e_sqlite3 natively.
  "browser SQLite E2E|scripts/run-browser-sqlite-e2e-local.sh|"
  # The rask.sh data demo in a real browser: published as pages.yml publishes it (native-linked, under /demos/data/).
  "data demo E2E|scripts/run-data-demo-e2e-local.sh|"
  "CLI build|scripts/run-cli-build-e2e.sh|RASK_CLI_BUILD_E2E=1"
  # Every template's C# half scaffolded and built. The seven front ends are NOT here: each is minutes
  # (npm ci, a bundle, a published host), so CI gives each a job and by hand it is one at a time —
  # scripts/run-template-e2e.sh --front-end=<key>.
  "templates|scripts/run-template-e2e.sh|RASK_TEMPLATE_E2E=1"
  "watch hot reload|scripts/run-watch-e2e.sh|RASK_WATCH_E2E=1"
  "deploy|scripts/run-deploy-e2e-local.sh|RASK_DEPLOY_E2E=1"
  "storage providers|scripts/run-storage-providers-local.sh|RASK_STORAGE_PROVIDERS=1"
  "installer|scripts/run-install-e2e-local.sh|RASK_INSTALL_E2E=1"
  "providers|scripts/run-providers-local.sh|"
)

# The gates that pack with MinVer on. They share artifacts/cli-gate-packages and one obj/ mode, so
# they stay in one lane, in order.
packing_gates="CLI build|templates|watch hot reload"

list=0
parallel=0
only=""
while [ $# -gt 0 ]; do
  case "$1" in
    --list) list=1 ;;
    --parallel) parallel=1 ;;
    --only) only="${2:?run-all-gates: --only needs a pattern}"; shift ;;
    *) echo "run-all-gates: unknown argument $1" >&2; exit 2 ;;
  esac
  shift
done

if [ -n "$only" ]; then
  kept=()
  for entry in "${gates[@]}"; do
    if printf '%s\n' "${entry%%|*}" | grep -Eq -- "$only"; then kept+=("$entry"); fi
  done
  if [ "${#kept[@]}" -eq 0 ]; then
    echo "run-all-gates: --only '$only' matches no gate (see --list)." >&2
    exit 2
  fi
  gates=("${kept[@]}")
fi

if [ "$list" -eq 1 ]; then
  echo "run-all-gates would run, in order:"
  for entry in "${gates[@]}"; do
    label="${entry%%|*}"
    rest="${entry#*|}"
    script="${rest%%|*}"
    env_needed="${rest#*|}"
    if [ -x "$script" ] || [ -f "$script" ]; then
      printf '  %-18s %s%s\n' "$label" "$script" \
        "${env_needed:+   (needs $env_needed)}"
    else
      printf '  %-18s %s   MISSING\n' "$label" "$script"
    fi
  done
  exit 0
fi

results="$(mktemp -t rask-all-gates.XXXXXX)"
logs="$(mktemp -d -t rask-all-gates-logs.XXXXXX)"

# run_lane <directory> <gate entries...> — each gate in order, in that tree; one result line per gate.
# With a log directory (the parallel case) output goes to a file per gate instead of the terminal,
# because two lanes writing one terminal is a failure spliced through another gate's output.
run_lane() {
  lane_dir="$1"; shift
  for entry in "$@"; do
    label="${entry%%|*}"
    rest="${entry#*|}"
    script="${rest%%|*}"
    env_needed="${rest#*|}"

    if [ ! -f "$lane_dir/$script" ]; then
      echo "run-all-gates: MISSING $script — skipping '$label'." >&2
      printf '%s\t%s\t%s\n' "$label" "missing" 0 >>"$results"
      continue
    fi

    began=$SECONDS
    # The per-gate opt-in variable is exported for the child only. A gate whose suite skips itself
    # without it would otherwise report a green run having executed nothing, which is precisely the
    # failure this repository keeps paying for.
    if [ "$parallel" -eq 1 ]; then
      log="$logs/$(printf '%s' "$label" | tr -c 'A-Za-z0-9' '-').log"
      echo "==> $label   started ($script) — $log"
      ( cd "$lane_dir" && env ${env_needed:+"$env_needed"} bash "$script" ) >"$log" 2>&1
    else
      echo
      echo "==================================================================="
      echo "==> $label   ($script)"
      echo "==================================================================="
      ( cd "$lane_dir" && env ${env_needed:+"$env_needed"} bash "$script" )
    fi
    status=$?
    printf '%s\t%s\t%s\n' "$label" "$status" "$((SECONDS - began))" >>"$results"
    [ "$parallel" -eq 0 ] || echo "==> $label   finished: exit $status after $((SECONDS - began))s"
  done
}

began_all=$SECONDS
if [ "$parallel" -eq 1 ]; then
  if [ -n "$(git status --porcelain)" ]; then
    echo "run-all-gates: --parallel needs a clean tree — its second worktree is checked out at HEAD, so it would not test what is on disk. Commit first, or run without --parallel." >&2
    exit 2
  fi

  here=()
  there=()
  for entry in "${gates[@]}"; do
    if printf '%s\n' "${entry%%|*}" | grep -Eqx -- "$packing_gates"; then there+=("$entry"); else here+=("$entry"); fi
  done

  there_pid=""
  if [ "${#there[@]}" -gt 0 ] && [ "${#here[@]}" -gt 0 ]; then
    # Beside the worktrees this repository already keeps, named for the commit so a rerun on the same
    # commit reuses its build and a new commit never inherits a stale one.
    second="$(cd "$(git rev-parse --git-common-dir)/.." && pwd)/.claude/worktrees/gates-$(git rev-parse --short HEAD)"
    if [ ! -d "$second" ]; then
      echo "==> Second worktree for the packing gates: $second"
      git worktree add --detach "$second" HEAD >/dev/null
    fi
    run_lane "$second" "${there[@]}" &
    there_pid=$!
    run_lane "$root" "${here[@]}"
    wait "$there_pid"
    # Kept when anything failed, so the tree that failed is there to look at.
    if ! cut -f2 "$results" | grep -qvx 0; then
      git worktree remove --force "$second"
    else
      echo "run-all-gates: left $second in place for the failure; remove it with: git worktree remove --force $second"
    fi
  else
    run_lane "$root" "${gates[@]}"
  fi
else
  run_lane "$root" "${gates[@]}"
fi

echo
echo "==================================================================="
printf '  %-20s %-8s %s\n' "gate" "result" "seconds"
failed=""
ran=0
while IFS="$(printf '\t')" read -r label status seconds; do
  ran=$((ran + 1))
  if [ "$status" = "0" ]; then verdict="passed"; else verdict="FAILED"; failed="$failed
  $label (exit $status)"; fi
  printf '  %-20s %-8s %s\n' "$label" "$verdict" "$seconds"
done <"$results"
echo "  $((SECONDS - began_all))s wall clock in all."
rm -f "$results"
echo "==================================================================="

if [ -n "$failed" ]; then
  echo "run-all-gates: $ran gate(s) ran; these FAILED:$failed" >&2
  [ "$parallel" -eq 0 ] || echo "run-all-gates: per-gate logs are in $logs" >&2
  exit 1
fi

rm -rf "$logs"
echo "run-all-gates: all $ran gates passed."

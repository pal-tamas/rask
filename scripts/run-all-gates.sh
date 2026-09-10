#!/usr/bin/env bash
# Everything the fast hooks no longer run.
#
# .githooks/pre-commit and .githooks/pre-push are both held to a hard one-minute budget, which the
# heavyweight gates cannot fit: the browser E2E starts by publishing a WASM bundle, the CLI build gate
# packs 19 packages, the deploy and installer gates boot containers. They did not stop mattering when
# they stopped being automatic — this script is where they live now.
#
# RUN THIS BEFORE A RELEASE, and after any change big enough that you want the old guarantee back.
# The browser suite in particular has caught most of the false greens in this repository's casebook.
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
# Usage: scripts/run-all-gates.sh
#        scripts/run-all-gates.sh --list     # what it would run, and skip
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

# label · script · the env it needs to actually do anything (empty = runs unconditionally)
gates=(
  "unit + format|scripts/run-unit-local.sh|"
  "browser E2E|scripts/run-e2e-local.sh|"
  "CLI build|scripts/run-cli-build-e2e.sh|RASK_CLI_BUILD_E2E=1"
  "watch hot reload|scripts/run-watch-e2e.sh|RASK_WATCH_E2E=1"
  "meta publish|scripts/run-meta-publish-e2e.sh|RASK_META_PUBLISH_E2E=1"
  "deploy|scripts/run-deploy-e2e-local.sh|RASK_DEPLOY_E2E=1"
  "installer|scripts/run-install-e2e-local.sh|RASK_INSTALL_E2E=1"
)

if [ "${1:-}" = "--list" ]; then
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

failed=""
ran=0

for entry in "${gates[@]}"; do
  label="${entry%%|*}"
  rest="${entry#*|}"
  script="${rest%%|*}"
  env_needed="${rest#*|}"

  if [ ! -f "$script" ]; then
    echo "run-all-gates: MISSING $script — skipping '$label'." >&2
    failed="$failed
  $label (script missing: $script)"
    continue
  fi

  echo
  echo "==================================================================="
  echo "==> $label   ($script)"
  echo "==================================================================="

  # The per-gate opt-in variable is exported for the child only. A gate whose suite skips itself
  # without it would otherwise report a green run having executed nothing, which is precisely the
  # failure this repository keeps paying for.
  if [ -n "$env_needed" ]; then
    env "$env_needed" bash "$script"
  else
    bash "$script"
  fi

  status=$?
  ran=$((ran + 1))
  [ "$status" -eq 0 ] || failed="$failed
  $label (exit $status)"
done

echo
echo "==================================================================="
if [ -n "$failed" ]; then
  echo "run-all-gates: $ran gate(s) ran; these FAILED:$failed" >&2
  exit 1
fi

echo "run-all-gates: all $ran gates passed."

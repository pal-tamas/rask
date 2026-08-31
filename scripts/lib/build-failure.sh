#!/usr/bin/env bash
# Shared failure classification for the local gates.
#
# A gate that fails is only useful if it names the right culprit. Both the CLI build gate and the E2E
# gate build browser targets, and both used to report *any* failure as "the generated code doesn't
# compile" — which is a lie whenever the machine simply cannot build browser targets at that moment.
#
# The case that motivated this (#718): a concurrent `sudo dotnet workload install <anything>` installs a
# new workload SET and bumps the shared mono.toolchain.current / emscripten.current manifests for the
# whole SDK band. Between the manifest bump and the pack restore, the manifests reference Emscripten
# packs that are not on disk yet, so `wasm-tools` resolves as MISSING — machine-wide, in every worktree —
# while `dotnet workload list` still cheerfully lists it as installed. Measured on the failing run: 0
# `error CS`, 24 `error NETSDK1147`. It clears on its own when the installing process exits, and racing
# it with your own `dotnet workload restore` does not help.
#
# It cost two sessions about an hour between them, chasing a scaffolder bug that did not exist. The
# distinction is a couple of greps, and getting it wrong trains people to distrust the gate — which is
# worse than the gate simply failing.
#
# Source it, don't execute it:  . "$root/scripts/lib/build-failure.sh"

# Classify a captured build/test log. Echoes exactly one kind:
#
#   busy     — the gate REFUSED TO START because another browser gate held the machine. Nothing ran,
#              so every other kind would be a guess about a run that never happened.
#   code     — real compile errors (`error CS…`). The gate's own message is correct; the branch is broken.
#   workload — `error NETSDK1147` and no CS errors. A browser target could not resolve its workload.
#   sdk      — some other `error NETSDK…` and no CS errors. An SDK/restore problem, still not the branch.
#   unknown  — neither appears. The gate failed somewhere that is not a compile at all (a failing
#              assertion, a timeout, a crashed host), so claiming "it doesn't compile" would be wrong too.
#
# `busy` is checked FIRST and wins outright. Without it the concurrency guard's refusal fell through to
# `unknown`, whose advice is "look for a failing assertion, a timeout, or a host that exited early" —
# advice about a suite that never started, printed directly beneath the guard's own correct explanation
# and contradicting it. The guard says "wait"; the classifier said "go read your test output".
#
# CS wins over the machine kinds when both appear: a NETSDK error alongside genuine compile errors does
# not excuse them.
rask_build_failure_kind() {
  local log="${1:-}"

  [ -n "$log" ] && [ -f "$log" ] || { printf 'unknown\n'; return 0; }

  # `grep -c`, never `grep -q`. With `set -o pipefail` a `-q` grep exits on the first match, the writer
  # takes SIGPIPE, and the pipeline reports failure — the same trap that let a 421-file commit past the
  # pre-commit hook (see the note in .githooks/pre-commit). `|| true` because grep exits 1 on no match.
  local cs netsdk workload busy
  cs=$(grep -Ec 'error[[:space:]]+CS[0-9]+' "$log" 2>/dev/null || true)
  netsdk=$(grep -Ec 'error[[:space:]]+NETSDK[0-9]+' "$log" 2>/dev/null || true)
  workload=$(grep -Ec 'error[[:space:]]+NETSDK1147' "$log" 2>/dev/null || true)

  # Matched on the guard's own refusal line, anchored to the script name at the start of a line so a
  # test log that merely QUOTES the phrase — a case that exists, since the guard's wording is itself
  # asserted — cannot trip it.
  busy=$(grep -Ec '^run-e2e-local: another browser E2E gate is already running' "$log" 2>/dev/null || true)

  # `busy` sits BELOW code and above the machine kinds. A refusal means the suite never started, so it
  # outranks anything inferred from an absence — but it must never outrank a real `error CS`: if
  # something got far enough to fail compiling then something did run, and hiding that would be #718
  # in reverse, blaming the machine for a broken branch.
  if [ "${cs:-0}" -gt 0 ]; then
    printf 'code\n'
  elif [ "${busy:-0}" -gt 0 ]; then
    printf 'busy\n'
  elif [ "${workload:-0}" -gt 0 ]; then
    printf 'workload\n'
  elif [ "${netsdk:-0}" -gt 0 ]; then
    printf 'sdk\n'
  else
    printf 'unknown\n'
  fi
}

# Print the operator-facing explanation for a kind, to stderr.
#
#   $1 — kind, as returned by rask_build_failure_kind
#   $2 — gate label, e.g. "CLI build gate"
#   $3 — the message for the `code` kind: the branch is broken, and only the gate knows in what way
#   $4 — the message for the `unknown` kind, optional. A gate that failed without compiling anything
#        failed at what it actually does — a journey, an assertion, a deploy — and that is the one
#        remaining case where the gate's own "what to do now" is still the right thing to say. The
#        machine kinds get no such line on purpose: pointing at your own diff there is what #718 was.
rask_explain_build_failure() {
  local kind="${1:-unknown}" gate="${2:-gate}" code_message="${3:-}" other_message="${4:-}"

  case "$kind" in
    busy)
      {
        echo "$gate: nothing ran — the machine was already busy with another browser gate."
        echo
        echo "  The guard above refused to start this suite and named the run that holds the machine."
        echo "  There is no failure here to investigate: no journey ran, no assertion was evaluated."
        echo "  Wait for that run to finish and push again."
      } >&2
      ;;
    code)
      [ -n "$code_message" ] && echo "$gate: $code_message" >&2
      ;;
    workload)
      {
        echo "$gate: this is NOT your branch."
        echo
        echo "  Your machine cannot build browser targets right now: the 'wasm-tools' workload is"
        echo "  unresolvable (error NETSDK1147), and nothing failed to compile (0 'error CS')."
        echo
        echo "  The usual cause is a workload install in flight from another session or another worktree —"
        echo "  it bumps the shared manifests for the whole SDK band machine-wide, and for a moment they"
        echo "  reference Emscripten packs that are not on disk yet. 'dotnet workload list' still lists"
        echo "  wasm-tools as installed throughout, so it will not tell you this."
        echo
        echo "  Check for one:"
        echo "    ps aux | grep 'workload install'"
        echo "    ls -la /usr/local/share/dotnet/metadata/workloads/InstalledWorkloadSets/"
        echo
        echo "  A workload set dated in the last few minutes is the cause. Wait for that process to exit"
        echo "  and re-run. Do NOT run 'dotnet workload restore' alongside it — racing it does not help."
        echo
        echo "  A stale SDK feature-band registration (a workload registered for an SDK that is no longer"
        echo "  installed, with no global.json pinning one) produces the same NETSDK1147."
      } >&2
      ;;
    sdk)
      {
        echo "$gate: this looks like an SDK or restore problem, not your branch."
        echo
        echo "  The build reported 'error NETSDK…' and nothing failed to compile (0 'error CS'), so the"
        echo "  code is not what broke. Read the NETSDK error above — it names the missing piece."
      } >&2
      ;;
    *)
      {
        echo "$gate: failed without any compile error."
        echo
        echo "  Neither 'error CS' nor 'error NETSDK' appears in the output, so this is not a build"
        echo "  failure — look for a failing assertion, a timeout, or a host that exited early."
        [ -n "$other_message" ] && { echo; echo "  $other_message"; }
      } >&2
      ;;
  esac
}

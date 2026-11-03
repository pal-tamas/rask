#!/usr/bin/env bash
# gate-inputs: scripts/lib/nightly_(pick|wait|anchor)\.sh$
# What nightly.yml leans on to publish a commit minutes after its push, against a fake `gh` that answers
# from saved API replies and a throwaway repository:
#   nightly_pick.sh    which commit: an early run takes the head of the ci run that started, unless the last
#                      nightly holds it; the backstop takes the newest commit with a green build (or a whole
#                      green run), never one the last nightly holds, and nothing while another nightly runs.
#   nightly_wait.sh    whether the commit may be pushed: only once that run's `build` job passed.
#   nightly_anchor.sh  where the rolling tag can go: never on a commit that changes a workflow, which GitHub
#                      refuses the workflow token (nightly 41608821292, 2026-11-03).
#
# Reading these wrong is lopsided: a commit whose build failed must never be published, while a nightly
# skipped is published by the next run.
#
# Usage:  scripts/tests/nightly-pick.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-nightly-pick.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

failures=0
checked=0
check() {
  checked=$((checked + 1))
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}

# The fake: `gh api [--paginate] <url> --jq <expr>` runs jq over $FAKE/<url without per_page, as a file name>.json,
# or answers {} when nothing was saved for it.
mkdir -p "$tmp/bin" "$tmp/fake"
cat > "$tmp/bin/gh" <<'FAKE'
#!/usr/bin/env bash
case "$1" in
  api)
    shift; url=""; expr="."
    while [ $# -gt 0 ]; do
      case "$1" in --paginate) ;; --jq) expr="$2"; shift ;; *) url="$1" ;; esac
      shift
    done
    file="$FAKE/$(printf '%s' "$url" | sed 's/[?&]per_page=[0-9]*//' | tr -c 'A-Za-z0-9\n' '_').json"
    if [ -f "$file" ]; then jq -r "$expr" "$file"; else jq -r "$expr" <<<'{}'; fi ;;
esac
FAKE
chmod +x "$tmp/bin/gh"
export PATH="$tmp/bin:$PATH" FAKE="$tmp/fake" GITHUB_REPOSITORY=o/r GITHUB_RUN_ID=900 GITHUB_REF=refs/heads/main GITHUB_SHA=mainhead
reply() { printf '%s' "$2" > "$FAKE/$(printf '%s' "$1" | tr -c 'A-Za-z0-9\n' '_').json"; }
forget() { rm -f "$FAKE"/*.json; }

A=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
B=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
published() { reply "repos/o/r/releases/tags/nightly" "{\"name\":\"Nightly 0.1.1-alpha.0.7 ($1)\"}"; }
compared() { reply "repos/o/r/compare/$1...$2" "{\"status\":\"$3\"}"; }
# runs <id:sha:conclusion>... -> main's push runs of ci, newest first
runs() {
  local out='{"workflow_runs":[' sep='' r id sha conclusion
  for r in "$@"; do
    IFS=: read -r id sha conclusion <<<"$r"
    if [ -n "$conclusion" ]; then conclusion="\"$conclusion\""; else conclusion=null; fi
    out+="$sep{\"id\":$id,\"head_sha\":\"$sha\",\"conclusion\":$conclusion}"; sep=','
  done
  reply "repos/o/r/actions/workflows/ci.yml/runs?branch=main&event=push" "$out]}"
}
# build <run id> <status> [conclusion] -> that run's jobs, with its build gate
build() {
  local conclusion=null
  [ -z "${3:-}" ] || conclusion="\"$3\""
  reply "repos/o/r/actions/runs/$1/jobs" "{\"jobs\":[{\"name\":\"gates / plan\",\"status\":\"completed\",\"conclusion\":\"success\"},{\"name\":\"gates / build\",\"status\":\"$2\",\"conclusion\":$conclusion}]}"
}
others() { reply "repos/o/r/actions/workflows/nightly.yml/runs?status=$1" "{\"workflow_runs\":[{\"id\":$2}]}"; }

# pick <EVENT> [ACTION HEAD_SHA CI_RUN CI_EVENT] -> "sha|run|wait"
pick() {
  : > "$tmp/out"
  EVENT="$1" ACTION="${2:-}" HEAD_SHA="${3:-}" CI_RUN="${4:-}" CI_EVENT="${5:-}" GITHUB_OUTPUT="$tmp/out" \
    "$root/scripts/lib/nightly_pick.sh" >/dev/null
  printf '%s|%s|%s' "$(sed -n 's/^sha=//p' "$tmp/out")" "$(sed -n 's/^run=//p' "$tmp/out")" "$(sed -n 's/^wait=//p' "$tmp/out")"
}

echo "==> nightly_pick: early, when main's ci run starts"
forget
check "a push run that started is packed at once, and waited for" "$B|11|true" "$(pick workflow_run requested "$B" 11 push)"
published "$A"; compared "$A" "$B" ahead
check "...after the last nightly too" "$B|11|true" "$(pick workflow_run requested "$B" 11 push)"
published "$B"; compared "$B" "$B" identical
check "...but not when the last nightly is that commit" "||false" "$(pick workflow_run requested "$B" 11 push)"
published "$B"; compared "$B" "$A" behind
check "...or already holds it" "||false" "$(pick workflow_run requested "$A" 10 push)"
forget
check "a manual ci run is no push to main" "||false" "$(pick workflow_run requested "$B" 11 workflow_dispatch)"

echo "==> nightly_pick: the backstop, after a run or on the hour"
forget
runs "12:$B:failure" "11:$A:success"
build 12 completed failure
check "a commit whose build failed is never published" "$A|11|false" "$(pick schedule)"
build 12 completed success
check "a commit whose build passed is, though a later gate failed" "$B|12|false" "$(pick workflow_run completed)"
runs "13:$B:"
build 13 in_progress
check "a build still going is not a verdict" "||false" "$(pick schedule)"
forget
runs "14:$B:success"
check "a whole green run with no build job counts (a scoped run that reached no code)" "$B|14|false" "$(pick schedule)"
published "$B"; compared "$B" "$B" identical
check "nothing when the last nightly holds the newest green commit" "||false" "$(pick schedule)"
forget
runs "14:$B:success"
others in_progress 901
check "nothing while another nightly run is going" "||false" "$(pick schedule)"
others in_progress 900
check "...which is not this run itself" "$B|14|false" "$(pick schedule)"

echo "==> nightly_pick: by hand"
forget
check "main's head, by hand" "mainhead||false" "$(pick workflow_dispatch)"
check "never another branch" "||false" "$(GITHUB_REF=refs/heads/ci/x pick workflow_dispatch)"

echo "==> nightly_wait: the build job decides"
# wait <run id> -> the go= it writes
wait_for() {
  : > "$tmp/out"
  GITHUB_OUTPUT="$tmp/out" RASK_NIGHTLY_WAIT="${2:-0}" RASK_NIGHTLY_POLL=0 \
    "$root/scripts/lib/nightly_wait.sh" "$1" >/dev/null
  sed -n 's/^go=//p' "$tmp/out"
}
forget
build 20 completed success
check "a green build is pushed" "true" "$(wait_for 20)"
build 21 completed failure
check "a red build is not" "false" "$(wait_for 21)"
build 22 completed cancelled
check "nor a cancelled one" "false" "$(wait_for 22)"
build 23 in_progress
check "a build that never finishes is not, when the wait runs out" "false" "$(wait_for 23)"
reply "repos/o/r/actions/runs/24" '{"status":"completed","conclusion":"success"}'
check "a green run with no build job is pushed" "true" "$(wait_for 24)"
reply "repos/o/r/actions/runs/25" '{"status":"completed","conclusion":"failure"}'
check "a red one with no build job is not" "false" "$(wait_for 25)"

echo "==> nightly_anchor: never on a commit that changes a workflow"
repo="$tmp/repo"
mkdir -p "$repo"
g() { git -C "$repo" -c user.name=t -c user.email=t@example.invalid -c commit.gpgsign=false -c core.hooksPath=/dev/null "$@"; }
g init -q -b main
change() { mkdir -p "$(dirname "$repo/$1")"; echo "$2" >> "$repo/$1"; g add -A; g commit -qm "$1"; g rev-parse HEAD; }
c1="$(change README one)"
c2="$(change .github/workflows/ci.yml two)"
c3="$(change src/a.cs three)"
g checkout -q -b branch "$c3"
b1="$(change .github/workflows/ci.yml four)"
g checkout -q main
c4="$(change src/b.cs five)"
g merge -q --no-edit branch
m="$(g rev-parse HEAD)"
anchor() { ( cd "$repo" && "$root/scripts/lib/nightly_anchor.sh" "$1" ); }
check "a commit that changes no workflow is its own anchor" "$c3" "$(anchor "$c3")"
check "one that changes a workflow anchors at its parent" "$c1" "$(anchor "$c2")"
check "a merge that brings a workflow change anchors below it" "$c4" "$(anchor "$m")"
: "$b1"

echo
if [ "$failures" -gt 0 ]; then
  echo "nightly-pick: $failures of $checked checks FAILED" >&2
  exit 1
fi
echo "nightly-pick: $checked checks passed."

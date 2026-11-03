#!/usr/bin/env bash
# gate-inputs: scripts/lib/(green_base|cancel_superseded|ci_runs)\.sh$
# What ci.yml leans on to spend runners on the verdicts somebody is waiting for, against a fake `gh` that
# answers from saved API replies and a throwaway repository:
#   green_base.sh  where a scoped run measures from — on main, the head of the newest earlier push run that
#                  started its gates, green or red, that HEAD descends from; on a ci/** branch, its merge-base
#                  with main, so main's churn never turns a branch into the whole set.
#   cancel_superseded.sh  which older main runs a push's run cancels — only those that have not started a
#                  gate, never a manual one, and never a full.yml run.
#
# Reading these wrong is lopsided like affected_gates.py: a base too far back costs gates, a base that is
# not an ancestor would scope a run against a diff that is not its change; a cancel too many throws a
# verdict away, a cancel too few only costs runners.
#
# Usage:  scripts/tests/ci-runs.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-ci-runs.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}

# The fake: `gh api <url> --jq <expr>` runs jq over $FAKE/<url up to the query, as a file name>.json, or
# answers {} when nothing was saved for it; every call is logged to $FAKE/calls.
mkdir -p "$tmp/bin"
cat > "$tmp/bin/gh" <<'FAKE'
#!/usr/bin/env bash
echo "$*" >> "$FAKE/calls"
case "$1" in
  api)
    shift; url=""; expr="."
    while [ $# -gt 0 ]; do
      case "$1" in --paginate) ;; --jq) expr="$2"; shift ;; *) url="$1" ;; esac
      shift
    done
    file="$FAKE/$(printf '%s' "$url" | sed 's/&status=.*//; s/[?&]per_page=.*//' | tr -c 'A-Za-z0-9\n' '_').json"
    if [ -f "$file" ]; then jq -r "$expr" "$file"; else jq -r "$expr" <<<'{}'; fi ;;
esac
FAKE
chmod +x "$tmp/bin/gh"
export PATH="$tmp/bin:$PATH" FAKE="$tmp/fake" GITHUB_REPOSITORY=o/r
reply() { mkdir -p "$FAKE"; printf '%s' "$2" > "$FAKE/$(printf '%s' "$1" | tr -c 'A-Za-z0-9\n' '_').json"; }
MAIN='ci.yml/runs?branch=main&event=push'
FULL='full.yml/runs?branch=main'

# runs <id:status:event[:conclusion[:sha]]>...  -> a run listing, newest first as the API gives it
runs() {
  local out='{"workflow_runs":[' sep='' r id status event conclusion sha
  for r in "$@"; do
    IFS=: read -r id status event conclusion sha <<<"$r"
    if [ -n "$conclusion" ]; then conclusion="\"$conclusion\""; else conclusion=null; fi
    out+="$sep{\"id\":$id,\"status\":\"$status\",\"event\":\"$event\",\"conclusion\":$conclusion,\"head_sha\":\"$sha\"}"
    sep=','
  done
  printf '%s]}' "$out"
}
# jobs <run id> <name=status[/conclusion]>...  -> that run's jobs
jobs() { local id="$1" out='{"jobs":[' sep='' j s; shift; for j in "$@"; do s="${j##*=}"; out+="$sep{\"name\":\"${j%=*}\",\"status\":\"${s%%/*}\",\"conclusion\":\"${s#*/}\"}"; sep=','; done; reply "repos/o/r/actions/runs/$id/jobs" "$out]}"; }
STARTED=('base=completed/success' 'gates / plan=completed/success' 'gates / build=in_progress')
WAITING=('base=completed/success' 'gates / plan=completed/success' 'gates / build=queued')
RED=('base=completed/success' 'gates / plan=completed/success' 'gates / build=completed/failure')

# main: m1 - m2 - m3 - m4 (origin/main); ci/x cut at m2 with b1 on top; ci/y cut at m2 with main merged in;
# x is a commit off all of them.
repo="$tmp/repo"
mkdir -p "$repo"
g() { git -C "$repo" -c user.name=t -c user.email=t@example.invalid -c commit.gpgsign=false -c core.hooksPath=/dev/null "$@"; }
g init -q -b main
commit() { echo "$1" > "$repo/$1"; g add "$1"; g commit -qm "$1"; g rev-parse HEAD; }
m1="$(commit m1)"; m2="$(commit m2)"; m3="$(commit m3)"; m4="$(commit m4)"
g update-ref refs/remotes/origin/main "$m4"
g checkout -q -b ci/x "$m2"; commit b1 >/dev/null
g checkout -q -b ci/y "$m2"; commit b2 >/dev/null; g merge -q --no-edit main; commit b3 >/dev/null
g checkout -q --orphan off; x="$(commit x)"

base() { ( cd "$repo" && g checkout -q "$1" && "$root/scripts/lib/green_base.sh" "${@:2}" ); }

echo "==> green_base main: each run checks its own push"

rm -rf "$FAKE"
reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:in_progress:push::"$m3" 12:completed:push:success:"$m2")"
jobs 13 "${STARTED[@]}"
check "an earlier run that started its gates is the base, done or not"  "$m3" "$(base main main 14)"

jobs 13 "${WAITING[@]}"
check "one still waiting is skipped: this run holds its change"          "$m2" "$(base main main 14)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:in_progress:push::"$m3" 12:completed:push:failure:"$m2" 11:completed:push:success:"$m1")"
jobs 12 "${RED[@]}"
check "a red one is a base: its push was named, not run again"           "$m2" "$(base main main 14)"

jobs 12 'base=completed/failure' 'gates / plan=completed/skipped' 'gates / build=completed/skipped'
check "a run that failed before any gate is no base"                     "$m1" "$(base main main 14)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:completed:push:cancelled:"$m3" 12:completed:push:success:"$m2")"
jobs 13 "${STARTED[@]}"
check "a cancelled run is no base, whatever it started"                  "$m2" "$(base main main 14)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 15:completed:push:success:"$m4" 13:completed:push:success:"$m2")"
check "a later run is never this one's base"                             "$m2" "$(base main main 14)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:completed:push:success:"$x" 12:completed:push:success:"$m1")"
check "a commit off HEAD's history is never a base"                      "$m1" "$(base main main 14)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:completed:push:success:"$m3")"
jobs 13
check "a green run with no gate to run counts"                           "$m3" "$(base main main 14)"

rm -rf "$FAKE"
check "no earlier run at all is no base"                                 ""    "$(base main main 14)"

echo "==> green_base branch: a ci/** branch is measured by its own commits"

mkdir -p "$FAKE"; : > "$FAKE/calls"
check "a branch cut from an older main is measured from where it left"   "$m2" "$(base ci/x branch)"
check "one that merged main is measured from main as merged"             "$m4" "$(base ci/y branch)"
check "main's own runs are not asked"                                    "0"   "$(grep -c actions/workflows "$FAKE/calls" || true)"
g update-ref -d refs/remotes/origin/main
check "with no origin/main there is no base"                             ""    "$(base ci/x branch)"

echo "==> cancel_superseded: what a push's run on main cancels"

cancelled() { grep '^run cancel' "$FAKE/calls" | awk '{ print $3 }' | sort -n | paste -sd' ' -; }
supersede() { : > "$FAKE/calls"; "$root/scripts/lib/cancel_superseded.sh" "$1" >/dev/null; cancelled; }

rm -rf "$FAKE"; mkdir -p "$FAKE"
reply "repos/o/r/actions/workflows/$MAIN" "$(runs 10:in_progress:push 11:queued:push 12:in_progress:push 13:in_progress:push 14:queued:push 9:completed:push)"
jobs 10 'base=completed/success' 'gates / plan=completed/success' 'gates / build=in_progress'
jobs 11
jobs 12 'base=completed/success' 'gates / plan=completed/success' 'gates / build=queued' 'gates / unit=queued'
check "older ci runs with no gate started go, a started one stays" "11 12" "$(supersede 13)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:in_progress:push 12:queued:workflow_dispatch)"
jobs 12
check "a manual run is never cancelled"                            ""      "$(supersede 13)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:in_progress:push)"
reply "repos/o/r/actions/workflows/$FULL" "$(runs 21:pending:schedule 20:in_progress:schedule)"
jobs 20 'moved=completed/success' 'gates / plan=completed/success' 'gates / build=queued'
check "a full run is never cancelled: it runs on its own clock"    ""      "$(supersede 13)"

if [ "$failures" -gt 0 ]; then
  echo "ci runs: $failures check(s) failed" >&2
  exit 1
fi
echo "ci runs: all checks passed"

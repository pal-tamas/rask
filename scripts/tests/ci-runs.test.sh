#!/usr/bin/env bash
# gate-inputs: scripts/lib/(green_base|cancel_superseded)\.sh$
# What ci.yml leans on to spend runners on the verdicts somebody is waiting for, against a fake `gh` that
# answers from saved API replies and a throwaway repository:
#   green_base.sh  where a scoped run measures from — the newest green commit HEAD descends from, so a
#                  ci/** branch cut from an older main is scoped, and a commit off HEAD's history never counts.
#   cancel_superseded.sh  which older main runs a push's run cancels — only those that have not started a
#                  gate, never a manual one, and a full run only when a newer one waits behind it.
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
greens() { local out='{"workflow_runs":[' sep=''; for sha in "$@"; do out+="$sep{\"head_sha\":\"$sha\"}"; sep=','; done; printf '%s]}' "$out"; }

# main: m1 - m2 - m3; a branch cut at m2 with b1 on top; x is a commit off both.
repo="$tmp/repo"
mkdir -p "$repo"
g() { git -C "$repo" -c user.name=t -c user.email=t@example.invalid -c commit.gpgsign=false -c core.hooksPath=/dev/null "$@"; }
g init -q -b main
commit() { echo "$1" > "$repo/f"; g add f; g commit -qm "$1"; g rev-parse HEAD; }
m1="$(commit m1)"; m2="$(commit m2)"; m3="$(commit m3)"
g checkout -q -b ci/x "$m2"; b1="$(commit b1)"
g checkout -q --orphan off; x="$(commit x)"

base() { ( cd "$repo" && g checkout -q "$1" && "$root/scripts/lib/green_base.sh" "${@:2}" ); }
MAIN='ci.yml/runs?branch=main&event=push'
FULL='full.yml/runs?branch=main'
OWN='ci.yml/runs?branch=ci%2Fx&event=push'

echo "==> green_base: where a scoped run measures from"

rm -rf "$FAKE"; reply "repos/o/r/actions/workflows/$MAIN" "$(greens "$m3" "$m1")"; reply "repos/o/r/actions/workflows/$FULL" "$(greens "$m2")"
check "main takes the newest green of the two workflows"     "$m3" "$(base main "$MAIN" "$FULL")"
check "a branch cut before it takes the newest it contains"  "$m2" "$(base ci/x "$MAIN" "$FULL")"

reply "repos/o/r/actions/workflows/$FULL" "$(greens "$x")"
check "an older green of one workflow is still a candidate"  "$m1" "$(base ci/x "$MAIN" "$FULL")"
reply "repos/o/r/actions/workflows/$OWN" "$(greens "$b1")"
check "a branch's own green push is newer than main's"       "$b1" "$(base ci/x "$MAIN" "$FULL" "$OWN")"

rm -rf "$FAKE"; reply "repos/o/r/actions/workflows/$MAIN" "$(greens "$x" "$m3")"
check "no green commit HEAD contains is no base"             ""    "$(base ci/x "$MAIN")"
rm -rf "$FAKE"
check "no green run at all is no base"                       ""    "$(base main "$MAIN" "$FULL")"

echo "==> cancel_superseded: what a push's run on main cancels"

# runs <id:status:event>...  -> a run listing
runs() { local out='{"workflow_runs":[' sep='' r; for r in "$@"; do IFS=: read -r id status event <<<"$r"; out+="$sep{\"id\":$id,\"status\":\"$status\",\"event\":\"$event\"}"; sep=','; done; printf '%s]}' "$out"; }
# jobs <run id> <name=status[/conclusion]>...  -> that run's jobs
jobs() { local id="$1" out='{"jobs":[' sep='' j s; shift; for j in "$@"; do s="${j##*=}"; out+="$sep{\"name\":\"${j%=*}\",\"status\":\"${s%%/*}\",\"conclusion\":\"${s#*/}\"}"; sep=','; done; reply "repos/o/r/actions/runs/$id/jobs" "$out]}"; }
cancelled() { grep '^run cancel' "$FAKE/calls" | awk '{ print $3 }' | sort -n | paste -sd' ' -; }
supersede() { : > "$FAKE/calls"; "$root/scripts/lib/cancel_superseded.sh" "$1" >/dev/null; cancelled; }

rm -rf "$FAKE"; mkdir -p "$FAKE"
reply "repos/o/r/actions/workflows/$MAIN" "$(runs 10:in_progress:push 11:queued:push 12:in_progress:push 13:in_progress:push 14:queued:push 9:completed:push)"
jobs 10 'base=completed/success' 'gates / plan=completed/success' 'gates / build=in_progress'
jobs 11
jobs 12 'base=completed/success' 'gates / plan=completed/success' 'gates / build=queued' 'gates / unit=queued'
reply "repos/o/r/actions/workflows/$FULL" "$(runs 20:in_progress:push)"
check "older ci runs with no gate started go, a started one stays" "11 12" "$(supersede 13)"

reply "repos/o/r/actions/workflows/$MAIN" "$(runs 13:in_progress:push)"
reply "repos/o/r/actions/workflows/$FULL" "$(runs 21:pending:push 20:in_progress:push 19:queued:workflow_dispatch)"
jobs 20 'moved=completed/success' 'gates / plan=completed/success' 'gates / build=queued'
check "a full run whose gates all wait gives way to the newer one" "20" "$(supersede 13)"

jobs 20 'moved=completed/success' 'gates / plan=completed/success' 'gates / build=completed/failure'
check "a full run that has run a gate is left to finish"           ""   "$(supersede 13)"

reply "repos/o/r/actions/workflows/$FULL" "$(runs 20:in_progress:schedule)"
jobs 20 'moved=completed/success' 'gates / plan=queued'
check "the only full run is never cancelled"                       ""   "$(supersede 13)"

if [ "$failures" -gt 0 ]; then
  echo "ci runs: $failures check(s) failed" >&2
  exit 1
fi
echo "ci runs: all checks passed"

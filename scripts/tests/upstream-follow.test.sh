#!/usr/bin/env bash
# gate-inputs: scripts/upstream/(follow|issue|main-already-red)\.sh$
# What upstream-follow.yml leans on, against a fake `gh` that answers from saved API replies:
# follow.sh names its followers and refuses one it does not have; issue.sh --close closes only the open
# issue of that exact title; main-already-red.sh calls a red main's only when EVERY red gate is red on
# main's last decided full run too — anything else must open the follower's issue.
#
# This decides, with nobody reading it, whether a person is asked. Calling upstream's red main's would
# silence a broken release; the other way round is the daily false alarm it replaces.
#
# Usage:  scripts/tests/upstream-follow.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-upstream-follow.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s (expected %s, got %s)\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}

# The fake: `gh api <url> --jq <expr>` runs jq over $FAKE/<url as a file name>.json; `gh issue list` prints
# $FAKE/issues.json; every call is logged.
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
    jq -r "$expr" "$FAKE/$(printf '%s' "${url%%\?*}" | tr -c 'A-Za-z0-9\n' '_').json" ;;
  issue)
    if [ "$2" = list ]; then cat "$FAKE/issues.json"; fi ;;
esac
FAKE
chmod +x "$tmp/bin/gh"
export PATH="$tmp/bin:$PATH" FAKE="$tmp/fake" GITHUB_REPOSITORY=o/r
reply() { mkdir -p "$FAKE"; printf '%s' "$2" > "$FAKE/$(printf '%s' "$1" | tr -c 'A-Za-z0-9\n' '_').json"; }
jobs() { local out='{"jobs":[' sep=''; for j in "$@"; do out+="$sep{\"name\":\"${j%=*}\",\"conclusion\":\"${j##*=}\"}"; sep=','; done; printf '%s]}' "$out"; }

echo "==> upstream follow"

listed="$("$root/scripts/upstream/follow.sh" --list)"
check "--list names MDN and CLDR"             2 "$(grep -cxE 'mdn|cldr' <<<"$listed")"
status=0; "$root/scripts/upstream/follow.sh" no-such 2>/dev/null || status=$?
check "an unknown follower is refused"        2 "$status"
check "Flux is measured in its own job, not listed" 0 "$(grep -cxE 'flux|flux-parity' <<<"$listed" || true)"

rm -rf "$FAKE"; mkdir -p "$FAKE"
printf '[{"number":7,"title":"Upstream: mdn could not be followed","state":"CLOSED"},{"number":9,"title":"Upstream: mdn could not be followed!","state":"OPEN"}]' > "$FAKE/issues.json"
"$root/scripts/upstream/issue.sh" --close "Upstream: mdn could not be followed" "Followed"
check "--close leaves a closed or other-titled issue" 0 "$(grep -c '^issue close' "$FAKE/calls" || true)"
printf '[{"number":8,"title":"Upstream: mdn could not be followed","state":"OPEN"}]' > "$FAKE/issues.json"
"$root/scripts/upstream/issue.sh" --close "Upstream: mdn could not be followed" "Followed"
check "--close closes the open one"           "issue close 8 --comment Followed" "$(grep '^issue close' "$FAKE/calls")"

verdict() {
  rm -rf "$FAKE"
  reply repos/o/r/actions/runs/1/jobs "$1"
  reply repos/o/r/actions/workflows/full.yml/runs "{\"workflow_runs\":[{\"id\":2,\"conclusion\":\"cancelled\"},{\"id\":3,\"conclusion\":\"$2\"}]}"
  reply repos/o/r/actions/runs/3/jobs "$3"
  if "$root/scripts/upstream/main-already-red.sh" 1 "mdn / " >/dev/null; then echo main; else echo upstream; fi
}

ours="$(jobs 'mdn / follow=success' 'mdn / gates / benchmarks=failure' 'mdn / gates / templates=failure' 'cldr / gates / unit (1/2)=failure')"
check "every red gate red on main is main's"  main     "$(verdict "$ours" failure "$(jobs 'gates / benchmarks=failure' 'gates / templates=failure' 'gates / unit (1/2)=success')")"
check "one red gate main has not is upstream's" upstream "$(verdict "$ours" failure "$(jobs 'gates / benchmarks=failure' 'gates / templates=success')")"
check "a green main makes every red upstream's" upstream "$(verdict "$ours" success "$(jobs)")"
check "a red outside the gates is the follower's" upstream "$(verdict "$(jobs 'mdn / land=failure')" failure "$(jobs 'gates / benchmarks=failure')")"

if [ "$failures" -gt 0 ]; then
  echo "upstream follow: $failures check(s) failed" >&2
  exit 1
fi
echo "upstream follow: all checks passed"

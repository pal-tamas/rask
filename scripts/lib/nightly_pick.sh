#!/usr/bin/env bash
# Which commit a nightly publishes. nightly.yml's `pick` job.
#
# A nightly no longer waits for main's whole ci run: it starts when that run STARTS (workflow_run
# `requested`), packs the commit while the run builds, and pushes the moment the run's `build` job — the
# build and the unit tests — is green (scripts/lib/nightly_wait.sh). Browser E2E, templates and the CLI
# build may still be going; a red one there is fixed forward by the next push. Restore at 1.0: publish only
# from a ci run that passed whole (docs/development-workflow.md, "Loosened until 1.0").
#
#   workflow_run requested    EARLY: main's push run of ci for <head> has started. Publish <head>, after its
#                             build (`wait=true`), unless the last nightly already holds it.
#   workflow_run completed,   the BACKSTOP, for an early nightly that never came or did not finish: the newest
#   schedule                  commit of main whose push run has a green build — or passed whole, which a scoped
#                             run that reached no code does with no build job — unless the last nightly holds it.
#                             Nothing while another nightly run is going: it publishes something at least as new,
#                             and a backstop for an older commit would cancel its pack (nightly.yml, concurrency).
#   workflow_dispatch         by hand: main's head, verdict or not (main only).
#
# "The last nightly" is the rolling `nightly` release, titled "Nightly <version> (<sha>)": the tag cannot always
# point at the commit (scripts/lib/nightly_anchor.sh), so the title is what is read.
#
# Writes sha= (empty: publish nothing), run= (the ci run to wait for) and wait= to $GITHUB_OUTPUT, and why to
# stdout and the run page.
#
# Usage:  EVENT=… ACTION=… HEAD_SHA=… CI_RUN=… CI_EVENT=… scripts/lib/nightly_pick.sh
#         (the workflow_run fields from the event; needs gh, GITHUB_REPOSITORY, GITHUB_REF, GITHUB_SHA, GITHUB_RUN_ID)
set -euo pipefail

repo="$GITHUB_REPOSITORY"
api() { gh api "$@" 2>/dev/null || true; }

# build_passed <ci run id> -> true when that run's build gate is green
build_passed() {
  [ "$(api --paginate "repos/$repo/actions/runs/$1/jobs?per_page=100" \
        --jq '[.jobs[] | select(.name == "gates / build" and .conclusion == "success")] | length' \
      | awk '{ n += $1 } END { print n + 0 }')" -gt 0 ]
}

published="$(api "repos/$repo/releases/tags/nightly" --jq '.name // ""' \
  | sed -n 's/.*(\([0-9a-f]\{40\}\))$/\1/p')"

# holds <sha> -> true when the last nightly already holds that commit (it is that commit, or after it)
holds() {
  [ -n "$published" ] || return 1
  case "$(api "repos/$repo/compare/$published...$1" --jq '.status // ""')" in
    identical | behind) return 0 ;;
    *) return 1 ;;
  esac
}

# another_in_flight -> true when a nightly run other than this one is queued or going
another_in_flight() {
  local status n
  for status in queued waiting pending in_progress; do
    n="$(api "repos/$repo/actions/workflows/nightly.yml/runs?status=$status&per_page=50" \
      --jq "[.workflow_runs[] | select(.id != ${GITHUB_RUN_ID:-0})] | length")"
    [ "${n:-0}" -gt 0 ] && return 0
  done
  return 1
}

sha=""
run=""
wait=false
if [ "$EVENT" = "workflow_dispatch" ]; then
  if [ "$GITHUB_REF" != "refs/heads/main" ]; then
    # Run by hand from another branch -- a ci/** one, say -- it would publish that branch's head.
    say="Nothing published: a nightly is packed from main only, and this run was dispatched on \`$GITHUB_REF\`."
  else
    sha="$GITHUB_SHA"
    say="Publishing \`$sha\` by hand: no ci run was asked about it."
  fi
elif [ "$EVENT" = "workflow_run" ] && [ "${ACTION:-}" = "requested" ]; then
  if [ "${CI_EVENT:-}" != "push" ]; then
    say="Nothing published: ci run ${CI_RUN:-?} is a ${CI_EVENT:-?} run, not a push to main."
  elif holds "$HEAD_SHA"; then
    say="Nothing published: the last nightly (\`$published\`) already holds \`$HEAD_SHA\`."
  else
    sha="$HEAD_SHA"
    run="$CI_RUN"
    wait=true
    say="Packing \`$sha\` while ci run $run builds it; pushed once its build job is green (the last nightly is ${published:-none})."
  fi
elif another_in_flight; then
  say="Nothing published: another nightly run is going, and it publishes a commit at least as new."
else
  candidate=""
  while read -r id head conclusion; do
    [ -n "$id" ] || continue
    if [ "$conclusion" = "success" ] || build_passed "$id"; then
      candidate="$head"
      run="$id"
      break
    fi
  done <<<"$(api "repos/$repo/actions/workflows/ci.yml/runs?branch=main&event=push&per_page=20" \
    --jq '.workflow_runs[] | "\(.id) \(.head_sha) \(.conclusion // "")"')"

  if [ -z "$candidate" ]; then
    run=""
    say="Nothing published: no recent push run of ci on main has a green build."
  elif holds "$candidate"; then
    run=""
    say="Nothing published: \`$candidate\` is the newest commit of main with a green build, and the last nightly (\`$published\`) holds it."
  else
    sha="$candidate"
    say="Publishing \`$sha\`, the newest commit of main with a green build (ci run $run; the last nightly is ${published:-none})."
  fi
fi

# On the run page, because the run list shows main's head -- not the commit that is packed,
# and not whether anything was.
echo "$say"
[ -z "${GITHUB_STEP_SUMMARY:-}" ] || echo "$say" >> "$GITHUB_STEP_SUMMARY"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  { echo "sha=$sha"; echo "run=$run"; echo "wait=$wait"; } >> "$GITHUB_OUTPUT"
fi

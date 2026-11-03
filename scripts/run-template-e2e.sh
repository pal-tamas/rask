#!/usr/bin/env bash
# The TEMPLATE gate: scaffold every template `rask new` offers and build what it wrote.
#
# Opt-in, like every other heavyweight here (CLI build, watch, deploy, installer) and for the same
# reason: it packs this commit's Rask packages, then restores and builds each scaffolded project, which is
# minutes. CI runs it as its "templates" job, and as two "front end K/2" jobs that each take half the
# front ends (.github/workflows/gates.yml); run it by hand to reproduce either.
#
# Three tiers, because the costs differ by an order of magnitude:
#
#   scripts/run-template-e2e.sh                    scaffold and build every template's C# half, and
#                                                  npm-install and BUNDLE one scaffolded island per runtime
#   scripts/run-template-e2e.sh --front-end=<key>  ONE front-end template (react, vue, …) end to end:
#                                                  npm ci and the bundle through `dotnet publish`, lint,
#                                                  format check, then the published app's page and its
#                                                  starter query. Minutes each. A comma-separated list
#                                                  (--front-end=react,vue) runs them one after another
#                                                  over ONE packed feed, with a verdict per front end.
#   scripts/run-template-e2e.sh --front-end        everything: the first tier, every front end one after
#                                                  another, and the server journey in a browser
#
#   scripts/run-template-e2e.sh --list-front-ends  the keys --front-end= takes, one per line. CI's job
#                                                  matrix and scripts/lib/affected_gates.py go by the
#                                                  same rule, so a new template is no workflow edit.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# A front-end template is a tree with a committed npm client. SpaFramework.All is the CLI's list of
# the same thing; TemplateTreeContractTests holds the two together.
front_ends() {
  local manifest
  for manifest in "$root"/src/Rask.Templates/*/client/package.json; do
    if [ -f "$manifest" ]; then basename "$(dirname "$(dirname "$manifest")")"; fi
  done
}

front_end=0
only=""
filter=""
for arg in "$@"; do
  case "$arg" in
    --list-front-ends) front_ends; exit 0 ;;
    --front-end)   front_end=1 ;;
    --front-end=*) only="${arg#--front-end=}" ;;
    --filter=*)    filter="${arg#--filter=}" ;;
    *) echo "usage: $0 [--front-end[=<key>[,<key>…]]] [--list-front-ends] [--filter=<vstest filter>]" >&2; exit 2 ;;
  esac
done

# Captured first: `front_ends | grep -q` under pipefail fails on the SIGPIPE grep's early exit sends.
known="$(front_ends)"
# --front-end=<key>[,<key>…]: several front ends in ONE test run, so the feed is packed once for all
# of them (CliBuildE2E.LocalFeed is per process) and they are scaffolded one after another. CI's two
# "front end" jobs run that way; each used to be a job of its own that packed the feed again.
if [ -n "$only" ]; then
  for key in ${only//,/ }; do
    if ! grep -qxF -- "$key" <<<"$known"; then
      echo "$0: '$key' is not a front-end template. Known: $(tr '\n' ' ' <<<"$known")" >&2
      exit 2
    fi
  done
fi

# shellcheck source=lib/node-path.sh
. "$root/scripts/lib/node-path.sh"
rask_ensure_node
# shellcheck source=lib/dotnet-env.sh
. "$root/scripts/lib/dotnet-env.sh"
cd "$root"

export RASK_TEMPLATE_E2E=1
export RASK_CLI_BUILD_E2E=1   # the local-feed plumbing is shared with the CLI build gate

# A gate-private package cache. The feed is packed at this commit's version and MinVer stamps the same
# version for an uncommitted tree, so a shared global cache would serve a stale copy of a package this
# run just built — the trap recorded in CliBuildE2E.EvictFromGlobalCache.
export NUGET_PACKAGES="$root/artifacts/template-gate-packages"
mkdir -p "$NUGET_PACKAGES"

if [ -n "$only" ]; then
  export RASK_TEMPLATE_FRONTEND_E2E=1
  # Read by the theory data (TemplateSelection): a VSTest filter cannot name one case of a theory.
  export RASK_TEMPLATE_ONLY="$only"
  filter="${filter:-FullyQualifiedName~.FrontEndBuildE2ETests}"
  echo "==> Template gate, front end(s) $only: publish with its client, lint, format check, serve."
elif [ "$front_end" -eq 1 ]; then
  export RASK_TEMPLATE_FRONTEND_E2E=1
  echo "==> Template gate (build + every front end in turn + the server journey in a browser)."
else
  echo "==> Template gate (build only). Pass --front-end=<key> for one front end, end to end."
fi

args=(test tests/Rask.Templates.E2E.Tests/Rask.Templates.E2E.Tests.csproj -c Release
      -p:MinVerSkip=true --logger "console;verbosity=normal")   # normal: each case's time is in the log

if [ -n "$filter" ]; then
  args+=(--filter "$filter")
  echo "    filter: $filter (NOT the full gate)"
fi

if [ -z "$only" ]; then
  dotnet "${args[@]}"
  echo "==> Template gate passed."
  exit 0
fi

# One verdict per front end, as each used to have a job of its own: read off the theory's cases, printed
# last, and written to the job's summary on CI. A front end with no line did not run at all (the pack, or
# the run, failed before it), which is red too.
test_log="$(mktemp -t rask-template-e2e.XXXXXX)"
trap 'rm -f "$test_log"' EXIT
set +e
dotnet "${args[@]}" 2>&1 | tee "$test_log"
status=${PIPESTATUS[0]}
set -e

summary="| Front end | Verdict |
|---|---|"
for key in ${only//,/ }; do
  case_line="$(grep -E "^ *(Passed|Failed|Skipped) .*\(key: \"$key\"\)" "$test_log" | tail -1 || true)"
  verdict="$(awk '{ print $1 }' <<<"$case_line")"
  verdict="${verdict:-did not run}"
  if [ "$verdict" != "Passed" ] && [ "$status" -eq 0 ]; then status=1; fi
  echo "==> front end $key: $verdict"
  summary="$summary
| \`$key\` | $verdict |"
done
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  printf '%s\n' "$summary" >>"$GITHUB_STEP_SUMMARY"
fi

[ "$status" -eq 0 ] && echo "==> Template gate passed."
exit "$status"

#!/usr/bin/env bash
# The TEMPLATE gate: scaffold every template `rask new` offers, build what it wrote, publish the server
# template in Release, and run it in a browser.
#
# Opt-in, like every other heavyweight here (CLI build, watch, deploy, installer) and for the same
# reason: it packs this commit's Rask packages, then restores and builds each scaffolded project, which is
# minutes. CI runs it as its "templates" job (.github/workflows/gates.yml); run it by hand to reproduce it.
#
#   scripts/run-template-e2e.sh                          the whole gate
#   scripts/run-template-e2e.sh --template=<key>[,<key>]  only those templates' cases (RASK_TEMPLATE_ONLY)
#   scripts/run-template-e2e.sh --filter=<vstest filter>  only the tests the filter names
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

only=""
filter=""
for arg in "$@"; do
  case "$arg" in
    --template=*) only="${arg#--template=}" ;;
    --filter=*)   filter="${arg#--filter=}" ;;
    *) echo "usage: $0 [--template=<key>[,<key>…]] [--filter=<vstest filter>]" >&2; exit 2 ;;
  esac
done

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
  # Read by the theory data (TemplateSelection): a VSTest filter cannot name one case of a theory.
  export RASK_TEMPLATE_ONLY="$only"
  echo "==> Template gate, template(s) $only."
else
  echo "==> Template gate."
fi

args=(test tests/Rask.Templates.E2E.Tests/Rask.Templates.E2E.Tests.csproj -c Release
      -p:MinVerSkip=true --logger "console;verbosity=normal")   # normal: each case's time is in the log

if [ -n "$filter" ]; then
  args+=(--filter "$filter")
  echo "    filter: $filter (NOT the full gate)"
fi

dotnet "${args[@]}"
echo "==> Template gate passed."

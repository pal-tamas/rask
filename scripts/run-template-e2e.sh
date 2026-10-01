#!/usr/bin/env bash
# The TEMPLATE gate: scaffold every template `rask new` offers and build what it wrote.
#
# Opt-in, like every other heavyweight here (CLI build, watch, deploy, installer) and for the same
# reason: it packs this commit's Rask packages, then restores and builds each scaffolded project, which is
# minutes rather than the one the hooks are held to. Run it by hand and before a release.
#
# Two tiers, because the costs differ by an order of magnitude:
#
#   scripts/run-template-e2e.sh              scaffold and build server, wasm and wasm-hosted
#   scripts/run-template-e2e.sh --front-end  also build and RUN the scaffolded app in a browser (server
#                                            journey)
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

front_end=0
filter=""
for arg in "$@"; do
  case "$arg" in
    --front-end) front_end=1 ;;
    --filter=*)  filter="${arg#--filter=}" ;;
    *) echo "usage: $0 [--front-end] [--filter=<vstest filter>]" >&2; exit 2 ;;
  esac
done

export RASK_TEMPLATE_E2E=1
export RASK_CLI_BUILD_E2E=1   # the local-feed plumbing is shared with the CLI build gate

# A gate-private package cache. The feed is packed at this commit's version and MinVer stamps the same
# version for an uncommitted tree, so a shared global cache would serve a stale copy of a package this
# run just built — the trap recorded in CliBuildE2E.EvictFromGlobalCache.
export NUGET_PACKAGES="$root/artifacts/template-gate-packages"
mkdir -p "$NUGET_PACKAGES"

if [ "$front_end" -eq 1 ]; then
  export RASK_TEMPLATE_FRONTEND_E2E=1
  echo "==> Template gate (build + the server journey in a browser)."
else
  echo "==> Template gate (build only). Pass --front-end to also run the server journey in a browser."
fi

args=(test tests/Rask.Templates.E2E.Tests/Rask.Templates.E2E.Tests.csproj -c Release
      -p:MinVerSkip=true --logger "console;verbosity=minimal")

if [ -n "$filter" ]; then
  args+=(--filter "$filter")
  echo "    filter: $filter (NOT the full gate)"
fi

dotnet "${args[@]}"
echo "==> Template gate passed."

#!/usr/bin/env bash
# The TEMPLATE gate: scaffold every template `rask new` offers and build what it wrote.
#
# Opt-in, like every other heavyweight here (CLI build, watch, deploy, installer) and for the same
# reason: it packs this commit's Rask packages, restores and builds fifteen projects, which is minutes
# rather than the one the hooks are held to. Run it by hand and before a release.
#
# Why it exists: eleven of the fifteen templates had nothing proving they compile. Only server, wasm
# and react were ever scaffolded-and-built, plus angular for its Tailwind output, and NO meta template
# was built by anything — the meta lane's only gate publishes a hand-written stub csproj against
# stand-in files, so a real Nuxt or SvelteKit app compiling was never checked anywhere.
#
# Two tiers, because the costs differ by two orders of magnitude:
#
#   scripts/run-template-e2e.sh              the C# half of all fifteen
#   scripts/run-template-e2e.sh --front-end  plus each client's real npm ci, lint and production
#                                            build — 4.5-6 minutes PER template on a cold cache
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
  echo "==> Template gate (C# + front ends). This installs and builds thirteen front ends; expect an hour."
else
  echo "==> Template gate (C# half). Pass --front-end to also install and build every client."
fi

args=(test tests/Rask.Templates.E2E.Tests/Rask.Templates.E2E.Tests.csproj -c Release
      -p:MinVerSkip=true --logger "console;verbosity=minimal")

if [ -n "$filter" ]; then
  args+=(--filter "$filter")
  echo "    filter: $filter (NOT the full gate)"
fi

dotnet "${args[@]}"
echo "==> Template gate passed."

#!/usr/bin/env bash
# The island-props follower: re-read the props of the site's package islands (src/Rask.Site/**/*.props.json)
# from the npm packages its package.json installs, with the pinned TypeScript 6 compiler. The site's own
# build never does (RaskExternalPropsExtract=false in Rask.Site.csproj); this turns it back on, and unlocks
# the snapshots CI otherwise only compares (RaskExternalPropsLocked), for one build.
#
# Usage:  scripts/upstream/island-props.sh   (scripts/upstream/follow.sh island-props runs it)
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root"

# -m:2, no node reuse: the shared-machine budget every script here keeps.
dotnet build src/Rask.Site -c Release -m:2 -nodeReuse:false -nologo -clp:NoSummary \
  -p:RaskExternalPropsExtract=true -p:RaskExternalPropsLocked=false -p:TreatWarningsAsErrors=false

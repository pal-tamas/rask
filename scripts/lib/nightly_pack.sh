#!/usr/bin/env bash
# Packs the commit checked out in the current directory as its nightly version. nightly.yml's `prepare` job.
#
# The version comes from THIS script's nightly_version.py (main's, in nightly.yml: a sparse checkout beside
# the tree being packed), read out of the history the checkout already holds, so nothing lands in the tree
# being packed. MinVer's own height can repeat across a merge that lands a worktree branch; this one is
# distinct for every commit of main, and only grows. A re-run packs the same commit as the same version.
#
# ONE pack, for the whole solution — see release.yml for why the hand-written list went.
# MinVerVersionOverride stamps the package AND assembly versions, as MinVer would have.
# RunAnalyzers=false: the analyzers change no byte of a package, and ci's `analyzers` job runs them over the
# same commit; the pack is on the way to a nightly, which waits for it. Restore at 1.0 together with the
# nightly waiting for the whole run (docs/development-workflow.md, "Loosened until 1.0").
#
# Usage:  scripts/lib/nightly_pack.sh <output directory>   (writes version= to $GITHUB_OUTPUT)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
out="${1:?usage: nightly_pack.sh <output directory>}"
sha="$(git rev-parse HEAD)"
version="$(python3 -I "$here/nightly_version.py" . --commit "$sha")"

echo "Packing \`$sha\` as **$version**."
[ -z "${GITHUB_STEP_SUMMARY:-}" ] || echo "Packing \`$sha\` as **$version**." >> "$GITHUB_STEP_SUMMARY"
[ -z "${GITHUB_OUTPUT:-}" ] || echo "version=$version" >> "$GITHUB_OUTPUT"

dotnet pack Rask.slnx -c Release -o "$out" -p:MinVerVersionOverride="$version" -p:RunAnalyzers=false

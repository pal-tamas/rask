#!/usr/bin/env bash
# The nightly version of the commit checked out in the current directory. nightly.yml's `prepare` job runs
# it right before its `dotnet pack Rask.slnx`, which stays in the workflow itself where
# PackageDependencyTests can see it: the solution is packed, never a hand-written list (#1044).
#
# The version comes from THIS script's nightly_version.py (main's, in nightly.yml: extracted beside the
# tree being packed), read out of the history the checkout already holds, so nothing lands in the tree
# being packed. MinVer's own height can repeat across a merge that lands a worktree branch; this one is
# distinct for every commit of main, and only grows. A re-run packs the same commit as the same version.
#
# Usage:  scripts/lib/nightly_pack.sh   (prints the version; writes version= to $GITHUB_OUTPUT)
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
sha="$(git rev-parse HEAD)"
version="$(python3 -I "$here/nightly_version.py" . --commit "$sha")"

echo "Packing \`$sha\` as **$version**." >&2
[ -z "${GITHUB_STEP_SUMMARY:-}" ] || echo "Packing \`$sha\` as **$version**." >> "$GITHUB_STEP_SUMMARY"
[ -z "${GITHUB_OUTPUT:-}" ] || echo "version=$version" >> "$GITHUB_OUTPUT"
echo "$version"

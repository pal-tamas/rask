#!/usr/bin/env python3
"""The version a nightly packs a commit as: MinVer's, with a height that no other commit on main shares.

    nightly_version.py [<repository>] [--commit HEAD]

Why it exists (2026-10-26): MinVer's height is the depth at which its first-parent-first walk meets the
tag. A merge whose FIRST parent is a worktree branch -- how every land on main ends, a branch that merged
main pushed as main's new head -- reaches the tag through that branch, and can come out at the very height
of the commit main held before it. 1ecef2685 and the merge on top of it, cc68a07dd, were both
`0.23.1-alpha.0.526`; the second nightly's `--skip-duplicate` push kept the first one's packages, and
nuget.org served one version packed from two commits.

The height here is `git rev-list --count <tag>..<commit>`: every commit the tag does not reach. A commit
counts all its ancestors, so one that descends from another -- main's next head always does, main only
moves forward -- counts strictly more. Distinct and increasing along main, whatever the merges look like.
Counting the first-parent chain would not do: it is the same 529 for both commits above.

The rest is MinVer's rule, so a tag still decides the line:
  the commit is tagged vX.Y.Z[-pre]   that version, unchanged
  newest tag below it is vX.Y.Z       X.Y.(Z+1)-alpha.0.<height>
  newest tag below it is vX.Y.Z-pre   X.Y.Z-pre.<height>
"""

from __future__ import annotations

import argparse
import re
import subprocess
import sys

TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$")   # MinVerTagPrefix is `v`
DEFAULT_PRERELEASE = "alpha.0"                                    # MinVer's MinVerDefaultPreReleaseIdentifiers


class VersionError(Exception):
    """No version can be given to the commit."""


def git(repository: str, *args: str) -> str:
    return subprocess.run(["git", "-C", repository, *args], check=True, capture_output=True, text=True).stdout


def precedence(tag: str) -> tuple:
    """SemVer order: a release sorts above its prereleases, numeric identifiers as numbers."""
    major, minor, patch, pre = TAG.match(tag).groups()
    identifiers = tuple((0, int(p), "") if p.isdigit() else (1, 0, p) for p in pre.split(".")) if pre else ()
    return (int(major), int(minor), int(patch), 0 if pre else 1, identifiers)


def version_of(tag: str, height: int) -> str:
    major, minor, patch, pre = TAG.match(tag).groups()
    if height == 0:
        return tag[1:]
    if pre:
        return f"{major}.{minor}.{patch}-{pre}.{height}"
    return f"{major}.{minor}.{int(patch) + 1}-{DEFAULT_PRERELEASE}.{height}"


def nightly_version(repository: str, commit: str = "HEAD") -> str:
    tags = [t for t in git(repository, "tag", "--merged", commit, "--list", "v*").split() if TAG.match(t)]
    if not tags:
        raise VersionError(f"no version tag is an ancestor of {commit}")
    tag = max(tags, key=precedence)
    height = int(git(repository, "rev-list", "--count", f"{tag}..{commit}").strip())
    return version_of(tag, height)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.partition("\n")[0])
    parser.add_argument("repository", nargs="?", default=".")
    parser.add_argument("--commit", default="HEAD")
    args = parser.parse_args()
    try:
        print(nightly_version(args.repository, args.commit))
    except (VersionError, subprocess.CalledProcessError) as problem:
        print(f"no nightly version: {problem}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())

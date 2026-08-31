#!/usr/bin/env python3
"""Pick the versions of one package that a release supersedes.

Policy (chosen deliberately, see docs/development-workflow.md): when a version is released, every
OLDER version of that package is unlisted, prereleases and previous stables alike, so the gallery
shows the current release and nothing else. Unlisting is not deletion -- nuget.org keeps every
version restorable by exact reference, so a pinned PackageReference is unaffected.

Ordering is real semver, not lexical: 0.20.1-alpha.0.9 must sort BELOW 0.20.1-alpha.0.10, and any
prerelease must sort below the stable it leads to. Getting that wrong would either leave alphas
listed forever or, far worse, unlist a version NEWER than the one being released.

Reads the version list on stdin (one per line), takes the released version as argv[1], and writes
the versions to unlist to stdout. Anything newer than the released version is left alone.
"""
import sys


def parse(version):
    """(release-tuple, prerelease-key) for semver comparison. Build metadata is ignored."""
    v = version.strip().split('+', 1)[0]
    core, _, pre = v.partition('-')
    nums = []
    for part in core.split('.'):
        try:
            nums.append(int(part))
        except ValueError:
            nums.append(0)
    while len(nums) < 3:
        nums.append(0)
    # A version with NO prerelease outranks one with a prerelease of the same core, so it gets a
    # sentinel that sorts above every identifier list.
    if not pre:
        return (tuple(nums[:3]), (1,))
    key = [0]
    for ident in pre.split('.'):
        if ident.isdigit():
            # Numeric identifiers compare numerically and rank below alphanumeric ones.
            key.append((0, int(ident), ''))
        else:
            key.append((1, 0, ident))
    return (tuple(nums[:3]), tuple(key))


def is_prerelease(version):
    return '-' in version.strip().split('+', 1)[0]


def older_than(released, versions):
    released = released.strip()
    target = parse(released)
    # A prerelease never retires a stable. By semver 0.21.0 IS older than 0.21.1-alpha.0.1, so the
    # plain comparison below would have a nightly unlist the current stable release the moment it ran.
    # This script is wired to release.yml (stable tags) and not to nightly.yml, so the case should not
    # arise -- but "should not arise" is how the current release disappears from the gallery.
    keep_stables = is_prerelease(released)
    out = []
    for v in versions:
        v = v.strip()
        if not v or v == released:
            continue
        if keep_stables and not is_prerelease(v):
            continue
        if parse(v) < target:
            out.append(v)
    return out


def main():
    if len(sys.argv) != 2:
        print('usage: unlist_select.py <released-version>  (versions on stdin)', file=sys.stderr)
        return 2
    for v in older_than(sys.argv[1], sys.stdin.read().splitlines()):
        print(v)
    return 0


if __name__ == '__main__':
    sys.exit(main())

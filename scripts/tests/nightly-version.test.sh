#!/usr/bin/env bash
# Tests for scripts/lib/nightly_version.py -- the version a nightly packs a commit as.
#
# What it pins is 2026-10-26: 1ecef2685 and the merge cc68a07dd on top of it were both packed as
# 0.23.1-alpha.0.526, because MinVer's height follows a merge's FIRST parent, and the merge that lands a
# worktree branch on main has the branch there, not main. The repository below is built in that shape.
#
# Usage:  scripts/tests/nightly-version.test.sh   (run by scripts/run-unit-local.sh)
set -uo pipefail

root="$(git rev-parse --show-toplevel)"
nightly="$root/scripts/lib/nightly_version.py"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
checked=0
ok()   { checked=$((checked + 1)); printf '  ok   %s\n' "$1"; }
fail() { checked=$((checked + 1)); failures=$((failures + 1)); printf '  FAIL %s\n%s\n' "$1" "${2:-}" | sed '2,$s/^/       /' >&2; }
same() { if [ "$2" = "$3" ]; then ok "$1"; else fail "$1" "got:      $2
expected: $3"; fi; }

repo="$work/repo"
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_NOSYSTEM=1
export GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@example.invalid GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@example.invalid
g() { git -C "$repo" "$@" >/dev/null 2>&1; }
commit() { g commit --allow-empty -q -m "$1"; }
version() { python3 -I "$nightly" "$repo" --commit "$1" 2>&1; }

mkdir -p "$repo"; g init -q -b main
commit "base"; g tag v0.1.0
commit "released"; g tag -a v0.23.0 -m "v0.23.0"   # annotated, as the release tags are

echo "==> nightly_version: MinVer's rule decides the line"
same "a tagged commit is its tag" "$(version v0.23.0)" "0.23.0"
commit "m1"
same "after a release tag: the next patch, alpha.0, height" "$(version HEAD)" "0.23.1-alpha.0.1"
commit "m2"
same "the newest tag wins, not the nearest one named" "$(version HEAD)" "0.23.1-alpha.0.2"

echo "==> nightly_version: a merge that lands a worktree branch gets a version of its own"
# A worktree branch forks from m1, works, and merges main (m2) in: first parent the branch, second main.
# Pushed as main, the merge is main's next head -- the shape of cc68a07dd over 1ecef2685.
main_head="$(git -C "$repo" rev-parse HEAD)"
g checkout -q -b worktree HEAD~1
commit "w1"
g merge -q --no-ff -m "Merge branch 'main' into worktree" main
merge="$(git -C "$repo" rev-parse HEAD)"
same "the merge's first parent is the branch" "$(git -C "$repo" rev-parse "$merge^1^1")" "$(git -C "$repo" rev-parse main~1)"
before="$(version "$main_head")"; after="$(version "$merge")"
same "main's head before it" "$before" "0.23.1-alpha.0.2"
same "the merge counts every commit the tag does not reach, the branch's too" "$after" "0.23.1-alpha.0.4"

# A side branch's own commits are not on main's first-parent line, yet the merge still counts them,
# and a commit always counts more than any ancestor of its: main's next head can only go up.
g checkout -q main; g merge -q --ff-only worktree
commit "m3"
same "and the next commit on main goes on up" "$(version HEAD)" "0.23.1-alpha.0.5"

echo "==> nightly_version: a prerelease tag carries its own identifiers"
g tag v0.24.0-beta.1
same "a tagged prerelease is its tag" "$(version HEAD)" "0.24.0-beta.1"
commit "b1"
same "after it: the tag's prerelease, then the height" "$(version HEAD)" "0.24.0-beta.1.1"
g tag v0.24.0-beta.10 HEAD~1
same "identifiers compare as numbers: beta.10 is above beta.1" "$(version HEAD)" "0.24.0-beta.10.1"

echo "==> nightly_version: no tag, no version"
empty="$work/empty"; mkdir -p "$empty"; git -C "$empty" init -q -b main
git -C "$empty" commit --allow-empty -q -m "only"
python3 -I "$nightly" "$empty" >/dev/null 2>&1
same "a history with no version tag is refused" "$?" "2"

echo
if [ "$failures" -gt 0 ]; then
  echo "nightly-version: $failures of $checked checks FAILED" >&2
  exit 1
fi
echo "nightly-version: all $checked checks passed"

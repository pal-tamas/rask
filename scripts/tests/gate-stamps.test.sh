#!/usr/bin/env bash
# Tests for scripts/lib/gate_stamps.py — the record that lets a gate skip a test project that already
# passed on an unchanged tree.
#
# Like the scoper it leans on, this is only ever consulted to decide what NOT to run, so a bug here
# is a break that reaches main rather than a red gate. Every row below is therefore about the stamp
# being REFUSED: a changed input, an untracked file, a different SDK, a tree that no longer exists.
#
# Runs against a throwaway repository with three projects: src/A, tests/A.Tests (references A) and
# tests/B.Tests (references nothing).
set -uo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
stamps="$root/scripts/lib/gate_stamps.py"

# A hook hands its children the real repository's GIT_* variables; left set, every git call below
# would land there instead of in the throwaway one.
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES GIT_PREFIX

repo="$(mktemp -d -t rask-gate-stamps-test.XXXXXX)"
trap 'rm -rf "$repo"' EXIT

mkdir -p "$repo/src/A" "$repo/tests/A.Tests" "$repo/tests/B.Tests" "$repo/scripts/lib"
cp "$root/scripts/lib/affected_projects.py" "$stamps" "$repo/scripts/lib/"
stamps="$repo/scripts/lib/gate_stamps.py"
echo '<Project/>' >"$repo/src/A/A.csproj"
echo 'class A {}' >"$repo/src/A/A.cs"
echo '<Project><ItemGroup><ProjectReference Include="..\..\src\A\A.csproj"/></ItemGroup></Project>' >"$repo/tests/A.Tests/A.Tests.csproj"
echo '<Project/>' >"$repo/tests/B.Tests/B.Tests.csproj"
printf 'artifacts/\n' >"$repo/.gitignore"
git -C "$repo" init -q
git -C "$repo" add -A
git -C "$repo" -c user.name=t -c user.email=t@t commit -q -m init

a="tests/A.Tests/A.Tests.csproj"
b="tests/B.Tests/B.Tests.csproj"
failures=0
checks=0

tree() { python3 "$stamps" tree "$repo"; }
# The projects that may be skipped right now, on one line.
reuse() { printf '%s\n%s\n' "$a" "$b" | python3 "$stamps" reuse "$repo" "$(tree)" "${1:-sdk1}" | tr '\n' ' ' | sed 's/ *$//'; }
record_both() { printf '%s\n%s\n' "$a" "$b" | python3 "$stamps" record "$repo" "$(tree)" sdk1; }

# $1 label · $2 expected · $3 actual
check() {
  checks=$((checks + 1))
  if [ "$2" = "$3" ]; then
    echo "  ok   $1"
  else
    echo "  FAIL $1: expected '$2', got '$3'" >&2
    failures=$((failures + 1))
  fi
}

echo "==> gate_stamps.py"

check "nothing is reusable before anything passed" "" "$(reuse)"

record_both
check "an unchanged tree reuses both" "$a $b" "$(reuse)"
check "a different salt (another SDK) reuses nothing" "" "$(reuse sdk2)"

echo 'class A { int x; }' >"$repo/src/A/A.cs"
check "an UNSTAGED edit to A re-runs A.Tests, keeps B.Tests" "$b" "$(reuse)"

git -C "$repo" checkout -q -- src/A/A.cs
check "reverting the edit makes A.Tests reusable again" "$a $b" "$(reuse)"

echo 'class New {}' >"$repo/src/A/New.cs"
check "an UNTRACKED file in A re-runs A.Tests" "$b" "$(reuse)"
rm "$repo/src/A/New.cs"

echo 'x' >"$repo/tests/B.Tests/T.cs"
check "a file in B.Tests re-runs only B.Tests" "$a" "$(reuse)"
rm "$repo/tests/B.Tests/T.cs"

echo '# x' >>"$repo/scripts/lib/affected_projects.py"
check "a gate script change (FULL) reuses nothing" "" "$(reuse)"
git -C "$repo" checkout -q -- scripts/lib/affected_projects.py

mkdir -p "$repo/artifacts" && echo 'x' >"$repo/artifacts/out.log"
check "an ignored file changes nothing" "$a $b" "$(reuse)"

printf '%s\n%s\n' 0000000000000000000000000000000000000000 sdk1 >"$repo/artifacts/gate-stamps/tests__A.Tests__A.Tests.csproj.stamp"
echo 'class A { }' >"$repo/src/A/A.cs"
check "a stamp whose tree is gone is not trusted" "$b" "$(reuse)"

[ -z "$(git -C "$repo" status --porcelain --untracked-files=no | grep -v 'src/A/A.cs')" ]
check "asking never touches the real index" "0" "$?"

if [ "$failures" -ne 0 ]; then
  echo "gate-stamps.test.sh: $failures of $checks checks FAILED." >&2
  exit 1
fi
echo "gate-stamps.test.sh: $checks checks passed."

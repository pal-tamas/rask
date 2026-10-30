#!/usr/bin/env bash
# gate-inputs: ^\.gitattributes$|^Directory\.Build\.targets$|^tests/Rask\.Benchmarks/ClientBundleSizeReport\.cs$
# The files every branch touches merge without a conflict when two branches only ADD to them, and still
# conflict when two branches change the same thing.
#
#   CHANGELOG.md            merge=rask-append (.githooks/merge-append): both bullets kept, whole
#   client-bundle-size.csv  merge=union: both blocks kept; the gate reads the newest-dated block
#   llms.txt                one sentence per line, so edits to different sentences of a guide are
#                           different lines (git still calls two ADJACENT changed lines a conflict)
#
# Driven on copies of the real files in a throwaway repository, two branches merged as `git merge
# origin/main` would merge them. Touches nothing here.
#
# Usage:  scripts/tests/merge-friendly-files.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"

# git exports GIT_DIR and friends to whatever it runs; inherited, they would point every command below at
# THIS repository instead of the throwaway one (attribution-guard.test.sh has the full story).
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES \
  GIT_COMMON_DIR GIT_PREFIX 2>/dev/null || true

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0
check() { # name expected actual
  if [ "$2" = "$3" ]; then
    printf '  ok    %s\n' "$1"
  else
    printf '  FAIL  %s\n        expected: %s\n        actual:   %s\n' "$1" "$2" "$3"
    failures=$((failures + 1))
  fi
}

g() { git -C "$repo" -c user.name=gate -c user.email=gate@example.invalid -c commit.gpgsign=false \
  -c core.hooksPath=/dev/null "$@"; }

csv=tests/Rask.Benchmarks/Baselines/client-bundle-size.csv

# A fresh repository holding the real files, with the merge driver configured as the build configures it
# (pass "no-driver" to leave it out, as in a clone that has not built yet).
new_repo() {
  repo="$work/$1"
  mkdir -p "$repo/.githooks" "$repo/$(dirname "$csv")"
  cp "$root/.gitattributes" "$root/CHANGELOG.md" "$root/llms.txt" "$repo/"
  cp "$root/.githooks/merge-append" "$repo/.githooks/"
  cp "$root/$csv" "$repo/$csv"
  g init -q -b main
  if [ "${2:-}" != "no-driver" ]; then
    g config merge.rask-append.driver 'sh .githooks/merge-append %O %A %B %L'
  fi
  g add -A
  g commit -qm base
}

# Inserts lines (a file of them) after the first line equal to $2.
insert_after() { # file heading lines-file
  awk -v h="$2" -v f="$3" '{ print } !done && $0 == h { while ((getline l < f) > 0) print l; done = 1 }' \
    "$repo/$1" > "$repo/$1.tmp" && mv "$repo/$1.tmp" "$repo/$1"
}

# Appends $3 to line $2 of the file.
edit_line() { # file line suffix
  awk -v n="$2" -v s="$3" 'NR == n { $0 = $0 s } { print }' "$repo/$1" > "$repo/$1.tmp" \
    && mv "$repo/$1.tmp" "$repo/$1"
}

bullet() { # name — a bullet with a code fence, ending in the same line as the other branch's
  printf '%s\n' "- **$1 does a thing.** Before: it did not." "  \`\`\`csharp" "  var x = $1();" "  \`\`\`" \
    "  See [the guide](docs/forms.md)." > "$work/bullet-$1"
}

block() { # name rask.js rask-hooks.js
  printf '%s\n' "#" "# 2026-10-29, $1's change." "bundle,bytes" "rask.js,$2" "rask.wasm.js,96734" \
    "rask-hooks.js,$3" >> "$repo/$csv"
}

# The Forms guide's entry in llms.txt and its continuation lines: edit its 2nd and 4th lines.
forms="$(grep -n '^- \[Forms\](docs/forms.md)' "$root/llms.txt" | cut -d: -f1)"

echo "==> two branches that only add: no conflict, nothing lost"
new_repo additive
bullet Alpha && bullet Beta
g checkout -qb a
insert_after CHANGELOG.md "### Fixed" "$work/bullet-Alpha"
block Alpha 112500 120136
edit_line llms.txt $((forms + 1)) " (edited on a)"
g commit -qam a
g checkout -q main && g checkout -qb b
insert_after CHANGELOG.md "### Fixed" "$work/bullet-Beta"
block Beta 111971 121000
edit_line llms.txt $((forms + 3)) " (edited on b)"
g commit -qam b
check "the merge is clean" 0 "$(g merge -q a -m merge >/dev/null 2>&1; echo $?)"
check "no conflict markers" 0 "$(grep -c '^<<<<<<<\|^>>>>>>>' "$repo/CHANGELOG.md" "$repo/llms.txt" "$repo/$csv" \
  | awk -F: '{ s += $2 } END { print s }')"
for name in Alpha Beta; do
  # Whole: every line of the bullet, consecutive, in its own order — not spliced into the other one.
  check "$name's bullet is whole under ### Fixed" yes "$(awk -v f="$work/bullet-$name" '
    BEGIN { while ((getline l < f) > 0) want[++n] = l }
    { line[NR] = $0 } END { for (i = 1; i <= NR; i++) { ok = 1; for (j = 1; j <= n; j++) if (line[i + j - 1] != want[j]) { ok = 0; break }
      if (ok) { print "yes"; exit } } print "no" }' "$repo/CHANGELOG.md")"
done
check "both bundle blocks kept" 2 "$(grep -c "^# 2026-10-29, \(Alpha\|Beta\)'s change\.$" "$repo/$csv")"
check "both llms.txt edits kept" 2 "$(grep -c '(edited on [ab])$' "$repo/llms.txt")"
check "llms.txt differs from the base by those two lines only" "2 2" \
  "$(g diff main HEAD --numstat -- llms.txt | awk '{ print $1, $2 }')"

echo "==> two branches that change the same thing: still a conflict"
new_repo conflicting
first="$(awk '/^- / { print NR; exit }' "$repo/CHANGELOG.md")"
g checkout -qb a
edit_line CHANGELOG.md "$first" " Said one way."
edit_line llms.txt $((forms + 1)) " (one way)"
g commit -qam a
g checkout -q main && g checkout -qb b
edit_line CHANGELOG.md "$first" " Said another."
edit_line llms.txt $((forms + 1)) " (another)"
g commit -qam b
g merge -q a -m merge >/dev/null 2>&1 || true
check "both files are left conflicted" "CHANGELOG.md llms.txt" \
  "$(g diff --name-only --diff-filter=U | sort | tr '\n' ' ' | sed 's/ $//')"
check "the CHANGELOG shows both sides and the base" 3 \
  "$(grep -c '^<<<<<<< \|^||||||| \|^>>>>>>> ' "$repo/CHANGELOG.md")"

echo "==> a clone with no driver configured yet: an ordinary conflict, never damage"
new_repo undriven no-driver
g checkout -qb a && insert_after CHANGELOG.md "### Fixed" "$work/bullet-Alpha" && g commit -qam a
g checkout -q main && g checkout -qb b && insert_after CHANGELOG.md "### Fixed" "$work/bullet-Beta" && g commit -qam b
g merge -q a -m merge >/dev/null 2>&1 || true
check "CHANGELOG.md conflicts as git's text merge would" "CHANGELOG.md" "$(g diff --name-only --diff-filter=U)"

if [ "$failures" -gt 0 ]; then
  echo "merge-friendly-files: $failures check(s) failed"
  exit 1
fi
echo "merge-friendly-files: all checks passed"

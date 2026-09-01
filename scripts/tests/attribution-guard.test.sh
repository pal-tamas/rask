#!/usr/bin/env bash
# Table test for the attribution guard (scripts/lib/attribution.sh) and the two hooks that use it.
#
# The guard exists because GitHub's contributor list credits `Co-authored-by:` trailers as well as
# authors, so a footer a coding agent appends out of habit silently adds an account to the sidebar.
# Two of them (Claude, Copilot Autofix) did reach main, and removing them cost a rewrite of all 970
# commits plus a force-push of main and 18 release tags. Everything about this guard is cheap before
# a push and very expensive after one, so it is stated as a table rather than left to the cases
# someone happened to try — same as build-failure-kind.test.sh and e2e-concurrency.test.sh.
#
# Both directions cost something. A miss is the failure above. A false positive blocks a legitimate
# commit, so the prose rows below are as load-bearing as the trailer rows: a body that *discusses*
# the trailer, or a human Signed-off-by, must pass.
#
# Four sections, because "the regex is right" and "the callers consult it" are different claims:
#   1. the predicate, over a table
#   2. the real .githooks/commit-msg, driven end to end
#   3. the real .githooks/pre-push, driven end to end in an isolated throwaway repository
#   4. .github/workflows/commitlint.yml — structural only; it needs a pull_request event to run,
#      so what is pinned is that it still sources the shared predicate and still checks the PR body
#   5. proof that all of the above left THIS repository untouched — section 3 makes commits, and a
#      test that commits has to show it committed somewhere else (see the GIT_DIR note below)
#
# Usage:  scripts/tests/attribution-guard.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

# Scrub the git environment before anything else runs, and do not skip this.
#
# git EXPORTS GIT_DIR (and friends) to the hooks it invokes, and this file is run by
# scripts/run-unit-local.sh, which .githooks/pre-commit runs — so under a commit, every `git` call
# below inherits a GIT_DIR pointing at the real repository. Section 3 creates a throwaway repository
# and commits into it; with GIT_DIR still set, `cd`-ing there changes nothing and those commits land
# on the branch being developed instead. That is not hypothetical: it happened on the first run of
# this test under the hook, which put two "chore: probe" commits on the branch and left the whole
# tree looking untracked. Passing standalone and destroying the repo under a hook is exactly the
# shape of failure this suite exists to catch.
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE GIT_OBJECT_DIRECTORY GIT_ALTERNATE_OBJECT_DIRECTORIES \
      GIT_COMMON_DIR GIT_PREFIX GIT_REFLOG_ACTION GIT_INDEX_VERSION GIT_QUARANTINE_PATH

# Pinned so the last assertion can prove this run left the real repository exactly as it found it.
repo_head_before="$(git rev-parse HEAD)"
repo_status_before="$(git status --porcelain)"

# shellcheck source=../lib/attribution.sh
. "$root/scripts/lib/attribution.sh"

failures=0
checked=0

pass() { printf '  ok   %-58s\n' "$1"; }
fail() { printf '  FAIL %-58s %s\n' "$1" "$2" >&2; failures=$((failures + 1)); }

# assert_predicate <name> <dirty|clean> <message>
assert_predicate() {
  checked=$((checked + 1))
  if printf '%b' "$3" | rask_message_has_attribution; then actual="dirty"; else actual="clean"; fi
  if [ "$actual" = "$2" ]; then pass "$1"; else fail "$1" "-> $actual (expected $2)"; fi
}

echo "==> rask_message_has_attribution"

# The two footers that actually reached main.
assert_predicate "the Claude trailer that reached main" dirty \
  'fix: x\n\nCo-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>\n'
assert_predicate "the Copilot Autofix trailer" dirty \
  'fix: x\n\nCo-authored-by: Copilot Autofix powered by AI <62310815+github-advanced-security[bot]@users.noreply.github.com>\n'

# The rest of the family, spelled the ways they arrive.
assert_predicate "lower-case co-authored-by"        dirty 'fix: x\n\nco-authored-by: a <a@b.c>\n'
assert_predicate "leading whitespace"               dirty 'fix: x\n\n  Co-authored-by: a <a@b.c>\n'
assert_predicate "a bot co-author"                  dirty 'build: b\n\nCo-authored-by: dependabot[bot] <1@users.noreply.github.com>\n'
assert_predicate "the session footer"               dirty 'fix: x\n\nClaude-Session: https://claude.ai/code/session_01\n'
assert_predicate "generated-with, emoji form"       dirty 'fix: x\n\n🤖 Generated with [Claude Code](https://claude.com/claude-code)\n'
assert_predicate "generated-with, Copilot"          dirty 'fix: x\n\nGenerated with GitHub Copilot\n'
assert_predicate "a bot sign-off"                   dirty 'build: b\n\nSigned-off-by: dependabot[bot] <support@github.com>\n'
assert_predicate "an AI-vendor sign-off"            dirty 'fix: x\n\nSigned-off-by: someone <noreply@anthropic.com>\n'

# Clean messages. These are the false-positive guard rails.
assert_predicate "an ordinary commit"               clean 'feat(forms): add RadioGroup disabled state\n\nA normal body.\n'
assert_predicate "a merge commit"                   clean 'Merge branch main into topic\n'
assert_predicate "a revert"                         clean 'Revert "feat: x"\n'
assert_predicate "prose naming the trailer"         clean 'docs: explain\n\nThe co-authored-by footer is banned in this repository.\n'
assert_predicate "a HUMAN sign-off"                 clean 'fix: x\n\nSigned-off-by: Tamas Pal <pal.tamas@pptgateway.hu>\n'
assert_predicate "a body mentioning Claude Code"    clean 'docs: mention the agent\n\nClaude Code is one of the supported agents.\n'
assert_predicate "a subject naming a bot file"      clean 'chore: update dependabot.yml\n'

echo "==> .githooks/commit-msg (the real hook)"

msg_tmp="$(mktemp)"
trap 'rm -f "$msg_tmp"' EXIT

# assert_commit_msg <name> <expected-exit> <message>
assert_commit_msg() {
  checked=$((checked + 1))
  printf '%b' "$3" > "$msg_tmp"
  set +e
  sh "$root/.githooks/commit-msg" "$msg_tmp" >/dev/null 2>&1
  actual=$?
  set -e
  if [ "$actual" = "$2" ]; then pass "$1"; else fail "$1" "-> exit $actual (expected $2)"; fi
}

assert_commit_msg "rejects the trailer"            1 'fix: x\n\nCo-Authored-By: Claude <noreply@anthropic.com>\n'
assert_commit_msg "rejects it on a MERGE commit"   1 'Merge branch a\n\nCo-authored-by: Claude <noreply@anthropic.com>\n'
assert_commit_msg "accepts a clean commit"         0 'feat(forms): add RadioGroup disabled state\n'
assert_commit_msg "accepts a clean merge"          0 'Merge branch main into topic\n'
assert_commit_msg "still rejects a bad subject"    1 'random text\n'

echo "==> .githooks/pre-push (the real hook, isolated repository)"

# Driven in a throwaway repository rather than this one: the hook makes commits' worth of decisions
# and the test must not need to create, or clean up, commits in the repository being developed. The
# hook and the libs it sources are COPIED from this tree, so what runs is the shipped file.
push_repo="$(mktemp -d)"
trap 'rm -f "$msg_tmp"; rm -rf "$push_repo"' EXIT

mkdir -p "$push_repo/scripts/lib" "$push_repo/.githooks"
cp "$root/scripts/lib/attribution.sh" "$root/scripts/lib/build-failure.sh" "$push_repo/scripts/lib/"
cp "$root/.githooks/pre-push" "$push_repo/.githooks/pre-push"

(
  cd "$push_repo"
  git init -q -b main
  git config user.email "test@example.invalid"
  git config user.name "Test"
  echo one > f && git add f && git commit -q -m "chore: base"
) >/dev/null 2>&1

base="$(git -C "$push_repo" rev-parse HEAD)"

(
  cd "$push_repo"
  echo two >> f
  git commit -qa -m "chore: probe

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>"
) >/dev/null 2>&1
dirty="$(git -C "$push_repo" rev-parse HEAD)"

(cd "$push_repo" && git commit -q --amend -m "chore: probe") >/dev/null 2>&1
clean="$(git -C "$push_repo" rev-parse HEAD)"

# assert_pre_push <name> <sha> <expected-exit>
assert_pre_push() {
  checked=$((checked + 1))
  set +e
  actual="$(
    cd "$push_repo" || exit 9
    printf 'refs/heads/main %s refs/heads/main %s\n' "$2" "$base" \
      | RASK_SKIP_E2E=1 RASK_SKIP_BENCHMARKS=1 RASK_SKIP_CLI_BUILD_E2E=1 RASK_SKIP_WATCH_E2E=1 \
        RASK_SKIP_DEPLOY_E2E=1 RASK_SKIP_INSTALL_E2E=1 \
        bash .githooks/pre-push origin https://example.invalid >/dev/null 2>&1
    echo $?
  )"
  set -e
  if [ "$actual" = "$3" ]; then pass "$1"; else fail "$1" "-> exit $actual (expected $3)"; fi
}

assert_pre_push "blocks a push carrying the trailer" "$dirty" 1
assert_pre_push "lets a clean push through"          "$clean" 0

echo "==> .github/workflows/commitlint.yml (the CI backstop)"

# Structural, and deliberately so: this one cannot be driven here, because it only runs inside a
# pull_request event with a base and head sha. What it CAN be held to is that it still consults the
# shared predicate rather than a second copy of the regex that would drift, and that it still checks
# the PR body — a squash merge builds the commit message from the title and description, so the body
# is the one path onto main that no local hook sees.
workflow="$root/.github/workflows/commitlint.yml"

assert_contains() {
  checked=$((checked + 1))
  if grep -q "$2" "$workflow"; then pass "$1"; else fail "$1" "-> '$2' is gone from commitlint.yml"; fi
}

assert_contains "CI sources the shared predicate"  'scripts/lib/attribution.sh'
assert_contains "CI calls it, not its own regex"   'rask_message_has_attribution'
assert_contains "CI checks the PR body"            'PR_BODY'

if grep -q 'RASK_ATTRIBUTION_RE=' "$workflow"; then
  fail "CI does not redefine the regex" "-> commitlint.yml sets RASK_ATTRIBUTION_RE; it must source the lib"
else
  pass "CI does not redefine the regex"
fi
checked=$((checked + 1))

echo "==> this test left the repository alone"

# The regression pin for the bug above. Section 3 commits, and a test that commits must prove it
# committed somewhere else — a stale GIT_DIR is invisible until it has already rewritten your branch.
checked=$((checked + 1))
if [ "$(git rev-parse HEAD)" = "$repo_head_before" ]; then
  pass "HEAD is where it was"
else
  fail "HEAD is where it was" "-> $(git rev-parse HEAD), was $repo_head_before — a git env var leaked"
fi

checked=$((checked + 1))
if [ "$(git status --porcelain)" = "$repo_status_before" ]; then
  pass "the working tree is where it was"
else
  fail "the working tree is where it was" "-> status changed; a git env var leaked into the temp repo"
fi

echo
if [ "$failures" -eq 0 ]; then
  echo "  $checked cases, all passed"
else
  echo "  $checked cases, $failures FAILED" >&2
  exit 1
fi

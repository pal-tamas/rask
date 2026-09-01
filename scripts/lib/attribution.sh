# Attribution-trailer predicate, shared by .githooks/commit-msg and .githooks/pre-push.
#
# This repository credits a single contributor, and that is a property of the COMMIT MESSAGE rather
# than of the author field: GitHub's contributor list counts `Co-authored-by:` trailers as well as
# authors. Two footers a coding agent appended out of habit — one Claude, one Copilot Autofix — put
# two extra accounts in the sidebar, and taking them back off cost a rewrite of all 970 commits plus
# a force-push of main and 18 release tags. The cost is entirely on the far side of the push, which
# is why both hooks check and why the rule lives in one file instead of two.
#
# POSIX sh: .githooks/commit-msg is `#!/bin/sh`. Nothing here may be a bashism.
#
# Table tested by scripts/tests/attribution-guard.test.sh, which also drives the real hooks — the
# regex being right and the hooks actually consulting it are two different claims.

# Matched case-insensitively, because these trailers arrive spelled every possible way.
#
#   - co-authored-by / claude-session   the two footers agents append; anchored so that prose
#                                       mentioning the trailer mid-sentence is not a false positive
#   - generated with <tool>             the "🤖 Generated with [Claude Code]" family
#   - signed-off-by / assisted-by       only when they name a bot or an AI vendor; a human
#                                       sign-off is none of this guard's business
RASK_ATTRIBUTION_RE='^[[:space:]]*(co-authored-by|claude-session):|generated with .*(claude|copilot|chatgpt|cursor|codeium|gemini)|^[[:space:]]*(signed-off-by|assisted-by):.*(\[bot\]|anthropic\.com)'

# rask_message_has_attribution — reads a commit message on stdin.
# Returns 0 when it carries an attribution trailer, 1 when it is clean.
rask_message_has_attribution() {
  grep -qiE "$RASK_ATTRIBUTION_RE"
}

# rask_attribution_offenders — reads a commit message on stdin, prints the offending lines with
# their line numbers. Prints nothing, and still succeeds, when the message is clean.
rask_attribution_offenders() {
  grep -inE "$RASK_ATTRIBUTION_RE" || true
}

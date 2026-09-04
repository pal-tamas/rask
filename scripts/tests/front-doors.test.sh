#!/usr/bin/env bash
# Table test for the Counter sample — the first Rask code almost anyone reads.
#
# It is written in three places, and each of them is somebody's front door:
#   README.md                          the repository landing page
#   NUGET.md                           packed into every published package, so the nuget.org page
#   samples/Rask.Example.Site/Pages/HomePage.cs   the hero on the published site
#
# Nothing pinned them to each other, and they drifted exactly as you would expect. #924 cut the
# sample to one button and left the other two on the old three-line version; worse, the hero had
# been sitting on `Counter : Page` with a `protected override string Route`, an API deleted from the
# tree long before, so the first Rask code a visitor read could not compile. Both were found by
# reading them, not by a gate.
#
# So this asserts the three are the SAME CODE. The hero is syntax-highlighted HTML rather than
# markdown, so it is normalised — tags stripped, entities decoded, indentation removed — and then
# compared verbatim against the README. Drift in either direction fails, including a regression to
# an API that no longer exists, because the README's copy is compiled for real by
# tests/Rask.Example.Playground.Tests/ChainSnippetTests.cs.
#
# NOT included, deliberately, so a future reader does not "fix" it into this list:
#   docs/getting-started.md teaches state with a heading and a paragraph, which is what the prose
#     around it is explaining.
#
# (There used to be a third front door here: ChainAnimation.cs, a generated SVG that typed this same
# sample out character by character, and assets/rask-chain.svg baked from it. Both are gone — nothing
# embedded the baked asset any more, and the landing page leads with the source in a code window.)
#
# Usage:  scripts/tests/front-doors.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

failures=0
checked=0

check() {
    local name="$1" expected="$2" actual="$3"
    checked=$((checked + 1))
    if [ "$actual" = "$expected" ]; then
        printf '  ok   %-52s\n' "$name"
    else
        printf '  FAIL %-52s\n' "$name" >&2
        printf '       expected:\n%s\n       actual:\n%s\n' "$expected" "$actual" >&2
        failures=$((failures + 1))
    fi
}

# The markdown copies: the fenced block from the [Route] line to the closing brace.
markdown_counter() {
    awk '/^\[Route\("\/counter"\)\]$/{f=1} f{print} f&&/^}$/{exit}' "$1"
}

# The hero: the raw-string body of CounterCodeHtml, normalised back to plain C#. The site stores it
# pre-highlighted because the page renders it through Raw, so those tags are the only difference
# that is allowed to exist between it and the README.
hero_counter() {
    awk '
        /private const string CounterCodeHtml =/ { inconst = 1; next }
        inconst && /^        """$/               { body = 1; next }
        inconst && body && /^        """;$/      { exit }
        inconst && body                          { print }
    ' samples/Rask.Example.Site/Pages/HomePage.cs \
    | sed -e 's/<[^>]*>//g' \
          -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&quot;/"/g' -e 's/&amp;/\&/g' \
          -e 's/^        //'
}

readme="$(markdown_counter README.md)"
nuget="$(markdown_counter NUGET.md)"
hero="$(hero_counter)"

echo "==> the Counter sample is the same code on every front door"

# Guard the extractors themselves. A silently empty match would make every comparison below pass,
# which is the failure mode this whole file exists to prevent.
check "the README block was found"           yes "$([ -n "$readme" ] && printf yes || printf no)"
check "the NUGET.md block was found"         yes "$([ -n "$nuget" ]  && printf yes || printf no)"
check "the site hero block was found"        yes "$([ -n "$hero" ]   && printf yes || printf no)"
check "the README block is a whole class"    yes \
    "$(printf '%s' "$readme" | grep -q '^}$' && printf yes || printf no)"

check "NUGET.md matches the README"          "$readme" "$nuget"
check "the site hero matches the README"     "$readme" "$hero"

# The hero regressed to a deleted API once and nothing said so. These name the two symbols directly,
# so that failure reads as what it is rather than as a wall of diff.
check "the hero names no Page base class"    "" \
    "$(printf '%s' "$hero" | grep -n ': *Page$' || true)"
check "the hero has no 'override string Route'" "" \
    "$(printf '%s' "$hero" | grep -n 'override string Route' || true)"

# --- gate wiring ---------------------------------------------------------------------------------
# A guard nothing invokes is not a guard. README.md and NUGET.md sit at the repo root and match none
# of the pre-commit filter's directory prefixes, so without its own hook entry this file would never
# run for the commit most likely to break it: an edit to the README alone. The filter is extracted
# from the hook and run, rather than grepped for, so it cannot pass against one that has been edited
# into something that no longer matches.
echo
echo "==> gate wiring"

check "pre-commit invokes this test" yes \
    "$(grep -q 'scripts/tests/front-doors\.test\.sh' .githooks/pre-commit && printf yes || printf no)"

front_door_filter="$(sed -n "s/^front_doors='\(.*\)'$/\1/p" .githooks/pre-commit)"
check "the front-door filter is still where we think" yes \
    "$([ -n "$front_door_filter" ] && printf yes || printf no)"

matches_front_doors() {
    printf '%s\n' "$1" | grep -qE "$front_door_filter" && printf yes || printf no
}
check "a README-only commit runs this guard"   yes "$(matches_front_doors README.md)"
check "a NUGET.md-only commit runs this guard" yes "$(matches_front_doors NUGET.md)"

# The hero needs no entry of its own: it lives under samples/, which the ordinary filter already
# matches, so its commits run the full gate — and that runs this file via run-unit-local.sh.
check "the site hero is under samples/" yes \
    "$([ -f samples/Rask.Example.Site/Pages/HomePage.cs ] && printf yes || printf no)"

echo
if [ "$failures" -gt 0 ]; then
    echo "front-doors.test.sh: $failures of $checked checks FAILED." >&2
    echo "The Counter sample differs between the README, NUGET.md and the site hero." >&2
    echo "They are one sample in three places — update all three, or none." >&2
    exit 1
fi

echo "front-doors.test.sh: $checked checks passed."

#!/usr/bin/env bash
# Drive the Playwright CLI that Microsoft.Playwright already ships — without PowerShell.
#
# The package drops a `playwright.ps1` next to the test assembly, which makes pwsh look like a hard
# dependency of running the browser suite. It is not. That file is six lines: it sets
# PLAYWRIGHT_DRIVER_SEARCH_PATH, loads Microsoft.Playwright.dll, and calls its Main. The same package
# also lays down `.playwright/node/<rid>/node` and `.playwright/package/cli.js` — the actual driver the
# binding launches at runtime — so the CLI can be invoked directly with no shell of any kind between.
#
# That matters beyond tidiness. pwsh is not in Fedora's repositories, nor in a plain Debian install, so
# on a Linux dev box the old check fell through to "skipped auto-install" and the suite then failed
# minutes later with a missing-browser error that named the wrong cause.
#
# Going to the bundled driver is also the VERSION-CORRECT route, not merely a workaround: the browser
# revisions it installs are the ones THIS Microsoft.Playwright asks for. `npx playwright install` is the
# tempting one-liner and is wrong — npx resolves the newest Playwright, whose browser build numbers can
# differ from the pinned binding's, and the mismatch surfaces much later, inside a test run, as
# "Executable doesn't exist at ...". Prefer the driver that shipped with the package.
#
# Lives here rather than inline for the same reason as the other two libs: so it is table-tested
# (scripts/tests/playwright.test.sh). Resolution walks the build output, and a resolver that quietly
# finds nothing is exactly the kind of thing that becomes a skipped step nobody reads.

# rask_playwright_driver <search-root>
#
# Prints two lines — the bundled node binary, then cli.js — and returns 0. Prints nothing and returns 1
# when either is absent, which is the normal state before the E2E project has been built once.
rask_playwright_driver() {
  search_root="$1"

  [ -d "$search_root" ] || return 1

  cli="$(find "$search_root" -path '*/.playwright/package/cli.js' 2>/dev/null | head -1)"
  [ -n "$cli" ] || return 1

  # .playwright/package/cli.js -> .playwright, then down into node/<rid>/node. The rid directory is
  # named for the build host, so it is globbed rather than assumed: linux-arm64 in a UTM guest,
  # darwin-arm64 on an Apple laptop, linux-x64 on a CI box.
  pw_dir="$(dirname "$(dirname "$cli")")"
  node="$(find "$pw_dir/node" -type f -name node 2>/dev/null | head -1)"
  [ -n "$node" ] || return 1

  printf '%s\n%s\n' "$node" "$cli"
}

# There is deliberately no rask_playwright wrapper that both resolves and runs. Callers need the two
# steps separated: `set -e` is suspended inside an `if` condition, so a combined helper invoked as
# `if rask_playwright ... install chromium` would swallow a failed download into the else branch and
# carry on having installed nothing. Resolve in the condition, run in the body.

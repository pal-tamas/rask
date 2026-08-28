#!/usr/bin/env bash
# The Playwright CLI, driven through the driver Microsoft.Playwright already ships.
#
# This is the replacement for `pwsh <path>/playwright.ps1 <args>`. It needs no PowerShell, and it uses
# the exact driver the binding launches at runtime, so installed browser revisions always match the
# pinned Microsoft.Playwright. See scripts/lib/playwright.sh for why `npx playwright` is not equivalent.
#
#   scripts/playwright.sh install chromium      # what the E2E gate needs
#   scripts/playwright.sh show-trace <file>     # open a trace from a failed run
#   scripts/playwright.sh --help
#
# Release is preferred over Debug because that is what scripts/run-e2e-local.sh builds; either works.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

for cfg in Release Debug; do
  if driver="$(rask_playwright_driver "tests/Rask.Examples.E2E.Tests/bin/$cfg")"; then
    pw_node="$(printf '%s\n' "$driver" | sed -n 1p)"
    pw_cli="$(printf '%s\n' "$driver" | sed -n 2p)"
    exec "$pw_node" "$pw_cli" "$@"
  fi
done

echo "playwright: no bundled driver under tests/Rask.Examples.E2E.Tests/bin/{Release,Debug}." >&2
echo "            Build the E2E project once first:" >&2
echo "              dotnet build tests/Rask.Examples.E2E.Tests -c Release" >&2
exit 1

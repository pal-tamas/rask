#!/usr/bin/env bash
# Table test for scripts/lib/push_verdict.sh -- did nuget.org accept one package of a push?
#
# The release gate waits HOURS for an accepted package and fails at once for one that was never
# accepted (#1125), so reading this wrong in either direction is expensive: "accepted" for a refused
# push sits out a three-hour deadline, "refused" for an accepted one reddens a release that is fine.
#
# Usage:  scripts/tests/push-verdict.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
# shellcheck source=../lib/push_verdict.sh
. "$root/scripts/lib/push_verdict.sh"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

failures=0

# assert <name> <expected> <log>
assert() {
  local name="$1" expected="$2" actual
  printf '%s\n' "$3" > "$work/push.log"
  actual="$(push_verdict "$work/push.log" Rask.Cli 0.23.0)"
  if [ "$actual" = "$expected" ]; then
    printf '  ok   %s\n' "$name"
  else
    printf '  FAIL %s\n       got:      %s\n       expected: %s\n' "$name" "$actual" "$expected" >&2
    failures=$((failures + 1))
  fi
}

echo "==> push_verdict: did nuget.org accept the package?"

assert "Created is accepted" accepted \
"Pushing Rask.Core.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  PUT https://www.nuget.org/api/v2/package/
  Created https://www.nuget.org/api/v2/package/ 812ms
Pushing Rask.Cli.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  PUT https://www.nuget.org/api/v2/package/
  Created https://www.nuget.org/api/v2/package/ 2345ms
Your package was pushed."

assert "a duplicate under --skip-duplicate is accepted (a re-run)" accepted \
"Pushing ./artifacts/Rask.Cli.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  PUT https://www.nuget.org/api/v2/package/
  Conflict https://www.nuget.org/api/v2/package/ 402ms
Package './artifacts/Rask.Cli.0.23.0.nupkg' already exists at feed 'https://api.nuget.org/v3/index.json'."

assert "a 403 is refused" refused \
"Pushing Rask.Cli.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  PUT https://www.nuget.org/api/v2/package/
  Forbidden https://www.nuget.org/api/v2/package/ 301ms
error: Response status code does not indicate success: 403 (The specified API key is invalid)."

# The section ends at the next "Pushing": a Created further down belongs to someone else.
assert "a neighbour's Created is not ours" refused \
"Pushing Rask.Cli.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  PUT https://www.nuget.org/api/v2/package/
  InternalServerError https://www.nuget.org/api/v2/package/ 30001ms
Pushing Rask.Data.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  Created https://www.nuget.org/api/v2/package/ 700ms"

assert "a package the log never mentions is absent" absent \
"Pushing Rask.Core.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  Created https://www.nuget.org/api/v2/package/ 812ms"

# Rask.Cli.Tool, or a 0.23.0-rc: a prefix is not a match.
assert "a longer id or version is not this package" absent \
"Pushing Rask.Cli.Tool.0.23.0.nupkg to 'https://www.nuget.org/api/v2/package'...
  Created https://www.nuget.org/api/v2/package/ 812ms
Pushing Rask.Cli.0.23.0-rc.1.nupkg to 'https://www.nuget.org/api/v2/package'...
  Created https://www.nuget.org/api/v2/package/ 812ms"

if [ "$failures" -gt 0 ]; then
  echo "!!  $failures push_verdict case(s) failed" >&2
  exit 1
fi
echo "==> push_verdict: all cases pass"

#!/usr/bin/env bash
# Table test for scripts/lib/format-scope.sh — which files CI's formatter checks for a change.
#
# The direction that matters: a row that answers ALL where the changed files would do costs a ten-minute
# formatter run; a row that answers the changed files where ALL was due lets a rule change land with the
# tree out of step with it until somebody formats a file it moved. Keep the ALL rows when in doubt.
#
# Usage:  scripts/tests/format-scope.test.sh   (run by scripts/run-unit-local.sh)
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../lib/format-scope.sh
. "$root/scripts/lib/format-scope.sh"
failures=0
checked=0

# scope <changed path...> -> what the formatter is given, on one line
scope() {
  printf '%s\n' "$@" | rask_format_scope | tr '\t\n' ' ' | sed 's/ $//'
}

# expect <name> <answer> <changed path...>
expect() {
  name="$1"; want="$2"; shift 2
  checked=$((checked + 1))
  got="$(scope "$@")"
  if [ "$got" = "$want" ]; then
    printf '  ok   %-52s -> %s\n' "$name" "${got:-(nothing)}"
  else
    printf '  FAIL %-52s -> %s (expected %s)\n' "$name" "${got:-(nothing)}" "${want:-(nothing)}" >&2
    failures=$((failures + 1))
  fi
}

echo "==> format-scope.sh"
expect "a changed .cs file is checked"                   "src/Rask.Mail/Mail.cs" src/Rask.Mail/Mail.cs
expect "...each of them, and nothing else"               "src/A.cs tests/B.cs"   src/A.cs docs/x.md tests/B.cs src/site.css
expect "no .cs file: nothing to check"                   ""                      docs/x.md src/Rask.Site/wwwroot/app.css
expect "the root .editorconfig checks everything"        "ALL .editorconfig"     src/A.cs .editorconfig
expect "a nested .editorconfig too"                      "ALL tests/.editorconfig" tests/.editorconfig
expect "a .globalconfig"                                 "ALL src/rask.globalconfig" src/rask.globalconfig
expect "the build props wire the analyzers"              "ALL Directory.Build.props" Directory.Build.props
expect "...at any depth"                                 "ALL src/Directory.Build.targets" src/Directory.Build.targets
expect "the package pins hold the analyzer versions"     "ALL Directory.Packages.props" src/A.cs Directory.Packages.props
expect "the SDK ships the formatter"                     "ALL global.json"       global.json
expect "a project file is not a rule change"             "src/A.cs"              src/A/A.csproj src/A.cs
expect "a props file of one project is not either"       ""                      src/Rask.Pdf/build/Rask.Pdf.props

echo
if [ "$failures" -gt 0 ]; then
  echo "format-scope: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "format-scope: $checked checks passed."

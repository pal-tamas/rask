#!/usr/bin/env bash
# Tests for scripts/lib/affected_projects.py — the graph that decides whether a pre-commit run may
# narrow to a few projects or must do the whole solution.
#
# This is exactly the kind of logic that is quietly wrong until it costs someone an afternoon: it is
# only ever consulted to decide what NOT to run, so a bug in it shows up as a break that reached main
# rather than as a red gate. Hence a table test, and hence the bias every case below asserts — when
# in doubt, FULL.
set -uo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
script="$root/scripts/lib/affected_projects.py"

failures=0
checks=0

echo "==> affected_projects.py"

# $1 label · $2 newline-separated changed paths · $3 expectation: FULL, or a project that MUST appear
expect() {
  label="$1"
  changed="$2"
  mode="$3"
  want="${4:-}"   # only the scoped/absent modes carry one
  checks=$((checks + 1))

  out="$(printf '%s\n' "$changed" | python3 "$script" "$root" 2>&1)"

  case "$mode" in
    full)
      if printf '%s' "$out" | head -1 | grep -q '^FULL'; then
        echo "  ok   $label -> FULL"
      else
        echo "  FAIL $label: expected FULL, got:" >&2
        printf '%s\n' "$out" | sed 's/^/         /' >&2
        failures=$((failures + 1))
      fi
      ;;
    scoped)
      if printf '%s' "$out" | head -1 | grep -q '^FULL'; then
        echo "  FAIL $label: expected a scoped list naming $want, got FULL: $out" >&2
        failures=$((failures + 1))
      elif printf '%s\n' "$out" | grep -q "$want"; then
        echo "  ok   $label -> scoped, includes $want"
      else
        echo "  FAIL $label: expected $want in the list, got:" >&2
        printf '%s\n' "$out" | sed 's/^/         /' >&2
        failures=$((failures + 1))
      fi
      ;;
    absent)
      if printf '%s\n' "$out" | grep -q "$want"; then
        echo "  FAIL $label: did NOT expect $want in the list, got:" >&2
        printf '%s\n' "$out" | sed 's/^/         /' >&2
        failures=$((failures + 1))
      else
        echo "  ok   $label -> $want correctly absent"
      fi
      ;;
  esac
}

# --- the things that must never be narrowed -------------------------------------------------------
expect "a repo-root MSBuild import"   "Directory.Packages.props"                 full
expect "the repo-root Build props"    "Directory.Build.props"                    full
expect "the solution"                 "Rask.slnx"                                full
expect "a gate script"                "scripts/run-unit-local.sh"                full
expect "a git hook"                   ".githooks/pre-push"                       full
expect "the test-wide props"          "tests/Directory.Build.props"              full
expect "the test-wide runner config"  "tests/xunit.runner.json"                  full
expect "a packaged MSBuild import"    "src/Rask.Core/build/Rask.Core.targets"    full
expect "the formatting configuration" ".editorconfig"                            full
expect "a file outside the projects"  "docs/routing.md"                          full
expect "nothing staged at all"        ""                                         full

# A shared, source-linked file belongs to no project of its own. The graph cannot see which
# assemblies compile it from the file alone, so it must refuse rather than guess.
expect "a source-linked shared file"  "src/Rask.Generators.Shared/DiagnosticHelp.cs" full
expect "a shared test helper"         "tests/Shared.TestFiles/RepoFiles.cs"          full

# --- the things that may be narrowed --------------------------------------------------------------
expect "a Rask.Ui component reaches its own tests" \
  "src/Rask.Ui/Components/UiSelect.cs" scoped "tests/Rask.Ui.Tests/Rask.Ui.Tests.csproj"

# Rask.Site.Tests compiles the site's sources through a "..\Rask.Site\**\*.cs" glob rather than a
# ProjectReference. A graph built from ProjectReference alone calls it unaffected and is wrong.
expect "a site page reaches the site tests through a Compile glob" \
  "site/Rask.Site/Features/TodosPage.cs" scoped "site/Rask.Site.Tests/Rask.Site.Tests.csproj"

# ...and does NOT drag in something it cannot reach.
expect "a site page does not reach the CLI tests" \
  "site/Rask.Site/Features/TodosPage.cs" absent "tests/Rask.Cli.Tests"

# Rask.Core is underneath everything, so its fan-out is nearly the whole tree. Asserted so that a
# future narrowing of the graph cannot quietly make the most load-bearing project in the repo cheap.
expect "Rask.Core reaches Rask.Server.Tests" \
  "src/Rask.Core/Components/Div.cs" scoped "tests/Rask.Server.Tests/Rask.Server.Tests.csproj"

echo "affected-projects: $checks checks, $failures failed."
[ "$failures" -eq 0 ]

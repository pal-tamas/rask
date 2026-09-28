#!/usr/bin/env bash
# gate-inputs: .*\.csproj$
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
  first="${out%%$'\n'*}"

  # Every check reads a here-string, never a pipe from printf. Under pipefail, `printf | grep -q` fails
  # whenever grep matches and exits before printf has written everything: printf dies of SIGPIPE, and a
  # list that DOES name the project reports it missing. The Rask.Core case prints nearly the whole tree.
  case "$mode" in
    full)
      if grep -q '^FULL' <<<"$first"; then
        echo "  ok   $label -> FULL"
      else
        echo "  FAIL $label: expected FULL, got:" >&2
        printf '%s\n' "$out" | sed 's/^/         /' >&2
        failures=$((failures + 1))
      fi
      ;;
    scoped)
      if grep -q '^FULL' <<<"$first"; then
        echo "  FAIL $label: expected a scoped list naming $want, got FULL: $out" >&2
        failures=$((failures + 1))
      elif grep -q -- "$want" <<<"$out"; then
        echo "  ok   $label -> scoped, includes $want"
      else
        echo "  FAIL $label: expected $want in the list, got:" >&2
        printf '%s\n' "$out" | sed 's/^/         /' >&2
        failures=$((failures + 1))
      fi
      ;;
    absent)
      if grep -q -- "$want" <<<"$out"; then
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
expect "a root file nobody reads"     "LICENSE"                                  full
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
  "src/Rask.Site/Features/TodosPage.cs" scoped "tests/Rask.Site.Tests/Rask.Site.Tests.csproj"

# ...and does NOT drag in something it cannot reach.
expect "a site page does not reach the CLI tests" \
  "src/Rask.Site/Features/TodosPage.cs" absent "tests/Rask.Cli.Tests"

# The scaffolder's template payload holds no project of its own — fifteen trees, every one of them
# with a Company.RaskServer.csproj that is never built here. It maps to the CLI, which embeds it.
#
# A PROJECT FILE, not the directory: everything downstream hands these straight to MSBuild, and a
# directory comes back as MSB3202 ("project file was not found") — a build failure inside the gate
# rather than a wrong answer, which is how this was caught.
expect "a template file rebuilds the CLI that embeds it" \
  "src/Rask.Templates/react/client/package.json" scoped "src/Rask.Cli/Rask.Cli.csproj"

# ...and does not answer FULL, which is what "belongs to no project" would otherwise mean. Editing a
# template is meant to be the cheap, ordinary way to change what `rask new` writes.
expect "a template file does not force the whole solution" \
  "src/Rask.Templates/react/client/package.json" absent "src/Rask.Core/Rask.Core.csproj"

# Rask.Core is underneath everything, so its fan-out is nearly the whole tree. Asserted so that a
# future narrowing of the graph cannot quietly make the most load-bearing project in the repo cheap.
expect "Rask.Core reaches Rask.Server.Tests" \
  "src/Rask.Core/Components/Div.cs" scoped "tests/Rask.Server.Tests/Rask.Server.Tests.csproj"

# --- files a test reads at runtime (<RaskTestReads/> in the test's csproj) ------------------------
# A file outside src/ and tests/ scopes to the tests that declare reading it, instead of FULL — which
# is what nearly every commit used to be, because a feature carries its CHANGELOG line and its docs.
expect "the CHANGELOG reaches the repo-wide Bootstrap scan" \
  "CHANGELOG.md" scoped "tests/Rask.Ui.Tests/Rask.Ui.Tests.csproj"
expect "the CHANGELOG does not force the whole solution" \
  "CHANGELOG.md" absent "tests/Rask.Server.Tests/Rask.Server.Tests.csproj"
expect "a docs page reaches the tests that walk docs/" \
  "docs/routing.md" scoped "tests/Rask.Site.Tests/Rask.Site.Tests.csproj"
expect "a docs page rebuilds the site that embeds it" \
  "docs/routing.md" scoped "src/Rask.Site/Rask.Site.csproj"
expect "llms.txt reaches the test that regenerates it" \
  "llms.txt" scoped "tests/Rask.Site.Tests/Rask.Site.Tests.csproj"
expect "a release workflow reaches the pack-list test" \
  ".github/workflows/release.yml" scoped "tests/Rask.Generators.Tests/Rask.Generators.Tests.csproj"

# ...and a file inside src/ reaches a test the project graph cannot see. Each of these was a blind spot:
# a change to it ran none of the tests that pin it.
expect "the server runtime's rask.ts reaches the Core client-contract tests" \
  "src/Rask.Server/Resources/rask.ts" scoped "tests/Rask.Core.Tests/Rask.Core.Tests.csproj"
expect "the site shell reaches the WASM prerender test" \
  "src/Rask.Site/wwwroot/index.html" scoped "tests/Rask.Wasm.Tests/Rask.Wasm.Tests.csproj"
expect "the kit stylesheet reaches the site's contrast tests" \
  "src/Rask.Ui/Styles/ui.css" scoped "tests/Rask.Site.Tests/Rask.Site.Tests.csproj"
expect "a precise glob keeps its extension filter" \
  "docs/installation.md" absent "tests/Rask.Blazor.Tests/Rask.Blazor.Tests.csproj"

# A packed <None Include="..\..."/> is a build input like a linked Compile item.
expect "the shared browser layer reaches the SPA host that packs it" \
  "src/Rask.Core/Resources/browser/storage.ts" scoped "src/Rask.Spa.Hosting/Rask.Spa.Hosting.csproj"

# The declarations must never be EVALUATED: MSBuild expands an item glob at every evaluation, and
# "..\..\**\*.cs" would walk the whole repo — bin/, obj/, node_modules/ — on every build of the project.
checks=$((checks + 1))
unguarded=""
for csproj in "$root"/tests/*/*.csproj; do
  grep -q 'RaskTestReads' "$csproj" || continue
  if ! grep -B20 'RaskTestReads' "$csproj" | grep -q '<ItemGroup Condition="false">'; then
    unguarded="$unguarded ${csproj#"$root"/}"
  fi
done
if [ -z "$unguarded" ]; then
  echo "  ok   every RaskTestReads declaration sits in an ItemGroup that is never evaluated"
else
  echo "  FAIL RaskTestReads outside an <ItemGroup Condition=\"false\">:$unguarded" >&2
  failures=$((failures + 1))
fi

# --- which gate-script tests a scoped run executes (the `# gate-inputs:` header) ------------------
# Mirrors rask_gate_test_applies in scripts/run-unit-local.sh: scripts/ and .githooks/ run every test,
# anything else runs a test only when its header names it.
gate_test_runs() {
  inputs="$(sed -n 's/^# gate-inputs: //p' "$root/scripts/tests/$1.test.sh" | head -1)"
  if printf '%s\n' "$2" | grep -E "^(scripts/|\.githooks/)${inputs:+|$inputs}" >/dev/null; then
    printf yes
  else
    printf no
  fi
}

gate_expect() {
  checks=$((checks + 1))
  got="$(gate_test_runs "$2" "$3")"
  if [ "$got" = "$4" ]; then
    echo "  ok   $1"
  else
    echo "  FAIL $1: expected $4, got $got" >&2
    failures=$((failures + 1))
  fi
}

gate_expect "the public-API prober runs for its probe project"   public-api-gate "src/Rask.Cache/CacheEntry.cs"       yes
gate_expect "the public-API prober runs for a nested MSBuild import" public-api-gate "src/Directory.Build.targets"    yes
gate_expect "the public-API prober runs for the analyzer severity" public-api-gate ".editorconfig"                    yes
gate_expect "the public-API prober skips an unrelated component" public-api-gate "src/Rask.Ui/Components/UiSelect.cs" no
gate_expect "the installer test runs for the installer"          install-script  "rask.ps1"                           yes
gate_expect "the installer test runs for the install docs"       install-script  "docs/installation.md"               yes
gate_expect "the front-doors test runs for the site hero"        front-doors     "src/Rask.Site/Features/Home/HomePage.cs" yes
gate_expect "the graph test runs for a project file"             affected-projects "tests/Rask.Ui.Tests/Rask.Ui.Tests.csproj" yes
gate_expect "a pure-script test runs for its script"             machine-lane    "scripts/lib/machine-lane.sh"        yes
gate_expect "a pure-script test skips a source change"           machine-lane    "src/Rask.Core/Component.cs"         no
gate_expect "every test runs for a hook change"                  push-verdict    ".githooks/pre-push"                 yes

echo "affected-projects: $checks checks, $failures failed."
[ "$failures" -eq 0 ]

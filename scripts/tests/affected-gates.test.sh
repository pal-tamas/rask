#!/usr/bin/env bash
# gate-inputs: .*\.csproj$|\.github/workflows/
# Table test for scripts/lib/affected_gates.py — which CI gates a change reaches on a push to main.
#
# Driven against THIS repository's real project graph, because the answer is a property of that graph:
# a table over a toy graph would keep passing after a reference that carries a gate is removed.
#
# What matters most is the direction of every error. A row that says "site" where none is needed costs
# minutes on a runner; a row that omits it lets a browser break through until the next full run. So
# the rows that assert a gate IS reached are the ones to keep when in doubt.
#
# Usage:  scripts/tests/affected-gates.test.sh   (run by scripts/run-unit-local.sh)
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
failures=0
checked=0

gates() {
  printf '%s\n' "$@" | python3 "$root/scripts/lib/affected_gates.py" "$root"
}

# expect_has <name> <key> <changed file...>
expect_has() {
  name="$1"; key="$2"; shift 2
  checked=$((checked + 1))
  out="$(gates "$@")"
  case " $out " in
    *" $key "*|FULL*) printf '  ok   %-58s -> %s\n' "$name" "$out" ;;
    *) printf '  FAIL %-58s -> %s (expected %s)\n' "$name" "$out" "$key" >&2; failures=$((failures + 1)) ;;
  esac
}

# expect_lacks <name> <key> <changed file...>   — and it must NOT be a FULL run, or the row proves nothing
expect_lacks() {
  name="$1"; key="$2"; shift 2
  checked=$((checked + 1))
  out="$(gates "$@")"
  case " $out " in
    FULL*|*" $key "*) printf '  FAIL %-58s -> %s (expected no %s, and no FULL)\n' "$name" "$out" "$key" >&2; failures=$((failures + 1)) ;;
    *) printf '  ok   %-58s -> %s\n' "$name" "$out" ;;
  esac
}

# expect_full <name> <changed file...>
expect_full() {
  name="$1"; shift
  checked=$((checked + 1))
  out="$(gates "$@")"
  case "$out" in
    FULL*) printf '  ok   %-58s -> %s\n' "$name" "$(printf '%s' "$out" | tr '\t' ' ')" ;;
    *) printf '  FAIL %-58s -> %s (expected FULL)\n' "$name" "$out" >&2; failures=$((failures + 1)) ;;
  esac
}

echo "==> affected_gates.py"

# The core reaches everything that is drawn with it.
expect_has   "the core reaches the site's browser journeys"        site      src/Rask.Core/Component.cs
expect_has   "the core reaches the Rask.Server journeys"           server    src/Rask.Server/RaskServerOptions.cs
expect_has   "a .cs change reaches the formatter"                  cs        src/Rask.Mail/Mail.cs
expect_has   "any change reaches the build and the unit tests"     code      src/Rask.Mail/Mail.cs

# A battery nobody draws with does not pay for a browser.
expect_lacks "a mail change does not publish the site"             site      src/Rask.Mail/Mail.cs
expect_lacks "a site page does not pack the CLI feed"              packaging src/Rask.Site/Program.cs

# The site, and only the site's own journeys.
expect_has   "a site page reaches the site journeys"               site      src/Rask.Site/Program.cs
expect_lacks "a site page does not reach the Rask.Server journeys" server    src/Rask.Site/Program.cs
expect_has   "a journey edit reaches its own suite"                site      tests/Rask.Site.E2E.Tests/WasmExampleTests.cs
expect_has   "the data demo reaches its journeys"                  datademo  src/Rask.Site.DataDemo/Program.cs
expect_has   "a devtools fixture reaches the devtools journeys"    devtools  tests/Rask.DevTools.Fixture.Wasm/Program.cs

# The CLI gates are selected by file, not by the graph (affected_gates.py says why): the graph would
# select them for every source change, and they are the longest gates there are.
expect_lacks "an ordinary core change does not pack the CLI feed"  packaging src/Rask.Core/Component.cs
expect_has   "the feed's own fixture reaches the CLI gates"        packaging tests/Rask.Cli.Tests/CliBuildE2E.cs
expect_has   "the CLI reaches the CLI gates"                       packaging src/Rask.Cli/Program.cs
expect_has   "a template reaches the CLI gates"                    packaging src/Rask.Templates/server/Program.cs
expect_has   "a shipped project file reaches the CLI gates"        packaging src/Rask.Mail/Rask.Mail.csproj

# Whatever the project scoper refuses to narrow is not narrowed here either.
expect_full  "a workflow"                                          .github/workflows/gates.yml
expect_full  "a gate script"                                       scripts/run-unit-local.sh
expect_full  "the package pins"                                    Directory.Packages.props
expect_full  "nothing at all"                                      ""

# A non-.cs change does not start the formatter.
expect_lacks "a stylesheet does not start the formatter"           cs        src/Rask.Site/wwwroot/global.css

# --- the bargain a scoped push rests on -----------------------------------------------------------
# A push is scoped only because a whole run follows it and publishing waits for THAT run. Each half
# is one line in a workflow, and losing either turns "fast" into "unchecked" without anything failing.
echo "==> wiring"

wired() {
  checked=$((checked + 1))
  if grep -qE -- "$3" "$root/.github/workflows/$2"; then
    printf '  ok   %s\n' "$1"
  else
    printf '  FAIL %s (no match for %s in %s)\n' "$1" "$3" "$2" >&2
    failures=$((failures + 1))
  fi
}

wired "the whole run is unscoped"                    full.yml    '^      scope: full$'
wired "the whole run is the push set"                full.yml    '^      set: push$'
wired "the whole run is on a schedule"               full.yml    '^    - cron: '
wired "nightly publishes from the whole run"         nightly.yml '^    workflows: \[full\]$'
wired "pages publishes from the whole run"           pages.yml   '^    workflows: \[full\]$'
wired "a push hands its base to the gates"           ci.yml      '^      base: \$\{\{ needs\.base\.outputs\.sha \}\}$'
wired "the gates ask affected_gates.py"              gates.yml   'scripts/lib/affected_gates\.py'
wired "a release is never scoped"                    release.yml '^      set: all$'

checked=$((checked + 1))
if grep -qE '^      scope:' "$root/.github/workflows/release.yml" "$root/.github/workflows/soak.yml"; then
  printf '  FAIL %s\n' "release.yml and soak.yml pass no scope (the default is the whole set)" >&2
  failures=$((failures + 1))
else
  printf '  ok   %s\n' "release.yml and soak.yml pass no scope (the default is the whole set)"
fi

echo
if [ "$failures" -gt 0 ]; then
  echo "affected-gates: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "affected-gates: $checked checks passed."

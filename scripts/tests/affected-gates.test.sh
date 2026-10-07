#!/usr/bin/env bash
# gate-inputs: .*\.csproj$|\.github/workflows/|src/Rask\.Templates/[^/]+/client/package\.json$
# Table test for scripts/lib/affected_gates.py — which CI gates a change reaches on a push to main —
# and for the plan step in gates.yml that turns that answer, or a pull request's diff, into jobs.
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

# The byte and allocation budgets follow what they measure.
expect_has   "the core reaches the benchmark budgets"              bench     src/Rask.Core/Component.cs
expect_has   "the browser runtime's bundle is one of them"          bench     src/Rask.Wasm/Browser/rask.wasm.ts
expect_lacks "a site page does not reach them"                     bench     src/Rask.Site/Program.cs

# The CLI gates are selected by file, not by the graph (affected_gates.py says why): the graph would
# select them for every source change, and they are the longest gates there are.
expect_lacks "an ordinary core change does not pack the CLI feed"  packaging src/Rask.Core/Component.cs
expect_has   "the feed's own fixture reaches the CLI gates"        packaging tests/Rask.Cli.Tests/CliBuildE2E.cs
expect_has   "the CLI reaches the CLI gates"                       packaging src/Rask.Cli/Program.cs
expect_has   "a template reaches the CLI gates"                    packaging src/Rask.Templates/server/Program.cs
expect_has   "a shipped project file reaches the CLI gates"        packaging src/Rask.Mail/Rask.Mail.csproj

# The front-end gates are one job per template, also selected by file: a template's tree reaches its
# own job, and what installs, generates, bundles and serves every front end reaches all of them.
expect_has   "a front end's lockfile reaches its own job"          frontend-vue     src/Rask.Templates/vue/client/package-lock.json
expect_lacks "...and no other front end's"                         frontend-react   src/Rask.Templates/vue/client/package-lock.json
expect_has   "a front end's host half reaches it too"              frontend-angular src/Rask.Templates/angular/Program.cs
expect_lacks "the server template reaches no front end"            frontend-react   src/Rask.Templates/server/Program.cs
expect_lacks "an island fragment reaches no front end"             frontend-react   src/Rask.Templates/_islands/react/island.json
expect_has   "a template that is gone reaches every front end"     frontend-lit     src/Rask.Templates/removed-front-end/client/package.json
expect_has   "the SPA host reaches every front end"                frontend-lit     src/Rask.Spa.Hosting/client/client.ts
expect_has   "the task that writes the typed client"               frontend-solid   src/Rask.Spa.Tasks/WriteGeneratedTypeScriptTask.cs
expect_has   "the TypeScript emitter"                              frontend-svelte  src/Rask.Batteries.Generators/TypeScriptModule.cs
expect_has   "the sign-in client every front end is handed"        frontend-preact  src/Rask.Core/Resources/browser/auth.ts
expect_has   "the scaffolder"                                      frontend-react   src/Rask.Cli/Scaffolding/ProjectGenerator.Spa.cs
expect_has   "the front-end gate's own test"                       frontend-vue     tests/Rask.Templates.E2E.Tests/FrontEndBuildE2ETests.cs
expect_lacks "another generator reaches no front end"              frontend-react   src/Rask.Batteries.Generators/JobRegistryGenerator.cs
expect_lacks "an ordinary core change reaches no front end"        frontend-react   src/Rask.Core/Component.cs
expect_lacks "a CLI command reaches no front end"                  frontend-react   src/Rask.Cli/Program.cs

# The scoper and the gate script list the front ends by one rule; a key only one of them knew would be a
# job nothing selects, or a selection no job answers.
checked=$((checked + 1))
listed="$("$root/scripts/run-template-e2e.sh" --list-front-ends | sed 's/^/frontend-/' | tr '\n' ' ')"
scoped="$(gates src/Rask.Spa.Hosting/client/client.ts | tr ' ' '\n' | grep '^frontend-' | tr '\n' ' ')"
if [ -n "$listed" ] && [ "$listed" = "$scoped" ]; then
  printf '  ok   %s\n' "the gate script and the scoper list the same front ends: $listed"
else
  printf '  FAIL the gate script lists [%s], the scoper answers [%s]\n' "$listed" "$scoped" >&2
  failures=$((failures + 1))
fi

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

# --- the plan ---------------------------------------------------------------------------------------
# gates.yml's own plan step, lifted out of the workflow and run as written over a throwaway repository
# with two front ends. It is what turns one "front end" entry into a job per template, and what decides
# whether a Dependabot pull request is held to one — the rule dependabot-merge.yml's merge rests on.
echo "==> the plan"

sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
python3 - "$root/.github/workflows/gates.yml" "$sandbox" <<'PY'
import sys, textwrap

lines = open(sys.argv[1], encoding="utf-8").read().split("\n")
indent = lambda line: len(line) - len(line.lstrip())
gates = next(i for i, line in enumerate(lines) if line.strip() == "GATES: >-")
run = next(i for i, line in enumerate(lines) if i > gates and line.strip() == "run: |")
end = next(i for i, line in enumerate(lines) if i > run and line.strip() and indent(line) <= indent(lines[run]))
open(sys.argv[2] + "/gates.json", "w", encoding="utf-8").write(" ".join(line.strip() for line in lines[gates + 1:run]))
open(sys.argv[2] + "/plan.sh", "w", encoding="utf-8").write(textwrap.dedent("\n".join(lines[run + 1:end])) + "\n")
PY

repo="$sandbox/repo"
mkdir -p "$repo/scripts" "$repo/src/Rask.Templates/vue/client" "$repo/src/Rask.Templates/react/client" "$repo/src/Rask.Templates/server"
cp "$root/scripts/run-template-e2e.sh" "$repo/scripts/"
echo '{}' > "$repo/src/Rask.Templates/vue/client/package.json"
echo '{}' > "$repo/src/Rask.Templates/react/client/package.json"
echo '//' > "$repo/src/Rask.Templates/server/Program.cs"
sandbox_git() {
  git -C "$repo" -c user.name=gate -c user.email=gate@example.invalid -c commit.gpgsign=false -c core.hooksPath=/dev/null "$@"
}
sandbox_git init -q
sandbox_git add -A && sandbox_git commit -qm "base"
echo '{}' > "$repo/src/Rask.Templates/vue/client/package-lock.json"
sandbox_git add -A && sandbox_git commit -qm "a vue lockfile bump"
vue_bump="$(sandbox_git rev-parse HEAD)"
echo '<Project/>' > "$repo/Directory.Packages.props"
sandbox_git add -A && sandbox_git commit -qm "a NuGet bump"
nuget_bump="$(sandbox_git rev-parse HEAD)"

# planned <set> <only> <pull request base> [<head>]  -> the gates as JSON
planned() {
  [ -z "${4:-}" ] || sandbox_git checkout -q "$4"
  ( cd "$repo" && SET="$1" ONLY="$2" SCOPE=full BASE="" PR_BASE="$3" GATES="$(cat "$sandbox/gates.json")" \
      GITHUB_OUTPUT="$sandbox/output" bash -eo pipefail "$sandbox/plan.sh" >/dev/null 2>&1 )
  sed -n 's/^gates=//p' "$sandbox/output"
  : > "$sandbox/output"
}

# plans <name> <jq filter that must be true> <planned args...>
plans() {
  name="$1"; holds="$2"; shift 2
  checked=$((checked + 1))
  out="$(planned "$@")"
  if [ -n "$out" ] && [ "$(jq -r "$holds" <<<"$out")" = "true" ]; then
    printf '  ok   %s\n' "$name"
  else
    printf '  FAIL %s -> %s\n' "$name" "$(jq -c '[.[].name]' <<<"${out:-[]}")" >&2
    failures=$((failures + 1))
  fi
}

named='[.[].name]'
plans "the push set has a job per front end"               "$named | index(\"front end react\") != null and index(\"front end vue\") != null" push "" ""
plans "a job runs its own template and answers to its key" 'any(.[]; .name == "front end vue" and .run == "scripts/run-template-e2e.sh --front-end=vue" and .when == "frontend-vue")' push "" ""
plans "only= names one front end"                          "$named == [\"front end vue\"]" push 'front end vue$' ""
plans "a lockfile bump is held to the template it changed" "$named | index(\"front end vue\") != null and index(\"front end react\") == null" deps "" "$vue_bump~1" "$vue_bump"
plans "...with the rest of the deps set"                   "$named | index(\"templates\") != null and index(\"build\") != null" deps "" "$vue_bump~1" "$vue_bump"
plans "a NuGet bump is held to no front end"               "$named | any(.[]; startswith(\"front end\")) | not" deps "" "$nuget_bump~1" "$nuget_bump"
plans "a deps run that cannot see its change takes them all" "$named | index(\"front end react\") != null and index(\"front end vue\") != null" deps "" ""

echo
if [ "$failures" -gt 0 ]; then
  echo "affected-gates: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "affected-gates: $checked checks passed."

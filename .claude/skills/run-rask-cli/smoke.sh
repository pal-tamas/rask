#!/usr/bin/env bash
# Smoke driver for the `rask` CLI (src/Rask.Cli).
#
# The CLI is the framework's front door: `rask new` scaffolds a project, `rask generate` scaffolds
# pages/components/CRUD features/jobs/emails into it, `rask info|db|dev|deploy` wrap the SDK. This
# script drives the two that actually produce artifacts end-to-end (new + generate), asserting exit
# codes AND that the expected files / output show up — the thing a unit test of an internal method
# can't prove: that the assembled tool scaffolds a real project on disk.
#
# It stays HERMETIC: the full `generate feature` reaches out to nuget.org to add EF Core/Cqrs/Data
# packages, so the feature step here uses --dry-run (prints the plan, writes nothing, no network).
# The real-write proof is `generate page`, which writes one .cs file and touches no packages.
#
# Usage (from anywhere — paths resolve off this script's location):
#   .claude/skills/run-rask-cli/smoke.sh              # build the CLI, then run the smoke
#   RASK_CLI_NO_BUILD=1 .claude/skills/run-rask-cli/smoke.sh   # skip the build (already built)
set -uo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CLI_PROJ="$REPO/src/Rask.Cli"
WORK="$(mktemp -d)"
PASS=0; FAIL=0

cleanup() { rm -rf "$WORK"; }
trap cleanup EXIT

rask() { dotnet run --project "$CLI_PROJ" --no-build -- "$@"; }

# check <name> <expected-exit> <grep-pattern|-> -- <rask args...>   (runs in the current dir)
check() { checkin "." "$@"; }

# checkin <dir> <name> <expected-exit> <grep-pattern|-> -- <rask args...>
checkin() {
  local dir="$1" name="$2" want="$3" pat="$4"; shift 4; [ "$1" = "--" ] && shift
  local out code
  out="$(cd "$dir" && rask "$@" 2>&1)"; code=$?
  local ok=1
  [ "$code" = "$want" ] || ok=0
  if [ "$pat" != "-" ]; then echo "$out" | grep -qE "$pat" || ok=0; fi
  if [ "$ok" = 1 ]; then
    echo "  PASS  $name  (exit $code)"; PASS=$((PASS+1))
  else
    echo "  FAIL  $name  (exit $code, wanted $want${pat:+, /$pat/})"; echo "$out" | sed 's/^/        | /' | head -6; FAIL=$((FAIL+1))
  fi
}

# assert a file exists
have() {
  if [ -f "$1" ]; then echo "  PASS  file: ${1#$WORK/}"; PASS=$((PASS+1));
  else echo "  FAIL  file missing: ${1#$WORK/}"; FAIL=$((FAIL+1)); fi
}

if [ "${RASK_CLI_NO_BUILD:-}" != "1" ]; then
  echo "==> Build the CLI (Debug)"
  dotnet build "$CLI_PROJ" -c Debug -m:1 --nologo | tail -2
fi

echo "==> Environment"
check "info"            0 "Rask CLI"          -- info
check "--version"       0 "[0-9]+\.[0-9]+"    -- --version

echo "==> Help is discoverable (options table + examples; hidden feature flags surfaced)"
check "generate --help shows feature flags" 0 "Feature options" -- generate --help
check "generate --help shows examples"      0 "Examples:"       -- generate --help
# --outbox/--tests were previously undocumented — help must now list them.
helpout="$( rask generate --help 2>&1 )"
if echo "$helpout" | grep -q -- "--outbox" && echo "$helpout" | grep -q -- "--tests"; then
  echo "  PASS  generate --help lists --outbox/--tests"; PASS=$((PASS+1))
else
  echo "  FAIL  generate --help missing hidden flags"; echo "$helpout" | sed 's/^/        | /' | head -6; FAIL=$((FAIL+1))
fi

echo "==> Scaffold a new server project"
check "new Shop"        0 "Created Shop"      -- new Shop --template server --output "$WORK/Shop"
have "$WORK/Shop/Shop.csproj"
have "$WORK/Shop/Program.cs"
have "$WORK/Shop/App.cs"

echo "==> Generate into it (real write: a page — no packages, no network)"
( cd "$WORK/Shop" && rask generate page Dashboard --route /dashboard ) >/dev/null 2>&1 \
  && { echo "  PASS  generate page (exit 0)"; PASS=$((PASS+1)); } \
  || { echo "  FAIL  generate page"; FAIL=$((FAIL+1)); }
have "$WORK/Shop/Features/Dashboard/DashboardPage.cs"

echo "==> Generate a CRUD feature (dry-run: hermetic, shows the plan, no nuget)"
featureout="$( cd "$WORK/Shop" && rask generate feature Product Name:string Price:decimal --dry-run 2>&1 )"
if echo "$featureout" | grep -q "would write Features/Products/Product.cs" \
   && echo "$featureout" | grep -q "AggregateRoot"; then
  echo "  PASS  generate feature --dry-run (CRUD plan)"; PASS=$((PASS+1))
else
  echo "  FAIL  generate feature --dry-run"; echo "$featureout" | sed 's/^/        | /' | head -6; FAIL=$((FAIL+1))
fi

echo "==> Error paths (must exit 1 with a helpful message)"
mkdir -p "$WORK/empty"
# Project is resolved by walking UP for a single .csproj — so "no project" must run in an empty dir,
# and field validation (which happens AFTER project resolution) must run INSIDE the Shop project.
checkin "$WORK/empty" "generate outside a project" 1 "Couldn't find a single .csproj" -- generate page Nope --dry-run
check                 "unknown command"            1 "Unknown command"                -- frobnicate
checkin "$WORK/Shop"  "bad field type"             1 "Unknown field type"             -- generate feature Bad Name:wobble --dry-run

echo
echo "==> $PASS passed, $FAIL failed."
[ "$FAIL" = 0 ]

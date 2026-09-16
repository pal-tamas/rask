#!/usr/bin/env bash
# Smoke driver for the `rask` CLI (src/Rask.Cli).
#
# The CLI is the framework's front door: `rask new` scaffolds a project and `rask info|db|dev|deploy`
# wrap the SDK. Code *inside* a project is written by hand — there is no `generate` verb. This script
# drives the one command that produces artifacts end-to-end (`new`), asserting exit codes AND that the
# expected files show up — the thing a unit test of an internal method can't prove: that the assembled
# tool scaffolds a real project on disk.
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

# The built assembly, run directly — NOT `dotnet run --project`.
#
# `dotnet run` evaluates the project even under --no-build, and this repository declares
# RaskRequireSdk11 as an InitialTargets (Directory.Build.targets), so that evaluation resolves an SDK
# against the CURRENT DIRECTORY. Nearly every check below runs inside a scaffolded app, and a scaffold
# now ships a global.json pinning its own .NET band — a 10.0.x SDK. The repository then refuses to
# evaluate at all (RASKSDK001), and seven checks fail reporting an error about the FRAMEWORK's build
# rather than anything about the tool's behaviour. Running the assembly needs no SDK resolution and no
# MSBuild, so the driver tests the tool from wherever it likes.
rask() { dotnet "$CLI_DLL" "$@"; }

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
    echo "  FAIL  $name  (exit $code, wanted $want${pat:+, /$pat/})"; echo "$out" | tail -20 | sed 's/^/        | /'; FAIL=$((FAIL+1))
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

# Resolved once, here rather than inside rask(): every check runs it in a $( ) subshell, so a lookup
# done in there is thrown away with the subshell and repeated on all thirty-odd calls.
CLI_DLL="$(ls -1 "$CLI_PROJ"/bin/Debug/*/Rask.Cli.dll 2>/dev/null | head -1)"
if [ -z "$CLI_DLL" ]; then
  echo "No built Rask.Cli.dll under $CLI_PROJ/bin/Debug — run without RASK_CLI_NO_BUILD=1." >&2
  exit 1
fi

echo "==> Environment"
check "info"            0 "Rask CLI"          -- info
check "--version"       0 "[0-9]+\.[0-9]+"    -- --version

echo "==> Help is discoverable (options table + examples)"
check "new --help shows examples"           0 "Examples:"       -- new --help
echo "==> Shell completion (generated from the live command + option set)"
check "completion bash"  0 "complete -F _rask_complete rask" -- completion bash
check "completion fish"  0 "complete -c rask"                 -- completion fish
check "completion bad shell" 2 "Unknown 'rask completion' action" -- completion tcsh
# Completion is generated from the schema, so subcommands and closed-set values come along for free.
check "completion knows db's actions" 0 "backup"              -- completion bash
check "completion knows --template's values" 0 "wasm"         -- completion zsh

echo "==> new --dry-run previews without writing"
check "new --dry-run"    0 "would write"      -- new Ghost --template server --output "$WORK/Ghost" --dry-run
[ -e "$WORK/Ghost" ] && { echo "  FAIL  new --dry-run wrote files"; FAIL=$((FAIL+1)); } || { echo "  PASS  new --dry-run wrote nothing"; PASS=$((PASS+1)); }

echo "==> Scaffold a new server project"
check "new Shop"        0 "Created Shop"      -- new Shop --template server --output "$WORK/Shop"
have "$WORK/Shop/Shop.csproj"
have "$WORK/Shop/Program.cs"
have "$WORK/Shop/Features/Shared/App.cs"
have "$WORK/Shop/global.json"
# The pin decides which SDK compiles the app. Asserted on the bytes rather than trusted, because the
# two ways to get it wrong are both silent: latestMajor still selects a preview SDK of the NEXT major
# (which is the behaviour the file exists to stop), and a version naming a real SDK release resolves
# nothing at all while that band is still a release candidate.
if grep -q '"version": "10.0.0"' "$WORK/Shop/global.json" \
   && grep -q '"rollForward": "latestFeature"' "$WORK/Shop/global.json"; then
  echo "  PASS  global.json pins the 10.0 band"; PASS=$((PASS+1))
else
  echo "  FAIL  global.json does not pin the 10.0 band"; sed 's/^/        | /' "$WORK/Shop/global.json"; FAIL=$((FAIL+1))
fi

echo "==> Deploy scaffolding + preview (hermetic: no host, no docker, no network)"
# `deploy` itself needs Docker + SSH + a real box, so it can't run here. Its two offline modes can:
# --dry-run prints the docker commands, and --github-actions is pure file scaffolding.
printf 'FROM scratch\n' > "$WORK/Shop/Dockerfile"
checkin "$WORK/Shop" "deploy --dry-run previews docker"   0 "docker -H ssh://root@box build" -- deploy --host root@box --domain shop.example.com --name shop --dry-run
checkin "$WORK/Shop" "deploy --help groups host setup"    0 "Host setup options"             -- deploy --help
checkin "$WORK/Shop" "deploy --github-actions"            0 "gh secret set RASK_SSH_PRIVATE_KEY" -- deploy --host root@box.example.com --name shop --github-actions
have "$WORK/Shop/.github/workflows/deploy.yml"
# CI must never provision a host: the generated workflow deploys with --no-setup-host.
if grep -q -- "rask deploy --no-setup-host" "$WORK/Shop/.github/workflows/deploy.yml"; then
  echo "  PASS  generated workflow never provisions the host"; PASS=$((PASS+1))
else
  echo "  FAIL  generated workflow should deploy with --no-setup-host"; FAIL=$((FAIL+1))
fi
checkin "$WORK/Shop" "deploy --github-actions won't clobber" 1 "already exists"             -- deploy --host root@box.example.com --name shop --github-actions
# Contradictory host-setup flags must be caught before anything reaches the network.
checkin "$WORK/Shop" "deploy rejects a bad --deploy-user" 2 "isn't a valid Linux user name" -- deploy --host root@box --name shop --deploy-user "bad; rm -rf /"

echo "==> A wrong command line exits 2, names the fix, and guesses what you meant"
mkdir -p "$WORK/empty"
check                 "unknown command suggests"   2 "Did you mean 'deploy'"          -- deplyo
check                 "unknown command"            2 "Unknown command"                -- frobnicate
check                 "bad option value suggests"  2 "Did you mean 'server'"          -- new X --template srever
check                 "unknown option suggests"    2 "Did you mean '--template'"      -- new X --tempate server
check                 "missing action lists them"  2 "Specify a 'rask db' action"     -- db
check                 "unknown action suggests"    2 "Did you mean 'backup'"          -- db bakcup
check                 "every rejection says where to look" 2 "Run 'rask db --help' for details" -- db bakcup
# `-h` is help for every command; `deploy --host` therefore has no short form.
check                 "-h is help, not --host"     0 "Usage: rask deploy"             -- deploy -h root@box

echo "==> Error paths (work that was attempted and failed still exits 1)"
# Project is resolved by walking UP for a single .csproj — so "no project" must run in an empty dir,
# and field validation (which happens AFTER project resolution) must run INSIDE the Shop project.
checkin "$WORK/empty" "db outside a project"      1 "Couldn't find a .csproj"        -- db list

echo
echo "==> $PASS passed, $FAIL failed."
[ "$FAIL" = 0 ]

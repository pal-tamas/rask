#!/usr/bin/env bash
# Proves the public-API gate (docs/api-style.md) actually fails.
#
# The gate's whole value is that an unrecorded public member cannot land. A green build says nothing
# about that: it is equally consistent with "the surface matches the baseline" and with "the analyzer
# never ran" — a mis-scoped condition in Directory.Build.targets, a PackageReference that stopped
# flowing, a severity someone turned down to keep a build moving. This repo has shipped several gates
# that passed by not running, so this one is proved the only way that means anything: by breaking it
# on purpose and requiring the specific diagnostic.
#
# Four cases, and the control matters as much as the other three. Without it, a repo that was already
# red would make every failure case "pass" for the wrong reason.
#
# Scoped to one small single-framework project so the whole thing is four builds of Rask.Cache rather
# than four builds of the solution.
#
# Usage:  scripts/tests/public-api-gate.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

project="src/Rask.Cache/Rask.Cache.csproj"

tmp="$(mktemp -d -t rask-public-api-gate.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

# Nothing under src/ is written, moved or deleted (#1084). This test used to add a probe file to
# src/Rask.Cache, append to its real baseline, and move its PublicAPI folder out of the tree — while
# run-unit-local.sh ran the other self-tests concurrently, one of which walks src/ and met the folder
# vanishing mid-walk. Every case below instead hands the build its inputs by property: a probe file
# from $tmp (RaskPublicApiGateProbe), and a baseline directory that is a copy in $tmp or an empty one
# (RaskPublicApiDir). The real tree is only ever read.
baseline_copy="$tmp/baseline"
cp -R "$root/src/Rask.Cache/PublicAPI" "$baseline_copy"
empty_baseline="$tmp/no-baseline"
mkdir -p "$empty_baseline"

failures=0
checked=0

# build_log <name> [msbuild-property ...] -> writes the build output to $tmp/<name>.log, echoes the exit code
build_log() {
  local name="$1" log="$tmp/$1.log" rc=0
  shift
  CI=true dotnet build "$project" -m:1 --nologo "$@" > "$log" 2>&1 || rc=$?
  echo "$rc"
}

# assert_green <name> [msbuild-property ...]
assert_green() {
  local name="$1" rc
  shift
  rc="$(build_log "$name" "$@")"
  checked=$((checked + 1))
  if [ "$rc" -eq 0 ]; then
    printf '  ok   %-52s -> build succeeded\n' "$name"
  else
    printf '  FAIL %-52s -> build failed (rc=%s), so the two failure cases below would prove nothing\n' "$name" "$rc" >&2
    sed -n '/error /p' "$tmp/$name.log" | head -5 >&2
    failures=$((failures + 1))
  fi
}

# assert_red <name> <expected-diagnostic-id> [msbuild-property ...]
assert_red() {
  local name="$1" want="$2" rc
  shift 2
  rc="$(build_log "$name" "$@")"
  checked=$((checked + 1))
  if [ "$rc" -eq 0 ]; then
    printf '  FAIL %-52s -> build SUCCEEDED; the gate did not run\n' "$name" >&2
    failures=$((failures + 1))
  elif ! grep -q "$want" "$tmp/$name.log"; then
    printf '  FAIL %-52s -> failed, but not with %s\n' "$name" "$want" >&2
    sed -n '/error /p' "$tmp/$name.log" | head -5 >&2
    failures=$((failures + 1))
  else
    printf '  ok   %-52s -> %s\n' "$name" "$want"
  fi
}

echo "==> public-API gate"

# The control. Everything below is only evidence if the clean tree is green -- built against the COPY, so
# the property the other cases rely on is proved to be read at all.
assert_green "clean tree builds" "-p:RaskPublicApiDir=$baseline_copy/"

# A public member nobody recorded. This is the case the gate exists for.
cat > "$tmp/PublicApiGateProbe.cs" <<'CS'
namespace Rask.Cache;

/// <summary>Compiled in from a temp directory by the test that wrote it; never part of the tree.</summary>
public sealed class PublicApiGateProbe
{
    /// <summary>Unrecorded on purpose.</summary>
    public int Value { get; set; }
}
CS
assert_red "unrecorded public member" "RS0016" \
  "-p:RaskPublicApiDir=$baseline_copy/" "-p:RaskPublicApiGateProbe=$tmp/PublicApiGateProbe.cs"

# The other direction: a baseline entry with nothing behind it. Catches a rename that edited the
# source and left the file, which is exactly the shape of a half-finished API change.
stale_baseline="$tmp/stale-baseline"
cp -R "$baseline_copy" "$stale_baseline"
printf 'Rask.Cache.ThisTypeDoesNotExist\n' >> "$stale_baseline/net10.0/PublicAPI.Unshipped.txt"
assert_red "baseline entry with no member" "RS0017" "-p:RaskPublicApiDir=$stale_baseline/"

# And the way this gate would come to pass by not running: no baseline at all, so the analyzer has
# nothing to compare against and reports nothing. Without the RaskVerifyPublicApiBaseline target that
# is a GREEN build on an untracked surface.
assert_red "no baseline means no silent pass" "Rask.Cache is covered by the public-API gate" \
  "-p:RaskPublicApiDir=$empty_baseline/"

echo
if [ "$failures" -ne 0 ]; then
  echo "public-API gate: $failures of $checked case(s) FAILED" >&2
  exit 1
fi
echo "public-API gate: $checked case(s) ok"

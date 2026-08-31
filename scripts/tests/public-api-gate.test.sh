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
api_file="src/Rask.Cache/PublicAPI/net10.0/PublicAPI.Unshipped.txt"
probe="src/Rask.Cache/__PublicApiGateProbe.cs"

tmp="$(mktemp -d -t rask-public-api-gate.XXXXXX)"

# Restore by copy rather than `git checkout`: the baseline is a normal file, and a test that repairs
# the tree only when its subject happens to be committed leaves a modified baseline behind on the one
# run where that is not true -- the first.
cp "$root/$api_file" "$tmp/api-file.orig"

cleanup() {
  rm -f "$root/$probe"
  if [ -d "$tmp/PublicAPI" ]; then mv "$tmp/PublicAPI" "$root/src/Rask.Cache/PublicAPI"; fi
  cp "$tmp/api-file.orig" "$root/$api_file"
  rm -rf "$tmp"
}
trap cleanup EXIT

failures=0
checked=0

# build_log <name> -> writes the build output to $tmp/<name>.log, echoes the exit code
build_log() {
  local name="$1" log="$tmp/$1.log" rc=0
  CI=true dotnet build "$project" -m:1 --nologo > "$log" 2>&1 || rc=$?
  echo "$rc"
}

# assert_green <name>
assert_green() {
  local name="$1" rc
  rc="$(build_log "$name")"
  checked=$((checked + 1))
  if [ "$rc" -eq 0 ]; then
    printf '  ok   %-52s -> build succeeded\n' "$name"
  else
    printf '  FAIL %-52s -> build failed (rc=%s), so the two failure cases below would prove nothing\n' "$name" "$rc" >&2
    sed -n '/error /p' "$tmp/$name.log" | head -5 >&2
    failures=$((failures + 1))
  fi
}

# assert_red <name> <expected-diagnostic-id>
assert_red() {
  local name="$1" want="$2" rc
  rc="$(build_log "$name")"
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

# The control. Everything below is only evidence if the clean tree is green.
assert_green "clean tree builds"

# A public member nobody recorded. This is the case the gate exists for.
cat > "$root/$probe" <<'CS'
namespace Rask.Cache;

/// <summary>Deleted by the test that wrote it. If you are reading this in a diff, something leaked.</summary>
public sealed class PublicApiGateProbe
{
    /// <summary>Unrecorded on purpose.</summary>
    public int Value { get; set; }
}
CS
assert_red "unrecorded public member" "RS0016"
rm -f "$root/$probe"

# The other direction: a baseline entry with nothing behind it. Catches a rename that edited the
# source and left the file, which is exactly the shape of a half-finished API change.
printf 'Rask.Cache.ThisTypeDoesNotExist\n' >> "$root/$api_file"
assert_red "baseline entry with no member" "RS0017"
cp "$tmp/api-file.orig" "$root/$api_file"

# And the way this gate would come to pass by not running: no baseline at all, so the analyzer has
# nothing to compare against and reports nothing. Without the RaskVerifyPublicApiBaseline target that
# is a GREEN build on an untracked surface.
mv "$root/src/Rask.Cache/PublicAPI" "$tmp/PublicAPI"
assert_red "no baseline means no silent pass" "Rask.Cache is covered by the public-API gate"
mv "$tmp/PublicAPI" "$root/src/Rask.Cache/PublicAPI"

echo
if [ "$failures" -ne 0 ]; then
  echo "public-API gate: $failures of $checked case(s) FAILED" >&2
  exit 1
fi
echo "public-API gate: $checked case(s) ok"

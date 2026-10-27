#!/usr/bin/env bash
# Tests for scripts/lib/publish_packages.py -- the order, the retries and the waits of a publish.
#
# Nothing here reaches a feed. The packages are nuspecs zipped on the spot, the push command is a
# stand-in injected through PUSH_CMD, and "the feed" is a directory read through file:// URLs.
#
# What it pins is the 2026-10-12 nightly: a push that stopped half way left Rask.Server listed at a
# version Rask had not reached, and every app floating `0.*-*` failed restore. So: nothing is pushed
# ahead of what it depends on, a stall is retried, and a failure stops BELOW the packages above it.
#
# Usage:  scripts/tests/publish-packages.test.sh   (run by scripts/run-unit-local.sh)
set -uo pipefail

root="$(git rev-parse --show-toplevel)"
publish="$root/scripts/lib/publish_packages.py"
# shellcheck source=../lib/push_verdict.sh
. "$root/scripts/lib/push_verdict.sh"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version="0.23.1-alpha.0.403"   # ends in an HTTP status on purpose: a file name is not a verdict
commit="1111111111111111111111111111111111111111"
key="s3cr3t-key-never-printed"

failures=0
checked=0
ok()   { checked=$((checked + 1)); printf '  ok   %s\n' "$1"; }
fail() { checked=$((checked + 1)); failures=$((failures + 1)); printf '  FAIL %s\n%s\n' "$1" "${2:-}" | sed '2,$s/^/       /' >&2; }
# check <name> <command...>   passes when the command does
check() { local name="$1"; shift; if "$@" >/dev/null 2>&1; then ok "$name"; else fail "$name" "$*"; fi; }
same()  { if [ "$2" = "$3" ]; then ok "$1"; else fail "$1" "got:      $2
expected: $3"; fi; }

# package <dir> <id> [dependency ...]   a dependency is `Id` (this set's version) or `Id=1.2.3`
package() {
  python3 - "$@" "$version" "$commit" <<'PY'
import sys, zipfile
directory, pid, *deps, version, commit = sys.argv[1:]
rows = "".join('<dependency id="%s" version="%s" exclude="Build,Analyzers" />' % ((d.split("=") + [version])[:2][0], (d.split("=") + [version])[1]) for d in deps)
nuspec = f'''<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
<id>{pid}</id><version>{version}</version>
<repository type="git" url="https://example.invalid/rask.git" commit="{commit}" />
<dependencies><group targetFramework="net10.0">{rows}</group></dependencies>
</metadata></package>'''
with zipfile.ZipFile(f"{directory}/{pid}.{version}.nupkg", "w") as z:
    z.writestr(f"{pid}.nuspec", nuspec)
PY
}

# The shape of the real set: a core, batteries on it, hosts on those, and a tool on nothing.
the_set() {
  mkdir -p "$1"
  package "$1" Rask Microsoft.Extensions.Options=10.0.12
  package "$1" Rask.Cli
  package "$1" Rask.Ui Rask
  package "$1" Rask.Data Rask Microsoft.EntityFrameworkCore=10.0.12
  package "$1" Rask.Server Rask.Ui Rask Rask.Data
  package "$1" Rask.Dashboard Rask.Data Rask.Ui
  package "$1" Rask.Postgres Rask.Server
}

# The stand-in for `dotnet nuget push`. $FAKE/fail/<file> holds "<times> <what to print>"; every
# attempt lands in attempts.log, every accepted push in pushed.log and, with $FAKE/feed, on "the feed":
# a flat container, index.json beside {version}/{id}.nuspec. $FAKE/foreign/<id> holds a commit the feed
# serves instead of the pushed one's -- what a "duplicate" of an earlier run's push looks like.
cat > "$work/push" <<'SH'
#!/usr/bin/env bash
file="$(basename "$1")"
echo "$file" >> "$FAKE/attempts.log"
echo "Pushing $file to 'the feed' with $*..."
if [ -f "$FAKE/fail/$file" ]; then
  read -r times what < "$FAKE/fail/$file"
  if [ "$times" -gt 0 ]; then
    echo "$((times - 1)) $what" > "$FAKE/fail/$file"
    echo "  $what"
    exit 1
  fi
fi
echo "  Created https://feed.invalid/api/v2/package/ 12ms"
echo "Your package was pushed."
echo "$file" >> "$FAKE/pushed.log"
id="${file%."$FAKE_VERSION".*}"
if [ -d "$FAKE/feed" ] && [ ! -e "$FAKE/unserved/$id" ]; then
  lower="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
  mkdir -p "$FAKE/feed/$lower/$FAKE_VERSION"
  echo "{\"versions\":[\"0.1.0\",\"$FAKE_VERSION\"]}" > "$FAKE/feed/$lower/index.json"
  unzip -p "$1" '*.nuspec' > "$FAKE/feed/$lower/$FAKE_VERSION/$lower.nuspec"
  if [ -f "$FAKE/foreign/$id" ]; then
    sed -i.bak "s/commit=\"[^\"]*\"/commit=\"$(cat "$FAKE/foreign/$id")\"/" "$FAKE/feed/$lower/$FAKE_VERSION/$lower.nuspec"
  fi
fi
SH
chmod +x "$work/push"

# fresh <name>   a new set and a new fake feed; sets $dir and $FAKE
fresh() {
  dir="$work/$1/artifacts"; FAKE="$work/$1/fake"
  the_set "$dir"; mkdir -p "$FAKE/fail" "$FAKE/unserved" "$FAKE/foreign"; : > "$FAKE/attempts.log"; : > "$FAKE/pushed.log"
  export FAKE
}
# push <args...>   the script against the stand-in, waits scaled down to nothing; output in $out
push() {
  out="$(FAKE_VERSION="$version" PUSH_CMD="$work/push" PUBLISH_KEY="$key" python3 "$publish" push "$dir" \
    --source the-feed --api-key-env PUBLISH_KEY --backoff 0.01 --throttle-wait 0.03 --poll 0.05 "$@" 2>&1)"
}
attempts() { grep -c "^$1\.$version\.nupkg$" "$FAKE/attempts.log"; }
line_of()  { grep -n "^$1\.$version\.nupkg$" "$FAKE/pushed.log" | head -1 | cut -d: -f1; }
# before <a> <b>   a was accepted ahead of b
before()   { [ -n "$(line_of "$1")" ] && [ -n "$(line_of "$2")" ] && [ "$(line_of "$1")" -lt "$(line_of "$2")" ]; }

echo "==> publish_packages: the order comes out of the nuspecs"

fresh order
same "leaves first, hosts last, third parties ignored" \
  "$(python3 "$publish" plan "$dir" | cut -f1,2 | tr '\t' ':' | xargs)" \
  "0:Rask 0:Rask.Cli 1:Rask.Data 1:Rask.Ui 2:Rask.Dashboard 2:Rask.Server 3:Rask.Postgres"

package "$dir" Rask.Query Rask.Cache
python3 "$publish" plan "$dir" > "$work/plan.out" 2>&1
same "a dependency of ours that was not packed stops the publish" "$?" "2"
check "and it is named" grep -q "Rask.Query depends on Rask.Cache $version, which was not packed" "$work/plan.out"
rm "$dir"/Rask.Query.*

package "$dir" Rask Rask.Postgres
python3 "$publish" plan "$dir" > "$work/plan.out" 2>&1
same "a circle is refused, not pushed in some order" "$?" "2"
check "and it is named" grep -q "in a circle" "$work/plan.out"

fresh declared
for id in Rask Rask.Cli Rask.Ui Rask.Data Rask.Server Rask.Dashboard Rask.Postgres Rask.Site; do
  mkdir -p "$work/src/$id"; echo "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>" > "$work/src/$id/$id.csproj"
done
echo "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>" > "$work/src/Rask.Site/Rask.Site.csproj"
mkdir -p "$work/src/Rask.Core"   # the project is Rask.Core, the package is Rask
mv "$work/src/Rask/Rask.csproj" "$work/src/Rask.Core/Rask.Core.csproj"; rmdir "$work/src/Rask"
sed -i.bak 's|<IsPackable>true</IsPackable>|<PackageId>Rask</PackageId>|' "$work/src/Rask.Core/Rask.Core.csproj"
check "the set the tree declares is the set that was packed" python3 "$publish" plan "$dir" --projects "$work/src"
rm "$dir/Rask.Data.$version.nupkg" "$dir/Rask.Server.$version.nupkg" "$dir/Rask.Dashboard.$version.nupkg" "$dir/Rask.Postgres.$version.nupkg"
package "$dir" Rask.Stray
python3 "$publish" plan "$dir" --projects "$work/src" > "$work/plan.out" 2>&1
same "a declared project that was not packed stops the publish" "$?" "2"
check "it is named" grep -q "declared and not packed: Rask.Dashboard, Rask.Data, Rask.Postgres, Rask.Server;" "$work/plan.out"
check "and so is a package no project declares" grep -q "packed and not declared: Rask.Stray" "$work/plan.out"

fresh leftover
( version="0.23.1-alpha.0.1"; package "$dir" Rask.Old )
python3 "$publish" plan "$dir" > "$work/plan.out" 2>&1
same "a leftover of another version is refused" "$?" "2"

echo "==> publish_packages: the push"

fresh happy
push; status=$?
same "a clean publish succeeds" "$status" "0"
check "Rask before Rask.Ui" before Rask Rask.Ui
check "Rask.Ui before Rask.Server" before Rask.Ui Rask.Server
check "Rask.Data before Rask.Dashboard" before Rask.Data Rask.Dashboard
check "Rask.Server before Rask.Postgres" before Rask.Server Rask.Postgres
same "every package is pushed, once" "$(sort "$FAKE/pushed.log" | uniq | wc -l | xargs):$(wc -l < "$FAKE/attempts.log" | xargs)" "7:7"
if printf '%s' "$out" | grep -q "$key"; then fail "the key is never printed" "$out"; else ok "the key is never printed"; fi
check "the key did reach the push command" grep -q -- "--api-key \*\*\*" <<< "$out"

fresh stall
echo "2 error: Pushing took too long. You can change the default timeout of 300 seconds by using the --timeout <seconds> option." > "$FAKE/fail/Rask.$version.nupkg"
push --log "$work/stall/push.log"; status=$?
same "a stalled push is retried until it goes through" "$status:$(attempts Rask)" "0:3"
check "what depends on it still follows it" before Rask Rask.Server
same "the push log carries the attempt that counted" "$(push_verdict "$work/stall/push.log" Rask "$version")" "accepted"

fresh down
echo "99 Response status code does not indicate success: 503 (Service Unavailable)." > "$FAKE/fail/Rask.Ui.$version.nupkg"
push --attempts 4; status=$?
same "a feed that stays down fails the job after the last attempt" "$status:$(attempts Rask.Ui)" "1:4"
same "nothing that depends on the failed package was pushed" \
  "$(grep -cE "^Rask\.(Server|Dashboard|Postgres)\." "$FAKE/attempts.log")" "0"
check "its siblings were" grep -q "^Rask.Data.$version.nupkg$" "$FAKE/pushed.log"
check "the failure names the package" grep -q "Push failed at level 1.*Rask.Ui.$version.nupkg" <<< "$out"

echo "0 x" > "$FAKE/fail/Rask.Ui.$version.nupkg"
push; status=$?
same "a re-run finishes the same version" "$status:$(sort -u "$FAKE/pushed.log" | wc -l | xargs)" "0:7"

fresh refused
echo "99 Response status code does not indicate success: 403 (The specified API key is invalid).." > "$FAKE/fail/Rask.$version.nupkg"
push; status=$?
same "a refusal is not retried" "$status:$(attempts Rask)" "1:1"

fresh throttled
echo "1 Response status code does not indicate success: 429 (Too Many Requests). Retry-After: 1" > "$FAKE/fail/Rask.$version.nupkg"
echo "1 Response status code does not indicate success: 429 (Too Many Requests)." > "$FAKE/fail/Rask.Cli.$version.nupkg"
push; status=$?
same "a throttled push is retried" "$status:$(attempts Rask):$(attempts Rask.Cli)" "0:2:2"
check "after the wait the feed asked for" grep -q "Rask.$version.nupkg: throttled, attempt 1 of 5 -- retrying in 1 s" <<< "$out"
check "or the throttle floor when it named none" grep -q "Rask.Cli.$version.nupkg: throttled, attempt 1 of 5 -- retrying in 0.03 s" <<< "$out"

fresh duplicate
echo "99 Response status code does not indicate success: 409 (Conflict - The feed already contains 'Rask')." > "$FAKE/fail/Rask.$version.nupkg"
push; status=$?
same "a duplicate is the package being there" "$status:$(attempts Rask)" "0:1"

fresh othercommit
push --commit 2222222222222222222222222222222222222222; status=$?
same "a set packed from another commit is not published" "$status:$(wc -l < "$FAKE/attempts.log" | xargs)" "2:0"

echo "==> publish_packages: a package waits for the feed to SERVE what it depends on"

fresh gated
mkdir -p "$FAKE/feed"
push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 5; status=$?
same "a feed that serves what it accepts lets every level through" "$status:$(wc -l < "$FAKE/pushed.log" | xargs)" "0:7"

fresh validating
mkdir -p "$FAKE/feed"; : > "$FAKE/unserved/Rask.Ui"   # accepted, and never served: still in validation
push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 0.3; status=$?
same "an unserved dependency stops the levels above it" \
  "$status:$(grep -cE "^Rask\.(Server|Dashboard|Postgres)\." "$FAKE/attempts.log")" "1:0"
check "and is named" grep -q "Stopped before level 2.*Rask.Ui" <<< "$out"

echo "==> publish_packages: the publish is not done until the feed serves the whole set"

verify() {
  out="$(GITHUB_STEP_SUMMARY="$work/summary.md" python3 "$publish" verify "$dir" \
    --index "file://$FAKE/feed/{id}/index.json" --poll 0.05 "$@" 2>&1)"
}
: > "$work/summary.md"
verify --wait 0.3; status=$?
same "a set with a package missing is incomplete" "$status" "1"
check "and the missing ones are listed" grep -q "Incomplete.*still does not serve .Rask.Ui., .Rask.Dashboard., .Rask.Server., .Rask.Postgres." <<< "$out"

# The feed catches up while the job is waiting.
( sleep 0.5; rm "$FAKE/unserved/Rask.Ui"; export FAKE_VERSION="$version"
  for id in Rask.Ui Rask.Server Rask.Dashboard Rask.Postgres; do "$work/push" "$dir/$id.$version.nupkg" >/dev/null; done ) &
verify --wait 60; status=$?
wait
same "it waits for the feed to catch up" "$status" "0"
check "the run page says which version" grep -q "Complete.*$version" "$work/summary.md"
check "and which commit it was packed from" grep -q "Complete.*$commit" "$work/summary.md"

echo "==> publish_packages: a version is one commit's (2026-10-26: .526 was served from two)"

other="2222222222222222222222222222222222222222"
# served <id> <commit>   the feed already serves this version of <id>, packed from <commit>
served() {
  local lower; lower="$(echo "$1" | tr '[:upper:]' '[:lower:]')"
  mkdir -p "$FAKE/feed/$lower/$version"
  echo "{\"versions\":[\"$version\"]}" > "$FAKE/feed/$lower/index.json"
  unzip -p "$dir/$1.$version.nupkg" '*.nuspec' | sed "s/commit=\"[^\"]*\"/commit=\"$2\"/" > "$FAKE/feed/$lower/$version/$lower.nuspec"
}

fresh mixed
mkdir -p "$FAKE/feed"; served Rask "$commit"; served Rask.Ui "$other"; served Rask.Data "$other"
push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 5 --commit "$commit"; status=$?
same "the version already served from another commit is refused before anything is pushed" \
  "$status:$(wc -l < "$FAKE/attempts.log" | xargs)" "3:0"
check "loudly, naming each package and the commit it is served from" \
  grep -q "MIXED VERSION.*before pushing.*Rask.Data. from .$other.*Rask.Ui. from .$other" <<< "$out"

fresh resumed
mkdir -p "$FAKE/feed"; served Rask "$commit"; served Rask.Ui "$commit"
push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 5 --commit "$commit"; status=$?
same "served from this very commit, it is a re-run finishing the version" "$status:$(sort -u "$FAKE/pushed.log" | wc -l | xargs)" "0:7"

fresh nocommit
mkdir -p "$FAKE/feed"; served Rask.Cli ""
push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 5; status=$?
same "a served nuspec that names no commit is not taken for ours" "$status:$(wc -l < "$FAKE/attempts.log" | xargs)" "3:0"

fresh duplicated
mkdir -p "$FAKE/feed"; echo "$other" > "$FAKE/foreign/Rask.Ui"   # accepted as a duplicate of another run's
push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 5; status=$?
same "a duplicate from another commit stops the publish at the next gate" \
  "$status:$(grep -cE "^Rask\.(Server|Dashboard|Postgres)\." "$FAKE/attempts.log")" "3:0"
check "and is named" grep -q "MIXED VERSION.*level 2.*Rask.Ui. from .$other" <<< "$out"

fresh twocommits
mkdir -p "$FAKE/feed"; push --gate-index "file://$FAKE/feed/{id}/index.json" --wait 5
# The 0.23.1-alpha.0.526 shape: every id listed, three of them from the earlier commit.
served Rask "$other"; served Rask.Cli "$other"; served Rask.Data "$other"
verify --wait 5; status=$?
same "a whole set from two commits is not complete" "$status" "3"
check "and says so, naming the strays" grep -q "MIXED VERSION.*verify.*Rask. from .$other.*Rask.Cli. from .$other.*Rask.Data. from .$other" <<< "$out"

echo
if [ "$failures" -gt 0 ]; then
  echo "publish-packages: $failures of $checked checks FAILED" >&2
  exit 1
fi
echo "publish-packages: all $checked checks passed"

#!/usr/bin/env bash
# Builds and launches a throwaway `rask new` app for dashboard-driver.cs to drive.
#
# The console (Rask.Dashboard) is mounted by no app in this repo, so it needs one to live in. This
# scaffolds one and — the part that matters — builds it against THIS WORKING TREE rather than nuget.org.
# A scaffold restored from nuget.org screenshots the RELEASED console, which proves nothing about the
# change you are trying to see; and it does not even restore here, because `rask new` pins the last
# published stable and the templates it writes track the tree, not that release.
#
# So: pack the solution to a folder feed under the version `rask new` pins, point the throwaway app at
# that feed, and give it its OWN package folder — the user's ~/.nuget/packages already holds a real
# package at that version and the global folder is consulted before any feed, so without the override the
# released bits silently win.
#
# Prints the base URL and the one-time first-run token, which dashboard-driver.cs needs to claim the app.
#
#   .claude/skills/run-rask/ops-app.sh            # pack, scaffold, migrate, launch on :5123
#   PORT=5124 .claude/skills/run-rask/ops-app.sh  # somewhere else
#   RASK_OPS_NO_PACK=1 …                          # reuse the feed (only if src/ is untouched since)
#
# Stop it with:  lsof -ti :5123 | xargs kill
set -euo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(cd "$here/../../.." && pwd)"
port="${PORT:-5123}"

# Keyed on the checkout, because every worktree shares one TMPDIR and two of these must not collide.
key="$(printf '%s' "$root" | cksum | cut -d' ' -f1)"
work="${TMPDIR:-/tmp}/rask-ops-$key"
feed="$work/feed"
app="$work/Shop"

if lsof -ti ":$port" >/dev/null 2>&1; then
  echo "Port $port is busy — a different app would answer the driver. Kill it or set PORT=." >&2
  exit 1
fi

echo "==> Building the CLI"
dotnet build "$root/src/Rask.Cli" -c Debug -m:1 -v:q --nologo >/dev/null

rm -rf "$app"
mkdir -p "$work"

echo "==> Scaffolding $app"
# --no-restore because the pin does not exist on nuget.org yet; we restore below against the local feed.
# It also skips the first migration, which is why db add/update run here rather than inside `rask new`.
dotnet run --project "$root/src/Rask.Cli" --no-build -- \
  new Shop --output "$app" --no-restore >/dev/null

# Whatever `rask new` pinned. Derived rather than hardcoded: NewCommand.ResolvePackageVersion walks a
# prerelease back to the release it came after, so this moves with the repo's version.
version="$(sed -n 's/.*Include="Rask\.[^"]*" Version="\([^"]*\)".*/\1/p' "$app/Shop.csproj" | head -1)"
if [ -z "$version" ]; then
  echo "Could not read the pinned Rask package version out of $app/Shop.csproj." >&2
  exit 1
fi
echo "==> Scaffold pins Rask $version"

if [ "${RASK_OPS_NO_PACK:-0}" = "1" ] && [ -d "$feed" ]; then
  echo "==> Reusing $feed (RASK_OPS_NO_PACK=1)"
else
  echo "==> Packing this tree as $version into $feed"
  # The extracted copies go too, and this is the whole ballgame. The version is FIXED (whatever `rask
  # new` pins), so a second run writes the same 0.20.0 into the feed — and NuGet, finding
  # packages/rask.dashboard/0.20.0/ already extracted, never looks at the feed again. You would edit the
  # console, re-run this, and screenshot the build from before your change, with everything green. That
  # is the exact failure this whole skill exists to make impossible.
  rm -rf "$feed" "$work/packages"
  # The generators first, in Release: the packable projects that embed one check for its DLL on disk and
  # fail the pack rather than shipping a package whose consumers get no generated code.
  for gen in Rask.Generators Rask.Api.Generators Rask.Batteries.Generators; do
    dotnet build "$root/src/$gen" -c Release -m:1 -v:q --nologo >/dev/null
  done
  dotnet pack "$root/Rask.slnx" -c Release -m:1 -v:q --nologo \
    -p:MinVerVersionOverride="$version" -o "$feed" >/dev/null
fi

# The package folder lives OUTSIDE the project: inside it, the project's own **/*.css glob sweeps up the
# packages' content files and the scoped-CSS analyzer fails the build on them (RASK015).
cat > "$app/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config>
    <add key="globalPackagesFolder" value="$work/packages" />
  </config>
  <packageSources>
    <clear />
    <add key="rask-local" value="$feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF

echo "==> Restoring and migrating"
# Kept, not discarded: `set -e` catches a failure here, but only the log says what it was.
setup_log="$work/setup.log"
: > "$setup_log"
(
  cd "$app"
  dotnet restore -v:q --nologo >>"$setup_log" 2>&1
  # The batteries store their state in the database and a hosted service that cannot find its table
  # stops the app, so this has to happen before the first run. `rask new` does it itself when it
  # restores; --no-restore skipped it along with the restore.
  dotnet run --project "$root/src/Rask.Cli" --no-build -- db add Init >>"$setup_log" 2>&1
  dotnet run --project "$root/src/Rask.Cli" --no-build -- db update >>"$setup_log" 2>&1
  dotnet build -m:1 -v:q --nologo >>"$setup_log" 2>&1
) || {
  echo "Setting the app up failed — tail of $setup_log:" >&2
  tail -40 "$setup_log" >&2
  exit 1
}

echo "==> Launching on http://localhost:$port"
(
  cd "$app"
  ASPNETCORE_ENVIRONMENT=Development nohup \
    dotnet run --no-build --urls "http://localhost:$port" > "$work/app.log" 2>&1 &
  echo $! > "$work/app.pid"
)

if ! curl -sf -o /dev/null --retry 60 --retry-delay 1 --retry-all-errors "http://localhost:$port/"; then
  echo "The app never answered on :$port. Log:" >&2
  tail -40 "$work/app.log" >&2
  exit 1
fi

# Logged at Warning by Rask.Auth's FirstRunTokenInitializer, and only while the app is unclaimed.
token="$(sed -n 's/.*one-time token: \([0-9a-f]*\).*/\1/p' "$work/app.log" | head -1)"

echo
echo "App:   http://localhost:$port   (pid $(cat "$work/app.pid"), log $work/app.log)"
if [ -n "$token" ]; then
  echo "Token: $token"
  echo
  echo "Drive the console:"
  echo "  cd $here && dotnet run dashboard-driver.cs http://localhost:$port $token"
else
  echo "Token: (none logged — this app already has an account)"
  echo
  echo "Drive the console:"
  echo "  cd $here && dotnet run dashboard-driver.cs http://localhost:$port"
fi
echo
echo "Stop it:  lsof -ti :$port | xargs kill"

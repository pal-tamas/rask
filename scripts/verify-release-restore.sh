#!/usr/bin/env bash
# Does a project scaffolded by the RELEASED CLI actually restore from nuget.org?
#
# This is the one moment the whole package set is supposed to exist, and until #1044 nothing checked.
# Rask.Cli 0.20.0 shipped and pinned every scaffolded project to 0.20.0 -- while Rask.Query 0.20.0 and
# Rask.Auth 0.20.0, both on the default battery set, had never been pushed. `rask new Shop` could not
# restore, and the release that caused it was green: a tag, a GitHub release, and a CLI on nuget.
#
# The cause was a hand-maintained `dotnet pack` list in release.yml, 24 projects long at the last tag
# while 16 packable projects had been added since. Both workflows pack the SOLUTION now, so the list
# cannot go stale -- and this checks the result rather than trusting it, because "we pack everything"
# is exactly the kind of claim that is true until it isn't.
#
# Usage: scripts/verify-release-restore.sh <version>
set -euo pipefail

version="${1:?usage: verify-release-restore.sh <version>}"
version="${version#v}"

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "==> Verifying a scaffolded project restores against nuget.org at $version"

# ---------------------------------------------------------------- indexing wait
#
# nuget.org indexes a push ASYNCHRONOUSLY, and in TWO stages that finish minutes apart:
#
#   * the flat container (`v3-flatcontainer/<id>/index.json`) lists the version almost immediately;
#   * the REGISTRATION index, which is what `dotnet restore` and `dotnet tool install` actually read,
#     lags behind it.
#
# Watching only the first is why this gate failed on its own first run: the flat container reported
# 0.21.0 while `dotnet tool install` still answered "Version 0.21.0 of package rask.cli is not found",
# and the registration index's `upper` was still 0.20.1-alpha. Both were true at the same moment.
#
# So the flat container answers "was it pushed at all", and the install itself answers "can a user get
# it yet" -- because the operation the gate needs to work is the only honest test of that.
#
# A gate that flakes for a reason nobody can act on is a gate that gets disabled within a month, so
# the waits are generous and the two failures say different things.
flat_container_has() {
  local lower
  lower="$(echo "$1" | tr '[:upper:]' '[:lower:]')"
  curl -fsS "https://api.nuget.org/v3-flatcontainer/$lower/index.json" 2>/dev/null \
    | grep -q "\"$version\""
}

echo "==> Waiting for the push to appear on nuget.org"
deadline=$(( SECONDS + 900 ))
until flat_container_has "Rask.Cli"; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "!!  Rask.Cli $version never reached nuget.org's flat container within 15 minutes." >&2
    echo "    That is the failure this gate exists for: the push did not include it." >&2
    exit 1
  fi
  sleep 20
done
echo "    Rask.Cli $version is pushed."

# ---------------------------------------------------------------- scaffold + restore
#
# nuget.org ONLY, and a private package directory. Restoring against the machine's global packages
# folder would let a package cached by an earlier build satisfy a reference that nuget.org cannot --
# which is precisely the failure being tested for, silently passing.
cat > "$work/NuGet.config" <<'XML'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
XML

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export NUGET_PACKAGES="$work/packages"

# The HTTP cache has to be private and disposable too, and this is the subtler half.
#
# NuGet caches the REGISTRATION index on disk for 30 minutes. Inside a retry loop that is fatal: the
# first attempt caches an index that predates the push, and every later attempt is served that same
# stale copy -- so the loop can spin out its whole deadline while nuget.org has long since caught up.
# It looks exactly like a package that was never published. Cleared between attempts below.
export NUGET_HTTP_CACHE_PATH="$work/http-cache"

# Rask.Cli is the PACKAGE id; `rask` is the command it installs. Naming the command here fails with
# "Package rask is not a .NET tool", which is what this gate did on its first real run.
echo "==> Installing the released rask CLI (retrying while the registration index catches up)"
deadline=$(( SECONDS + 1800 ))
until dotnet tool install --tool-path "$work/tools" Rask.Cli --version "$version" \
        --configfile "$work/NuGet.config" > "$work/install.log" 2>&1; do
  if [ "$SECONDS" -ge "$deadline" ]; then
    echo "!!  Rask.Cli $version is pushed but still not installable after 30 minutes:" >&2
    sed 's/^/      /' "$work/install.log" >&2
    exit 1
  fi
  sleep 30
  rm -rf "$work/http-cache"
done

# --no-restore: the scaffolder runs its own restore against the ambient NuGet config, and a failure
# there exits non-zero and would kill this script before its OWN restore -- the one that controls the
# sources and the package cache, and therefore the one whose result means anything.
echo "==> rask new"
mkdir -p "$work/app"
(cd "$work/app" && "$work/tools/rask" new ReleaseProbe --no-restore)

project_dir="$work/app/ReleaseProbe"
[ -d "$project_dir" ] || project_dir="$work/app"

cp "$work/NuGet.config" "$project_dir/NuGet.config"

# Retried for the same reason the install above is: the registration index lags the flat container
# per PACKAGE, not per push, so the CLI can be installable minutes before one of its dependencies is.
# On the v0.21.0 run Rask.Logging was in the flat container while restore still reported
# "Nearest version: 0.20.1-alpha.0.312".
echo "==> dotnet restore (nuget.org only, cold package cache)"
deadline=$(( SECONDS + 1800 ))
while true; do
  if (cd "$project_dir" && dotnet restore > "$work/restore.log" 2>&1); then
    echo "==> A scaffolded project restores at $version."
    exit 0
  fi

  if [ "$SECONDS" -ge "$deadline" ]; then
    break
  fi

  echo "    not yet resolvable; waiting for nuget.org to finish indexing…"
  sleep 30

  # Or the next attempt just re-reads the index that failed this one.
  rm -rf "$work/http-cache"
done

echo "!!  A project scaffolded by the released CLI cannot restore, 30 minutes after the push." >&2
echo "    This is #1044's shape: the CLI pins itself, so every package the default template" >&2
echo "    references has to exist at $version. What nuget.org could not resolve:" >&2
grep -oE "Unable to find package [A-Za-z.]+|Unable to find a stable package [A-Za-z.]+" "$work/restore.log" \
  | sort -u | sed 's/^/      /' >&2 || true
exit 1

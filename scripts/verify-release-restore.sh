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
# nuget.org indexes a push ASYNCHRONOUSLY. Restoring the instant the push returns fails for reasons
# that have nothing to do with this bug, and a gate that flakes for a reason nobody can act on is a
# gate that gets disabled within a month. So: a generous bounded wait, and a message that says which
# of the two things went wrong.
wait_for() {
  local id="$1" lower deadline
  lower="$(echo "$id" | tr '[:upper:]' '[:lower:]')"
  deadline=$(( SECONDS + 900 ))

  while [ "$SECONDS" -lt "$deadline" ]; do
    if curl -fsS "https://api.nuget.org/v3-flatcontainer/$lower/index.json" 2>/dev/null \
         | grep -q "\"$version\""; then
      echo "    $id $version is indexed."
      return 0
    fi
    sleep 20
  done

  echo "!!  $id $version never appeared on nuget.org within 15 minutes." >&2
  echo "    Either the push did not include it -- which is the failure this gate exists for -- or" >&2
  echo "    nuget.org is indexing unusually slowly. The flat-container index above is the source of" >&2
  echo "    truth; check it before assuming the latter." >&2
  return 1
}

wait_for "Rask.Cli"

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

echo "==> Installing the released rask CLI"
dotnet tool install --tool-path "$work/tools" rask --version "$version" \
  --configfile "$work/NuGet.config"

echo "==> rask new"
mkdir -p "$work/app"
(cd "$work/app" && "$work/tools/rask" new ReleaseProbe)

project_dir="$work/app/ReleaseProbe"
[ -d "$project_dir" ] || project_dir="$work/app"

cp "$work/NuGet.config" "$project_dir/NuGet.config"

echo "==> dotnet restore (nuget.org only, cold package cache)"
if (cd "$project_dir" && dotnet restore 2>&1 | tee "$work/restore.log"); then
  echo "==> A scaffolded project restores at $version."
  exit 0
fi

echo "!!  A project scaffolded by the released CLI cannot restore." >&2
echo "    This is #1044's shape: the CLI pins itself, so every package the default template" >&2
echo "    references has to exist at $version. The packages nuget.org could not find:" >&2
grep -oE "Unable to find package [A-Za-z.]+|Unable to find a stable package [A-Za-z.]+ " "$work/restore.log" \
  | sort -u | sed 's/^/      /' >&2 || true
exit 1

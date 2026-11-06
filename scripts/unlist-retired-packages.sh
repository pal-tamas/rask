#!/usr/bin/env bash
# Unlist EVERY listed version of packages Rask no longer ships (a retired package id).
#
# unlist-old-versions.sh keeps the newest of a live package; a retired one has no newest to keep, and no
# release will ever pick up its leftovers. So this one does not stop at nuget.org's quota (~250 unlist
# calls, then a 403 "Quota Exceeded" with a wait of tens of minutes): it waits the quota out and goes on,
# until nothing of the given ids is listed. Unlisting is not deletion — a pinned PackageReference still
# restores — and it is idempotent: it only ever reads what is still listed (lib/listed_versions.py).
#
# Usage:  scripts/unlist-retired-packages.sh <package-id>...
#   NUGET_API_KEY           required (Unlist scope)
#   RASK_UNLIST_DRYRUN      1 = print what would be unlisted, call nothing
#   RASK_UNLIST_QUOTA_WAIT  seconds to wait after a quota rejection (default 1800)
#   RASK_UNLIST_MAX_WAITS   quota waits before giving up (default 10)
#   RASK_LISTED_VERSIONS    the lister to run as `<cmd> <id>` (tests swap it; default lib/listed_versions.py)
set -uo pipefail

[ "$#" -gt 0 ] || { echo "usage: $0 <package-id>..." >&2; exit 2; }
for id in "$@"; do
  [[ "$id" =~ ^[A-Za-z0-9][A-Za-z0-9._-]*$ ]] || { echo "unlist-retired: '$id' is not a package id" >&2; exit 2; }
done

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
dryrun="${RASK_UNLIST_DRYRUN:-0}"
wait_s="${RASK_UNLIST_QUOTA_WAIT:-1800}"
max_waits="${RASK_UNLIST_MAX_WAITS:-10}"
lister="${RASK_LISTED_VERSIONS:-python3 $here/lib/listed_versions.py}"
source_url="https://api.nuget.org/v3/index.json"

if [ "$dryrun" != "1" ] && [ -z "${NUGET_API_KEY:-}" ]; then
  echo "unlist-retired: NUGET_API_KEY is not set" >&2
  exit 1
fi

waits=0
total=0
for id in "$@"; do
  while :; do
    # Re-read after every quota wait: what is listed is the only work left, so a rerun never repeats one.
    versions="$($lister "$id")" || { echo "unlist-retired: could not read $id's listed versions" >&2; exit 1; }
    [ -z "$versions" ] && { echo "  $id: nothing listed."; break; }
    echo "  $id: $(printf '%s\n' "$versions" | grep -c .) listed"

    quota=0
    while IFS= read -r v; do
      [ -z "$v" ] && continue
      if [ "$dryrun" = "1" ]; then
        echo "    would unlist $id $v"; total=$((total + 1)); continue
      fi
      out="$(dotnet nuget delete "$id" "$v" --source "$source_url" --api-key "$NUGET_API_KEY" --non-interactive 2>&1)"
      if grep -q "Quota Exceeded" <<<"$out"; then quota=1; break; fi
      if grep -q "^error" <<<"$out"; then
        echo "unlist-retired: $id $v — $(grep '^error' <<<"$out" | head -1)" >&2
        exit 1
      fi
      total=$((total + 1))
    done <<< "$versions"

    # A full pass with no rejection is the end of this id. It is NOT re-read: nuget.org's registration index
    # lags an unlist by minutes, so a re-read would list the same versions again and spend quota on no-ops.
    [ "$dryrun" = "1" ] || [ "$quota" -eq 0 ] && break
    waits=$((waits + 1))
    if [ "$waits" -gt "$max_waits" ]; then
      echo "unlist-retired: still quota-limited after $max_waits waits; run again to finish" >&2
      exit 1
    fi
    echo "    quota reached ($total unlisted so far); waiting ${wait_s}s"
    sleep "$wait_s"
  done
done

echo "unlist-retired: $total unlisted"

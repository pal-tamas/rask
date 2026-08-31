#!/usr/bin/env bash
# Unlist every version of the just-released packages that the release supersedes.
#
# Policy: a released version is the only one that stays listed -- previous stables and every nightly
# prerelease are unlisted. Unlisting is NOT deletion: nuget.org has no delete for owners, and an
# unlisted version still restores by exact reference, so a pinned PackageReference keeps building. See
# docs/development-workflow.md.
#
# Two properties matter more than completeness, because this runs AFTER the packages are pushed:
#
#   1. It must never fail the release. The packages are already on nuget.org by the time this runs; a
#      tidy-up that reds the job would leave a released tag looking broken. Every path exits 0.
#   2. It must not stall the job for an hour. nuget.org rate-limits unlisting hard -- roughly 250 calls
#      and then a 403 with a retry-after measured in tens of minutes. So this spends one budget's worth
#      of calls and stops on the first quota rejection; whatever is left is simply picked up by the next
#      release, which supersedes it anyway.
#
# Usage:  scripts/unlist-old-versions.sh <version> <artifacts-dir>
#   NUGET_API_KEY      required (needs the Unlist scope; a push-only key returns 403 Forbidden)
#   RASK_UNLIST_BUDGET max unlist calls this run (default 240, below the observed quota)
#   RASK_UNLIST_DRYRUN 1 = print what would be unlisted and call nothing
set -uo pipefail

version="${1:-}"
artifacts="${2:-./artifacts}"
budget="${RASK_UNLIST_BUDGET:-240}"
dryrun="${RASK_UNLIST_DRYRUN:-0}"
source_url="https://api.nuget.org/v3/index.json"
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ -z "$version" ]; then
  echo "==> unlist: no version given; nothing to do." >&2
  exit 0
fi
version="${version#v}"

if [ "$dryrun" != "1" ] && [ -z "${NUGET_API_KEY:-}" ]; then
  echo "==> unlist: NUGET_API_KEY unset; skipping (the release itself is unaffected)." >&2
  exit 0
fi

if [ ! -d "$artifacts" ]; then
  echo "==> unlist: no artifacts directory at $artifacts; skipping." >&2
  exit 0
fi

# The package ids are exactly what was just packed. Deriving them from the artifacts rather than
# hardcoding a list means a package added to release.yml is covered without touching this script --
# the failure mode that left Rask.Templates published for four releases after it was discontinued.
ids=()
while IFS= read -r f; do
  b="$(basename "$f")"
  ids+=("${b%".$version.nupkg"}")
done < <(find "$artifacts" -maxdepth 1 -name "*.$version.nupkg" ! -name "*.snupkg" | sort)

if [ "${#ids[@]}" -eq 0 ]; then
  echo "==> unlist: no *.$version.nupkg in $artifacts; skipping." >&2
  exit 0
fi

echo "==> unlist: superseding versions older than $version across ${#ids[@]} packages (budget $budget)"

spent=0
unlisted=0
skipped=0
quota_hit=0

for id in "${ids[@]}"; do
  [ "$quota_hit" -eq 1 ] && break

  # Candidates come from the versions still LISTED, not from the flat-container index. Flat-container
  # reports every version ever pushed, unlisted ones included, so using it would spend the whole quota
  # budget re-unlisting what is already done and never reach the rest. See lib/listed_versions.py.
  olds="$(python3 "$here/lib/listed_versions.py" "$id" \
    | python3 "$here/lib/unlist_select.py" "$version")"

  [ -z "$olds" ] && { echo "  $id: nothing older."; continue; }

  count="$(printf '%s\n' "$olds" | grep -c . || true)"
  echo "  $id: $count older version(s)"

  while IFS= read -r v; do
    [ -z "$v" ] && continue
    if [ "$spent" -ge "$budget" ]; then
      skipped=$((skipped + 1))
      continue
    fi
    if [ "$dryrun" = "1" ]; then
      echo "    would unlist $id $v"
      spent=$((spent + 1)); unlisted=$((unlisted + 1))
      continue
    fi
    out="$(dotnet nuget delete "$id" "$v" --source "$source_url" \
             --api-key "$NUGET_API_KEY" --non-interactive 2>&1)"
    spent=$((spent + 1))
    if grep -q "Quota Exceeded" <<<"$out"; then
      echo "    quota reached after $unlisted unlisted; the next release picks up the rest."
      quota_hit=1
      break
    fi
    if grep -q "^error" <<<"$out"; then
      echo "    warn: $id $v -- $(grep '^error' <<<"$out" | head -1)"
      continue
    fi
    unlisted=$((unlisted + 1))
  done <<< "$olds"
done

echo "==> unlist: $unlisted unlisted, $skipped left for the next release (budget $budget, spent $spent)"
exit 0

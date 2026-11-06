#!/usr/bin/env bash
# gate-inputs: ^scripts/unlist-retired-packages\.sh$|^\.github/workflows/unlist-retired\.yml$
# scripts/unlist-retired-packages.sh against a stub nuget.org: a lister that answers from a state file and a
# `dotnet` that unlists into it and rejects one call with nuget's quota message. The script must wait the quota
# out, go on, and finish with nothing listed — and refuse anything that is not a package id.
set -uo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
script="$root/scripts/unlist-retired-packages.sh"
tmp="$(mktemp -d)"; trap 'rm -rf "$tmp"' EXIT
failures=0
check() { if [ "$2" = "$3" ]; then echo "  ok   $1"; else echo "  FAIL $1 (expected '$2', got '$3')"; failures=$((failures + 1)); fi; }

mkdir -p "$tmp/bin"
printf '%s\n' "Old.Pkg 1.0.0" "Old.Pkg 1.1.0" "Old.Pkg 2.0.0" "Other.Pkg 0.1.0" > "$tmp/listed"
cat > "$tmp/lister" <<EOF
#!/usr/bin/env bash
awk -v id="\$1" '\$1 == id { print \$2 }' "$tmp/listed"
EOF
# The second unlist call is refused once with the quota message; every other call unlists.
cat > "$tmp/bin/dotnet" <<EOF
#!/usr/bin/env bash
n=\$(( \$(cat "$tmp/calls" 2>/dev/null || echo 0) + 1 )); echo "\$n" > "$tmp/calls"
if [ "\$n" -eq 2 ]; then echo "Response status code does not indicate success: 403 (Quota Exceeded)."; exit 1; fi
grep -v "^\$3 \$4\$" "$tmp/listed" > "$tmp/listed.next"; mv "$tmp/listed.next" "$tmp/listed"
echo "Successfully deleted"
EOF
chmod +x "$tmp/lister" "$tmp/bin/dotnet"

echo "==> unlist-retired"
out="$(PATH="$tmp/bin:$PATH" NUGET_API_KEY=k RASK_UNLIST_QUOTA_WAIT=0 RASK_LISTED_VERSIONS="$tmp/lister" \
  bash "$script" Old.Pkg Other.Pkg 2>&1)"; code=$?
check "it finishes"                         0 "$code"
check "nothing is left listed"              0 "$(grep -c . "$tmp/listed")"
check "it waited the quota out once"        1 "$(grep -c 'quota reached' <<<"$out")"
check "it says how many it unlisted"        1 "$(grep -c 'unlist-retired: 4 unlisted' <<<"$out")"

bash "$script" 'Old.Pkg;rm' >/dev/null 2>&1; check "a non-id is refused"  2 "$?"
bash "$script" >/dev/null 2>&1;              check "no id is a usage error" 2 "$?"
NUGET_API_KEY='' bash "$script" Old.Pkg >/dev/null 2>&1; check "no key, no calls" 1 "$?"

[ "$failures" -eq 0 ] || { echo "unlist-retired: $failures check(s) failed" >&2; exit 1; }
echo "unlist-retired.test.sh: all checks passed."

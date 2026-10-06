#!/usr/bin/env bash
# scripts/public-api/record.py, against a build log written here: a new member is recorded beside its
# neighbours, a vanished one is removed, per target framework, and everything else is left as it was.
#
# The upstream workflow lands a regenerated surface with nobody watching, so the one thing between an
# MDN release and a wrong baseline on main is this parse.
#
# Usage:  scripts/tests/public-api-record.test.sh   (run by scripts/run-unit-local.sh)
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
tmp="$(mktemp -d -t rask-public-api-record.XXXXXX)"
trap 'rm -rf "$tmp"' EXIT

project="$tmp/src/Rask.Thing/Rask.Thing.csproj"
for tfm in net10.0 net10.0-browser; do
  mkdir -p "$tmp/src/Rask.Thing/PublicAPI/$tfm"
  printf '#nullable enable\nRask.Alpha\nRask.Gone.get -> string!\nRask.Zeta\n' \
    > "$tmp/src/Rask.Thing/PublicAPI/$tfm/PublicAPI.Unshipped.txt"
done
touch "$project"

cat > "$tmp/build.log" <<EOF
$tmp/src/Rask.Thing/Thing.cs(4,19): warning RS0016: Symbol 'Rask.Thing.Say(string! text = "it's") -> void' is not part of the declared public API (https://example.invalid/RS0016) [$project::TargetFramework=net10.0]
$tmp/src/Rask.Thing/Thing.cs(4,19): warning RS0016: Symbol 'Rask.Thing.Say(string! text = "it's") -> void' is not part of the declared public API (https://example.invalid/RS0016) [$project::TargetFramework=net10.0]
$tmp/src/Rask.Thing/PublicAPI/net10.0/PublicAPI.Unshipped.txt(3,1): warning RS0017: Symbol 'Rask.Gone.get -> string!' is part of the declared API, but is either not public or could not be found (https://example.invalid/RS0017) [$project::TargetFramework=net10.0]
$tmp/src/Rask.Thing/Thing.cs(9,5): warning CS0168: The variable 'x' is declared but never used [$project::TargetFramework=net10.0]
EOF

python3 -I "$root/scripts/public-api/record.py" --log "$tmp/build.log" >/dev/null

failures=0
check() {
  if [ "$2" = "$3" ]; then printf '  ok   %s\n' "$1"; else printf '  FAIL %s\n--- expected\n%s\n--- actual\n%s\n' "$1" "$2" "$3" >&2; failures=$((failures + 1)); fi
}

echo "==> public-api record"
check "the reported framework gains the new member in place and loses the stale one" \
  "$(printf '#nullable enable\nRask.Alpha\nRask.Thing.Say(string! text = "it'"'"'s") -> void\nRask.Zeta')" \
  "$(cat "$tmp/src/Rask.Thing/PublicAPI/net10.0/PublicAPI.Unshipped.txt")"
check "a framework the build said nothing about is untouched" \
  "$(printf '#nullable enable\nRask.Alpha\nRask.Gone.get -> string!\nRask.Zeta')" \
  "$(cat "$tmp/src/Rask.Thing/PublicAPI/net10.0-browser/PublicAPI.Unshipped.txt")"

[ "$failures" -eq 0 ] || { echo "public-api record: $failures failed" >&2; exit 1; }
echo "public-api record: ok"

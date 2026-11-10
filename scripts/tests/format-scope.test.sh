#!/usr/bin/env bash
# gate-inputs: scripts/lib/analyzer-shards\.txt$|Rask\.slnx$
# Table test for scripts/lib/format-scope.sh — which files CI's formatter checks for a change, and
# which analyzer shard checks each.
#
# The direction that matters: a row that answers ALL where the changed files would do costs a ten-minute
# formatter run; a row that answers the changed files where ALL was due lets a rule change land with the
# tree out of step with it until somebody formats a file it moved. Keep the ALL rows when in doubt. And
# a project no shard's share holds is a project whose files no formatter checks on a rule change.
#
# Usage:  scripts/tests/format-scope.test.sh   (run by scripts/run-unit-local.sh)
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
# shellcheck source=../lib/format-scope.sh
. "$root/scripts/lib/format-scope.sh"
failures=0
checked=0

# scope <changed path...> -> what the formatter is given, on one line
scope() {
  printf '%s\n' "$@" | rask_format_scope | tr '\t\n' ' ' | sed 's/ $//'
}

# expect <name> <answer> <changed path...>
expect() {
  name="$1"; want="$2"; shift 2
  checked=$((checked + 1))
  got="$(scope "$@")"
  if [ "$got" = "$want" ]; then
    printf '  ok   %-52s -> %s\n' "$name" "${got:-(nothing)}"
  else
    printf '  FAIL %-52s -> %s (expected %s)\n' "$name" "${got:-(nothing)}" "${want:-(nothing)}" >&2
    failures=$((failures + 1))
  fi
}

echo "==> format-scope.sh"
expect "a changed .cs file is checked"                   "src/Rask.Mail/Mail.cs" src/Rask.Mail/Mail.cs
expect "...each of them, and nothing else"               "src/A.cs tests/B.cs"   src/A.cs docs/x.md tests/B.cs src/site.css
expect "no .cs file: nothing to check"                   ""                      docs/x.md src/Rask.Site/wwwroot/app.css
expect "the root .editorconfig checks everything"        "ALL .editorconfig"     src/A.cs .editorconfig
expect "a nested .editorconfig too"                      "ALL tests/.editorconfig" tests/.editorconfig
expect "a .globalconfig"                                 "ALL src/rask.globalconfig" src/rask.globalconfig
expect "the build props wire the analyzers"              "ALL Directory.Build.props" Directory.Build.props
expect "...at any depth"                                 "ALL src/Directory.Build.targets" src/Directory.Build.targets
expect "the package pins hold the analyzer versions"     "ALL Directory.Packages.props" src/A.cs Directory.Packages.props
expect "the SDK ships the formatter"                     "ALL global.json"       global.json
expect "a project file is not a rule change"             "src/A.cs"              src/A/A.csproj src/A.cs
expect "a props file of one project is not either"       ""                      src/Rask.Pdf/build/Rask.Pdf.props

# --- which analyzer shard formats a file ----------------------------------------------------------
# check <name> <want> <got>
check() {
  checked=$((checked + 1))
  if [ "$2" = "$3" ]; then
    printf '  ok   %-52s -> %s\n' "$1" "${3:-(nothing)}"
  else
    printf '  FAIL %-52s -> %s (expected %s)\n' "$1" "${3:-(nothing)}" "${2:-(nothing)}" >&2
    failures=$((failures + 1))
  fi
}

echo "==> the shards' shares (rask_format_owner, a fixture deal)"
fixture="$(mktemp)"
deal="$(mktemp)"
trap 'rm -f "$fixture" "$deal"' EXIT
printf '1\tsrc/Rask.A/\tsrc/Rask.A/Rask.A.csproj\n2\tsrc/Rask.AB/\tsrc/Rask.AB/Rask.AB.csproj\n3\ttests/Rask.A.Tests/\ttests/Rask.A.Tests/Rask.A.Tests.csproj\n' >"$fixture"
owner() { printf '%s\n' "$1" | rask_format_owner "$fixture" | cut -f1,3 | tr '\t' ' '; }
check "a file is its project's shard"                    "1 src/Rask.A/Rask.A.csproj"   "$(owner src/Rask.A/Deep/X.cs)"
check "...by directory, never by name prefix"            "2 src/Rask.AB/Rask.AB.csproj" "$(owner src/Rask.AB/X.cs)"
check "a test project is its own shard's"                "3 tests/Rask.A.Tests/Rask.A.Tests.csproj" "$(owner tests/Rask.A.Tests/XTests.cs)"
check "a file in no project's directory is nobody's"     "0 "                           "$(owner tests/Shared/X.cs)"
check "a shard formats its own changed files"            "src/Rask.AB/X.cs"             "$(printf 'src/Rask.A/X.cs\nsrc/Rask.AB/X.cs\n' | rask_format_shard_files "$fixture" 2)"
check "...and shard 1 the ones nobody's"                 "src/Rask.A/X.cs tests/Shared/X.cs" \
  "$(printf 'src/Rask.A/X.cs\nsrc/Rask.AB/X.cs\ntests/Shared/X.cs\n' | rask_format_shard_files "$fixture" 1 | tr '\n' ' ' | sed 's/ $//')"

# The real deal: on a rule change every shard formats (its share is the projects it builds), and
# between them the shards hold every project in Rask.slnx exactly once, in directories none of which
# holds another (a file is then checked by one shard, and the shard that owns its project).
echo "==> the shards' shares on a rule change (scripts/lib/analyzer-shards.txt, Rask.slnx)"
shard_list="$root/scripts/lib/analyzer-shards.txt"
rask_format_deal "$shard_list" "$root/Rask.slnx" >"$deal"
shard_n="$(awk '!/^#/ && NF >= 2 && $2 > n { n = $2 } END { print n + 0 }' "$shard_list")"
[ "$shard_n" -ge 1 ] || shard_n=1
cs_files="$(git -C "$root" ls-files '*.cs')"
shared_dirs=""
idle=""
loose=""
for shard in $(seq 1 "$shard_n"); do
  share="$(printf '%s\n' "$cs_files" | rask_format_share "$deal" "$shard")"
  # A here-string, not `printf | grep -q`: grep -q exits at its first match, printf then dies of SIGPIPE
  # on a share too large for the pipe, and under pipefail the match reads as a miss — a row that went
  # red only on a loaded machine.
  grep -q '^dir'$'\t' <<<"$share" || idle="$idle $shard"
  grep -q '^file'$'\t' <<<"$share" && loose="$loose $shard"
  shared_dirs="$shared_dirs$(printf '%s\n' "$share" | awk -F'\t' '$1 == "dir" { print $2 }')
"
done
check "every shard formats on a rule change"             "" "$(echo $idle)"
check "...shard 1 the files in no project's directory"   "1" "$(echo $loose)"
solution_dirs="$(grep -oE 'Path="[^"]*\.csproj"' "$root/Rask.slnx" | sed -E 's/^Path="//; s/[^/]*\.csproj"$//' | LC_ALL=C sort)"
check "between them, every project in Rask.slnx"         "$(printf '%s\n' "$solution_dirs" | grep -c .)" \
  "$(printf '%s' "$shared_dirs" | grep . | LC_ALL=C sort | comm -12 - <(printf '%s\n' "$solution_dirs") | grep -c .)"
check "...each in one shard only"                        "" "$(printf '%s' "$shared_dirs" | grep . | LC_ALL=C sort | uniq -d | tr '\n' ' ')"
check "...and no project directory inside another"       "" \
  "$(printf '%s\n' "$solution_dirs" | awk '{ for (i = 1; i < NR; i++) if (index($0, d[i]) == 1 || index(d[i], $0) == 1) print d[i] " " $0; d[NR] = $0 }')"

echo
if [ "$failures" -gt 0 ]; then
  echo "format-scope: $failures of $checked checks FAILED." >&2
  exit 1
fi
echo "format-scope: $checked checks passed."

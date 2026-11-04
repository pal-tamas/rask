#!/usr/bin/env bash
# What CI's formatter checks for a change. Sourced, not run: scripts/run-unit-local.sh's `build` part asks
# it, scripts/tests/format-scope.test.sh holds it to its rows.
#
# rask_format_scope reads the change's paths on stdin (one per line, repo-relative) and prints either
#
#     ALL<TAB><the path that decided it>
#
# when the change moves what "formatted" MEANS — every file is then checked again — or the changed .cs
# files, one per line (nothing at all when none changed). `dotnet format` decides per document, so a
# file it is not shown is a file it had nothing new to say about, unless one of these changed:
#
#   .editorconfig / .globalconfig     the style and severity rules, at any depth
#   Directory.*.props / .targets      the analyzers wired in, their settings, the package pins
#                                     (Directory.Packages.props holds the analyzer versions)
#   global.json                       the SDK, which ships `dotnet format` and the .NET analyzers
#
# Until 2026-11-03 CI checked the whole tree on every whole run (format src, format tests: 603 s of
# formatter each on top of a build). Restore at 1.0: the whole tree in the daily full run
# (docs/development-workflow.md, "Loosened until 1.0").
rask_format_scope() {
  local path cs=""
  while IFS= read -r path; do
    case "$path" in
      "") continue ;;
      .editorconfig|*/.editorconfig|.globalconfig|*/.globalconfig|*.globalconfig \
        |Directory.*.props|*/Directory.*.props|Directory.*.targets|*/Directory.*.targets|global.json)
        printf 'ALL\t%s\n' "$path"
        return 0
        ;;
      *.cs) cs="$cs$path
" ;;
    esac
  done
  printf '%s' "$cs"
}

# Which analyzer shard formats a file (.github/workflows/gates.yml, "analyzers K/N"): the shard of the
# project whose directory holds it, so each shard formats the projects it already builds and analyses,
# every file is checked by exactly one shard, and none checks the whole tree. Until 2026-11-04 shard 1
# formatted everything on a rule change: 1140 s of `dotnet format` on top of a whole-solution build,
# 29 minutes, while the other shards took 5 to 8. A file in no project's directory (the source-linked
# src/*.Shared/ and tests/Shared*/ files) goes to shard 1.
#
# rask_format_deal <analyzer-shards.txt> <Rask.slnx> prints every solution project as
# "<shard><TAB><its directory>/<TAB><the .csproj>", one per line: the shard the list deals it, 1 when
# it is not listed (the rule Directory.Build.targets and scripts/run-unit-local.sh read the list by).
rask_format_deal() {
  awk '
    FNR == NR { if ($0 !~ /^#/ && NF >= 2) listed[$1] = $2; next }
    match($0, /Path="[^"]*\.csproj"/) {
      path = substr($0, RSTART + 6, RLENGTH - 7); name = path; dir = path
      sub(/.*\//, "", name); sub(/\.csproj$/, "", name); sub(/[^\/]*$/, "", dir)
      print (name in listed ? listed[name] : 1) "\t" dir "\t" path
    }' "$1" "$2"
}

# rask_format_owner <the deal, in a file> reads repo-relative paths on stdin and prints
# "<shard><TAB><path><TAB><project>" for each: the shard and .csproj of the solution project whose
# directory is nearest above it, or 0 and no project when none holds it (shard 1 formats those).
rask_format_owner() {
  awk -F'\t' '
    FNR == NR { shard[$2] = $1; project[$2] = $3; next }
    $0 != "" {
      owner = 0; owned_by = ""; dir = $0
      while (sub(/\/[^\/]*$/, "", dir)) if ((dir "/") in shard) { owner = shard[dir "/"]; owned_by = project[dir "/"]; break }
      print owner "\t" $0 "\t" owned_by
    }' "$1" -
}

# rask_format_shard_files <the deal> <shard> reads changed paths on stdin and prints the ones that shard
# formats: those in its projects' directories, and in shard 1 those in none.
rask_format_shard_files() {
  rask_format_owner "$1" | awk -F'\t' -v shard="$2" '$1 == shard || (shard == 1 && $1 == 0) { print $2 }'
}

# rask_format_share <the deal> <shard> reads the repository's .cs files on stdin and prints what that
# shard formats when the rules change: "dir<TAB><directory>/" for each of its projects, and in shard 1
# "file<TAB><path>" for each file no project's directory holds. Between them the shards' shares hold
# every project once (scripts/tests/format-scope.test.sh).
rask_format_share() {
  awk -F'\t' -v shard="$2" '$1 == shard { print "dir\t" $2 }' "$1"
  rask_format_owner "$1" | awk -F'\t' -v shard="$2" 'shard == 1 && $1 == 0 { print "file\t" $2 }'
}

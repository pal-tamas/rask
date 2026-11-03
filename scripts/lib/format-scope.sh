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

#!/usr/bin/env bash
# What `dotnet nuget push` said about ONE package: accepted, refused, or never mentioned.
#
# The release gate needs this because nuget.org's flat container cannot tell "the push never happened"
# from "the push was accepted and is still in validation". A plain library is listed within minutes; a
# package that carries an executable -- Rask.Cli ships an apphost -- goes through extended validation and
# took 2h17m on v0.23.0 (#1125). Only the push log knows which of the two a missing version is.
#
# Usage (sourced):  push_verdict <push-log> <package-id> <version>   -> prints accepted | refused | absent
#
#   accepted  nuget.org answered Created, or Conflict / "already exists" under --skip-duplicate (a re-run)
#   refused   the package was pushed and nuget.org answered anything else
#   absent    the log never mentions pushing it
push_verdict() {
  local log="$1" id="$2" version="$3"
  awk -v want="$(printf '%s.%s.nupkg' "$id" "$version" | tr '[:upper:]' '[:lower:]')" '
    # A section starts at "Pushing <file> to ..." and runs until the next one.
    tolower($0) ~ /^pushing / {
      if (inside) { exit }
      n = split($2, parts, "/"); file = tolower(parts[n])
      inside = (file == want); if (inside) seen = 1
      next
    }
    inside && ($1 == "Created" || $1 == "Conflict" || tolower($0) ~ /already exists/) { ok = 1 }
    END { print (ok ? "accepted" : (seen ? "refused" : "absent")) }
  ' "$log"
}

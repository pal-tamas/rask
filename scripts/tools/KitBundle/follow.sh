#!/usr/bin/env bash
# Follows the packages a Rask.Ui engine bundles and rebuilds it with them — KitBundle's `follow` (see Program.cs):
#
#   scripts/tools/KitBundle/follow.sh editor|phone            # move the pins, re-lock, rebuild
#   scripts/tools/KitBundle/follow.sh editor|phone --check    # exit 1 when a pin is not what it follows
#
# scripts/upstream/follow.sh runs it every day (scripts/upstream/followers.json: tiptap, intl-tel-input).
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
exec dotnet run --project "$root/scripts/tools/KitBundle" -- follow "$@"

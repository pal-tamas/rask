#!/usr/bin/env bash
# Retakes the screenshots in docs/devtools.md from the real devtools, by hand.
#
#   scripts/capture-devtools-screenshots.sh
#
# rask.sh is a Release build on a public origin, where the devtools never switch on, so the guide shows them
# through pictures instead of a live demo. The pictures are taken here rather than drawn: this builds
# tests/Rask.DevTools.Showcase in Debug, runs it in Development on a loopback port, drives the pill and the
# panel in Chromium (scripts/capture-devtools-screenshots.mjs) and writes WebP files to
# src/Rask.Site/wwwroot/img/devtools/. Re-run it whenever the panel changes, and commit what it wrote.
#
# Needs: the E2E project built once (for the Playwright driver it ships — see scripts/playwright.sh),
# Chromium installed through that driver, and cwebp (brew install webp / apt install webp).
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

# shellcheck source=lib/playwright.sh
. "$root/scripts/lib/playwright.sh"

pw_node=""
pw_package=""
for cfg in Release Debug; do
  # Rooted, because node resolves a bare relative require against the script's folder, not the working one.
  if driver="$(rask_playwright_driver "$root/tests/Rask.Site.E2E.Tests/bin/$cfg")"; then
    pw_node="$(printf '%s\n' "$driver" | sed -n 1p)"
    pw_package="$(dirname "$(printf '%s\n' "$driver" | sed -n 2p)")"
    break
  fi
done
if [ -z "$pw_node" ]; then
  echo "capture: no bundled Playwright driver. Build it once: dotnet build tests/Rask.Site.E2E.Tests -c Release" >&2
  exit 1
fi

if ! command -v cwebp >/dev/null 2>&1; then
  echo "capture: cwebp is missing (brew install webp, or apt install webp)." >&2
  exit 1
fi

out="$root/src/Rask.Site/wwwroot/img/devtools"
# RASK_CAPTURE_KEEP=<dir> keeps the PNGs and the app log there, to look at a capture that went wrong.
work="${RASK_CAPTURE_KEEP:-$(mktemp -d)}"
mkdir -p "$work"
app_pid=""
cleanup() {
  # Waited for, so the script never returns with the app still shutting down on its port.
  if [ -n "$app_pid" ]; then kill "$app_pid" 2>/dev/null || true; wait "$app_pid" 2>/dev/null || true; fi
  if [ -z "${RASK_CAPTURE_KEEP:-}" ]; then rm -rf "$work"; fi
}
trap cleanup EXIT

project=tests/Rask.DevTools.Showcase/Rask.DevTools.Showcase.csproj
dotnet build "$project" -c Debug -nologo -v quiet

# Port 0: the app picks a free loopback port and says which on its first lines.
ASPNETCORE_ENVIRONMENT=Development dotnet run --no-build -c Debug --project "$project" \
  --urls http://127.0.0.1:0 > "$work/app.log" 2>&1 &
app_pid=$!

url=""
for _ in $(seq 1 120); do
  url="$(sed -n 's/.*Now listening on: \(http:[^ ]*\).*/\1/p' "$work/app.log" | head -1)"
  [ -n "$url" ] && break
  if ! kill -0 "$app_pid" 2>/dev/null; then
    echo "capture: the showcase exited before listening:" >&2
    cat "$work/app.log" >&2
    exit 1
  fi
  sleep 0.5
done
if [ -z "$url" ]; then
  echo "capture: the showcase never said where it listens:" >&2
  cat "$work/app.log" >&2
  exit 1
fi

"$pw_node" "$root/scripts/capture-devtools-screenshots.mjs" "$url" "$work" "$pw_package"

mkdir -p "$out"
for png in "$work"/*.png; do
  name="$(basename "$png" .png)"
  cwebp -quiet -q 82 "$png" -o "$out/$name.webp"
  echo "wrote src/Rask.Site/wwwroot/img/devtools/$name.webp ($(wc -c < "$out/$name.webp" | tr -d ' ') bytes)"
done

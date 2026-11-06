#!/usr/bin/env bash
# Holds the whole of Rask.Ui to Flux UI's live documentation: every page's look on a desktop and on a phone, and
# every walk the behaviour checks take (open, keys, pointer). `flux sync` locks Flux's LOOK on one platform to see
# Flux move; this compares the two side by side on the same machine, so it needs no lock and runs anywhere — and,
# with main matching Flux, a red line here is Flux having moved (or Rask having drifted) since.
#
# Usage:  scripts/flux/parity-all.sh            # every check; the Rask pages are written first
# Needs:  dotnet, and Chromium installed by the harness (dotnet run --project scripts/flux/Flux -- install chromium).
# Exit:   0 everything matches · 2 something differs (the log names it) · 1 it could not run
#
# Concurrent on every core: each check is its own browser, writing its own artifacts/flux-parity/<name>. The
# chart's three run one after another — the pinned load they share is written by the first.
set -uo pipefail
cd "$(dirname "$0")/../.." || exit 1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

out="${RASK_FLUX_PARITY_LOG:-artifacts/flux-parity/all}"
rm -rf "$out" && mkdir -p "$out" || exit 1

# The chart's data is made up per request, so one load is pinned first and the Rask page is drawn from it. The
# pin also compares the new load with the Rask page still drawn from the old one, so its own verdict is no news:
# what counts is that it wrote the load.
pinned=artifacts/flux-parity/flux/chart/document.html
rm -f "$pinned"
# The harness is built ONCE and every check runs the built app: a `dotnet run` per check would race on one obj/.
dotnet build scripts/flux/Flux -c Release -m:2 -nodeReuse:false > "$out/flux-build.log" 2>&1 \
  || { echo "parity-all: could not build the harness"; tail -20 "$out/flux-build.log"; exit 1; }
export FLUX="$PWD/scripts/flux/Flux/bin/Release/net10.0/flux.dll"
node scripts/flux/parity-chart.mjs --measure > "$out/chart-measure.log" 2>&1
[ -s "$pinned" ] || { echo "parity-all: could not pin the chart's load"; tail -20 "$out/chart-measure.log"; exit 1; }
dotnet test tests/Rask.Ui.Tests -m:2 -nodeReuse:false -p:RaskMdnRefresh=false --filter FluxParityPages > "$out/pages.log" 2>&1 \
  || { echo "parity-all: could not write the Rask pages"; tail -20 "$out/pages.log"; exit 1; }

checks() {
  # Every docs page, desktop and phone (layouts are measured at both widths by parity.mjs itself).
  python3 -c '
import json
for p in json.load(open("tests/Rask.Ui.Tests/Flux/flux.snapshot.json"))["pages"]:
    page = "layouts/" + p["slug"] if p["kind"] == "layouts" else p["slug"]
    if page == "chart":
        continue
    # The composer is fitted to its text by the runtime as it arrives, so its page is given the hooks.
    given = " --prepare composer-grown" if page == "composer" else ""
    print(f"parity {page} --refresh{given}")
    if p["kind"] != "layouts":
        print(f"parity {page} --refresh --view 390x844{given}")'
  cat <<'EOF'
parity-autocomplete.mjs --refresh
parity-autocomplete.mjs --refresh --view 390x844
parity-carousel.mjs
parity-carousel.mjs --view 390x844
parity-color-picker.mjs --refresh
parity-command.mjs --refresh
parity-command.mjs --refresh --view 390x844
parity-date.mjs date-picker --refresh
parity-date.mjs time-picker --refresh
parity-date.mjs datetime-picker --refresh
parity-editor.mjs
parity-editor.mjs --view 390x844
parity empty-state --refresh --prepare empty-state
parity empty-state --refresh --prepare empty-state --view 390x844
parity-focus.mjs --webkit
parity-menu.mjs dropdown --refresh
parity-menu.mjs context --refresh
parity-menu.mjs dropdown --refresh --view 390x844
parity-menu.mjs context --refresh --view 390x844
parity-modal.mjs --refresh
parity-phone.mjs --refresh
parity-phone.mjs --refresh --view 390x844
parity-pillbox.mjs --refresh
parity-pillbox.mjs --refresh --view 390x844
parity-popover.mjs --refresh
parity-popover.mjs --refresh --view 390x844
parity-select.mjs --refresh
parity-select.mjs --refresh --view 390x844
parity-toast.mjs
parity-toast.mjs --view 390x844
parity-tooltip.mjs --refresh
parity-tooltip.mjs --refresh --view 390x844
chart
EOF
}

run() {
  local name
  name=$(echo "$*" | tr ' /' '__')
  if [ "$1" = chart ]; then
    { dotnet "$FLUX" parity chart && dotnet "$FLUX" parity chart --view 390x844 \
        && node scripts/flux/parity-chart.mjs && node scripts/flux/parity-chart.mjs --view 390x844 \
        && node scripts/flux/parity-chart.mjs --pointer && node scripts/flux/parity-chart.mjs --pointer --view 390x844; } > "$OUT/$name.log" 2>&1
  elif [ "${1%.mjs}" != "$1" ]; then
    node "scripts/flux/$1" "${@:2}" > "$OUT/$name.log" 2>&1
  else
    # A check of the harness: its command and arguments (README.md in scripts/flux/Flux).
    dotnet "$FLUX" "$@" > "$OUT/$name.log" 2>&1
  fi
  echo "$? $*"
}
export -f run
export OUT="$out"

checks | xargs -P "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" -L1 bash -c 'run "$@"' _ | sort -k2 > "$out/summary.txt"

total=$(wc -l < "$out/summary.txt" | tr -d ' ')
failed=$(grep -cv '^0 ' "$out/summary.txt")

# A check that ended badly WITHOUT naming one difference did not run — fluxui.dev down, a page that never went
# idle, a crash. That is no news about Flux, so the run says it could not tell (exit 1), never that Flux moved.
broken=$(grep -v '^0 ' "$out/summary.txt" | while read -r _ check; do
  grep -q '^FAIL' "$out/$(echo "$check" | tr ' /' '__').log" || echo "$check"
done)
if [ -n "$broken" ]; then
  echo "parity-all: could not run every check, so nothing is said about Flux. These ended without a difference:"
  while read -r check; do
    echo
    echo "== $check"
    tail -8 "$out/$(echo "$check" | tr ' /' '__').log"
  done <<< "$broken"
  exit 1
fi
if [ "$failed" -eq 0 ]; then
  echo "parity-all: all $total checks match Flux."
  exit 0
fi

echo "parity-all: $failed of $total checks differ from Flux:"
grep -v '^0 ' "$out/summary.txt" | while read -r _ check; do
  echo
  echo "== $check"
  grep -E '^(FAIL|       )' "$out/$(echo "$check" | tr ' /' '__').log" | head -12
done
exit 2

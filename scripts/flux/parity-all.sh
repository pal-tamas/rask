#!/usr/bin/env bash
# Holds the whole of Rask.Ui to Flux UI's live documentation: every page's look on a desktop and on a phone, and
# every walk the behaviour scripts take (open, keys, pointer). sync.mjs locks Flux's LOOK on one platform to see
# Flux move; this compares the two side by side on the same machine, so it needs no lock and runs anywhere — and,
# with main matching Flux, a red line here is Flux having moved (or Rask having drifted) since.
#
# Usage:  scripts/flux/parity-all.sh            # every check; the Rask pages are written first
# Needs:  node, dotnet, and RASK_FLUX_PLAYWRIGHT (or the Site E2E project's driver) — see scripts/flux/lib.mjs.
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
node scripts/flux/parity-chart.mjs --measure > "$out/chart-measure.log" 2>&1
[ -s "$pinned" ] || { echo "parity-all: could not pin the chart's load"; tail -20 "$out/chart-measure.log"; exit 1; }
dotnet test tests/Rask.Ui.Tests -m:2 -nodeReuse:false -p:RaskMdnRefresh=false --filter FluxParityPages > "$out/pages.log" 2>&1 \
  || { echo "parity-all: could not write the Rask pages"; tail -20 "$out/pages.log"; exit 1; }

checks() {
  # Every docs page, desktop and phone (layouts are measured at both widths by parity.mjs itself).
  node -e '
    const pages = require("./tests/Rask.Ui.Tests/Flux/flux.snapshot.json").pages;
    for (const p of pages) {
      const page = p.kind === "layouts" ? `layouts/${p.slug}` : p.slug;
      if (page === "chart") continue;
      console.log(`parity.mjs ${page} --refresh`);
      if (p.kind !== "layouts") console.log(`parity.mjs ${page} --refresh --view 390x844`);
    }'
  cat <<'EOF'
parity-autocomplete.mjs --refresh
parity-autocomplete.mjs --refresh --view 390x844
parity-carousel.mjs
parity-color-picker.mjs --refresh
parity-command.mjs --refresh
parity-command.mjs --refresh --view 390x844
parity-date.mjs date-picker --refresh
parity-date.mjs time-picker --refresh
parity-date.mjs datetime-picker --refresh
parity-editor.mjs
parity-menu.mjs dropdown --refresh
parity-menu.mjs context --refresh
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
parity-tooltip.mjs --refresh
chart
EOF
}

run() {
  local name
  name=$(echo "$*" | tr ' /' '__')
  if [ "$1" = chart ]; then
    { node scripts/flux/parity.mjs chart && node scripts/flux/parity.mjs chart --view 390x844 \
        && node scripts/flux/parity-chart.mjs && node scripts/flux/parity-chart.mjs --view 390x844 \
        && node scripts/flux/parity-chart.mjs --pointer && node scripts/flux/parity-chart.mjs --pointer --view 390x844; } > "$OUT/$name.log" 2>&1
  else
    node "scripts/flux/$1" "${@:2}" > "$OUT/$name.log" 2>&1
  fi
  echo "$? $*"
}
export -f run
export OUT="$out"

checks | xargs -P "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)" -L1 bash -c 'run "$@"' _ | sort -k2 > "$out/summary.txt"

total=$(wc -l < "$out/summary.txt" | tr -d ' ')
failed=$(grep -cv '^0 ' "$out/summary.txt")
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

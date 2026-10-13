#!/usr/bin/env node
// Holds the OPEN date picker and time picker to Flux UI's, example by example.
//
// parity.mjs measures a page as it loads, and a picker loads closed: its calendar or its list of times is in
// the tree and not displayed. The popup is most of the component, so this measures Flux's page again with
// each picker open — lib.mjs opens whatever carries `data-parity-open`, by a real press at its centre, and
// closes it again before the next — and files the result as the page `<slug>-open`, under the same section
// ids. DatePickerOpenParity and TimePickerOpenParity write the Rask pages of those names with the same mark,
// and parity.mjs compares the two as it compares any page: the popup's box, look and contents, and where it
// sits against the trigger it belongs to.
//
// Then WHERE IT LANDS with no room: the first pickers of the page opened with their trigger held at the edges
// of the viewport, and with more in the popup than fits (placement.mjs). The time picker at a phone's width and
// a desktop's. The date picker at 640 and a desktop's: UNDER 640 px Flux's is another control — a modal
// centred in the viewport over a dimmed page, its days 44 px, with Cancel and "Select date" under them — which
// the kit has not built (measured 2026-10-13; the kit shows the same popup at every width, kept in view).
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages          # writes the Rask pages
//         node scripts/flux/parity-date.mjs date-picker [--refresh] [--all]  # --refresh re-measures Flux
//         node scripts/flux/parity-date.mjs time-picker --section interval

import { spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium, measurePage, root } from './lib.mjs';
import { landings } from './placement.mjs';

const args = process.argv.slice(2);
const slug = args.find(a => a === 'date-picker' || a === 'time-picker');
if (!slug) {
  console.error('usage: node scripts/flux/parity-date.mjs <date-picker|time-picker> [--refresh] [--section <id>] [--all]');
  process.exit(1);
}

const out = join(root, 'artifacts', 'flux-parity', 'flux', `${slug}-open`);
const file = join(out, 'measurements.json');
if (!existsSync(file) || args.includes('--refresh')) {
  const browser = await chromium().launch();
  const open = await measurePage(browser, `https://fluxui.dev/components/${slug}`, out, page => page.evaluate(mark, slug));
  await browser.close();
  await mkdir(out, { recursive: true });
  await writeFile(file, JSON.stringify(open));
  console.log(`flux: ${open.light.length} ${slug} example(s) measured open`);
}

// Flux's date popup is a <dialog> it opens itself, its list of times a popover; the kit's are both popovers.
const popup = slug === 'date-picker' ? 'dialog' : '[popover]';
const edges = await chromium().launch();
const astray = await landings(edges, {
  name: `${slug}-open`, refresh: args.includes('--refresh'), all: args.includes('--all'), width: 260, hosts: [0, 1, 2],
  views: slug === 'date-picker' ? [[640, 844], [1920, 1080]] : undefined,
  flux: { url: `https://fluxui.dev/components/${slug}`, host: `[data-preview-wrapper] [data-flux-${slug}]`, panel: popup, trigger: '[data-parity-open]', prepare: page => page.evaluate(mark, slug) },
  rask: { url: pathToFileURL(join(root, 'artifacts', 'flux-parity', 'rask', `${slug}-open.html`)).href, host: `[data-preview-wrapper] [data-ui-${slug}]`, panel: popup, trigger: '[data-parity-open]' },
});
await edges.close();
console.log(astray ? `\nflux parity: the ${slug}'s popup lands elsewhere for ${astray} picker(s).\n` : `\nflux parity: the ${slug}'s popup lands where Flux's does.\n`);

const rest = args.filter(a => a !== '--refresh' && a !== slug);
const parity = spawnSync(process.execPath, [join(fileURLToPath(new URL('.', import.meta.url)), 'parity.mjs'), `${slug}-open`, ...rest], { stdio: 'inherit' });
process.exit(astray ? 1 : parity.status ?? 1);

// Runs in the page. Marks what a reader presses to open each picker: the picker itself where its trigger
// fills it (a press at the centre lands on the button), or the button beside the typed fields where the
// trigger is an input — a press on a field only puts the caret in it.
function mark(slug) {
  for (const picker of document.querySelectorAll(`[data-preview-wrapper] [data-flux-${slug}]`)) {
    // …a button where the trigger has one, else the chevron at the end of the typed field.
    const beside = picker.querySelector(`[data-flux-${slug}-trigger] button, ui-${slug}-trigger button`)
      ?? picker.querySelector(`ui-${slug}-trigger > svg:last-child`);
    (beside ?? picker).setAttribute('data-parity-open', '');
  }
}

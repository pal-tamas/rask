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
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages          # writes the Rask pages
//         node scripts/flux/parity-date.mjs date-picker [--refresh] [--all]  # --refresh re-measures Flux
//         node scripts/flux/parity-date.mjs time-picker --section interval

import { spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium, measurePage, root } from './lib.mjs';

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

const rest = args.filter(a => a !== '--refresh' && a !== slug);
const parity = spawnSync(process.execPath, [join(fileURLToPath(new URL('.', import.meta.url)), 'parity.mjs'), `${slug}-open`, ...rest], { stdio: 'inherit' });
process.exit(parity.status ?? 1);

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

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
// THE DATE PICKER IS ANOTHER THING ON A PHONE. Under 640 px Flux's is a sheet centred in the viewport over a
// page dimmed 10%: one month whatever `months` says, days 44 px, the presets a native select under them, and
// Cancel and "Select date" at the foot — a pick marks the day and writes nothing until that button is pressed.
// So the date picker is measured twice more at 390 × 844, as it loads and open, against the SAME two Rask pages
// (one markup; the stylesheet decides), and then WALKED there: `--live <url>` names a running site whose page
// holds a `Ui.DatePicker` with nothing chosen (`--picker <css>` says which; the first on the page unless given),
// and every step of the walk has to read the same on Flux's first example and on it.
//
// Then WHERE IT LANDS with no room: the first pickers of the page opened with their trigger held at the edges
// of the viewport, and with more in the popup than fits (placement.mjs). The time picker at a phone's width and
// a desktop's; the date picker at a phone's too, where the sheet is in the middle wherever the trigger is.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages          # writes the Rask pages
//         node scripts/flux/parity-date.mjs date-picker [--refresh] [--all]  # --refresh re-measures Flux
//         node scripts/flux/parity-date.mjs time-picker --section interval
//         node scripts/flux/parity-date.mjs date-picker --live http://localhost:5210/ui/date-picker/

import { spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium, measurePage, root, screen } from './lib.mjs';
import { landings } from './placement.mjs';

const args = process.argv.slice(2);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
const slug = args.find(a => a === 'date-picker' || a === 'time-picker' || a === 'datetime-picker');
if (!slug) {
  console.error('usage: node scripts/flux/parity-date.mjs <date-picker|time-picker|datetime-picker> [--refresh] [--section <id>] [--all] [--live <url> [--picker <css>] [--wait <ms>]]');
  process.exit(1);
}

const PHONE = [390, 844];
// The datetime picker's popup is the date picker's dialog, a sheet on a phone as well.
const dated = slug !== 'time-picker';
const refresh = args.includes('--refresh');
// The desktop every page is measured at, and a phone for the picker that is another control there.
const views = dated ? [undefined, PHONE] : [undefined];
const sized = view => (view ? `@${view.join('x')}` : '');

const out = join(root, 'artifacts', 'flux-parity', 'flux', `${slug}-open`);
for (const view of views) {
  const file = join(out, `measurements${sized(view)}.json`);
  if (existsSync(file) && !refresh) continue;
  const browser = await chromium().launch();
  const open = await measurePage(browser, `https://fluxui.dev/components/${slug}`, out + sized(view), page => page.evaluate(mark, slug), view);
  await browser.close();
  await mkdir(out, { recursive: true });
  await writeFile(file, JSON.stringify(open));
  console.log(`flux: ${open.light.length} ${slug} example(s) measured open${view ? ` at ${view.join(' × ')}` : ''}`);
}

// Flux's date popup is a <dialog> it opens itself, its list of times a popover; the kit's are both popovers.
const popup = dated ? 'dialog' : '[popover]';
const edges = await chromium().launch();
const astray = await landings(edges, {
  name: `${slug}-open`, refresh, all: args.includes('--all'), width: 260, hosts: [0, 1, 2],
  views: dated ? [PHONE, [640, 844], [1920, 1080]] : undefined,
  flux: { url: `https://fluxui.dev/components/${slug}`, host: `[data-preview-wrapper] [data-flux-${slug}]`, panel: popup, trigger: '[data-parity-open]', prepare: page => page.evaluate(mark, slug) },
  rask: { url: pathToFileURL(join(root, 'artifacts', 'flux-parity', 'rask', `${slug}-open.html`)).href, host: `[data-preview-wrapper] [data-ui-${slug}]`, panel: popup, trigger: '[data-parity-open]' },
});
console.log(astray ? `\nflux parity: the ${slug}'s popup lands elsewhere for ${astray} picker(s).\n` : `\nflux parity: the ${slug}'s popup lands where Flux's does.\n`);

const strayed = option('live') && slug === 'date-picker' ? await walk(edges, option('live')) : 0;
await edges.close();

const passed = args.filter((a, i) => a === '--all' || a === '--section' || args[i - 1] === '--section');
const parity = (page, view, more = []) => spawnSync(process.execPath,
  [join(fileURLToPath(new URL('.', import.meta.url)), 'parity.mjs'), page, ...(view ? ['--view', view.join('x')] : []), ...passed, ...more],
  { stdio: 'inherit' }).status ?? 1;
let failed = astray + strayed;
for (const view of views) {
  if (view) console.log(`\n— at ${view.join(' × ')} —`);
  failed += parity(`${slug}-open`, view);
  // As it loads at a phone's width too: the trigger is another size there, and so is what the closed popup holds.
  if (view) failed += parity(slug, view, refresh ? ['--refresh'] : []);
}

process.exit(failed ? 1 : 0);

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

// ----- The walk on a phone -------------------------------------------------------------------------------
// One scenario, driven with a real pointer and real keys on Flux's first example and on a picker of a running
// site: what each step leaves — whether the sheet is open, what the trigger reads, which day is marked, whether
// the page behind is held — has to be the same line on both.
async function walk(browser, live) {
  const sides = [
    { name: 'flux', url: 'https://fluxui.dev/components/date-picker', picker: '[data-preview-wrapper] [data-flux-date-picker]', wait: 350 },
    { name: 'rask', url: live, picker: option('picker') ?? '[data-ui-date-picker]', wait: Number(option('wait') ?? 700) },
  ];
  const transcripts = [];
  for (const side of sides) {
    const context = await browser.newContext(screen(PHONE[0], PHONE[1]));
    await context.clock.setFixedTime(new Date('2026-01-15T12:00:00Z'));
    const page = await context.newPage();
    await page.goto(side.url, { waitUntil: 'networkidle', timeout: 120000 });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(side.wait * 2);
    const picker = page.locator(side.picker).first();
    await picker.evaluate(el => el.scrollIntoView({ block: 'center' }));
    const lines = [];
    const say = async step => {
      await page.waitForTimeout(side.wait);
      lines.push(`${step}: ${await picker.evaluate(read)}`);
    };
    const open = async () => {
      const box = await picker.boundingBox();
      await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    };
    const day = number => picker.locator('dialog td:not([data-outside]) button').filter({ hasText: new RegExp(`^\\s*${number}\\s*$`) }).first().click();
    const press = text => picker.locator('dialog button').filter({ hasText: text }).first().click();

    await say('as loaded');
    await open(); await say('opened');
    await day(20); await say('picked the 20th');
    await day(22); await say('picked the 22nd');
    await press('Cancel'); await say('Cancel');
    await open(); await say('opened again');
    await day(20); await say('picked the 20th');
    await press('Select date'); await say('Select date');
    await open(); await say('opened a third time');
    await day(9); await say('picked the 9th');
    await page.keyboard.press('Escape'); await say('Escape');
    await open(); await say('opened a fourth time');
    await day(12); await say('picked the 12th');
    await page.mouse.click(8, 30); await say('a press on the dimmed page');
    await open(); await say('opened a fifth time');
    await press('Select date'); await say('Select date, the 12th still marked');
    transcripts.push(lines);
    await context.close();
  }

  let differing = 0;
  transcripts[0].forEach((theirs, i) => {
    const mine = transcripts[1][i];
    differing += theirs === mine ? 0 : 1;
    console.log(`${theirs === mine ? 'ok  ' : 'FAIL'} walk at 390 — ${theirs}${theirs === mine ? '' : `\n       vs ${mine}`}`);
  });
  console.log(differing ? `\nflux parity: the date picker's phone walk differs in ${differing} step(s).\n` : '\nflux parity: the date picker walks on a phone as Flux\'s does.\n');
  return differing;
}

// Runs in the page, on a picker of either kit.
function read(picker) {
  const dialog = picker.querySelector('dialog');
  const open = dialog.matches(':popover-open') || dialog.open;
  const box = dialog.getBoundingClientRect();
  const trigger = picker.querySelector('button[role=combobox]').textContent.replace(/\s+/g, ' ').trim();
  const marked = [...dialog.querySelectorAll('td[data-selected]')].map(cell => cell.textContent.trim()).join(',') || 'none';
  const root = getComputedStyle(document.documentElement);
  const foot = [...dialog.querySelectorAll('button')].filter(b => b.getClientRects().length && /Cancel|Select/.test(b.textContent)).map(b => b.textContent.trim()).join(' / ');
  return [
    open ? `open at ${Math.round(box.x)},${Math.round(box.y)} ${Math.round(box.width)}×${Math.round(box.height)}` : 'closed',
    `trigger "${trigger}"`, `marked ${marked}`, open ? `foot "${foot}"` : '',
    `page ${root.overflow === 'hidden' && root.pointerEvents === 'none' ? 'held' : 'free'}`,
  ].filter(Boolean).join(' · ');
}

#!/usr/bin/env node
// Holds every clear button of the kit to the box it clears, in the containers an app lays a form out with.
//
// The page is ClearPlacementParity's (artifacts/flux-parity/rask/clear-placement.html): each control with a
// value and `Clearable`, alone, labelled, in a row beside a date picker, grown, in a grid cell and in a column,
// at 1920 and at 390, in light and in dark. For each one the button must
//   - lie inside its trigger's box, centred on it top to bottom;
//   - be what a press at its centre lands on;
//   - stand the same distance from the trigger's end in every container.
// Flux's public pages draw one clear button, the input's. It is measured on fluxui.dev and the kit's input is
// held to it: its size and where it stands in the box. An input a row stretches is left as Flux's is (STRETCHED).
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the page
//         node scripts/flux/clear-placement.mjs                        # every case
//         node scripts/flux/clear-placement.mjs --offline              # without Flux's page
//         node scripts/flux/clear-placement.mjs --shots <dir>          # and a PNG per width and scheme
//
// Exit code 1 on any difference.

import { existsSync } from 'node:fs';
import { mkdir } from 'node:fs/promises';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root } from './lib.mjs';

const args = process.argv.slice(2);
const shots = args.includes('--shots') ? args[args.indexOf('--shots') + 1] : undefined;
const file = join(root, 'artifacts', 'flux-parity', 'rask', 'clear-placement.html');
if (!existsSync(file)) {
  console.error(`clear placement: ${file} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

const WIDTHS = [1920, 390];

// In the page: every case's button against its trigger. A trigger is the bordered box (`data-*-group-target`),
// or the control itself where it has none.
const read = () => {
  const box = el => { const r = el.getBoundingClientRect(); return { x: r.x, y: r.y, right: r.right, bottom: r.bottom, w: r.width, h: r.height }; };
  const shown = el => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== 'hidden';
  return [...document.querySelectorAll('[data-clear-case]')].map(host => {
    const name = `${host.dataset.control} · ${host.dataset.clearCase}`;
    const clear = [...host.querySelectorAll('[aria-label^="Clear"]')].filter(shown).at(-1);
    if (!clear) return { name, missing: true };
    // A press is asked of the viewport, so the button is brought into it first.
    clear.scrollIntoView({ block: 'center', behavior: 'instant' });
    const control = clear.closest('[data-ui-control], [data-flux-control], [data-ui-input], [data-flux-input]') ?? host;
    const trigger = [...control.querySelectorAll('[data-ui-group-target], [data-flux-group-target]')].find(shown) ?? control;
    const c = box(clear);
    const hit = document.elementFromPoint(c.x + c.w / 2, c.y + c.h / 2);
    return { name, control: host.dataset.control, where: host.dataset.clearCase, clear: c, trigger: box(trigger), hit: clear.contains(hit), position: getComputedStyle(clear).position,
      inTrigger: trigger !== clear && trigger.contains(clear), tab: clear.tabIndex };
  });
};

const near = (a, b) => Math.abs(a - b) <= 0.51;

// The input is Flux's to the pixel, and Flux places its buttons against the input's whole room: where a row or a
// grid cell stretches that room, Flux's own clear button drifts down with it (measured: 40px beside a 120px
// neighbour). The kit's does the same, so there the input is held to a press and to its distance from the end.
const STRETCHED = ['beside a labelled date picker', 'grown in a row', 'in a grid cell'];

// Flux's pickers draw their × INSIDE the trigger, between the choice and the chevron, out of the Tab order
// (measured on the datetime picker, 2026-10-26: a 32px div, 29px from the trigger's end). The kit's pickers and its
// listbox are held to that; Flux shows no clearable date picker, time picker or listbox, so its datetime picker's
// place is theirs (a typed field ends the same way).
const IN_TRIGGER = ['date-picker', 'date-picker-typed', 'date-picker-range', 'time-picker', 'time-picker-typed', 'select'];
const FLUX_END = 29;
const asFlux = one => one.control === 'input' && STRETCHED.includes(one.where);

function judge(found) {
  const wrong = [];
  const ends = new Map();
  for (const one of found) {
    if (one.missing) { wrong.push(`${one.name}: no clear button is shown`); continue; }
    const { clear: c, trigger: t } = one;
    const inside = c.x >= t.x - 0.5 && c.right <= t.right + 0.5 && c.y >= t.y - 0.5 && c.bottom <= t.bottom + 0.5;
    if (asFlux(one)) { /* drifts as Flux's does */ } else if (!inside) wrong.push(`${one.name}: the button is outside its trigger — ${Math.round(c.y - t.y)}px from its top, ${Math.round(t.right - c.right)}px from its end (trigger ${Math.round(t.w)}×${Math.round(t.h)})`);
    else if (!near(c.y + c.h / 2, t.y + t.h / 2)) wrong.push(`${one.name}: the button is ${(c.y + c.h / 2 - t.y - t.h / 2).toFixed(1)}px off the trigger's middle`);
    if (!one.hit) wrong.push(`${one.name}: a press at the button's centre lands on something else`);
    if (IN_TRIGGER.includes(one.control)) {
      if (!one.inTrigger) wrong.push(`${one.name}: the × is not inside its trigger`);
      if (one.tab !== -1) wrong.push(`${one.name}: the × is in the Tab order (tabIndex ${one.tab})`);
      if (!near(t.right - c.right, FLUX_END)) wrong.push(`${one.name}: the × is ${(t.right - c.right).toFixed(1)}px from the trigger's end, Flux's ${FLUX_END}`);
    }
    const end = +(t.right - c.right).toFixed(2);
    if (!ends.has(one.control)) ends.set(one.control, { end, name: one.name });
    else if (!near(ends.get(one.control).end, end)) wrong.push(`${one.name}: ${end}px from the trigger's end, ${ends.get(one.control).end}px in "${ends.get(one.control).name}"`);
  }

  return { wrong, ends };
}

// Flux's one drawn clear button: where it stands in the input's box.
async function fluxInput(browser, width) {
  const context = await browser.newContext({ viewport: { width, height: 900 } });
  const page = await context.newPage();
  await page.goto('https://fluxui.dev/components/input', { waitUntil: 'networkidle', timeout: 90000 });
  const found = await page.evaluate(() => {
    const clear = document.querySelector('[data-preview-wrapper] [data-flux-input] button[aria-label^="Clear"]');
    if (!clear) return null;
    const input = clear.closest('[data-flux-input]').querySelector('input').getBoundingClientRect();
    const c = clear.getBoundingClientRect();
    return { w: c.width, h: c.height, top: c.y - input.y, end: input.right - c.right };
  });
  await context.close();
  return found;
}

const browser = await chromium().launch();
let failed = 0;
for (const width of WIDTHS) {
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme: scheme });
    const page = await context.newPage();
    await page.goto(pathToFileURL(file).href, { waitUntil: 'networkidle' });
    await page.evaluate(() => document.fonts.ready);
    const found = await page.evaluate(read);
    const { wrong, ends } = judge(found);
    if (shots) {
      await mkdir(shots, { recursive: true });
      await page.screenshot({ path: join(shots, `clear-placement-${width}-${scheme}.png`), fullPage: true });
      // The row an app reported, whole: two pickers are 480px at their narrowest, wider than a phone.
      for (const control of ['time-picker', 'date-picker']) {
        const clip = await page.evaluate(name => {
          const row = document.querySelector(`[data-control="${name}"][data-clear-case="beside a date picker"]`);
          const box = row.getBoundingClientRect();
          return { x: box.x + scrollX, y: box.y + scrollY - 8, width: row.scrollWidth, height: box.height + 16 };
        }, control);
        await page.screenshot({ path: join(shots, `row-${control}-${width}-${scheme}.png`), fullPage: true, clip });
      }
    }

    if (scheme === 'light' && !args.includes('--offline')) {
      const flux = await fluxInput(browser, width);
      const mine = found.find(one => one.name === 'input · alone');
      if (!flux) wrong.push('Flux\'s input page draws no clear button any more: measure again');
      else if (mine && !mine.missing) {
        const kit = { w: mine.clear.w, h: mine.clear.h, top: mine.clear.y - mine.trigger.y, end: mine.trigger.right - mine.clear.right };
        for (const key of ['w', 'h', 'top', 'end']) {
          if (!near(kit[key], flux[key])) wrong.push(`input · alone: ${key} is ${kit[key]} and Flux's is ${flux[key]}`);
        }
        console.log(`  Flux's input at ${width}: ${flux.w}×${flux.h}, ${flux.top}px from the top, ${flux.end}px from the end — the kit's: ${kit.w}×${kit.h}, ${kit.top}, ${kit.end}`);
      }
    }

    console.log(`${width} ${scheme}: ${found.length} cases, ${wrong.length} wrong` + (scheme === 'light' ? ` — from the trigger's end: ${[...ends].map(([k, v]) => `${k} ${v.end}`).join(', ')}` : ''));
    for (const line of wrong) console.log(`  FAIL ${line}`);
    failed += wrong.length;
    await context.close();
  }
}

await browser.close();
console.log(failed === 0 ? 'clear placement: every button is in its trigger' : `clear placement: ${failed} wrong`);
process.exit(failed === 0 ? 0 : 1);

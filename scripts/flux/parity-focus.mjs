#!/usr/bin/env node
// Holds what FOCUS draws on Rask.Ui's controls to what it draws on Flux UI's: after a mouse press, and after the
// keyboard.
//
// parity.mjs forces `:focus` and `:focus-visible` together on each control, which cannot tell a `focus:` ring
// from a `focus-visible:` one, and never moves focus for real — so it cannot see a ring a CLICK leaves, or one
// drawn on what focus lands on next (a wrapper, an opened popup). This walks each control for real, on Flux's
// live page and on the page FluxParityPages wrote, and compares:
//   - where focus is (the control itself, its popup, the page) and whether the browser calls it :focus-visible;
//   - every ring the move drew anywhere on the page — an element whose outline is shown, or whose box-shadow
//     or border colour is not what it was before the move — by what is drawn: outline style, width, colour,
//     offset, the box-shadow, the border colour.
//
// Each control is moved three ways: `mouse` (a real press at its centre), `keyboard` (focus arrives with the
// keyboard as the last thing used: a Tab, then the control) and, for a control that opens a popup, `open`
// (focused that way, then Enter). In Chromium, and in WebKit as well where the driver has it (`--webkit`),
// since Safari draws its own blue ring.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-focus.mjs [--webkit] [--only <name>] [--shots <dir>] [--all]
//
// Exit code 1 on any difference. `--shots <dir>` writes a 1280-wide screenshot of every step on both sides.

import { mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root, webkit } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
const rask = join(root, 'artifacts', 'flux-parity', 'rask');

// name · Flux's page and the control on it · the Rask page and its control · whether it opens a popup.
// Each control is the first of its kind in its example, so both pages hold the same one. A third selector is
// what the pointer presses, where that is not the control: the kit's checkbox is a native input, out of sight
// inside the box a reader sees. A control that `toggles` is pressed twice, so it ends as it started and only
// focus is left to differ.
const CASES = [
  { name: 'button', flux: ['components/button', '[data-preview-wrapper] [data-flux-button]'], rask: ['button.html', '[data-ui-button]'] },
  { name: 'input', flux: ['components/input', '[data-preview-wrapper] input[data-flux-control]'], rask: ['input.html', 'input[data-ui-control]'] },
  { name: 'textarea', flux: ['components/textarea', '[data-preview-wrapper] textarea'], rask: ['textarea.html', 'textarea'] },
  { name: 'select (native)', flux: ['components/select', '[data-preview-wrapper] select'], rask: ['select.html', 'select'] },
  { name: 'select (listbox)', flux: ['components/select', '[data-preview-wrapper] [data-flux-select-button]'], rask: ['select.html', '[data-ui-select-button]'], opens: true },
  { name: 'checkbox', flux: ['components/checkbox', '[data-preview-wrapper] [data-flux-checkbox]'], rask: ['checkbox.html', 'input[type=checkbox]', '[data-ui-checkbox]'], toggles: true },
  { name: 'radio', flux: ['components/radio', '[data-preview-wrapper] [data-flux-radio]'], rask: ['radio.html', 'input[type=radio]', '[data-ui-radio]'], toggles: true },
  { name: 'switch', flux: ['components/switch', '[data-preview-wrapper] [data-flux-switch]'], rask: ['switch.html', 'input[role=switch]', '[data-ui-switch]'], toggles: true },
  { name: 'tabs', flux: ['components/tabs', '[data-preview-wrapper] [data-flux-tab]'], rask: ['tabs.html', '[data-ui-tab]'] },
  { name: 'navlist item', flux: ['demo/sidebar', '[data-flux-sidebar-item]:not([data-current])'], rask: ['sidebar/sidebar.html', '[data-ui-sidebar-item]:not([aria-current])'] },
  { name: 'dropdown trigger', flux: ['components/dropdown', '[data-preview-wrapper] [data-flux-button][aria-haspopup]'], rask: ['dropdown.html', '[data-ui-button][aria-haspopup]'], opens: true },
  { name: 'date picker', flux: ['components/date-picker', '[data-preview-wrapper] [data-flux-date-picker-button]'], rask: ['date-picker.html', '[data-ui-date-picker-button]'], opens: true },
  { name: 'date picker (input)', flux: ['components/date-picker', '[data-preview-wrapper] [data-flux-month-input]'], rask: ['date-picker.html', '[data-ui-month-input]'] },
];

// What focus can draw, read off every element of the page before and after the move.
const RING = ['outlineStyle', 'outlineWidth', 'outlineColor', 'outlineOffset', 'boxShadow', 'borderTopColor'];

// Where focus LANDS differs here, with nothing drawn on either page: Safari's own rule for a pressed native
// control, which no stylesheet changes. By engine, case and move, with why; printed as `ok*`, and failed again
// the moment either side draws a ring.
const SAFARI_PRESS = 'Safari does not focus a native control a mouse presses; Flux\'s is a custom element with a '
  + 'tabindex (or its script focuses it), which any engine focuses. Neither draws a ring.';
const ACCEPTED = {
  'webkit select (listbox) mouse': SAFARI_PRESS,
  'webkit checkbox mouse': SAFARI_PRESS,
  'webkit radio mouse': SAFARI_PRESS,
  'webkit switch mouse': SAFARI_PRESS,
  'webkit dropdown trigger mouse': 'Both menus take the focus as they open, the kit\'s by `autofocus`, which WebKit counts as '
    + ':focus-visible, Flux\'s by script, which after a press it does not. The menu draws no ring either way.',
};

const safari = flag('webkit') ? webkit() : null;
if (flag('webkit') && !safari) console.log('flux parity: the driver has no WebKit (its `playwright install webkit`) — Chromium alone.\n');
const engines = [['chromium', chromium()], ...(safari ? [['webkit', safari]] : [])];

const shots = option('shots');
const only = option('only');
// Every step is a page of its own on each side, so the steps run side by side: RASK_FLUX_JOBS at once (4).
const jobs = Number(process.env.RASK_FLUX_JOBS ?? 4);
const browsers = Object.fromEntries(await Promise.all(engines.map(async ([name, engine]) => [name, await engine.launch()])));
const steps = engines.flatMap(([engineName]) => ['light', 'dark'].flatMap(scheme => CASES
  .filter(test => !only || test.name === only)
  .flatMap(test => ['mouse', 'keyboard', ...(test.opens ? ['open'] : [])].map(mode => ({ engineName, scheme, test, mode })))));
const results = new Array(steps.length);
let next = 0;
await Promise.all(Array.from({ length: Math.min(jobs, steps.length) }, async () => {
  while (next < steps.length) {
    const at = next++;
    const { engineName, scheme, test, mode } = steps[at];
    const [theirs, mine] = await Promise.all(['flux', 'rask'].map(side => walk(browsers[engineName], scheme, test, mode, side, engineName)));
    results[at] = { ...steps[at], theirs, mine, diffs: compare(theirs, mine, engineName) };
  }
}));
await Promise.all(Object.values(browsers).map(browser => browser.close()));

let failures = 0;
for (const { engineName, scheme, test, mode, theirs, mine, diffs } of results) {
  // Accepted only while neither side draws anything: a ring that appears fails it again.
  const accepted = !theirs.rings.length && !mine.rings.length ? ACCEPTED[`${engineName} ${test.name} ${mode}`] : undefined;
  const failed = diffs.length && !accepted;
  failures += failed ? 1 : 0;
  console.log(`${failed ? 'FAIL' : diffs.length ? 'ok* ' : 'ok  '} ${engineName.padEnd(8)} ${scheme.padEnd(5)} ${test.name.padEnd(20)} ${mode.padEnd(8)} `
    + `flux: ${describe(theirs)} · rask: ${describe(mine)}`);
  if (diffs.length && accepted) console.log(`       accepted: ${accepted}`);
  for (const d of diffs.slice(0, flag('all') ? Infinity : 6)) console.log(`       ${d}`);
}

console.log(failures ? `\nflux parity: focus draws differently in ${failures} step(s).` : '\nflux parity: focus draws what Flux\'s draws.');
process.exit(failures ? 1 : 0);

// One control moved one way on one side, in a page of its own: what focus is on, and every ring the move drew.
async function walk(browser, scheme, test, mode, side, engineName) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
  const page = await context.newPage();
  const [where, selector, pressed] = test[side];
  const url = side === 'flux' ? `https://fluxui.dev/${where}` : pathToFileURL(join(rask, where)).href;
  if (side === 'rask' && !existsSync(join(rask, where))) {
    console.error(`flux parity: ${join(rask, where)} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
    process.exit(1);
  }

  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  if (side === 'rask') await withRuntime(page, 'rask-hooks.ts');
  await page.evaluate(() => document.fonts.ready);
  const control = page.locator(selector).first();
  const target = pressed ? page.locator(pressed).first() : control;
  await target.evaluate(el => el.scrollIntoView({ block: 'center', behavior: 'instant' }));
  await page.mouse.move(2, 2);
  await settle(page);
  await page.evaluate(remember, RING);

  if (mode === 'mouse') {
    const box = await target.boundingBox();
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    if (test.toggles) {
      await page.waitForTimeout(400);
      await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    }
  } else {
    // The keyboard as the last thing used, then focus on the control: what a Tab onto it leaves.
    await page.keyboard.press('Tab');
    await control.focus();
    if (mode === 'open') await page.keyboard.press('Enter');
  }

  // Away from it, so a hover is not mistaken for focus.
  await page.mouse.move(2, 2);
  await settle(page);
  const measured = await control.evaluate(drawn, RING);
  if (shots) {
    const dir = join(shots, engineName, scheme);
    await mkdir(dir, { recursive: true });
    await page.screenshot({ path: join(dir, `${test.name.replace(/[ ()]+/g, '-').replace(/-$/, '')}-${mode}-${side}.png`) });
  }

  await context.close();
  return measured;
}

// Two frames and a transition's length: what a move started has finished drawing.
async function settle(page) {
  await page.waitForTimeout(250);
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
}

// Runs in the page: every element's ring properties before the move.
function remember(RING) {
  window.__focusBefore = new WeakMap();
  for (const el of document.querySelectorAll('*')) {
    const style = getComputedStyle(el);
    window.__focusBefore.set(el, Object.fromEntries(RING.map(k => [k, style[k]])));
  }
}

// Runs in the page: where focus is, and every ring the move drew.
function drawn(control, RING) {
  const active = document.activeElement;
  // Drawn at all: a box bigger than the 1px an `sr-only` control is clipped to, and nothing over it transparent.
  const visible = el => {
    const box = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    if (box.width <= 1 || box.height <= 1 || style.visibility === 'hidden' || style.display === 'none') return false;
    for (let up = el; up; up = up.parentElement) if (getComputedStyle(up).opacity === '0') return false;
    return true;
  };
  const shown = style => style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0;
  // What a marker names on either page: data-flux-x and data-ui-x are the same part.
  const named = el => {
    const marker = [...el.attributes].map(a => a.name).find(n => /^data-(flux|ui)-/.test(n));
    return marker ? marker.replace(/^data-(flux|ui)-/, '') : el.localName;
  };
  const popup = active?.closest(':popover-open, dialog[open]');
  const where = !active || active === document.body ? 'page'
    : active === control ? 'control'
      : control.contains(active) || active.contains(control) ? 'control part'
        : popup ? `popup ${named(popup)}${popup === active ? '' : ` > ${active.getAttribute('role') ?? active.localName}`}`
          : `elsewhere ${named(active)}`;
  // A shadow drawn at all: Tailwind's ring utilities leave zero-width layers behind, which paint nothing.
  const painted = shadow => {
    const layers = [];
    let depth = 0;
    let layer = '';
    for (const ch of shadow) {
      depth += ch === '(' ? 1 : ch === ')' ? -1 : 0;
      if (ch === ',' && depth === 0) { layers.push(layer.trim()); layer = ''; } else layer += ch;
    }
    layers.push(layer.trim());
    // A layer paints when it has a colour that is not transparent and any blur or spread or offset.
    return layers.filter(l => l !== 'none' && !/rgba\([^)]*,\s*0\)/.test(l)
      && l.replace(/\([^)]*\)/g, '').match(/-?[\d.]+px/g)?.some(n => parseFloat(n) !== 0)).join(', ') || 'none';
  };
  // What the move could have drawn on: the control's own example (the docs page's chrome is not it), whatever
  // popup is open, and every box around where focus is.
  const scope = new Set([
    ...(control.closest('[data-preview-wrapper]') ?? document.body).querySelectorAll('*'),
    ...[...document.querySelectorAll(':popover-open, dialog[open]')].flatMap(popup => [popup, ...popup.querySelectorAll('*')]),
  ]);
  for (let up = active; up && up !== document.body; up = up.parentElement) scope.add(up);
  const rings = [];
  for (const el of scope) {
    if (!visible(el)) continue;
    const style = getComputedStyle(el);
    const before = window.__focusBefore.get(el);
    // An element the move put on the page (a popup's rows) is a ring only by its outline: its shadow and
    // border are its resting look.
    const outline = shown(style) && (!before || RING.slice(0, 4).some(k => before[k] !== style[k]));
    const shadow = Boolean(before) && painted(style.boxShadow) !== painted(before.boxShadow);
    const border = Boolean(before) && style.borderTopColor !== before.borderTopColor;
    if (!outline && !shadow && !border) continue;
    const box = el.getBoundingClientRect();
    rings.push({
      on: el === active ? 'focused' : el.contains(active) ? 'around focused' : named(el),
      outline: outline ? `${style.outlineStyle} ${style.outlineWidth} ${style.outlineColor} offset ${style.outlineOffset}` : 'none',
      boxShadow: shadow ? painted(style.boxShadow) : '(unchanged)',
      border: border ? style.borderTopColor : '(unchanged)',
      size: `${Math.round(box.width)}x${Math.round(box.height)}`,
    });
  }

  return { where, focusVisible: Boolean(active && active !== document.body && active.matches(':focus-visible')), rings };
}

function describe(m) {
  const rings = m.rings.map(r => (r.outline !== 'none' ? `outline ${r.outline.split(' ').slice(0, 2).join(' ')}` : '')
    + (r.boxShadow !== '(unchanged)' ? ' shadow' : '') + (r.border !== '(unchanged)' ? ' border' : '') + ` on ${r.on} ${r.size}`).join(', ');
  return `${m.where}${m.focusVisible ? ' :focus-visible' : ''}${rings ? ` [${rings.trim()}]` : ' [no ring]'}`;
}

// What differs: where focus is, :focus-visible, and the rings, by what each draws (not by the element that
// draws it: Flux's checkbox is a custom element where the kit's is a native input inside its box).
// WebKit draws an `auto` outline as the platform's focus ring, whatever width and colour it computes (the
// kit's checkbox ring computes 1px and the focus-ring colour where the browser's own computes 3px and
// currentcolor, and the two are the same pixels), so there only its style and offset are compared.
function compare(flux, mine, engine) {
  const diffs = [];
  const kind = where => where.replace(/ > .*/, '').replace(/^control part$/, 'control');
  if (kind(flux.where) !== kind(mine.where)) diffs.push(`focus is on: flux ${flux.where} · rask ${mine.where}`);
  if (flux.focusVisible !== mine.focusVisible) diffs.push(`:focus-visible: flux ${flux.focusVisible} · rask ${mine.focusVisible}`);
  const outline = o => (engine === 'webkit' && o.startsWith('auto ') ? `auto ${o.slice(o.indexOf(' offset '))}` : o);
  const key = r => `${outline(r.outline)} | ${r.boxShadow} | ${r.border}`;
  const theirs = flux.rings.map(key);
  const ours = mine.rings.map(key);
  for (const ring of new Set([...theirs, ...ours])) {
    const a = theirs.filter(k => k === ring).length;
    const b = ours.filter(k => k === ring).length;
    if (a !== b) diffs.push(`ring ${ring}: flux ${a} · rask ${b}`);
  }

  return diffs;
}

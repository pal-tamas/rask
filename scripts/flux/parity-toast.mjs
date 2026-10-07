#!/usr/bin/env node
// Holds Ui.Toast to Flux's toast AS SHOWN — the half of fluxui.dev/components/toast that parity.mjs
// cannot see, because that page loads as ten buttons and the toast only exists once one is pressed.
//
// Flux's side: each scenario below raises its toast on the live page the way the page's own buttons do
// (the `toast-show` / `toast-group-show` events they dispatch, and the documented `Flux.toast()`), waits
// for it to settle, and hands the toast's host to the SAME measuring code as every other component
// (lib.mjs). The result is written as the measurements of a page named `toast-shown`.
// Rask's side: a shown toast is plain markup, so ToastShownParity writes each scenario already drawn.
// Then parity.mjs — unchanged, the same comparison as every component — compares the two, and this script
// adds the one thing a subtree comparison leaves out: where on the screen each toast is.
//
// What is put right on Flux's measurement before it is compared, each in the open (see settle):
//   - Flux's custom elements pair with the native element Rask writes: ui-toast, ui-toast-group and
//     ui-close are all <div>; the two hosts are marked as `data-flux-*` parts so they pair by marker;
//   - Flux clones one template for every toast and hides what this one does not use (three of four
//     icons, an empty heading). Rask renders only what shows, so what is not displayed is left out;
//   - a group is compared front to back: Flux appends each new toast, Rask writes the newest first,
//     because CSS can only anchor an element to one earlier in the tree;
//   - in an OPEN stack Flux's script moves each toast by a pixel offset it measured (translateY), and
//     Rask's stylesheet places it by anchor with no offset. Both end in the same place, which the box
//     and the screen check state; the offset itself is not compared. The same goes for the paint order
//     Flux gives an always-expanded stack, where nothing overlaps.
//
// Last, the countdown (compareCountdowns): what holds a toast and what it does once let go, timed on both.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-toast.mjs                            # measure Flux, compare
//         node scripts/flux/parity-toast.mjs --cached --all             # reuse Flux's measurement
//
// Exit code 1 on any difference.

import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, measurePage, root } from './lib.mjs';
import { runtime } from './runtime.mjs';

const args = process.argv.slice(2);
const out = join(root, 'artifacts', 'flux-parity');
const fluxDir = join(out, 'flux', 'toast-shown');
const raskPage = join(out, 'rask', 'toast-shown.html');
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

const four = [
  { slots: { text: 'First, a plain one.' } },
  { slots: { heading: 'Second', text: 'With a heading, so it is taller.' }, dataset: { variant: 'success' } },
  { slots: { text: 'Third, plain again.' } },
  { slots: { text: 'Fourth.' } },
];
const saved = { slots: { text: 'Your changes have been saved.' } };
const corner = position => ({ show: [{ ...saved, dataset: { position } }] });

// Each scenario: what the page's buttons would dispatch (`show`), or a documented Flux.toast() call.
// Every toast is permanent (duration 0) so it is still there when it is measured.
const SCENARIOS = {
  basic: { show: [saved] },
  heading: { show: [{ slots: { heading: 'Changes saved', text: 'You can always update this in your settings.' } }] },
  success: { show: [{ slots: { heading: 'Post created', text: 'The post has been created successfully.' }, dataset: { variant: 'success' } }] },
  warning: { show: [{ slots: { heading: 'Unsaved changes', text: 'Your post has unsaved changes.' }, dataset: { variant: 'warning' } }] },
  danger: { show: [{ slots: { heading: 'Something went wrong', text: 'Your changes have not been saved.' }, dataset: { variant: 'danger' } }] },
  invert: { show: [{ slots: { heading: 'Changes saved', text: 'Your updates are now live.' }, dataset: { invert: '' } }] },
  'invert-success': { show: [{ slots: { heading: 'Changes saved', text: 'Your updates are now live.' }, dataset: { invert: '', variant: 'success' } }] },
  action: { toast: { heading: 'Changes saved', text: 'Your updates are now live.', variant: 'success', action: { label: 'Undo', event: 'undo-changes' } } },
  'action-link': { toast: { text: 'Invoice created.', action: { label: 'View', href: '/components/toast' } } },
  link: { toast: { text: 'Invoice created.', variant: 'success', link: { label: 'View invoice', href: '/components/toast' } } },
  'top-end': corner('top end'),
  'top-center': corner('top center'),
  'top-start': corner('top start'),
  'bottom-center': corner('bottom center'),
  'bottom-start': corner('bottom start'),
  group: { group: true, show: [saved] },
  'group-deck': { group: true, show: four },
  'group-open': { group: true, show: four, hover: true, open: true },
  'group-expanded': { group: true, show: four, expanded: true, open: true },
};

const SINGLE = 'ui-toast:not(ui-toast-group ui-toast)';
const NATIVE = { 'ui-toast': 'div', 'ui-toast-group': 'div', 'ui-close': 'div' };
const screens = { flux: { light: {}, dark: {} }, rask: { light: {}, dark: {} } };

let flux;
if (args.includes('--cached') && existsSync(join(fluxDir, 'measurements.json'))) {
  flux = JSON.parse(await readFile(join(fluxDir, 'measurements.json'), 'utf8'));
  Object.assign(screens.flux, JSON.parse(await readFile(join(fluxDir, 'screens.json'), 'utf8')));
} else {
  flux = await measureFlux();
  await mkdir(fluxDir, { recursive: true });
  await writeFile(join(fluxDir, 'measurements.json'), JSON.stringify(flux));
  await writeFile(join(fluxDir, 'screens.json'), JSON.stringify(screens.flux));
}

await measureRaskScreens();
const compared = spawnSync(process.execPath, [join(root, 'scripts', 'flux', 'parity.mjs'), 'toast-shown', ...args.filter(a => a !== '--cached')], { stdio: 'inherit' });
const moved = compareScreens();
const mistimed = await compareCountdowns();
process.exit(compared.status || moved || mistimed ? 1 : 0);

// ----- The countdown: what holds it, and what it does afterwards -------------------------------------------
//
// Timed, on both pages, the same way: two toasts in a group (3 s and 4 s), the pointer over the stack one
// second in and held there for five, then taken away; and one toast with focus on its close button and the
// pointer elsewhere. Flux's page raises them by its own events. Rask's page (ToastTimedParity) has them
// drawn, and is given Rask's runtime, which counts them down and presses each one's close button — that
// press is what is timed, and an app's handler would take the toast off the page in it.
//
// `late` is how long after its own remainder a toast went once the pointer had left: the fade, 350 ms on
// both. A countdown that RESTARTED under the pointer would be late by the second it had already run.
async function compareCountdowns() {
  const browser = await chromium().launch();
  const flux = await fluxCountdown(browser);
  const rask = await raskCountdown(browser);
  await browser.close();

  const checks = [
    ['every toast of a hovered stack stays', side => side.held === 2, side => `${side.held} of 2 there after 5 s under the pointer`],
    ['and none of them fades while it is held', side => side.opaque, side => (side.opaque ? 'all opaque' : 'one has faded')],
    ['each resumes its own remainder, then fades', side => side.late.every(ms => ms >= 250 && ms <= 520), side => `late by ${side.late.join(' and ')} ms`],
    ['the one with less left goes first', side => side.order === 'First,Second', side => side.order],
    ['focus inside holds nothing', side => side.focused >= 2250 && side.focused <= 2520, side => `gone ${side.focused} ms after it showed (2000 + the fade)`],
  ];
  let failures = 0;
  console.log('\ncountdown (flux | rask)');
  for (const [name, passes, says] of checks) {
    const ok = passes(flux) && passes(rask);
    failures += ok ? 0 : 1;
    console.log(`${ok ? 'ok  ' : 'FAIL'} ${name.padEnd(44)} ${says(flux)} | ${says(rask)}`);
  }

  const apart = Math.max(...flux.late.map((ms, i) => Math.abs(ms - rask.late[i])));
  failures += apart <= 120 ? 0 : 1;
  console.log(`${apart <= 120 ? 'ok  ' : 'FAIL'} ${'the two pages agree'.padEnd(44)} within ${apart} ms`);
  console.log(failures ? `\nflux parity: the toast's countdown differs in ${failures} check(s).` : '\nflux parity: the toast counts down as Flux\'s does.');
  return failures;
}

// Runs in the page, on either side: when each toast showed and went, on the page's own clock.
function clock() {
  window.__toasts = { shown: {}, gone: {} };
  window.__mark = () => Math.round(performance.now());
  window.__went = name => { window.__toasts.gone[name] ??= Math.round(performance.now()); };
}

async function hold(page, front) {
  const hover = await page.evaluate(() => window.__mark());
  await page.mouse.move(front[0], front[1]);
  await page.waitForTimeout(5000);
  const stacked = 'ui-toast-group > [data-flux-toast-dialog], [data-ui-toast-group] > [data-ui-toast-dialog]';
  const held = await page.evaluate(stacked => document.querySelectorAll(stacked).length, stacked);
  // Held is not enough: a toast whose fade ran on would be there and not be seen.
  const opaque = await page.evaluate(stacked => [...document.querySelectorAll(stacked)]
    .every(dialog => [dialog, dialog.firstElementChild].every(el => getComputedStyle(el).opacity === '1')), stacked);
  await page.mouse.move(5, 450);
  const leave = await page.evaluate(() => window.__mark());
  await page.waitForTimeout(6000);
  const { shown, gone } = await page.evaluate(() => window.__toasts);
  const lasts = { First: 3000, Second: 4000 };
  const late = ['First', 'Second'].map(name => (gone[name] - leave) - (lasts[name] - (hover - shown[name])));
  return { held, opaque, late, order: Object.keys(lasts).sort((a, b) => gone[a] - gone[b]).join(',') };
}

async function fluxCountdown(browser) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const raise = async (name, duration, event) => {
    await page.evaluate(({ name, duration, event }) => {
      window.__toasts.shown[name] = window.__mark();
      document.body.dispatchEvent(new CustomEvent(event, { detail: { duration, slots: { text: name } }, bubbles: true }));
    }, { name, duration, event });
  };
  const load = async () => {
    await page.goto('https://fluxui.dev/components/toast', { waitUntil: 'networkidle', timeout: 90000 });
    await page.mouse.move(5, 450);
    await page.evaluate(clock);
    // Flux takes a toast out of its host when it has gone.
    await page.evaluate(() => new MutationObserver(records => {
      for (const record of records) for (const node of record.removedNodes) if (node.nodeType === 1 && node.matches('[data-flux-toast-dialog]')) window.__went(node.textContent.trim());
    }).observe(document.body, { childList: true, subtree: true }));
  };

  await load();
  await raise('First', 3000, 'toast-group-show');
  await page.waitForTimeout(500);
  await raise('Second', 4000, 'toast-group-show');
  await page.waitForTimeout(500);
  const front = await page.evaluate(() => {
    const box = [...document.querySelectorAll('ui-toast-group > [data-flux-toast-dialog]')].pop().firstElementChild.getBoundingClientRect();
    return [box.x + 40, box.y + box.height / 2];
  });
  const stack = await hold(page, front);

  await load();
  await raise('Focus', 2000, 'toast-show');
  await page.waitForTimeout(400);
  await page.evaluate(() => [...document.querySelectorAll('[data-flux-toast-dialog]')].find(d => d.textContent.trim() === 'Focus').querySelector('button').focus());
  await page.waitForTimeout(3500);
  const { shown, gone } = await page.evaluate(() => window.__toasts);
  await context.close();
  return { ...stack, focused: gone.Focus - shown.Focus };
}

async function raskCountdown(browser) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  const timed = join(out, 'rask', 'toast-timed.html');
  const hooks = runtime('rask-dom.ts', 'rask-hooks.ts');
  // The runtime starts each countdown as it arrives, so that is when a toast "showed"; its press on the close
  // button is when it went.
  const load = async () => {
    await page.goto(pathToFileURL(timed).href, { waitUntil: 'load' });
    await page.mouse.move(5, 450);
    await page.evaluate(clock);
    await page.evaluate(() => document.addEventListener('click', e => {
      const dialog = e.target.closest?.('[data-rask-dismiss]')?.closest('[data-ui-toast-dialog]');
      if (dialog) { window.__went(dialog.textContent.trim()); dialog.remove(); }
    }, true));
    await page.addScriptTag({ content: hooks });
    await page.evaluate(() => {
      for (const dialog of document.querySelectorAll('[data-ui-toast-dialog]')) window.__toasts.shown[dialog.textContent.trim()] = window.__mark();
    });
  };

  await load();
  const front = await page.evaluate(() => {
    const box = document.querySelector('[data-ui-toast-group] > [data-ui-toast-dialog]').firstElementChild.getBoundingClientRect();
    return [box.x + 40, box.y + box.height / 2];
  });
  await page.waitForTimeout(1000);
  const stack = await hold(page, front);

  await load();
  await page.waitForTimeout(400);
  await page.evaluate(() => document.querySelector('[data-ui-toast] [data-rask-dismiss]').focus());
  await page.waitForTimeout(3500);
  const { shown, gone } = await page.evaluate(() => window.__toasts);
  await context.close();
  return { ...stack, focused: gone.Focus - shown.Focus };
}

async function measureFlux() {
  const browser = await chromium().launch();
  const result = { light: [], dark: [] };
  for (const [index, [name, scenario]] of Object.entries(SCENARIOS).entries()) {
    const schemes = await measurePage(browser, 'https://fluxui.dev/components/toast', join(fluxDir, name), (page, scheme) => raise(page, scheme, name, scenario));
    for (const scheme of ['light', 'dark']) {
      const example = schemes[scheme][0];
      if (!example) throw new Error(`flux parity: ${name} raised no toast on Flux's page (${scheme})`);
      result[scheme].push({ ...settle(example, scenario), index, section: name, ordinal: 0 });
    }

    console.log(`measured ${name}`);
  }

  await browser.close();
  return result;
}

// Runs before the measurement: raises the scenario's toast and makes its host the one thing measured.
async function raise(page, scheme, name, scenario) {
  const host = scenario.group ? 'ui-toast-group' : SINGLE;
  if (scenario.expanded) {
    // `expanded` is read when the group connects, so the page is given a group that has it from the start.
    await page.evaluate(() => {
      const group = document.querySelector('ui-toast-group');
      const expanded = group.cloneNode(true);
      expanded.setAttribute('expanded', '');
      group.replaceWith(expanded);
    });
    await page.waitForTimeout(300);
  }

  for (const detail of scenario.show ?? []) {
    await page.evaluate(({ detail, event }) => document.body.dispatchEvent(new CustomEvent(event, { detail: { duration: 0, ...detail }, bubbles: true })),
      { detail, event: scenario.group ? 'toast-group-show' : 'toast-show' });
    await page.waitForTimeout(300);
  }

  if (scenario.toast) await page.evaluate(toast => window.Flux.toast({ duration: 0, ...toast }), scenario.toast);
  await page.mouse.move(5, 450);
  await page.waitForTimeout(900);
  if (scenario.hover) {
    const front = await page.evaluate(() => {
      const box = [...document.querySelectorAll('ui-toast-group > [data-flux-toast-dialog]')].pop().firstElementChild.getBoundingClientRect();
      return [box.x + 40, box.y + box.height / 2];
    });
    await page.mouse.move(front[0], front[1]);
    await page.waitForTimeout(900);
  }

  screens.flux[scheme][name] = await page.evaluate(onScreen, { host, dialog: '[data-flux-toast-dialog]' });
  await page.evaluate(({ host, name, mark }) => {
    for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) wrapper.removeAttribute('data-preview-wrapper');
    const el = document.querySelector(host);
    el.setAttribute('data-preview-wrapper', '');
    el.setAttribute('data-section', name);
    el.setAttribute(mark, '');
  }, { host, name, mark: scenario.group ? 'data-flux-toast-group' : 'data-flux-toast' });
}

// Runs in the page. Where a toast is in the viewport: its host, and each card front to back.
function onScreen({ host, dialog }) {
  const box = el => { const r = el.getBoundingClientRect(); return [r.x, r.y, r.width, r.height].map(v => Math.round(v * 10) / 10); };
  const el = document.querySelector(host);
  const cards = [...el.querySelectorAll(`:scope > ${dialog}`)].map(d => ({
    text: d.textContent.trim().replace(/\s+/g, ' ').slice(0, 24), box: box(d.firstElementChild), shown: getComputedStyle(d).opacity !== '0', atomic: d.getAttribute('aria-atomic'), variant: d.getAttribute('data-variant'),
  }));
  return { host: box(el), role: el.getAttribute('role'), popover: el.getAttribute('popover'), open: el.matches(':popover-open'), cards };
}

// Flux's measurement, with the differences the header names put right.
function settle(example, scenario) {
  const byParent = new Map();
  for (const node of example.nodes) (byParent.get(node.parent) ?? byParent.set(node.parent, []).get(node.parent)).push(node);
  const kept = [];
  const walk = (node, depth, back) => {
    const children = byParent.get(node.id) ?? [];
    const leftover = node.style.display === 'none' || (node.tag === 'div' && !node.text && !children.length && node.box[3] === 0);
    if (leftover) return;
    kept.push({ ...node, tag: NATIVE[node.tag] ?? node.tag, style: { ...node.style, ...placed(depth, back, scenario) } });
    (depth === 0 && scenario.group ? [...children].reverse() : children).forEach((child, i) => walk(child, depth + 1, i));
  };
  walk(example.nodes[0], 0, 0);
  const ids = new Set(kept.map(node => node.id));
  return { ...example, nodes: kept, states: example.states.filter(state => ids.has(state.node)) };
}

// A toast of an open stack: the offset and paint order Flux's script wrote, which Rask's anchors replace.
function placed(depth, back, scenario) {
  if (!scenario.open || depth !== 1) return {};
  return { transform: 'matrix(1, 0, 0, 1, 0, 0)', ...(scenario.expanded ? { zIndex: String(5000 - back) } : {}) };
}

async function measureRaskScreens() {
  const browser = await chromium().launch();
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    const page = await context.newPage();
    await page.goto(pathToFileURL(raskPage).href, { waitUntil: 'networkidle' });
    await page.evaluate(() => document.fonts.ready);
    await page.evaluate(() => document.getAnimations().filter(a => a.effect?.getComputedTiming().endTime !== Infinity).forEach(a => a.finish()));
    for (const name of Object.keys(SCENARIOS)) {
      screens.rask[scheme][name] = await page.evaluate(({ name, onScreen }) =>
        new Function('args', `return (${onScreen})(args)`)({ host: `[data-section="${name}"] :is([data-ui-toast], [data-ui-toast-group])`, dialog: '[data-ui-toast-dialog]' }),
      { name, onScreen: onScreen.toString() });
    }

    await context.close();
  }

  await browser.close();
}

// The screen check: the same toast in the same place, announced the same way.
function compareScreens() {
  let failures = 0;
  const near = (a, b) => a.every((v, i) => Math.abs(v - b[i]) <= 0.6);
  for (const scheme of ['light', 'dark']) {
    for (const [name, scenario] of Object.entries(SCENARIOS)) {
      const theirs = screens.flux[scheme][name];
      const mine = screens.rask[scheme][name];
      const diffs = [];
      const cards = scenario.group ? [...theirs.cards].reverse() : theirs.cards;
      if (!near(theirs.host, mine.host)) diffs.push(`host at ${theirs.host} vs ${mine.host}`);
      for (const key of ['role', 'popover', 'open']) if (theirs[key] !== mine[key]) diffs.push(`${key}: ${theirs[key]} vs ${mine[key]}`);
      if (cards.length !== mine.cards.length) diffs.push(`toasts: ${cards.length} vs ${mine.cards.length}`);
      cards.forEach((card, i) => {
        const twin = mine.cards[i];
        if (!twin) return;
        if (card.shown !== twin.shown) diffs.push(`"${card.text}" shown: ${card.shown} vs ${twin.shown}`);
        if (card.shown && !near(card.box, twin.box)) diffs.push(`"${card.text}" at ${card.box} vs ${twin.box}`);
        for (const key of ['atomic', 'variant']) if (card[key] !== twin[key]) diffs.push(`"${card.text}" ${key}: ${card[key]} vs ${twin[key]}`);
      });
      failures += diffs.length ? 1 : 0;
      console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${scheme} ${name} on screen${diffs.length ? '' : `: ${cards.filter(c => c.shown).map(c => `[${c.box}]`).join(' ')}`}`);
      for (const diff of diffs) console.log(`       ${diff}`);
    }
  }

  console.log(failures ? `\nflux parity: the shown toast is elsewhere on screen in ${failures} case(s).` : '\nflux parity: every shown toast is where Flux puts it.');
  return failures;
}

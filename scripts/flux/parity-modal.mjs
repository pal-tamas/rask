#!/usr/bin/env node
// Holds Ui.Modal to Flux UI's modal with the dialog OPEN, which parity.mjs cannot see: a modal as loaded
// is a trigger and a dialog nobody is shown.
//
// For each example on fluxui.dev/components/modal and its twin on artifacts/flux-parity/rask/modal.html:
//   loaded     the page as it arrives, compared the way parity.mjs compares it;
//   open       the trigger pressed and the dialog settled — the dialog's subtree, where the viewport puts
//              it, its ::backdrop, data-open, the page lock and what holds focus, in light and in dark;
//   motion     the transitions that run as it opens and as it closes (property, duration, easing, values);
//   behaviour  Escape, a click outside, a press dragged out of or into the panel, a click on the panel's own
//              padding, the close button, a modal.close inside the content — which events each raises and
//              where focus goes back to.
// Then the same dialogs with the PAGE owning the state (artifacts/flux-parity/rask/modal-state.html, Rask's
// wire:model, which Flux's page does not show): the script changes data-rask-modal-open, as a render would,
// and the dialog that opens is held to the one Flux's trigger opened, and closed every way again.
//
// Last, the confirmation as an app writes it, which Flux's page does not show either: a question for a heading,
// no text under it, two buttons — with the example's min-w-[22rem] and as a BARE modal, without it. The script
// makes each case on Flux's page (CONFIRM, below) and holds artifacts/flux-parity/rask/modal-confirm.html to
// it: the open dialog in light and dark, then at 1280 and at 390 wide how wide it is, where the close button
// sits against its corner and how near the first line of the heading comes to that button. Ui.ConfirmLeave, on
// the same page, is not Flux's: its close button is held to Flux's corner and its question clear of it.
//
// What Flux does in script the kit asks the runtime for (data-rask-modal, data-rask-modal-open,
// data-rask-lock), so both Rask pages get the runtime's hooks (runtime.mjs). Flux's page has Flux's script.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-modal.mjs [--refresh] [--all]
//
// Flux's side is cached in artifacts/flux-parity/flux/modal/open.json and confirm.json; --refresh measures it again.
// Exit code 1 on any difference.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, measurePage, root, stateTargets, STYLES } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

const args = process.argv.slice(2);
const limit = args.includes('--all') ? Infinity : 12;
const out = join(root, 'artifacts', 'flux-parity');
const raskPage = join(out, 'rask', 'modal.html');
const statePage = join(out, 'rask', 'modal-state.html');
const confirmPage = join(out, 'rask', 'modal-confirm.html');
for (const page of [raskPage, statePage, confirmPage]) {
  if (existsSync(page)) continue;
  console.error(`flux parity: ${page} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

// parity.mjs's own tables, for the comparison at the foot of this file.
const IGNORED = new Set(['width', 'height']);
const NATIVE = {
  'ui-field': 'div', 'ui-label': 'label', 'ui-description': 'div', 'ui-legend': 'legend', 'ui-progress': 'div',
  'ui-table-scroll-area': 'div', 'ui-disclosure-group': 'div', 'ui-disclosure': 'details', 'ui-chart': 'div',
  'ui-button': 'button', 'ui-tooltip': 'div', 'ui-dropdown': 'div', 'ui-modal': 'div', 'ui-close': 'div',
};
const sameTag = (a, b) => (NATIVE[a.tag] ?? a.tag) === b.tag || (a.tag === 'button' && b.tag === 'summary');
const MISMARKED = {};
const EXTRA = [];
const OWN = new Set(['modal', 'modal-trigger', 'modal-close']);

const SCHEMES = ['light', 'dark'];
const TRIGGER = '[data-flux-modal-trigger] button, [data-ui-modal-trigger] button';
const CLOSE = 'dialog [data-flux-modal-close] button, dialog [data-ui-modal-close] button';
const INHERITED = ['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];
const LAYER = ['display', 'position', 'backgroundColor', 'opacity', 'backdropFilter', 'transform', 'transitionProperty', 'transitionDuration', 'transitionTimingFunction', 'transitionBehavior'];
const HOOKS = 'rask-hooks.ts';

// A Rask parity page as an app has it: the runtime's hooks, and — as Flux's page goes on below its last
// example — more page than the viewport, so that it scrolls and shows the scrollbar Flux's shows.
async function asInAnApp(page) {
  await withRuntime(page, HOOKS);
  await page.evaluate(() => { document.body.style.paddingBottom = '100vh'; });
}

// What only a dialog the page opens can be asked: the page changing its mind, and a reader closing a dialog
// the page still says is open.
const STATE_ONLY = {
  'the page says closed': 'closed; events: close; focus on nothing (body); page overflow visible',
  'closed by the reader, then the page says open again': 'open, data-open; events: none; focus on nothing (body); page overflow hidden',
};

// The cases of ModalConfirmParity, in its order. `bare` drops the width Flux's example hands the modal.
const QUESTION = 'Biztos elhagyod mentés nélkül az oldalt?';
const LONG = 'Biztos elhagyod mentés nélkül az oldalt, és eldobod, amit eddig beírtál az űrlapba?';
const CONFIRM = [
  { name: 'a bare modal asking a question', bare: true, heading: QUESTION },
  { name: 'a bare modal under a short heading', bare: true, heading: 'Delete project?' },
  { name: 'a confirmation asking a question', heading: QUESTION },
  { name: 'a bare modal asking a long question', bare: true, heading: LONG },
  { name: 'a confirmation asking a long question', heading: LONG },
];
const WIDTHS = [1280, 390];

// A browser that SHOWS its scrollbars, as a reader's does. Headless hides them, and there Flux's
// `scrollbar-gutter: stable` reserves 15px that nothing was using: its own dialog sits 7.5px off centre and its
// flyout 15px short of the edge. The kit keeps the gutter only where a scrollbar was taking room (rask-lock.ts),
// so the two agree where it matters — with the scrollbar there.
const browser = await chromium().launch({ ignoreDefaultArgs: ['--hide-scrollbars'] });
const fluxFile = join(out, 'flux', 'modal', 'open.json');
let flux;
if (existsSync(fluxFile) && !args.includes('--refresh')) {
  flux = JSON.parse(await readFile(fluxFile, 'utf8'));
} else {
  flux = await observe('https://fluxui.dev/components/modal', join(out, 'flux', 'modal'));
  await mkdir(join(out, 'flux', 'modal'), { recursive: true });
  await writeFile(fluxFile, JSON.stringify(flux));
}

const confirmation = flux.loaded.light.findIndex(example => example.section === 'confirmation');
const fluxConfirmFile = join(out, 'flux', 'modal', 'confirm.json');
let fluxConfirm;
if (existsSync(fluxConfirmFile) && !args.includes('--refresh')) {
  fluxConfirm = JSON.parse(await readFile(fluxConfirmFile, 'utf8'));
} else {
  fluxConfirm = await observeConfirm('https://fluxui.dev/components/modal', join(out, 'flux', 'modal'));
  await writeFile(fluxConfirmFile, JSON.stringify(fluxConfirm));
}

const rask = await observe(pathToFileURL(raskPage).href, join(out, 'rask', 'modal'), flux);
const state = await observeState(pathToFileURL(statePage).href, join(out, 'rask', 'modal-state'), flux);
const confirm = await observeConfirm(pathToFileURL(confirmPage).href, join(out, 'rask', 'modal-confirm'), flux);
const leave = await observeLeave(pathToFileURL(confirmPage).href, join(out, 'rask', 'modal-confirm'));
await browser.close();

let failures = 0;
const accepted = new Set();
const report = (label, diffs, notes = []) => {
  failures += diffs.length ? 1 : 0;
  console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ''}`);
  for (const note of notes) console.log(`       (${note})`);
  for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
  if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
};

const name = i => flux.loaded.light[i].section || 'intro';
for (const scheme of SCHEMES) {
  flux.loaded[scheme].forEach((theirs, i) => {
    const mine = rask.loaded[scheme][i];
    if (!mine) return report(`${scheme} ${name(i)} loaded`, ['Rask has no such example']);

    const notes = [];
    report(`${scheme} ${name(i)} loaded`, compareExample(theirs, mine, notes), notes);

    const a = flux.open[scheme][i];
    const b = rask.open[scheme][i];
    report(`${scheme} ${name(i)} open`, [...compareOpen(a.example, b.example), ...compareFacts(a.facts, b.facts)]);

    const c = state.open[scheme][i];
    report(`${scheme} ${name(i)} open by the page's state`, [...compareOpen(a.example, c.example), ...compareFacts(a.facts, c.facts)]);
  });
}

flux.motion.forEach((theirs, i) => report(`motion    ${name(i)}`, compareFacts(theirs, rask.motion[i])));
flux.behaviour.forEach((theirs, i) => report(`behaviour ${name(i)}`, compareFacts(theirs, rask.behaviour[i])));
// A dialog the page opened has no trigger to hand focus back to: every other word of Flux's answer holds.
const untriggered = (facts, mine) => Object.fromEntries(Object.entries(facts)
  .filter(([key]) => key in mine)
  .map(([key, value]) => [key, value.replace(/; focus on [^;]+/, '')]));
const unfocused = facts => Object.fromEntries(Object.entries(facts).map(([key, value]) => [key, value.replace(/; focus on [^;]+/, '')]));
flux.behaviour.forEach((theirs, i) => report(`behaviour ${name(i)}, the page's state`, [
  ...compareFacts(untriggered(theirs, state.behaviour[i]), unfocused(state.behaviour[i])),
  ...compareFacts(STATE_ONLY, state.only[i]),
]));

CONFIRM.forEach(({ name: asked }, k) => {
  for (const scheme of SCHEMES) {
    const a = fluxConfirm.open[scheme][k];
    const b = confirm.open[scheme][k];
    report(`${scheme} ${asked}, open`, [...compareOpen(a.example, b.example), ...compareFacts(a.facts, b.facts)]);
  }

  for (const width of WIDTHS) report(`${width}px ${asked}, its corner`, compareCorner(fluxConfirm.corner[width][k], confirm.corner[width][k]));
});
// The leave dialog is a confirmation: Flux's corner and Flux's width. Its own: the question clear of the button.
const asConfirmation = CONFIRM.findIndex(c => !c.bare);
const CLEAR = 'the first line is clear of the close button';
for (const width of WIDTHS) {
  for (const [asked, mine] of Object.entries(leave[width])) {
    const theirs = fluxConfirm.corner[width][asConfirmation];
    report(`${width}px the leave dialog asking ${asked}`, compareCorner(
      { 'close button': theirs['close button'], 'min-width | max-width': theirs['min-width | max-width'], [CLEAR]: true },
      { 'close button': mine['close button'], 'min-width | max-width': mine['min-width | max-width'], [CLEAR]: mine['first line ends before the close button'] >= 0 }));
  }
}

for (const note of accepted) console.log(`accepted: ${note}`);
console.log(failures ? `\nflux parity: modal differs in ${failures} check(s).` : '\nflux parity: modal matches Flux, loaded and open.');
process.exit(failures ? 1 : 0);

// Each Rask example is given the text its Flux twin inherits from the docs page, and the page the runtime.
function dress(like) {
  return async (page, scheme) => {
    if (!like) return;
    await page.evaluate(({ examples, INHERITED }) => {
      document.querySelectorAll('[data-preview-wrapper]').forEach((wrapper, i) => {
        for (const key of examples[i] ? INHERITED : []) wrapper.style[key] = examples[i].nodes[0].style[key];
      });
    }, { examples: like.loaded[scheme], INHERITED });
    await asInAnApp(page);
  };
}

// Everything this script compares, for one side. `like` is Flux's side, when measuring Rask's.
async function observe(url, shots, like) {
  const loaded = await measurePage(browser, url, join(shots, 'loaded'), dress(like));
  const open = await measureOpen(url, shots, like, loaded.light.length, (page, i) => press(page, i));

  const page = await walked(url, like);
  const motion = [];
  const behaviour = [];
  for (let i = 0; i < loaded.light.length; i++) {
    motion.push(await watchMotion(page, i));
    behaviour.push(await watchBehaviour(page, i));
  }

  await page.context().close();
  return { loaded, open, motion, behaviour };
}

// The page whose state opens its modals. What a render does is all the script does: one attribute.
async function observeState(url, shots, like) {
  const count = like.loaded.light.length;
  const open = await measureOpen(url, shots, like, count, (page, i) => say(page, i, true));

  const page = await walked(url, like);
  const behaviour = [];
  const only = [];
  for (let i = 0; i < count; i++) {
    const facts = await watchStateBehaviour(page, i);
    only.push(Object.fromEntries(Object.keys(STATE_ONLY).map(key => [key, facts[key]])));
    behaviour.push(Object.fromEntries(Object.entries(facts).filter(([key]) => !(key in STATE_ONLY))));
  }

  await page.context().close();
  return { open, behaviour, only };
}

// Every case of CONFIRM, for one side. On Flux's page each is made from the confirmation example; the Rask
// page is written with one example per case, and is dressed as that Flux example is.
async function observeConfirm(url, shots, like) {
  const at = k => (like ? k : confirmation);
  const prepare = (k, page, scheme) => (like ? dressAs(page, like.loaded[scheme][confirmation]) : page.evaluate(ask, { i: confirmation, ...CONFIRM[k] }));
  const open = { light: [], dark: [] };
  const corner = Object.fromEntries(WIDTHS.map(width => [width, []]));
  for (const k of CONFIRM.keys()) {
    const facts = {};
    const schemes = await measurePage(browser, url, join(shots, `confirm-${k}`), async (page, scheme) => {
      await prepare(k, page, scheme);
      await press(page, at(k));
      facts[scheme] = await page.evaluate(openFacts, { i: at(k), LAYER });
      await page.screenshot({ path: join(shots, `confirm-${k}-${scheme}.png`) });
    });
    for (const scheme of SCHEMES) open[scheme].push({ example: schemes[scheme][at(k)], facts: facts[scheme] });

    for (const width of WIDTHS) {
      const page = await sized(url, width);
      await prepare(k, page, 'light');
      await press(page, at(k));
      corner[width].push(await page.evaluate(cornerFacts, at(k)));
      await page.screenshot({ path: join(shots, `confirm-${k}-${width}.png`) });
      await page.context().close();
    }
  }

  return { open, corner };
}

// Ui.ConfirmLeave, the last example of the confirm page: closed in every render and opened in the browser
// with the asking form's message, which is all this does.
async function observeLeave(url, shots) {
  const facts = Object.fromEntries(WIDTHS.map(width => [width, {}]));
  for (const width of WIDTHS) {
    for (const [asked, question] of [['a question', QUESTION], ['a long question', LONG]]) {
      const page = await sized(url, width);
      await dressAs(page, flux.loaded.light[confirmation]);
      await page.evaluate(({ i, question }) => {
        const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
        dialog.querySelector('[data-rask-leave="message"]').textContent = question;
        dialog.showModal();
      }, { i: CONFIRM.length, question });
      await settled(page, CONFIRM.length, true);
      facts[width][asked] = await page.evaluate(cornerFacts, CONFIRM.length);
      await page.screenshot({ path: join(shots, `leave-${width}-${asked.replaceAll(' ', '-')}.png`) });
      await page.context().close();
    }
  }

  return facts;
}

// One light page of a given width, with its fonts in: a phone's is where a modal meets the viewport's edges.
async function sized(url, width) {
  const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme: 'light' });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  await page.evaluate(() => document.fonts.ready);
  return page;
}

// Every example of a Rask page given the text one Flux example inherits from the docs page, and the runtime.
async function dressAs(page, example) {
  await page.evaluate(({ style, INHERITED }) => {
    for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) for (const key of INHERITED) wrapper.style[key] = style[key];
  }, { style: example.nodes[0].style, INHERITED });
  await asInAnApp(page);
}

// Runs in Flux's page. Its confirmation, turned into the one a case names: the width its example hands the
// modal taken off for a bare one, the question in the heading, and no text under it.
function ask({ i, bare, heading }) {
  const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
  if (bare) dialog.classList.remove(...[...dialog.classList].filter(name => /^min-w-\[22rem\]!?$/.test(name)));
  dialog.querySelector('[data-flux-heading]').textContent = heading;
  dialog.querySelector('[data-flux-text]').remove();
}

// Runs in the page. An open dialog's width, its close button against its corner, and its heading against that button.
function cornerFacts(i) {
  const fix = v => Math.round(v * 10) / 10;
  const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
  const box = dialog.getBoundingClientRect();
  const own = getComputedStyle(dialog);
  const close = [...dialog.querySelectorAll('[data-flux-modal-close] button, [data-ui-modal-close] button')].pop().getBoundingClientRect();
  const range = document.createRange();
  range.selectNodeContents(dialog.querySelector('[data-flux-heading], [data-ui-heading]'));
  const lines = [...range.getClientRects()];
  return {
    'dialog size': `${fix(box.width)}x${fix(box.height)}`,
    'min-width | max-width': `${own.minWidth} | ${own.maxWidth}`,
    'close button': `${fix(close.width)}x${fix(close.height)}, ${fix(close.top - box.top)} from the top edge, ${fix(box.right - close.right)} from the right edge`,
    'heading lines': new Set(lines.map(line => Math.round(line.top))).size,
    'first line ends before the close button': fix(close.left - lines[0].right),
  };
}

// Each example measured with its own dialog open, in both schemes, and what no node of its tree states.
async function measureOpen(url, shots, like, count, show) {
  const open = { light: [], dark: [] };
  for (let i = 0; i < count; i++) {
    const facts = {};
    const schemes = await measurePage(browser, url, join(shots, `open-${i}`), async (page, scheme) => {
      await dress(like)(page, scheme);
      await show(page, i);
      facts[scheme] = await page.evaluate(openFacts, { i, LAYER });
      await page.screenshot({ path: join(shots, `open-${i}-${scheme}.png`) });
    });

    for (const scheme of SCHEMES) open[scheme].push({ example: schemes[scheme][i], facts: facts[scheme] });
  }

  return open;
}

// One light page to walk with pointer and keyboard.
async function walked(url, like) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: 'light' });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  if (like) await asInAnApp(page);
  return page;
}

async function press(page, i) {
  const wrapper = (await page.$$('[data-preview-wrapper]'))[i];
  await (await wrapper.$(TRIGGER)).click();
  await settled(page, i, true);
}

// What a render that changed its mind writes: the attribute, and nothing else.
async function say(page, i, open) {
  await page.evaluate(({ i, open }) => {
    document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog').setAttribute('data-rask-modal-open', String(open));
  }, { i, open });
  await settled(page, i, open);
}

// Waits on STATE, never on a delay: the dialog open (or closed), no transition left running on it or on its
// backdrop, and its box the same for three frames. A dialog that never gets there is measured as it is.
async function settled(page, i, open) {
  await page.waitForFunction(({ i, open }) => {
    const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
    if (dialog.open !== open) return false;
    if (document.getAnimations().some(a => a.effect?.target === dialog && a.playState === 'running')) return false;
    const box = JSON.stringify(dialog.getBoundingClientRect());
    const state = (window.__settled ??= { box: '', frames: 0 });
    state.frames = state.box === box ? state.frames + 1 : 0;
    state.box = box;
    return state.frames >= 3;
  }, { i, open }, { polling: 'raf', timeout: 5000 }).catch(() => {});
  // The focus placeholder leaves 150ms after the dialog opens, on both pages.
  if (open) await page.waitForFunction(i => !document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog :focus'), i, { timeout: 2000 }).catch(() => {});
}

// Runs in the page. What an open dialog is that no node of the example's tree states.
function openFacts({ i, LAYER }) {
  const dialog = document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog');
  const box = dialog.getBoundingClientRect();
  const own = getComputedStyle(dialog);
  const backdrop = getComputedStyle(dialog, '::backdrop');
  const html = getComputedStyle(document.documentElement);
  const active = document.activeElement;
  return {
    'dialog open': dialog.open,
    'dialog :modal (top layer)': dialog.matches(':modal'),
    'dialog data-open': dialog.hasAttribute('data-open'),
    'dialog box in the viewport': [box.x, box.y, box.width, box.height].map(v => Math.round(v * 10) / 10).join(' '),
    'dialog names itself': dialog.getAttribute('data-modal') !== null,
    'dialog transition': `${own.transitionProperty} ${own.transitionDuration} ${own.transitionTimingFunction} ${own.transitionBehavior}`,
    ...Object.fromEntries(LAYER.map(key => [`::backdrop ${key}`, backdrop[key]])),
    'page lock (overflow | pointer-events | scrollbar-gutter)': `${html.overflowY} | ${html.pointerEvents} | ${html.scrollbarGutter}`,
    'focus rests on': active === document.body ? 'nothing (body)' : dialog.contains(active) ? `<${active.tagName.toLowerCase()}> in the dialog` : 'the page behind',
  };
}

// The transitions the dialog and its backdrop start on the way in and on the way out.
async function watchMotion(page, i) {
  const wrapper = (await page.$$('[data-preview-wrapper]'))[i];
  const running = () => wrapper.evaluate(w => {
    const dialog = w.querySelector('dialog');
    return document.getAnimations()
      .filter(a => a.effect.target === dialog && a.transitionProperty)
      .map(a => {
        // A transform as the matrix it is: `translateX(50px)` and `translate(50px)` are one movement.
        const frames = a.effect.getKeyframes()
          .map(frame => frame[a.transitionProperty.replace(/-(\w)/g, (_, c) => c.toUpperCase())])
          .map(value => (a.transitionProperty === 'transform' && value && value !== 'none' ? new DOMMatrixReadOnly(value).toString() : value));
        const timing = a.effect.getComputedTiming();
        return [`${a.effect.pseudoElement ?? 'dialog'} ${a.transitionProperty}`, `${timing.duration}ms ${timing.easing} +${timing.delay}ms: ${frames.join(' -> ')}`];
      })
      // A property that starts a transition to the value it already has says nothing about the component.
      .filter(([, value]) => !/: (\S.*) -> \1$/.test(value) && !value.includes('undefined'))
      .sort(([x], [y]) => x.localeCompare(y));
  });

  await (await wrapper.$(TRIGGER)).click();
  const entering = await running();
  await settled(page, i, true);
  await page.keyboard.press('Escape');
  const leaving = await running();
  await settled(page, i, false);
  return Object.fromEntries([...entering.map(([k, v]) => [`in  ${k}`, v]), ...leaving.map(([k, v]) => [`out ${k}`, v])]);
}

// Hears the dialog's own events — cancel, close — and says where things stand after each step.
async function listen(page, i) {
  const wrapper = (await page.$$('[data-preview-wrapper]'))[i];
  await wrapper.evaluate(w => {
    const dialog = w.querySelector('dialog');
    window.__events = [];
    for (const type of ['cancel', 'close']) dialog.addEventListener(type, () => window.__events.push(type));
  });
  const state = () => wrapper.evaluate((w, TRIGGER) => {
    const dialog = w.querySelector('dialog');
    const events = window.__events.join(' then ') || 'none';
    window.__events = [];
    const active = document.activeElement;
    const focus = active === w.querySelector(TRIGGER) ? 'the trigger' : active === document.body ? 'nothing (body)' : dialog.contains(active) ? 'the dialog' : 'elsewhere';
    const html = getComputedStyle(document.documentElement);
    return `${dialog.open ? 'open' : 'closed'}${dialog.hasAttribute('data-open') ? ', data-open' : ''}; events: ${events}; focus on ${focus}; page overflow ${html.overflowY}`;
  }, TRIGGER);
  // After a step that may close it: closed and at rest, or still open a moment later.
  const after = async () => {
    await page.waitForFunction(i => !document.querySelectorAll('[data-preview-wrapper]')[i].querySelector('dialog').open, i, { timeout: 600 }).catch(() => {});
    if (!(await wrapper.evaluate(w => w.querySelector('dialog').open))) await settled(page, i, false);
    return state();
  };
  return { wrapper, state, after };
}

// The steps both ways of opening share. `open()` opens the dialog again when a step closed it.
async function dismissals(page, i, { wrapper, after }, open, facts) {
  await open();
  await page.keyboard.press('Escape');
  facts['Escape'] = await after();

  await open();
  await page.mouse.click(20, 450);
  facts['click outside'] = await after();

  await open();
  const box = await wrapper.evaluate(w => { const r = w.querySelector('dialog').getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; });
  const inside = [box[0] + 6, box[1] + box[3] / 2];
  const outside = [box[0] > 100 ? 20 : box[0] + box[2] + 40, 450];
  const drag = async (from, to) => {
    await page.mouse.move(...from);
    await page.mouse.down();
    await page.mouse.move(...to);
    await page.mouse.up();
    return after();
  };
  facts['a press dragged from the panel out'] = await drag(inside, outside);
  await open();
  facts['a press dragged from outside onto the panel'] = await drag(outside, inside);

  await open();
  await page.mouse.click(...inside);
  facts["click on the panel's padding"] = await after();
  const buttons = await wrapper.$$(CLOSE);
  await open();
  await buttons.at(-1).click();
  facts['close button'] = await after();

  if (buttons.length > 1) {
    await open();
    await buttons[0].click();
    facts['a modal.close in the content'] = await after();
  }

  await open();
  const before = await page.evaluate(() => scrollY);
  await page.mouse.move(20, 450);
  await page.mouse.wheel(0, 300);
  await page.waitForTimeout(200);   // a wheel is answered a frame or two later, and "nothing moved" has no state to wait on
  facts['wheel over the page behind'] = (await page.evaluate(() => scrollY)) === before ? 'the page does not scroll' : 'the page scrolls';
  await page.keyboard.press('Escape');
  await after();
}

// What each way of closing it does, with a trigger to open it and to hand focus back to.
async function watchBehaviour(page, i) {
  const heard = await listen(page, i);
  const { wrapper, state } = heard;
  const trigger = await wrapper.$(TRIGGER);
  const isOpen = () => wrapper.evaluate(w => w.querySelector('dialog').open);
  const open = async () => {
    if (await isOpen()) return;
    await trigger.focus();
    await page.keyboard.press('Enter');
    await settled(page, i, true);
    await wrapper.evaluate(() => { window.__events = []; });
  };
  const facts = {};

  await open();
  facts['opened from the keyboard'] = await state();
  await page.keyboard.press('Tab');
  facts['first Tab'] = await wrapper.evaluate(w => (w.querySelector('dialog').contains(document.activeElement) ? 'a control in the dialog' : 'outside the dialog'));
  await page.keyboard.press('Shift+Tab');
  await page.keyboard.press('Shift+Tab');
  facts['Shift+Tab past the first control'] = await wrapper.evaluate((w, CLOSE) => ([...w.querySelectorAll(CLOSE)].pop() === document.activeElement ? 'the close button, the last control' : 'elsewhere'), CLOSE);
  await dismissals(page, i, heard, open, facts);
  return facts;
}

async function watchStateBehaviour(page, i) {
  const heard = await listen(page, i);
  const { wrapper, state, after } = heard;
  const isOpen = () => wrapper.evaluate(w => w.querySelector('dialog').open);
  // A page that heard `close` says "false"; opening again is its next render saying "true".
  const open = async () => {
    if (await isOpen()) return;
    await say(page, i, false);
    await say(page, i, true);
    await wrapper.evaluate(() => { window.__events = []; });
  };
  const facts = {};

  await dismissals(page, i, heard, open, facts);
  await open();
  await say(page, i, false);
  facts['the page says closed'] = await after();
  await open();
  await page.keyboard.press('Escape');
  await after();
  await open();
  facts['closed by the reader, then the page says open again'] = await state();
  await say(page, i, false);
  return facts;
}

function compareFacts(theirs, mine) {
  return [...new Set([...Object.keys(theirs), ...Object.keys(mine ?? {})])]
    .filter(key => !same(String(theirs[key]), String(mine?.[key])))
    .map(key => `${key}: ${theirs[key] ?? '(absent)'} vs ${mine?.[key] ?? '(absent)'}`);
}

// Lengths to the 0.6px a box is held to everywhere else.
function compareCorner(theirs, mine) {
  const NUMBER = /-?\d+(\.\d+)?/g;
  const near = (x, y) => {
    const [a, b] = [x, y].map(v => (String(v).match(NUMBER) ?? []).map(Number));
    return String(x).replace(NUMBER, '#') === String(y).replace(NUMBER, '#') && a.every((n, i) => Math.abs(n - b[i]) <= 0.6);
  };
  return Object.keys(theirs)
    .filter(key => !near(theirs[key], mine?.[key]))
    .map(key => `${key}: ${theirs[key]} vs ${mine?.[key] ?? '(absent)'}`);
}

// The open dialog, compared from the dialog down: where the page happens to put the element that holds it
// says nothing about a box the viewport places.
function compareOpen(theirs, mine) {
  const find = (example, prefix) => {
    const holder = example.nodes.find(n => `${prefix}modal` in n.attrs);
    return [holder, example.nodes.find(n => n.parent === holder?.id && n.tag === 'dialog')];
  };
  const [a, da] = find(theirs, 'data-flux-');
  const [b, db] = find(mine, 'data-ui-');
  if (!da || !db) return [`dialog: ${da ? 'only in Flux' : db ? 'only in Rask' : 'in neither'}`];

  const diffs = [];
  if (!sameTag(a, b)) diffs.push(`modal: tag <${a.tag}> vs <${b.tag}>`);
  compareStyles(a.style, b.style, 'modal', diffs);
  compareTree(theirs, da, mine, db, da, db, 'dialog', diffs);
  return diffs;
}

// ---------------------------------------------------------------------------------------------------------
// parity.mjs's comparison. That script runs as it is imported, so it cannot lend these; they are kept line
// for line with it, and belong in a module. The one thing added is `accepted`, in compareLook.

function compareExample(theirs, mine, notes) {
  const a = tops(theirs, 'data-flux-');
  const b = tops(mine, 'data-ui-');
  const named = (nodes, prefix, name) => nodes.filter(n => mark(n, prefix) === name);
  const diffs = [];
  for (const name of new Set([...a.map(n => mark(n, 'data-flux-')), ...b.map(n => mark(n, 'data-ui-'))])) {
    const x = named(a, 'data-flux-', name);
    const y = named(b, 'data-ui-', name);
    if (!OWN.has(name) && !name.startsWith('modal-') && y.length === 0) {
      notes.push(`${name} ×${x.length}: a stand-in on the Rask page, not compared`);
      continue;
    }

    if (x.length !== y.length) {
      diffs.push(`marked nodes: Flux has ${x.length} ${name}, Rask has ${y.length}`);
      continue;
    }

    x.forEach((node, i) => compareTree(theirs, node, mine, y[i], node, y[i], `${name}[${i}]${node.text ? ` "${node.text.slice(0, 16)}"` : ''}`, diffs));
  }

  return diffs;
}

function tops(example, prefix) {
  const byId = new Map(example.nodes.map(n => [n.id, n]));
  const marked = n => n.tag in MISMARKED || Object.keys(n.attrs).some(k => k.startsWith(prefix));
  const hasMarkedAncestor = n => {
    for (let p = byId.get(n.parent); p; p = byId.get(p.parent)) if (marked(p)) return true;
    return false;
  };
  return example.nodes.filter(n => marked(n) && !hasMarkedAncestor(n));
}

function mark(node, prefix) {
  if (MISMARKED[node.tag]) return MISMARKED[node.tag];
  const names = Object.keys(node.attrs).filter(k => k.startsWith(prefix)).map(k => k.slice(prefix.length));
  return names.sort((x, y) => x.length - y.length)[0] ?? node.tag;
}

function compareTree(theirs, a, mine, b, rootA, rootB, where, diffs, free = '') {
  const skip = b.attrs['data-parity-skip'];
  const standIn = skip === '';
  const own = !standIn && skip !== 'self';
  if (skip === 'width' || skip === 'height') free = skip;
  if (!sameTag(a, b) && own) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text && a !== rootA && own) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);

  const differs = (x, y) => Math.abs(x - y) > 0.6;
  const held = [free !== 'width', free !== 'height'];
  if ((held[0] && differs(a.box[2], b.box[2])) || (held[1] && differs(a.box[3], b.box[3]))) {
    diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  }

  const boxed = (shown(theirs, a) || shown(mine, b)) && [a, b].some(n => n.box[2] > 0 || n.box[3] > 0);
  const anchored = rootA.style.display !== 'contents' || rootB.style.display !== 'contents';
  if (a !== rootA && boxed && anchored) {
    const off = (n, r, i) => n.box[i] - r.box[i];
    if ([0, 1].some(i => held[i] && differs(off(a, rootA, i), off(b, rootB, i)))) {
      diffs.push(`${where}: offset ${fix(off(a, rootA, 0))},${fix(off(a, rootA, 1))} vs ${fix(off(b, rootB, 0))},${fix(off(b, rootB, 1))}`);
    }
  }

  if (standIn) return;
  if (own) compareLook(theirs, a, mine, b, where, diffs);

  const kids = (example, n) => example.nodes.filter(c => c.parent === n.id && c.tag !== 'template' && !EXTRA.some(name => name in c.attrs));
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => compareTree(theirs, child, mine, cb[i], rootA, rootB, `${where} > ${child.tag}[${i}]`, diffs, free));
}

function compareLook(theirs, a, mine, b, where, diffs) {
  compareStyles(a.style, b.style, where, diffs);
  for (const pseudo of ['::before', '::after']) {
    if (!a[pseudo] !== !b[pseudo]) diffs.push(`${where}${pseudo}: ${a[pseudo] ? 'only in Flux' : 'only in Rask'}`);
    else if (a[pseudo]) compareStyles(a[pseudo], b[pseudo], `${where}${pseudo}`, diffs);
  }

  const moves = n => JSON.stringify(n.animations ?? []).replaceAll('"name":"flux-', '"name":"ui-');
  if ('data-ui-focus-placeholder' in b.attrs) {
    // Flux's script takes the focus placeholder away once the dialog has focus; Rask.Ui ships no script, and
    // its stylesheet does it with an animation. Each ends with the placeholder gone, which IS compared.
    accepted.add('the focus placeholder leaves by a CSS animation (ui-modal-placeholder) where Flux removes it from script');
  } else if (moves(a) !== moves(b)) {
    diffs.push(`${where}: animations: ${moves(a)} vs ${moves(b)}`);
  }

  if (!measured(theirs).has(a.id) || !measured(mine).has(b.id)) return;
  for (const state of ['hover', 'active', 'focus-visible']) {
    const x = theirs.states.find(s => s.node === a.id && s.state === state)?.changed ?? {};
    const y = mine.states.find(s => s.node === b.id && s.state === state)?.changed ?? {};
    for (const key of new Set([...Object.keys(x), ...Object.keys(y)])) {
      if (undrawn(key, { ...a.style, ...x }, { ...b.style, ...y })) continue;
      if (!same(x[key], y[key])) diffs.push(`${where}:${state} ${key}: ${x[key] ?? '(unchanged)'} vs ${y[key] ?? '(unchanged)'}`);
    }
  }
}

function measured(example) {
  return (example.measured ??= new Set(stateTargets(example.nodes)));
}

function shown(example, node) {
  for (let n = node; n; n = example.nodes.find(p => p.id === n.parent)) if (n.style.display === 'none') return false;
  return true;
}

function compareStyles(x, y, where, diffs) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || undrawn(key, x, y) || same(x[key], y[key])) continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

function undrawn(key, x, y) {
  const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
  if (side) return x[`border${side}Width`] === '0px' && y[`border${side}Width`] === '0px';
  return key === 'outlineColor' && x.outlineStyle === 'none' && y.outlineStyle === 'none';
}

function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  const round = v => v.replace(/(okl(?:ch|ab)\([^)]*?) none\)/g, '$1 0)')
    .replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000));
  if (round(x) === round(y)) return true;
  const numbers = [];
  const shape = v => round(v).replace(/-?\d+(\.\d+)?(e-?\d+)?/g, n => (numbers.push(Number(n)), '#'));
  if (shape(x) !== shape(y)) return false;
  const half = numbers.length / 2;
  return numbers.slice(0, half).every((n, i) => Math.abs(n - numbers[half + i]) <= 0.0011);
}

function fix(v) {
  return Math.round(v * 10) / 10;
}

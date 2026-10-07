#!/usr/bin/env node
// Holds Ui.Modal to Flux UI's modal with the dialog OPEN, which parity.mjs cannot see: a modal as loaded
// is a trigger and a dialog nobody is shown.
//
// For each example on fluxui.dev/components/modal and its twin on artifacts/flux-parity/rask/modal.html:
//   loaded     the page as it arrives, compared the way parity.mjs compares it;
//   open       the trigger pressed and the dialog settled — the dialog's subtree, where the viewport puts
//              it, its ::backdrop, the scroll lock and what holds focus, in light and in dark;
//   motion     the transitions that run as it opens and as it closes (property, duration, easing, values);
//   behaviour  Escape, a click outside, a click on the panel's own padding, the close button, a
//              modal.close inside the content — which events each raises and where focus goes back to.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-modal.mjs [--refresh] [--all]
//
// Flux's side is cached in artifacts/flux-parity/flux/modal/open.json; --refresh measures it again.
// Exit code 1 on any difference.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, measurePage, root, STYLES } from './lib.mjs';

const args = process.argv.slice(2);
const limit = args.includes('--all') ? Infinity : 12;
const out = join(root, 'artifacts', 'flux-parity');
const raskPage = join(out, 'rask', 'modal.html');
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

// parity.mjs's own tables, for the comparison at the foot of this file.
const IGNORED = new Set(['width', 'height']);
const NATIVE = { 'ui-modal': 'div', 'ui-close': 'div' };
const UA_POPOVER = { position: 'fixed', overflowX: 'auto', overflowY: 'auto' };
const hidden = new WeakSet();

const SCHEMES = ['light', 'dark'];

const TRIGGER = '[data-flux-modal-trigger] button, [data-ui-modal-trigger] button';
const CLOSE = 'dialog [data-flux-modal-close] button, dialog [data-ui-modal-close] button';
const INHERITED = ['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];
const LAYER = ['display', 'position', 'backgroundColor', 'opacity', 'backdropFilter', 'transform', 'transitionProperty', 'transitionDuration', 'transitionTimingFunction', 'transitionBehavior'];

const browser = await chromium().launch();
const fluxFile = join(out, 'flux', 'modal', 'open.json');
let flux;
if (existsSync(fluxFile) && !args.includes('--refresh')) {
  flux = JSON.parse(await readFile(fluxFile, 'utf8'));
} else {
  flux = await observe('https://fluxui.dev/components/modal', join(out, 'flux', 'modal'));
  await mkdir(join(out, 'flux', 'modal'), { recursive: true });
  await writeFile(fluxFile, JSON.stringify(flux));
}

const rask = await observe(pathToFileURL(raskPage).href, join(out, 'rask', 'modal'), flux);
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

for (const scheme of SCHEMES) {
  flux.loaded[scheme].forEach((theirs, i) => {
    const mine = rask.loaded[scheme][i];
    const name = theirs.section || 'intro';
    if (!mine) return report(`${scheme} ${name} loaded`, ['Rask has no such example']);

    const notes = [];
    report(`${scheme} ${name} loaded`, compareExample(theirs, mine, notes), notes);

    const a = flux.open[scheme][i];
    const b = rask.open[scheme][i];
    report(`${scheme} ${name} open`, [...compareOpen(a.example, b.example), ...compareFacts(a.facts, b.facts)]);
  });
}

flux.motion.forEach((theirs, i) => report(`motion    ${flux.loaded.light[i].section || 'intro'}`, compareFacts(theirs, rask.motion[i])));
flux.behaviour.forEach((theirs, i) => report(`behaviour ${flux.loaded.light[i].section || 'intro'}`, compareFacts(theirs, rask.behaviour[i])));

for (const note of accepted) console.log(`accepted: ${note}`);
console.log(failures ? `\nflux parity: modal differs in ${failures} check(s).` : '\nflux parity: modal matches Flux, loaded and open.');
process.exit(failures ? 1 : 0);

// Everything this script compares, for one side. `like` is Flux's side, when measuring Rask's: each Rask
// example is given the text its Flux twin inherits from the docs page before anything is measured.
async function observe(url, shots, like) {
  const dress = (page, scheme) => like && page.evaluate(({ examples, INHERITED }) => {
    document.querySelectorAll('[data-preview-wrapper]').forEach((wrapper, i) => {
      for (const key of examples[i] ? INHERITED : []) wrapper.style[key] = examples[i].nodes[0].style[key];
    });
  }, { examples: like.loaded[scheme], INHERITED });

  const loaded = await measurePage(browser, url, join(shots, 'loaded'), dress);
  const open = { light: [], dark: [] };
  for (let i = 0; i < loaded.light.length; i++) {
    const facts = {};
    const schemes = await measurePage(browser, url, join(shots, `open-${i}`), async (page, scheme) => {
      await dress(page, scheme);
      await press(page, i);
      facts[scheme] = await page.evaluate(openFacts, { i, LAYER });
      await page.screenshot({ path: join(shots, `open-${i}-${scheme}.png`) });
      // Time held still for what measurePage does next. It forces :hover and :focus-visible on each node
      // and reads the style at once, and a dialog transitions `all`: read in flight, a forced state is
      // wherever the clock caught it. The durations themselves are in the facts above, and in `motion`.
      await page.addStyleTag({ content: '*,::before,::after,::backdrop{transition-duration:0s!important}' });
    });
    for (const scheme of SCHEMES) open[scheme].push({ example: schemes[scheme][i], facts: facts[scheme] });
  }

  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: 'light' });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  const motion = [];
  const behaviour = [];
  for (let i = 0; i < loaded.light.length; i++) {
    motion.push(await watchMotion(page, i));
    behaviour.push(await watchBehaviour(page, i));
  }

  await context.close();
  return { loaded, open, motion, behaviour };
}

async function press(page, i) {
  const wrapper = (await page.$$('[data-preview-wrapper]'))[i];
  await (await wrapper.$(TRIGGER)).click();
  // Past the 150ms it takes to arrive, so nothing is measured in flight.
  await page.waitForTimeout(450);
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
    'dialog box in the viewport': [box.x, box.y, box.width, box.height].map(v => Math.round(v * 10) / 10).join(' '),
    'dialog names itself': dialog.getAttribute('data-modal') !== null,
    'dialog transition': `${own.transitionProperty} ${own.transitionDuration} ${own.transitionTimingFunction} ${own.transitionBehavior}`,
    ...Object.fromEntries(LAYER.map(key => [`::backdrop ${key}`, backdrop[key]])),
    'page overflow': html.overflowY,
    'page scrollbar-gutter': html.scrollbarGutter,
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
  await page.waitForTimeout(450);
  await page.keyboard.press('Escape');
  const leaving = await running();
  await page.waitForTimeout(450);
  return Object.fromEntries([...entering.map(([k, v]) => [`in  ${k}`, v]), ...leaving.map(([k, v]) => [`out ${k}`, v])]);
}

// What each way of closing it does. Events are the dialog's own: cancel, close.
async function watchBehaviour(page, i) {
  const wrapper = (await page.$$('[data-preview-wrapper]'))[i];
  const trigger = await wrapper.$(TRIGGER);
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
    return `${dialog.open ? 'open' : 'closed'}; events: ${events}; focus on ${focus}; page overflow ${getComputedStyle(document.documentElement).overflowY}`;
  }, TRIGGER);
  const open = async () => {
    await trigger.focus();
    await page.keyboard.press('Enter');
    await page.waitForTimeout(450);
    await wrapper.evaluate(() => { window.__events = []; });
  };
  const settle = () => page.waitForTimeout(350);
  const facts = {};

  await open();
  facts['opened from the keyboard'] = await state();
  await page.keyboard.press('Tab');
  facts['first Tab'] = await wrapper.evaluate(w => (w.querySelector('dialog').contains(document.activeElement) ? 'a control in the dialog' : 'outside the dialog'));
  await page.keyboard.press('Shift+Tab');
  await page.keyboard.press('Shift+Tab');
  facts['Shift+Tab past the first control'] = await wrapper.evaluate((w, CLOSE) => ([...w.querySelectorAll(CLOSE)].pop() === document.activeElement ? 'the close button, the last control' : 'elsewhere'), CLOSE);
  await page.keyboard.press('Escape');
  await settle();
  facts['Escape'] = await state();

  await open();
  await page.mouse.click(20, 450);
  await settle();
  facts['click outside'] = await state();

  await open();
  const box = await wrapper.evaluate(w => { const r = w.querySelector('dialog').getBoundingClientRect(); return [r.x, r.y, r.height]; });
  await page.mouse.click(box[0] + 6, box[1] + box[2] / 2);
  await settle();
  facts["click on the panel's padding"] = await state();
  const buttons = await wrapper.$$(CLOSE);
  await buttons.at(-1).click();
  await settle();
  facts['close button'] = await state();

  if (buttons.length > 1) {
    await open();
    await buttons[0].click();
    await settle();
    facts['a modal.close in the content'] = await state();
  }

  await open();
  const before = await page.evaluate(() => scrollY);
  await page.mouse.move(20, 450);
  await page.mouse.wheel(0, 300);
  await settle();
  facts['wheel over the page behind'] = (await page.evaluate(() => scrollY)) === before ? 'the page does not scroll' : 'the page scrolls';
  await page.keyboard.press('Escape');
  await settle();
  return facts;
}

function compareFacts(theirs, mine) {
  return [...new Set([...Object.keys(theirs), ...Object.keys(mine ?? {})])]
    .filter(key => !same(String(theirs[key]), String(mine?.[key])))
    .map(key => `${key}: ${theirs[key] ?? '(absent)'} vs ${mine?.[key] ?? '(absent)'}`);
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
  if ((NATIVE[a.tag] ?? a.tag) !== b.tag) diffs.push(`modal: tag <${a.tag}> vs <${b.tag}>`);
  compareStyles(a.style, b.style, 'modal', diffs);
  compareTree(theirs, da, mine, db, da, db, 'dialog', diffs);
  return diffs;
}

// ---------------------------------------------------------------------------------------------------------
// parity.mjs's comparison. That script runs as it is imported, so it cannot lend these; they are kept line
// for line with it, and belong in lib.mjs. The one thing added is `accepted`, below.

function compareExample(theirs, mine, notes) {
  const a = tops(theirs, 'data-flux-');
  const b = tops(mine, 'data-ui-');
  const named = (nodes, prefix, name) => nodes.filter(n => mark(n, prefix) === name);
  const diffs = [];
  for (const name of new Set([...a.map(n => mark(n, 'data-flux-')), ...b.map(n => mark(n, 'data-ui-'))])) {
    const x = named(a, 'data-flux-', name);
    const y = named(b, 'data-ui-', name);
    if (!name.startsWith('modal') && y.length === 0) {
      notes.push(`${name} ×${x.length}: a stand-in on the Rask page, not compared`);
      continue;
    }

    if (x.length !== y.length) {
      diffs.push(`marked nodes: Flux has ${x.length} ${name}, Rask has ${y.length}`);
      continue;
    }

    x.forEach((node, i) => compareTree(theirs, node, mine, y[i], node, y[i], `${name}[${i}]`, diffs));
  }

  return diffs;
}

function tops(example, prefix) {
  const byId = new Map(example.nodes.map(n => [n.id, n]));
  const marked = n => Object.keys(n.attrs).some(k => k.startsWith(prefix));
  const hasMarkedAncestor = n => {
    for (let p = byId.get(n.parent); p; p = byId.get(p.parent)) if (marked(p)) return true;
    return false;
  };
  return example.nodes.filter(n => marked(n) && !hasMarkedAncestor(n));
}

function mark(node, prefix) {
  return Object.keys(node.attrs).find(k => k.startsWith(prefix))?.slice(prefix.length) ?? node.tag;
}

function compareTree(theirs, a, mine, b, rootA, rootB, where, diffs) {
  const standIn = 'data-parity-skip' in b.attrs;
  if ((NATIVE[a.tag] ?? a.tag) !== b.tag && !standIn) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text && a !== rootA && !standIn) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);

  const size = i => Math.abs(a.box[i] - b.box[i]) > 0.6;
  if (size(2) || size(3)) diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  const displayed = !hidden.has(a) && (a.style.display !== 'none' || b.style.display !== 'none');
  const anchored = rootA.style.display !== 'contents';
  if (a !== rootA && displayed && anchored) {
    const off = (n, r, i) => n.box[i] - r.box[i];
    if (Math.abs(off(a, rootA, 0) - off(b, rootB, 0)) > 0.6 || Math.abs(off(a, rootA, 1) - off(b, rootB, 1)) > 0.6) {
      diffs.push(`${where}: offset ${fix(off(a, rootA, 0))},${fix(off(a, rootA, 1))} vs ${fix(off(b, rootB, 0))},${fix(off(b, rootB, 1))}`);
    }
  }

  if (standIn) return;
  const closedPopover = 'popover' in b.attrs && a.style.display === 'none' && b.style.display === 'none';
  compareStyles(a.style, closedPopover ? { ...b.style, ...pick(a.style, b.style, UA_POPOVER) } : b.style, where, diffs);
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

  for (const state of ['hover', 'active', 'focus-visible']) {
    const x = theirs.states.find(s => s.node === a.id && s.state === state)?.changed ?? {};
    const y = mine.states.find(s => s.node === b.id && s.state === state)?.changed ?? {};
    for (const key of new Set([...Object.keys(x), ...Object.keys(y)])) {
      if (undrawn(key, { ...a.style, ...x }, { ...b.style, ...y })) continue;
      if (!same(x[key], y[key])) diffs.push(`${where}:${state} ${key}: ${x[key] ?? '(unchanged)'} vs ${y[key] ?? '(unchanged)'}`);
    }
  }

  const kids = (example, n) => example.nodes.filter(c => c.parent === n.id);
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => {
    if (!displayed) hidden.add(child);
    compareTree(theirs, child, mine, cb[i], rootA, rootB, `${where} > ${child.tag}[${i}]`, diffs);
  });
}

function pick(theirs, mine, rule) {
  return Object.fromEntries(Object.keys(rule).filter(key => mine[key] === rule[key]).map(key => [key, theirs[key]]));
}

function compareStyles(x, y, where, diffs) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || same(x[key], y[key])) continue;
    if (undrawn(key, x, y)) continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

function undrawn(key, x, y) {
  const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
  return Boolean(side) && x[`border${side}Width`] === '0px' && y[`border${side}Width`] === '0px';
}

function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  const round = v => v.replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000));
  return round(x) === round(y);
}

function fix(v) {
  return Math.round(v * 10) / 10;
}

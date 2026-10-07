#!/usr/bin/env node
// Holds Ui.Select's OPEN listbox to Flux UI's live documentation. parity.mjs measures every example as
// loaded, where a custom select's options are not displayed; this opens each one — on fluxui.dev and on the
// page FluxParityPages wrote — and compares what is then on screen:
//   - the trigger and the whole popup subtree: tag, box, computed styles, pseudo-elements;
//   - where the popup sits against its trigger (every box is measured from the trigger's corner);
//   - a row hovered, and a row pressed.
//
// The comparison is parity.mjs's, copied to its minimum: that file runs on import and exports nothing yet
// (see "Open work" in .claude/skills/flux-component/SKILL.md). The rules that are this page's own are named
// where they are applied.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-select.mjs                          # every custom example, light and dark
//         node scripts/flux/parity-select.mjs --refresh                # re-measure Flux
//         node scripts/flux/parity-select.mjs --section combobox --all # one section, every difference
//
// Exit code 1 on any difference. The measurements land in artifacts/flux-parity/{flux,rask}/select-open/.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root, STYLES } from './lib.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
const out = join(root, 'artifacts', 'flux-parity');
const raskPage = join(out, 'rask', 'select.html');
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

const browser = await chromium().launch();
if (option('live')) {
  const differs = await walkBoth(option('live'));
  await browser.close();
  console.log(differs ? `\nflux parity: the select's keyboard differs in ${differs} walk(s).` : '\nflux parity: the select walks as Flux does.');
  process.exit(differs ? 1 : 0);
}

const fluxFile = join(out, 'flux', 'select-open', 'measurements.json');
let flux;
if (existsSync(fluxFile) && !flag('refresh')) {
  flux = JSON.parse(await readFile(fluxFile, 'utf8'));
} else {
  flux = await measureOpen('https://fluxui.dev/components/select', join(out, 'flux', 'select-open'));
  await writeFile(fluxFile, JSON.stringify(flux));
}

const rask = await measureOpen(pathToFileURL(raskPage).href, join(out, 'rask', 'select-open'), flux);
await writeFile(join(out, 'rask', 'select-open', 'measurements.json'), JSON.stringify(rask));
await browser.close();

// Flux's custom elements and the native element Rask.Ui writes in their place, with the same role.
const NATIVE = {
  'ui-select': 'div', 'ui-selected': 'div', 'ui-options': 'div', 'ui-option': 'div', 'ui-option-empty': 'div',
  'ui-option-create': 'div',
};
const IGNORED = new Set(['width', 'height']);
const limit = flag('all') ? Infinity : 12;
let failures = 0;
for (const scheme of ['light', 'dark']) {
  for (const theirs of flux[scheme]) {
    if (option('section') && option('section') !== theirs.section) continue;
    const label = `${scheme} ${theirs.section || '(intro)'}#${theirs.ordinal}`;
    const mine = rask[scheme].find(e => e.section === theirs.section && e.ordinal === theirs.ordinal);
    if (!mine) {
      console.log(`MISSING ${label}`);
      failures++;
      continue;
    }

    const diffs = [];
    if (!theirs.open) diffs.push('Flux did not open');
    if (!mine.open) diffs.push('Rask did not open');
    compareTree(theirs, theirs.nodes[0], mine, mine.nodes[0], `select`, diffs);
    for (const state of ['hover', 'pressed']) {
      const x = theirs.rows[state] ?? {};
      const y = mine.rows[state] ?? {};
      for (const key of new Set([...Object.keys(x), ...Object.keys(y)])) {
        if (!same(x[key], y[key])) diffs.push(`row:${state} ${key}: ${x[key] ?? '(unchanged)'} vs ${y[key] ?? '(unchanged)'}`);
      }
    }

    failures += diffs.length ? 1 : 0;
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ''}`);
    for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
    if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
  }
}

console.log(failures ? `\nflux parity: the open select differs in ${failures} example(s).` : '\nflux parity: the open select matches Flux.');
process.exit(failures ? 1 : 0);

// ----- The keyboard, on a running app ------------------------------------------------------------------
// The parity page is static: it has no runtime, so its lists open (the popover is the browser's) and
// nothing more. `--live <url>` walks the same keys through Flux's examples and through the showcase of a
// running site (`dotnet run --project src/Rask.Site`, then the URL of /docs/ui/data-input) and compares what
// each key left behind: whether the list is open, the active row, the picked rows, what the trigger says
// and where focus is.
// A function, not a constant: the walk runs from the top of the file, before a constant here exists.
function walks() {
  return [
    {
      name: 'listbox', flux: 3, rask: '#ui-select-listbox',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowUp', 'End', 'Home', 'PageDown', 'PageUp', 'Enter',
        'ArrowUp', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'Escape', 'type:le', 'Space', 'type:acc', 'Space',
        'ArrowDown', 'hover:1', 'ArrowDown', 'click:4', 'ArrowDown', 'Escape', 'ArrowDown', 'Tab'],
    },
    {
      // Enter on the CLOSED button. Flux leaves the list shut; a native <button popovertarget> is pressed by
      // Enter, and C# cannot prevent a key's default. The runtime hook that would (contain Enter on a closed
      // listbox button) does not exist — see FluxConformanceTests.NotTranslated. Printed, and not counted.
      name: 'listbox, Enter on the closed button', flux: 3, rask: '#ui-select-listbox',
      keys: ['focus', 'Enter'],
      accepted: 'the browser presses a button on Enter; Flux\'s script does not open on it',
    },
    {
      name: 'searchable', flux: 10, rask: '#ui-select-searchable',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'type:co', 'ArrowDown', 'ArrowUp', 'ArrowUp', 'type:zz', 'Backspace', 'Backspace', 'Backspace',
        'Enter', 'ArrowDown', 'type:leg', 'Escape', 'ArrowDown', 'type:á', 'Tab'],
    },
    {
      name: 'multiple', flux: 12, rask: '#ui-select-multiple',
      keys: ['focus', 'ArrowDown', 'ArrowDown', 'Enter', 'ArrowDown', 'Enter', 'Enter', 'Escape', 'type:o', 'ArrowDown', 'Tab'],
    },
    {
      name: 'combobox', flux: 13, rask: '#ui-select-combobox',
      keys: ['click', 'Escape', 'type:de', 'ArrowDown', 'Enter', 'ArrowDown', 'ArrowDown', 'Escape', 'Escape', 'type:xyz', 'Tab'],
    },
  ];
}

async function walkBoth(live) {
  let differs = 0;
  for (const walk of walks()) {
    const theirs = await walkOne('https://fluxui.dev/components/select', walk, wrappers => wrappers[walk.flux]);
    const mine = await walkOne(live, walk, async (_, page) => (await page.$(walk.rask)).evaluateHandle(el => el.closest('[data-ui-select]').parentElement));
    const lines = walk.keys.map((key, i) => ({ key, theirs: theirs[i], mine: mine[i] }));
    const off = lines.filter(line => line.theirs !== line.mine);
    differs += off.length && !walk.accepted ? 1 : 0;
    const verdict = !off.length ? 'ok  ' : walk.accepted ? 'DIFF' : 'FAIL';
    console.log(`${verdict} ${walk.name} — ${walk.keys.length} steps${off.length ? `, ${off.length} differ` : ''}${off.length && walk.accepted ? ` (accepted: ${walk.accepted})` : ''}`);
    for (const line of flag('all') ? lines : off) {
      console.log(`       ${line.key.padEnd(10)} Flux: ${line.theirs}`);
      if (line.theirs !== line.mine) console.log(`       ${''.padEnd(10)} Rask: ${line.mine}`);
    }
  }

  return differs;
}

async function walkOne(url, walk, find) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 120000 });
  await page.waitForSelector('[data-flux-select], [data-ui-select]', { timeout: 120000 });
  const scope = await find(await page.$$('[data-preview-wrapper]'), page);
  await scope.evaluate(el => el.scrollIntoView({ block: 'center' }));
  const trigger = await scope.$('button[role=combobox], input[role=combobox]');
  const rows = () => scope.$$('[data-flux-option], [data-ui-option]');
  const states = [];
  for (const key of walk.keys) {
    const [verb, arg] = key.split(':');
    if (verb === 'focus') await trigger.focus();
    else if (verb === 'click' && arg === undefined) await trigger.click();
    else if (verb === 'click') await (await rows())[Number(arg)].click();
    else if (verb === 'hover') await (await rows())[Number(arg)].hover();
    else if (verb === 'type') await page.keyboard.type(arg, { delay: 60 });
    else await page.keyboard.press(verb);
    await page.waitForTimeout(450);
    states.push(await scope.evaluate(read));
  }

  await context.close();
  return states;
}

// Runs in the page. What a key left behind, in words both pages can be held to.
function read(scope) {
  const select = scope.querySelector('[data-flux-select], [data-ui-select]');
  const trigger = select.querySelector('button[role=combobox], input[role=combobox]');
  const popup = select.querySelector('[data-flux-options], [data-ui-options]');
  const words = el => (el.innerText ?? '').trim().replace(/\s+/g, ' ');
  const rows = [...select.querySelectorAll('[data-flux-option], [data-ui-option], [data-flux-option-create], [data-ui-option-create]')]
    .filter(row => getComputedStyle(row).display !== 'none');
  const open = popup.matches(':popover-open');
  const focused = document.activeElement;
  const focus = focused === trigger ? 'trigger'
    : focused?.closest?.('[data-flux-select-search], [data-ui-select-search]') ? 'search'
      : popup.contains(focused) ? 'list'
        : select.contains(focused) ? 'select' : 'elsewhere';
  const active = open ? rows.find(row => row.hasAttribute('data-active')) : undefined;
  return [
    open ? 'open' : 'closed',
    `says "${trigger.localName === 'input' ? trigger.value : words(trigger)}"`,
    `active ${active ? `"${words(active)}"` : '-'}`,
    `picked [${rows.filter(row => row.getAttribute('aria-selected') === 'true').map(words).join(', ')}]`,
    open ? `shows ${rows.length}` : '',
    `focus ${focus}`,
  ].filter(Boolean).join(' · ');
}

// Every custom select on `url`, opened one at a time and measured, in light and in dark. `like` is Flux's
// measurement when the page is Rask's: each example is given the text its Flux twin inherits from the docs
// page (parity.mjs's INHERITED rule), and the page the room under its last example that Flux's has, so a
// list at the foot of it opens downwards on both.
async function measureOpen(url, shots, like) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    await mkdir(join(shots, scheme), { recursive: true });
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    await page.evaluate(() => document.fonts.ready);
    const examples = [];
    const wrappers = await page.$$('[data-preview-wrapper]');
    if (like) await page.evaluate(() => { document.body.style.paddingBottom = '900px'; });
    for (const [index, wrapper] of wrappers.entries()) {
      if (!(await wrapper.$('[data-flux-options], [data-ui-options]'))) continue;
      const twin = like?.[scheme][examples.length];
      if (twin) await wrapper.evaluate((w, inherited) => Object.assign(w.style, inherited), twin.inherited);
      await wrapper.evaluate(w => w.scrollIntoView({ block: 'center' }));
      const trigger = await wrapper.$('button[role=combobox], input[role=combobox]');
      await trigger.click();
      await page.waitForTimeout(350);
      // A pointer under a hand never rests on one pixel: a hover that a re-render dropped (Livewire answers
      // the click on a backend-search example) is back with the next move.
      const at = await trigger.boundingBox();
      await page.mouse.move(at.x + at.width / 2 + 1, at.y + at.height / 2);
      await page.waitForTimeout(100);
      // A static page has no runtime to open a combobox from its input: the popover is shown as the
      // runtime shows it.
      await wrapper.evaluate(w => {
        const popup = w.querySelector('[data-ui-options]');
        if (popup && !popup.matches(':popover-open')) popup.showPopover();
      });
      const example = await wrapper.evaluate(collect, { STYLES, index });
      await page.screenshot({ path: join(shots, scheme, `${String(index).padStart(2, '0')}-${example.section || 'intro'}.png`) });

      example.rows = {};
      // The second option: the first is where the cursor already is.
      const row = (await wrapper.$$('[data-flux-option], [data-ui-option]'))[1];
      if (row) {
        const before = await row.evaluate(look, STYLES);
        await row.hover();
        await page.waitForTimeout(150);
        example.rows.hover = changed(before, await row.evaluate(look, STYLES));
        await page.mouse.down();
        await page.waitForTimeout(100);
        example.rows.pressed = changed(before, await row.evaluate(look, STYLES));
        // Released off the row, so nothing is picked.
        await page.mouse.move(2, 2);
        await page.mouse.up();
      }

      await page.keyboard.press('Escape');
      await wrapper.evaluate(w => w.querySelector('[data-ui-options]:popover-open')?.hidePopover());
      await page.mouse.click(2, 2);
      await page.waitForTimeout(150);
      examples.push(example);
    }

    schemes[scheme] = examples;
    await context.close();
  }

  return schemes;
}

function changed(before, after) {
  return Object.fromEntries(Object.keys(after).filter(k => after[k] !== before[k]).map(k => [k, after[k]]));
}

// Runs in the page. One node's computed look.
function look(el, STYLES) {
  const computed = getComputedStyle(el);
  return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
}

// Runs in the page. The select of one example, open: every element under its root, measured from the
// trigger's corner. lib.mjs's collect(), from another origin.
function collect(wrapper, { STYLES, index }) {
  for (const animation of document.getAnimations()) animation.finish?.();
  const headings = [...document.querySelectorAll('h2[id]')];
  const section = wrapper.dataset.section
    ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
  const ordinal = [...document.querySelectorAll('[data-preview-wrapper]')].slice(0, index)
    .filter(w => (w.getAttribute('data-o-section') ?? '') === section).length;
  wrapper.setAttribute('data-o-section', section);
  const select = wrapper.querySelector('[data-flux-select], [data-ui-select]');
  const trigger = select.querySelector('button[role=combobox], input[role=combobox]');
  const popup = select.querySelector('[data-flux-options], [data-ui-options]');
  const origin = trigger.getBoundingClientRect();
  const keep = name => !/^(class|style|wire:|x-|@|:|data-o$|data-m$|id$)/.test(name);
  const style = (el, pseudo) => {
    const computed = getComputedStyle(el, pseudo);
    return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
  };

  const nodes = [];
  [select, ...select.querySelectorAll('*')].slice(0, 600).forEach((el, i) => {
    const id = `${index}-${i}`;
    el.setAttribute('data-o', id);
    const box = el.getBoundingClientRect();
    const node = {
      id, parent: el === select ? null : el.parentElement.getAttribute('data-o'), tag: el.tagName.toLowerCase(),
      attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
      text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
      box: [box.x - origin.x, box.y - origin.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
      style: style(el),
    };
    for (const pseudo of ['::before', '::after']) {
      const content = getComputedStyle(el, pseudo).content;
      if (content && content !== 'none' && content !== 'normal') node[pseudo] = { content, ...style(el, pseudo) };
    }

    nodes.push(node);
  });

  const inherited = Object.fromEntries(['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing']
    .map(key => [key, getComputedStyle(wrapper)[key]]));
  return { index, section, ordinal, open: popup.matches(':popover-open'), inherited, nodes };
}

function compareTree(theirs, a, mine, b, where, diffs) {
  if ((NATIVE[a.tag] ?? a.tag) !== b.tag) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);

  const differs = (x, y) => Math.abs(x - y) > 0.6;
  const boxless = n => n.box[2] === 0 && n.box[3] === 0;
  const displayed = !(boxless(a) && boxless(b));
  if (differs(a.box[2], b.box[2]) || differs(a.box[3], b.box[3])) diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  if (displayed && [0, 1].some(i => differs(a.box[i], b.box[i]))) {
    diffs.push(`${where}: offset from the trigger ${a.box[0]},${a.box[1]} vs ${b.box[0]},${b.box[1]}`);
  }

  // Flux's script lays the open popup out with an inline `position: absolute` and pixel insets; Rask.Ui
  // anchors it in CSS, where a popover in the top layer is `fixed`. Where it ends up is the offset above.
  const placed = 'data-flux-options' in a.attrs && 'popover' in a.attrs;
  compareStyles(a.style, b.style, where, diffs, placed ? new Set(['position']) : undefined);
  for (const pseudo of ['::before', '::after']) {
    if (!a[pseudo] !== !b[pseudo]) diffs.push(`${where}${pseudo}: ${a[pseudo] ? 'only in Flux' : 'only in Rask'}`);
    else if (a[pseudo]) compareStyles(a[pseudo], b[pseudo], `${where}${pseudo}`, diffs);
  }

  // A <template> holds what Flux's script clones from; it draws nothing and Rask.Ui has no use for one.
  const kids = (example, n) => example.nodes.filter(c => c.parent === n.id && c.tag !== 'template');
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => compareTree(theirs, child, mine, cb[i], `${where} > ${child.tag}[${i}]`, diffs));
}

function compareStyles(x, y, where, diffs, let_go) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || let_go?.has(key) || undrawn(key, x, y) || same(x[key], y[key])) continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

// The colour of a border or an outline neither side draws is each PAGE's reset, not the component's.
function undrawn(key, x, y) {
  const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
  if (side) return x[`border${side}Width`] === '0px' && y[`border${side}Width`] === '0px';
  return key === 'outlineColor' && x.outlineStyle === 'none' && y.outlineStyle === 'none';
}

// Colours and lengths print with float noise that differs between two pages computing the same value.
function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  const round = v => v.replace(/(okl(?:ch|ab)\([^)]*?) none\)/g, '$1 0)')
    .replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000));
  return round(x) === round(y);
}

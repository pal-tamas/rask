#!/usr/bin/env node
// Holds a Rask.Ui menu to Flux UI's live documentation while it is OPEN.
//
// parity.mjs measures a page as it loads, and a menu loads closed: the component is the part it never
// sees. This opens every example the same way on both pages — a click on the trigger of a dropdown, a
// right-click on the target of a context menu — and compares what is then on screen:
//
//   * that it opened, where focus went, and what the page behind it was given (scroll lock, no pointer);
//   * the popup: where it sits against its trigger (or the pointer), its box and every row's computed
//     styles, pseudo-elements and glyphs;
//   * each row hovered by a real pointer, pressed, and under the keyboard cursor;
//   * the first submenu's flyout, opened by hovering its row: where it sits against the row, and its rows;
//   * that Escape and a click outside close it, and where Escape leaves focus.
//
// A Rask parity page is static HTML: the kit's sheet and no runtime. Everything above is the platform's and
// works there — `popovertarget`, the top layer, light dismiss, `:hover`. What is C#'s is not on the page, and
// is proved by the unit tests instead: which row each key moves the cursor to, `aria-expanded`, and focus
// handed back after a click outside. So here a Rask row is put under the cursor the way a render does it —
// `data-active`, and focus — and what is compared is how the row then LOOKS.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-menu.mjs dropdown
//         node scripts/flux/parity-menu.mjs context --refresh --all     # re-measure Flux, every difference
//
// Exit code 1 on any difference.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root, STYLES } from './lib.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const slug = args.find(a => !a.startsWith('--'));
if (!slug) {
  console.error('usage: node scripts/flux/parity-menu.mjs <dropdown|context> [--refresh] [--all]');
  process.exit(1);
}

const out = join(root, 'artifacts', 'flux-parity');
const raskPage = join(out, 'rask', `${slug}.html`);
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

// Flux's custom elements, and the native element Rask.Ui writes in their place.
const NATIVE = {
  'ui-menu': 'div', 'ui-submenu': 'div', 'ui-menu-radio-group': 'div', 'ui-menu-checkbox-group': 'div',
  'ui-menu-radio': 'button', 'ui-menu-checkbox': 'button',
};
// What is not compared, and why. `width`/`height` are the box's, already compared. On the popup itself:
// Flux places it with script — `position: absolute` at page coordinates, in a `popover="manual"` it
// dismisses itself — and Rask with the platform — a `popover="auto"` in the top layer, `position: fixed`,
// CSS anchor positioning and the margins that carry its gap. Where it ENDS UP is compared, as placement.
const IGNORED = new Set(['width', 'height']);
const PLACED = new Set(['position', 'marginTop', 'marginRight', 'marginBottom', 'marginLeft', 'transform', 'zIndex']);

const INHERITED = ['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];
// What the page behind an open menu is given.
const LOCK = ['overflowY', 'pointerEvents', 'scrollbarGutter'];

const browser = await chromium().launch();
const fluxFile = join(out, 'flux', slug, 'open.json');
let flux;
if (existsSync(fluxFile) && !flag('refresh')) {
  flux = JSON.parse(await readFile(fluxFile, 'utf8'));
} else {
  flux = await measureOpen(`https://fluxui.dev/components/${slug}`, 'data-flux-');
  await mkdir(join(out, 'flux', slug), { recursive: true });
  await writeFile(fluxFile, JSON.stringify(flux));
}

const rask = await measureOpen(pathToFileURL(raskPage).href, 'data-ui-', flux);
await browser.close();
if (flag('dump')) {
  await writeFile(join(out, 'rask', `${slug}.open.json`), JSON.stringify(rask));
}

const limit = flag('all') ? Infinity : 12;
let failures = 0;
for (const scheme of ['light', 'dark']) {
  for (const [index, theirs] of flux[scheme].entries()) {
    const mine = rask[scheme][index];
    const label = `${scheme} ${theirs.section || 'intro'}#${index}`;
    const diffs = mine ? compareExample(theirs, mine) : ['Rask has no such example'];
    failures += diffs.length ? 1 : 0;
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ` — ${theirs.rows.length} rows${theirs.flyout ? ', flyout' : ''}`}`);
    for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
    if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
  }
}

console.log(failures ? `\nflux parity (open): ${slug} differs in ${failures} example(s).` : `\nflux parity (open): ${slug} matches Flux.`);
process.exit(failures ? 1 : 0);

// ---- measuring ------------------------------------------------------------------------------------

async function measureOpen(url, prefix, like) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    // Every declared face, not only those the closed page uses (as parity-tooltip.mjs): the medium a row is
    // set in is first needed when the first menu opens, and was measured in the fallback face while it loaded.
    await page.evaluate(async () => {
      await Promise.all([...document.fonts].map(face => face.load().catch(() => {})));
      await document.fonts.ready;
    });
    if (like) {
      // The same surroundings Flux's example had, as parity.mjs gives them: only the component differs.
      await page.evaluate(({ examples, INHERITED }) => {
        // Flux's page goes on below its last example. Without as much room here, the last menu would have
        // nowhere to open but upward, and that would be the page's doing.
        document.body.style.paddingBottom = '100vh';
        document.querySelectorAll('[data-preview-wrapper]').forEach((wrapper, i) => {
          for (const key of examples[i] ? INHERITED : []) wrapper.style[key] = examples[i].inherited[key];
        });
      }, { examples: like[scheme], INHERITED });
    }

    const cdp = await context.newCDPSession(page);
    await cdp.send('DOM.enable');
    await cdp.send('CSS.enable');
    const examples = [];
    const count = await page.locator('[data-preview-wrapper]').count();
    for (let index = 0; index < count; index++) {
      examples.push(await measureExample(page, cdp, index, prefix));
    }

    schemes[scheme] = examples;
    await context.close();
  }

  return schemes;
}

async function measureExample(page, cdp, index, prefix) {
  const wrapper = page.locator('[data-preview-wrapper]').nth(index);
  const rest = async () => {
    await page.keyboard.press('Escape');
    await page.mouse.move(3, 3);
    await page.mouse.click(3, 3);
    await page.waitForTimeout(120);
  };
  await rest();
  await wrapper.evaluate(el => el.scrollIntoView({ block: 'center' }));

  const parts = await wrapper.evaluate(mark, { prefix, index, INHERITED });
  const example = { index, section: parts.section, inherited: parts.inherited, kind: parts.kind, rows: [], states: [] };
  if (!parts.kind) {
    return example;
  }

  const open = async () => {
    const box = await page.locator('[data-pm="trigger"]').boundingBox();
    const point = [Math.round(box.x + box.width / 2), Math.round(box.y + box.height / 2)];
    await page.mouse.click(point[0], point[1], { button: parts.kind === 'context' ? 'right' : 'left' });
    if (parts.kind === 'context' && prefix === 'data-ui-') {
      // The right-click hook is the runtime's (rask-dom.ts, "Context menus"), which a static parity page does
      // not load. This is what it does: the pointer's position on <html>, then the popover the area names.
      await page.evaluate(([x, y]) => {
        const area = document.querySelector('[data-pm="trigger"]').closest('[data-rask-contextmenu]');
        document.documentElement.style.setProperty('--rask-context-x', x + 'px');
        document.documentElement.style.setProperty('--rask-context-y', y + 'px');
        document.getElementById(area.getAttribute('data-rask-contextmenu')).showPopover();
      }, point);
    }

    await page.waitForTimeout(300);
    return point;
  };

  const point = await open();
  Object.assign(example, await page.evaluate(collect, { STYLES, LOCK, kind: parts.kind, point }));
  if (!example.opened) {
    await page.evaluate(unmark);
    return example;
  }

  // Each row under a real pointer, then pressed.
  for (const row of example.rows) {
    const box = await page.locator(`[data-pm-n="${row}"]`).boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForTimeout(60);
    example.states.push({ row, state: 'hover', changed: await page.evaluate(diff, { STYLES }) });
    const { root: doc } = await cdp.send('DOM.getDocument', { depth: 0 });
    const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: doc.nodeId, selector: `[data-pm-n="${row}"]` });
    await cdp.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: ['hover', 'active'] });
    example.states.push({ row, state: 'press', changed: await page.evaluate(diff, { STYLES }) });
    await cdp.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: [] });
  }

  // The keyboard cursor, a row at a time from the top.
  await page.mouse.move(3, 3);
  await page.waitForTimeout(60);
  example.walk = [];
  for (let step = 0; step < example.rows.length; step++) {
    if (prefix === 'data-flux-') {
      await page.keyboard.press('ArrowDown');
    } else if (example.nodes[0].attrs.role === 'menu') {
      // A navigation menu has no cursor on either side: its links are reached with Tab.
      await page.evaluate(cursor, example.rows[step]);
    }

    await page.waitForTimeout(60);
    const at = await page.evaluate(() => document.activeElement?.getAttribute('data-pm-n') ?? null);
    example.walk.push(at === null ? null : example.rows.indexOf(Number(at)));
    if (at !== null) {
      example.states.push({ row: Number(at), state: 'cursor', changed: await page.evaluate(diff, { STYLES }) });
    }
  }

  await page.evaluate(() => document.querySelectorAll('[data-pm-cursor]').forEach(el => {
    el.removeAttribute('data-active');
    el.removeAttribute('data-pm-cursor');
  }));

  // The first submenu, opened the way a pointer opens it.
  if (example.submenu !== null) {
    const box = await page.locator(`[data-pm-n="${example.submenu}"]`).boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForTimeout(400);
    example.flyout = await page.evaluate(collectFlyout, { STYLES, row: example.submenu });
  }

  // Leaving: Escape, then a click outside.
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  example.escape = await page.evaluate(closed);
  await open();
  await page.mouse.move(3, 3);
  await page.mouse.click(3, 3);
  await page.waitForTimeout(150);
  example.outside = await page.evaluate(closed);
  await page.evaluate(unmark);
  return example;
}

// Runs in the page. Names the example's trigger and popup.
function mark(wrapper, { prefix, index, INHERITED }) {
  const headings = [...document.querySelectorAll('h2[id]')];
  const section = wrapper.dataset.section
    ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
  const computed = getComputedStyle(wrapper);
  const inherited = Object.fromEntries(INHERITED.map(k => [k, computed[k]]));
  const host = wrapper.querySelector(`[${prefix}dropdown], [${prefix}context]`);
  if (!host) {
    return { section, inherited, kind: null, index };
  }

  host.firstElementChild.setAttribute('data-pm', 'trigger');
  host.querySelector('[popover]').setAttribute('data-pm', 'panel');
  return { section, inherited, kind: host.hasAttribute(`${prefix}context`) ? 'context' : 'dropdown' };
}

// Runs in the page. The open popup: every element in it, measured from the trigger (or the pointer).
function collect({ STYLES, LOCK, kind, point }) {
  const panel = document.querySelector('[data-pm="panel"]');
  const trigger = document.querySelector('[data-pm="trigger"]');
  const opened = panel.matches(':popover-open');
  const html = getComputedStyle(document.documentElement);
  const lock = Object.fromEntries(LOCK.map(k => [k, html[k]]));
  lock.panelPointerEvents = getComputedStyle(panel).pointerEvents;
  const active = document.activeElement;
  const focus = active === panel ? 'menu' : active === trigger ? 'trigger' : panel.contains(active) ? 'row' : 'elsewhere';
  if (!opened) {
    return { opened, focus, lock, nodes: [], rows: [], submenu: null };
  }

  const anchor = trigger.getBoundingClientRect();
  const origin = kind === 'context' ? { x: point[0], y: point[1] } : anchor;
  const { nodes, rows } = tree(panel, origin, STYLES);
  const sub = panel.querySelector('[data-flux-menu-submenu] > button, [data-ui-menu-submenu] > button');
  window.__pmBase = Object.fromEntries(nodes.map(n => [n.id, n.style]));
  return { opened, focus, lock, nodes, rows, submenu: sub ? Number(sub.getAttribute('data-pm-n')) : null };

  function tree(top, from, styles) {
    const keep = name => /^(role|aria-checked|aria-disabled|disabled|data-checked|popover)$/.test(name);
    const list = [];
    const picked = [];
    const next = document.querySelectorAll('[data-pm-n]').length;
    [top, ...top.querySelectorAll('*')].forEach((el, i) => {
      const id = next + i;
      el.setAttribute('data-pm-n', id);
      const box = el.getBoundingClientRect();
      const computed = getComputedStyle(el);
      const node = {
        id, parent: el === top ? null : Number(el.parentElement.getAttribute('data-pm-n')), tag: el.tagName.toLowerCase(),
        attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
        text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
        box: [box.x - from.x, box.y - from.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
        style: Object.fromEntries(styles.map(k => [k, computed[k]])),
      };
      for (const pseudo of ['::before', '::after']) {
        const drawn = getComputedStyle(el, pseudo);
        if (drawn.content && drawn.content !== 'none' && drawn.content !== 'normal') {
          node[pseudo] = { content: drawn.content, ...Object.fromEntries(styles.map(k => [k, drawn[k]])) };
        }
      }

      list.push(node);
      // A row someone can point at: an item of any kind, or a navigation menu's link, that is on screen.
      if (el.matches('[role^=menuitem], a') && box.width > 0) {
        picked.push(id);
      }
    });
    return { nodes: list, rows: picked };
  }
}

// Runs in the page. What changed in the popup's own rows since it was measured at rest. A submenu's flyout is
// left out — hovering its row opens it, and it is measured on its own — and so is anything with no box.
function diff({ STYLES }) {
  const changed = {};
  const panel = document.querySelector('[data-pm="panel"]');
  for (const [id, base] of Object.entries(window.__pmBase)) {
    const el = document.querySelector(`[data-pm-n="${id}"]`);
    if (!el || el.getClientRects().length === 0 || (el !== panel && el.closest('[role=menu], nav') !== panel)) continue;
    const computed = getComputedStyle(el);
    for (const k of STYLES) {
      if (computed[k] !== base[k]) (changed[id] ??= {})[k] = computed[k];
    }
  }

  return changed;
}

// Runs in the page, on a Rask parity page: puts the cursor on a row the way a render of the menu does.
function cursor(row) {
  document.querySelectorAll('[data-pm-cursor]').forEach(el => {
    el.removeAttribute('data-active');
    el.removeAttribute('data-pm-cursor');
  });
  const el = document.querySelector(`[data-pm-n="${row}"]`);
  el.setAttribute('data-active', '');
  el.setAttribute('data-pm-cursor', '');
  el.focus();
}

// Runs in the page. The flyout a submenu row opened, measured from that row.
function collectFlyout({ STYLES, row }) {
  const trigger = document.querySelector(`[data-pm-n="${row}"]`);
  const flyout = trigger.parentElement.querySelector('[role=menu]');
  const from = trigger.getBoundingClientRect();
  const keep = name => /^(role|aria-checked|aria-disabled|disabled|data-checked)$/.test(name);
  const ids = new Map();
  return [flyout, ...flyout.querySelectorAll('*')].map((el, i) => {
    ids.set(el, i);
    const box = el.getBoundingClientRect();
    const computed = getComputedStyle(el);
    return {
      id: i, parent: el === flyout ? null : ids.get(el.parentElement), tag: el.tagName.toLowerCase(),
      attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
      text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
      box: [box.x - from.x, box.y - from.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
      style: Object.fromEntries(STYLES.map(k => [k, computed[k]])),
    };
  });
}

// Runs in the page. Whether the popup is gone, and where focus was left.
function closed() {
  const panel = document.querySelector('[data-pm="panel"]');
  const active = document.activeElement;
  return { closed: !panel.matches(':popover-open'), focus: active === document.querySelector('[data-pm="trigger"]') ? 'trigger' : active === document.body ? 'body' : 'elsewhere' };
}

// Runs in the page.
function unmark() {
  document.querySelectorAll('[data-pm]').forEach(el => el.removeAttribute('data-pm'));
  document.querySelectorAll('[data-pm-n]').forEach(el => el.removeAttribute('data-pm-n'));
  delete window.__pmBase;
}

// ---- comparing ------------------------------------------------------------------------------------

function compareExample(theirs, mine) {
  const diffs = [];
  const say = (what, a, b) => {
    if (JSON.stringify(a) !== JSON.stringify(b)) diffs.push(`${what}: ${JSON.stringify(a)} vs ${JSON.stringify(b)}`);
  };
  say('kind', theirs.kind, mine.kind);
  if (!theirs.kind || !mine.kind) {
    return diffs;
  }

  say('opened', theirs.opened, mine.opened);
  if (!theirs.opened || !mine.opened) {
    return diffs;
  }

  // ACCEPTED, and deliberate: Flux's context menu opens without taking focus, so its arrow keys do nothing
  // until a row is clicked. Rask.Ui's takes focus as a dropdown's does — a menu a keyboard cannot use is not
  // one a keyboard user was given.
  if (theirs.kind !== 'context') {
    say('focus after opening', theirs.focus, mine.focus);
    say('the cursor walks', theirs.walk, mine.walk);
  }

  say('the page behind', theirs.lock, mine.lock);
  say('rows', theirs.rows.length, mine.rows.length);
  say('Escape', theirs.escape, mine.escape);
  // Only that it closed. Flux then puts focus back on the trigger from script; the browser's own light dismiss
  // leaves it on the page, and Rask's runtime hands it back — which a static parity page does not load.
  say('a click outside closes it', theirs.outside.closed, mine.outside.closed);

  const a = theirs.nodes[0];
  const b = mine.nodes[0];
  // Where the popup sits against its trigger, which a tree compared from its own root would not say.
  if (Math.abs(a.box[0] - b.box[0]) > 0.6 || Math.abs(a.box[1] - b.box[1]) > 0.6) {
    diffs.push(`placement: ${fix(a.box[0])},${fix(a.box[1])} vs ${fix(b.box[0])},${fix(b.box[1])} from the trigger`);
  }

  const pairs = new Map();
  compareTree(theirs.nodes, a, mine.nodes, b, a, b, 'menu', diffs, pairs);

  for (const state of ['hover', 'press', 'cursor']) {
    theirs.rows.forEach((row, i) => {
      const x = theirs.states.find(s => s.row === row && s.state === state)?.changed;
      const y = mine.states.find(s => s.row === mine.rows[i] && s.state === state)?.changed;
      if (!x || !y) {
        // The cursor never lands on a context menu's rows in Flux: see the note on focus above.
        if (!x !== !y && !(state === 'cursor' && theirs.kind === 'context')) {
          diffs.push(`row[${i}]:${state}: ${x ? 'only Flux reaches it' : 'only Rask reaches it'}`);
        }

        return;
      }

      const seen = new Set();
      for (const [id, twin] of pairs) {
        const changed = visible(x[id] ?? {}, theirs.nodes.find(n => n.id === id).style);
        const other = visible(y[twin.id] ?? {}, mine.nodes.find(n => n.id === twin.id).style);
        seen.add(String(twin.id));
        for (const key of new Set([...Object.keys(changed), ...Object.keys(other)])) {
          if (!same(changed[key], other[key])) diffs.push(`row[${i}]:${state} ${twin.where} ${key}: ${changed[key] ?? '(unchanged)'} vs ${other[key] ?? '(unchanged)'}`);
        }
      }

      for (const id of Object.keys(y).filter(id => !seen.has(id))) {
        diffs.push(`row[${i}]:${state}: Rask also changes node ${id}: ${JSON.stringify(y[id])}`);
      }
    });
  }

  if (!theirs.flyout !== !mine.flyout) {
    diffs.push(`flyout: ${theirs.flyout ? 'only in Flux' : 'only in Rask'}`);
  } else if (theirs.flyout) {
    const fa = theirs.flyout[0];
    const fb = mine.flyout[0];
    if (Math.abs(fa.box[0] - fb.box[0]) > 0.6 || Math.abs(fa.box[1] - fb.box[1]) > 0.6) {
      diffs.push(`flyout placement: ${fix(fa.box[0])},${fix(fa.box[1])} vs ${fix(fb.box[0])},${fix(fb.box[1])} from its row`);
    }

    compareTree(theirs.flyout, fa, mine.flyout, fb, fa, fb, 'flyout', diffs, new Map());
  }

  return diffs;
}

function compareTree(theirs, a, mine, b, rootA, rootB, where, diffs, pairs) {
  pairs.set(a.id, { id: b.id, where });
  if ((NATIVE[a.tag] ?? a.tag) !== b.tag) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);
  for (const name of ['role', 'aria-checked']) {
    if (a.attrs[name] !== b.attrs[name]) diffs.push(`${where}: ${name} ${a.attrs[name]} vs ${b.attrs[name]}`);
  }

  if (Math.abs(a.box[2] - b.box[2]) > 0.6 || Math.abs(a.box[3] - b.box[3]) > 0.6) {
    diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  }

  const displayed = a.style.display !== 'none' || b.style.display !== 'none';
  const shown = n => n.box[2] > 0 || n.box[3] > 0;
  if (a !== rootA && displayed && (shown(a) || shown(b))) {
    const off = (n, r, i) => n.box[i] - r.box[i];
    if (Math.abs(off(a, rootA, 0) - off(b, rootB, 0)) > 0.6 || Math.abs(off(a, rootA, 1) - off(b, rootB, 1)) > 0.6) {
      diffs.push(`${where}: offset ${fix(off(a, rootA, 0))},${fix(off(a, rootA, 1))} vs ${fix(off(b, rootB, 0))},${fix(off(b, rootB, 1))}`);
    }
  }

  compareStyles(a.style, b.style, where, diffs, a === rootA);
  for (const pseudo of ['::before', '::after']) {
    if (!a[pseudo] !== !b[pseudo]) diffs.push(`${where}${pseudo}: ${a[pseudo] ? 'only in Flux' : 'only in Rask'}`);
    else if (a[pseudo]) compareStyles(a[pseudo], b[pseudo], `${where}${pseudo}`, diffs, false);
  }

  const kids = (nodes, n) => nodes.filter(c => c.parent === n.id);
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => compareTree(theirs, child, mine, cb[i], rootA, rootB, `${where} > ${child.tag}[${i}]`, diffs, pairs));
}

function compareStyles(x, y, where, diffs, placed) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || (placed && PLACED.has(key)) || same(x[key], y[key])) continue;
    const width = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
    if (width && x[`border${width}Width`] === '0px' && y[`border${width}Width`] === '0px') continue;
    // Nor the width and colour of an outline neither side draws: a focused element computes the browser's
    // focus-ring values for them even with the ring turned off.
    if (/^outline(Color|Width)$/.test(key) && x.outlineStyle === 'none' && y.outlineStyle === 'none') continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

// A state's changes, without the ones nobody can see: the box (compared as a box), and the colour and
// width of an outline or a border that is not drawn — both follow the text colour, and a row's ink changes.
function visible(changed, base) {
  const out = {};
  for (const [key, value] of Object.entries(changed)) {
    if (IGNORED.has(key)) continue;
    if (/^outline(Color|Width)$/.test(key) && (changed.outlineStyle ?? base.outlineStyle) === 'none') continue;
    const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
    if (side && base[`border${side}Width`] === '0px') continue;
    out[key] = value;
  }

  return out;
}

function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  // `none` is a hue that has no say (a gray's): a minifier writes it for the 0 the source had.
  const round = v => v.replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000)).replace(/ none\)/g, ' 0)');
  return round(x) === round(y);
}

function fix(v) {
  return Math.round(v * 10) / 10;
}

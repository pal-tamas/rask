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
//   * the POINTER'S OWN WALK: which rows are lit (`data-active`) and where focus is with the pointer on each
//     row, and after it has left the menu; and on the diagonal from a submenu's row to the far corner of its
//     flyout, step by step, that the flyout is still showing and no row below was lit on the way;
//   * THE POINTER AND THE KEYS TOGETHER: the pointer lights a row, the first arrow lands on it, the next goes
//     on, the pointer lights another while focus stays, the arrow after counts from the lit row, and the
//     keyboard's row stays lit when the pointer leaves — lit rows and the focused row after every step;
//   * the first submenu's flyout, opened by hovering its row: where it sits against the row, and its rows;
//   * that Escape and a click outside close it, and where Escape leaves focus;
//   * THE PAGE BEHIND, in a browser that shows its scrollbars, at 1920 x 1080 and 390 x 844: `scrollY` and the
//     example's `x` and `width` before, while and after the menu is open, and the three declarations on
//     `<html>`. (The browser the rest runs in hides its scrollbars, as every headless one does, and there
//     the kit keeps no gutter: `scrollbar-gutter` is compared here, where there is a scrollbar to keep one for —
//     one that TAKES ROOM: macOS draws its scrollbars over the page even when asked to show them, and there
//     Flux's `stable` reserves nothing and the kit asks for nothing.)
//   * WHERE IT LANDS with no room: every menu opened with its trigger — or the pointer — held at the edges of
//     a phone and of a desktop, and with more rows than fit (placement.mjs).
//
// ON A PHONE (`--view 390x844`) the same walk is taken with a finger, as Flux's page answers one: a tap on a
// dropdown's trigger, a LONG PRESS on a context menu's target (the browser's `contextmenu`, as a right-click's;
// a tap opens nothing), and every row TAPPED in a menu opened afresh: open or closed after it, lit rows, focus,
// a flyout. A submenu's flyout is opened by a tap on its row, and that row is tapped again. A finger has no
// pointer to rest on a row, so the hover, the pointer's walk, the diagonal and the pointer with the keys are
// not taken there; the keys are, for a phone with a keyboard. The page behind and the landings are the
// desktop run's: both measure a phone already.
//
// A Rask parity page is static HTML: the kit's sheet, and the runtime's behaviour hooks added as one script
// (runtime.mjs) — the cursor, the pointer's lit row, the open flyout, the safe area and the page lock are
// theirs, by attribute, and none of it is C#'s. Everything else above is the platform's — `popovertarget`, the
// top layer, light dismiss. So the same keys and the same pointer drive both pages. What IS the page's is not
// here and is proved by the unit tests: `aria-expanded`, and what a press on a row runs.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-menu.mjs dropdown
//         node scripts/flux/parity-menu.mjs context --refresh --all     # re-measure Flux, every difference
//         node scripts/flux/parity-menu.mjs dropdown --view 390x844      # on a phone
//
// Exit code 1 on any difference.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, press, root, STYLES, viewOf } from './lib.mjs';
import { landings } from './placement.mjs';
import { withRuntime } from './runtime.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const slug = args.find(a => !a.startsWith('--'));
if (!slug) {
  console.error('usage: node scripts/flux/parity-menu.mjs <dropdown|context> [--refresh] [--all] [--view 390x844]');
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
// What the page behind an open menu is given. The gutter is compared where there is one: measureBehind.
const LOCK = ['overflowY', 'pointerEvents'];
const VIEWPORTS = { '1920x1080': { width: 1920, height: 1080 }, '390x844': { width: 390, height: 844 } };

const view = viewOf(args);
const browser = await chromium().launch();
const fluxFile = join(out, 'flux', slug, `open${view.sized}.json`);
let flux;
if (existsSync(fluxFile) && !flag('refresh')) {
  flux = JSON.parse(await readFile(fluxFile, 'utf8'));
} else {
  flux = await measureOpen(`https://fluxui.dev/components/${slug}`, 'data-flux-');
  flux.behind = view.sized ? {} : await measureBehind(`https://fluxui.dev/components/${slug}`, 'data-flux-');
  await mkdir(join(out, 'flux', slug), { recursive: true });
  await writeFile(fluxFile, JSON.stringify(flux));
}

if (!flux.behind) {
  console.error(`flux parity: ${fluxFile} was measured before the page behind was — run with --refresh`);
  process.exit(1);
}

const rask = await measureOpen(pathToFileURL(raskPage).href, 'data-ui-', flux);
rask.behind = view.sized ? {} : await measureBehind(pathToFileURL(raskPage).href, 'data-ui-');
const astray = view.sized ? 0 : await landings(browser, {
  name: slug, refresh: flag('refresh'), all: flag('all'),
  flux: { url: `https://fluxui.dev/components/${slug}`, host: `[data-preview-wrapper] [data-flux-${slug}]`, panel: '[popover]' },
  rask: { url: pathToFileURL(raskPage).href, host: `[data-preview-wrapper] [data-ui-${slug}]`, panel: '[popover]', open: slug === 'context' ? atPointer : undefined },
  hosts: (await hostsOf(`https://fluxui.dev/components/${slug}`)).map(index => (slug === 'context' ? { index, open: 'context' } : index)),
});
await browser.close();
if (flag('dump')) {
  await writeFile(join(out, 'rask', `${slug}.open.json`), JSON.stringify(rask));
}

const limit = flag('all') ? Infinity : 12;
let failures = 0;
for (const scheme of ['light', 'dark']) {
  for (const [index, theirs] of flux[scheme].entries()) {
    const mine = rask[scheme][index];
    const label = `${scheme}${view.sized} ${theirs.section || 'intro'}#${index}`;
    const diffs = mine ? compareExample(theirs, mine) : ['Rask has no such example'];
    failures += diffs.length ? 1 : 0;
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ` — ${theirs.rows.length} rows${theirs.flyout ? ', flyout' : ''}`}`);
    for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
    if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
  }
}

for (const [size, theirs] of Object.entries(flux.behind)) {
  const mine = rask.behind[size];
  // A gutter is kept where a scrollbar took room. Where none does, Flux's `scrollbar-gutter: stable` holds
  // nothing either, and the kit does not write it (rask-lock.ts: on a page that shows no scrollbar anywhere
  // else it would narrow the page by one). The other two declarations are compared everywhere.
  const kept = side => (theirs.scrollbar || mine.scrollbar ? side : { ...side, lock: side.lock.split(' | ').slice(0, 2).join(' | ') });
  const diffs = Object.keys(theirs).filter(key => JSON.stringify(kept(theirs)[key]) !== JSON.stringify(kept(mine)[key]))
    .map(key => `${key}: ${JSON.stringify(theirs[key])} vs ${JSON.stringify(mine[key])}`);
  failures += diffs.length ? 1 : 0;
  console.log(`${diffs.length ? 'FAIL' : 'ok  '} the page behind, ${size}, scrollbars showing${diffs.length ? '' : ` — ${theirs.lock}; moved ${theirs.moved}`}`);
  for (const d of diffs) console.log(`       ${d}`);
}

console.log(failures ? `\nflux parity (open): ${slug}${view.sized} differs in ${failures} example(s).` : `\nflux parity (open): ${slug}${view.sized} matches Flux.`);
if (!view.sized) console.log(astray ? `flux parity (open): ${slug} lands elsewhere for ${astray} menu(s).` : `flux parity (open): ${slug} lands where Flux's does.`);
process.exit(failures || astray ? 1 : 0);

// The hosts of Flux's page that hold a menu, by their place among all of them.
async function hostsOf(url) {
  const page = await browser.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  const held = await page.evaluate(marker => [...document.querySelectorAll(`[data-preview-wrapper] [${marker}]`)]
    .map((el, index) => (el.querySelector('[popover]') ? index : -1)).filter(index => index >= 0), `data-flux-${slug}`);
  await page.close();
  return held;
}

// What the runtime's right-click hook does (rask-dom.ts, "Context menus"), which a static parity page does not
// load: the pointer's position on <html>, then the popover the area names.
async function atPointer(page, point) {
  await page.mouse.click(point[0], point[1], { button: 'right' });
  await page.evaluate(([x, y]) => {
    // A place the held area was moved out of view from: no pointer gets there, on either page.
    const area = document.elementFromPoint(x, y)?.closest('[data-rask-contextmenu]');
    if (!area) return;
    document.documentElement.style.setProperty('--rask-context-x', `${x}px`);
    document.documentElement.style.setProperty('--rask-context-y', `${y}px`);
    document.getElementById(area.getAttribute('data-rask-contextmenu')).showPopover();
  }, point);
}

// A finger held on the screen: Chromium makes a `contextmenu` of it, as a phone's browser does — on a Mac. On Linux
// it makes none (measured 2026-10-30 in Playwright's 1.63 image: pointerdown, touchstart, then a second later
// pointerup and touchend, and nothing else, three runs of three), so there the `contextmenu` the press would have
// made is made at its point by the right button — the same event, at the same place, on both pages.
async function longPress(page, x, y) {
  await page.evaluate(() => {
    window.__parityHeld = false;
    addEventListener('contextmenu', () => { window.__parityHeld = true; }, { capture: true, once: true });
  });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Input.synthesizeTapGesture', { x, y, duration: 1000, tapCount: 1, gestureSourceType: 'touch' });
  await cdp.detach();
  if (!await page.evaluate(() => window.__parityHeld)) await page.mouse.click(x, y, { button: 'right' });
}

// ---- measuring ------------------------------------------------------------------------------------

async function measureOpen(url, prefix, like) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ ...view.screen, colorScheme: scheme });
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    // Every declared face, not only those the closed page uses (as parity-tooltip.mjs): the medium a row is
    // set in is first needed when the first menu opens, and was measured in the fallback face while it loaded.
    await page.evaluate(async () => {
      await Promise.all([...document.fonts].map(face => face.load().catch(() => {})));
      await document.fonts.ready;
      // A link a tap follows takes Flux's page away (it loads again, scrolled to the top). Stopped last, on the
      // window, once both menus have heard the press: the walk stays on the page.
      addEventListener('click', e => { if (e.target.closest?.('[popover] a[href]')) e.preventDefault(); });
    });
    if (like) {
      // What Flux does in script the kit asks the runtime for: the lit row, the safe area, the page lock.
      await withRuntime(page, 'rask-hooks.ts');
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
    if (!view.phone) await page.mouse.move(3, 3);
    await press(page, 3, 3, view.phone);
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
    if (parts.kind === 'context' && view.phone) await longPress(page, point[0], point[1]);
    else if (view.phone) await press(page, point[0], point[1], true);
    else await page.mouse.click(point[0], point[1], { button: parts.kind === 'context' ? 'right' : 'left' });
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

    // A long press opens Flux's menu from the `contextmenu` the browser makes of it, when it makes it: a loaded
    // Linux runner measured the menu shut where it opens a moment later. Waited on, up to two seconds; a menu
    // that does not open by then did not open.
    if (parts.kind === 'context' && view.phone) {
      await page.waitForFunction(() => document.querySelector('[data-pm="panel"]')?.matches(':popover-open'), null, { timeout: 2000 }).catch(() => {});
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

  // Each row under a real pointer (a finger has none), then pressed.
  for (const row of example.rows) {
    if (!view.phone) {
      const box = await page.locator(`[data-pm-n="${row}"]`).boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
      await page.waitForTimeout(60);
      example.states.push({ row, state: 'hover', changed: await page.evaluate(diff, { STYLES }) });
    }

    const { root: doc } = await cdp.send('DOM.getDocument', { depth: 0 });
    const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: doc.nodeId, selector: `[data-pm-n="${row}"]` });
    await cdp.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: ['hover', 'active'] });
    example.states.push({ row, state: 'press', changed: await page.evaluate(diff, { STYLES }) });
    await cdp.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: [] });
  }

  // The pointer's own walk: what is lit, and where focus is, with the pointer on each row and after it left.
  example.pointer = view.phone ? undefined : [];
  for (const row of view.phone ? [] : example.rows) {
    const box = await page.locator(`[data-pm-n="${row}"]`).boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 3 });
    await page.waitForTimeout(60);
    example.pointer.push(await page.evaluate(lit, example.rows));
  }

  // The keyboard cursor, a row at a time from the top.
  if (!view.phone) {
    await page.mouse.move(3, 3, { steps: 3 });
    await page.waitForTimeout(60);
    example.pointer.push(await page.evaluate(lit, example.rows));
  }

  example.walk = [];
  for (let step = 0; step < example.rows.length; step++) {
    // The same key on both sides: the kit's cursor is the runtime's (data-rask-menu-cursor), in the browser, as
    // Flux's is its script's. A navigation menu has no cursor on either: its links are reached with Tab.
    await page.keyboard.press('ArrowDown');

    await page.waitForTimeout(60);
    const at = await page.evaluate(() => document.activeElement?.getAttribute('data-pm-n') ?? null);
    example.walk.push(at === null ? null : example.rows.indexOf(Number(at)));
    if (at !== null) {
      example.states.push({ row: Number(at), state: 'cursor', changed: await page.evaluate(diff, { STYLES }) });
    }
  }

  // The pointer and the keys together, on a menu that takes focus and has rows to spare: the pointer lights the
  // second row and the first arrow lands ON it; the next goes on; the pointer lights the first row while focus
  // stays, and the arrow after that counts from the lit row; the pointer leaves and the keyboard's row stays lit.
  if (!view.phone && parts.kind === 'dropdown' && example.focus === 'menu' && example.rows.length >= 3) {
    await page.keyboard.press('Escape');
    await page.waitForTimeout(150);
    await open();
    const over = async index => {
      const box = await page.locator(`[data-pm-n="${example.rows[index]}"]`).boundingBox();
      await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 3 });
    };
    const steps = [() => over(1), () => page.keyboard.press('ArrowDown'), () => page.keyboard.press('ArrowDown'), () => over(0),
      () => page.keyboard.press('ArrowDown'), () => page.mouse.move(3, 3, { steps: 3 })];
    example.together = [];
    for (const step of steps) {
      await step();
      await page.waitForTimeout(80);
      example.together.push(await page.evaluate(where, example.rows));
    }

    await page.keyboard.press('Escape');
    await page.waitForTimeout(150);
    await open();
  }

  // On a phone, every row tapped in a menu opened afresh.
  if (view.phone) {
    example.taps = [];
    for (const row of example.rows) {
      await page.keyboard.press('Escape');
      await page.waitForTimeout(150);
      await open();
      const box = await page.locator(`[data-pm-n="${row}"]`).boundingBox();
      if (!box) {
        example.taps.push('not on screen');
        continue;
      }

      await press(page, box.x + box.width / 2, box.y + box.height / 2, true);
      await page.waitForTimeout(300);
      example.taps.push(await page.evaluate(tapped, example.rows));
    }

    await page.keyboard.press('Escape');
    await page.waitForTimeout(150);
    await open();
  }

  // The first submenu, opened the way a pointer opens it; on a phone by a tap on its row, then tapped again.
  if (example.submenu !== null && view.phone) {
    const box = await page.locator(`[data-pm-n="${example.submenu}"]`).boundingBox();
    await press(page, box.x + box.width / 2, box.y + box.height / 2, true);
    await page.waitForTimeout(400);
    example.flyout = await page.evaluate(collectFlyout, { STYLES, row: example.submenu });
    await press(page, box.x + box.width / 2, box.y + box.height / 2, true);
    await page.waitForTimeout(400);
    example.again = await page.evaluate(tapped, example.rows);
  } else if (example.submenu !== null) {
    const box = await page.locator(`[data-pm-n="${example.submenu}"]`).boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.waitForTimeout(400);
    example.flyout = await page.evaluate(collectFlyout, { STYLES, row: example.submenu });

    // The diagonal: from the middle of the row to the far corner of its flyout, across the rows below.
    const far = await page.evaluate(row => {
      const flyout = document.querySelector(`[data-pm-n="${row}"]`).parentElement.querySelector('[role=menu]').getBoundingClientRect();
      return [flyout.x + 8, flyout.y + flyout.height - 6];
    }, example.submenu);
    const from = [box.x + box.width / 2, box.y + box.height / 2];
    example.diagonal = [];
    for (let step = 1; step <= 8; step++) {
      await page.mouse.move(from[0] + (far[0] - from[0]) * step / 8, from[1] + (far[1] - from[1]) * step / 8);
      await page.waitForTimeout(40);
      example.diagonal.push(await page.evaluate(crossing, { row: example.submenu, rows: example.rows }));
    }
  }

  // Leaving: Escape, then a click outside.
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  example.escape = await page.evaluate(closed);
  await open();
  if (!view.phone) await page.mouse.move(3, 3);
  await press(page, 3, 3, view.phone);
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

// Runs in the page. Which of the popup's own rows are lit, by position, and where focus is.
function lit(rows) {
  const panel = document.querySelector('[data-pm="panel"]');
  const active = document.activeElement;
  return {
    lit: rows.map((row, i) => document.querySelector(`[data-pm-n="${row}"]`).hasAttribute('data-active') ? i : -1).filter(i => i >= 0),
    focus: active === panel ? 'menu' : panel.contains(active) ? 'row' : 'elsewhere',
  };
}

// Runs in the page. The lit rows and the focused row, by position among the popup's own rows.
function where(rows) {
  const at = el => rows.findIndex(row => document.querySelector(`[data-pm-n="${row}"]`) === el);
  return {
    lit: rows.map((row, i) => (document.querySelector(`[data-pm-n="${row}"]`).hasAttribute('data-active') ? i : -1)).filter(i => i >= 0),
    focus: at(document.activeElement),
  };
}

// Runs in the page. After a tap: is the popup open, which of its own rows are lit, where focus is (a row by its
// position), and how many flyouts are showing.
function tapped(rows) {
  const panel = document.querySelector('[data-pm="panel"]');
  const active = document.activeElement;
  const at = rows.findIndex(row => document.querySelector(`[data-pm-n="${row}"]`) === active);
  const where = active === panel ? 'menu' : active === document.querySelector('[data-pm="trigger"]') ? 'trigger' : active === document.body ? 'body' : 'elsewhere';
  const open = panel.matches(':popover-open');
  // A closed menu's lit row is seen by nobody: Flux leaves the tapped one lit, the kit does not.
  return {
    open,
    lit: !open ? [] : rows.map((row, i) => (document.querySelector(`[data-pm-n="${row}"]`).hasAttribute('data-active') ? i : -1)).filter(i => i >= 0),
    focus: at >= 0 ? at : where,
    flyouts: [...panel.querySelectorAll('[role=menu]')].filter(menu => menu.getClientRects().length > 0).length,
  };
}

// Runs in the page. On the way from a submenu's row to its flyout: is the flyout still there, and which of the
// popup's own rows are lit — the submenu's, until the pointer is inside the flyout, and never one it crossed.
function crossing({ row, rows }) {
  const trigger = document.querySelector(`[data-pm-n="${row}"]`);
  const flyout = trigger.parentElement.querySelector('[role=menu]');
  return {
    flyout: flyout.getClientRects().length > 0,
    lit: rows.map((id, i) => document.querySelector(`[data-pm-n="${id}"]`).hasAttribute('data-active') ? i : -1)
      .filter(i => i >= 0 && rows[i] !== row),
  };
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

// The page behind an open menu, in a browser that shows its scrollbars: where it is scrolled to and where the
// first example sits, before, while and after its menu is open, and what <html> is given meanwhile.
async function measureBehind(url, prefix) {
  const shown = await chromium().launch({ ignoreDefaultArgs: ['--hide-scrollbars'] });
  const sizes = {};
  for (const [size, viewport] of Object.entries(VIEWPORTS)) {
    const page = await shown.newPage({ viewport });
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    if (prefix === 'data-ui-') {
      await withRuntime(page, 'rask-hooks.ts');
      await page.evaluate(() => { document.body.style.paddingBottom = '100vh'; });
    }

    const kind = await page.evaluate(prefix => {
      const host = document.querySelector(`[data-preview-wrapper] :is([${prefix}dropdown], [${prefix}context])`);
      host.closest('[data-preview-wrapper]').setAttribute('data-pm', 'behind');
      host.firstElementChild.setAttribute('data-pm', 'trigger');
      host.firstElementChild.scrollIntoView({ block: 'center' });
      return host.hasAttribute(`${prefix}context`) ? 'context' : 'dropdown';
    }, prefix);
    await page.waitForTimeout(200);
    const place = () => page.evaluate(() => {
      const box = document.querySelector('[data-pm="behind"]').getBoundingClientRect();
      const html = getComputedStyle(document.documentElement);
      return { at: [window.scrollY, box.x, box.width], scrollbar: window.innerWidth > document.documentElement.clientWidth,
        lock: `${html.overflowY} | ${html.pointerEvents} | ${html.scrollbarGutter}` };
    });
    const before = await place();
    const box = await page.locator('[data-pm="trigger"]').boundingBox();
    const point = [Math.round(box.x + box.width / 2), Math.round(box.y + box.height / 2)];
    await page.mouse.click(point[0], point[1], { button: kind === 'context' ? 'right' : 'left' });
    if (kind === 'context' && prefix === 'data-ui-') {
      await page.evaluate(([x, y]) => {
        const area = document.querySelector('[data-pm="trigger"]').closest('[data-rask-contextmenu]');
        document.documentElement.style.setProperty('--rask-context-x', x + 'px');
        document.documentElement.style.setProperty('--rask-context-y', y + 'px');
        document.getElementById(area.getAttribute('data-rask-contextmenu')).showPopover();
      }, point);
    }

    await page.waitForTimeout(300);
    const during = await place();
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);
    const after = await place();
    const moved = step => step.at.map((value, i) => Math.round((value - before.at[i]) * 100) / 100).join(',');
    sizes[size] = { scrollbar: before.scrollbar, lock: during.lock,
      moved: `${moved(during)} then ${moved(after)}`, after: after.lock };
    await page.close();
  }

  await shown.close();
  return sizes;
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
  // A navigation menu's links are lit by :hover on both sides, and have no cursor.
  // Where focus is under the pointer is compared with the menu's own focus: not on a context menu (above).
  const lights = example => example.pointer?.map(step => (theirs.kind === 'context' ? step.lit : step));
  say('the pointer lights', lights(theirs), lights(mine));
  say('the diagonal to the flyout', theirs.diagonal, mine.diagonal);
  say('the pointer and the keys together', theirs.together, mine.together);
  say('a tap on each row', theirs.taps, mine.taps);
  say('the submenu\'s row tapped again', theirs.again, mine.again);
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

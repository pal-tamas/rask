// Where an open panel LANDS when there is no room for it where it asked to be.
//
// A docs page shows every example in the middle of a wide column, where a panel always fits. What an
// application has is a filter by the right edge, a user menu at the bottom of a sidebar, a select in the last
// row of a form, all of it again on a phone. So each family's open-state script hands this module its
// examples, and every one is opened with its trigger HELD at the edges of a phone and of a desktop — the host
// made `position: fixed` at that place, on Flux's live page and on the Rask parity page alike — and with more
// in the panel than there is room for. Where the panel then is in the viewport, and the height it was held
// to, must agree: Flux flips the side, flips the alignment, slides the panel back 5 px inside the edge and
// caps its height (the kit: rask-place.ts).
//
// Flux's side is measured once and kept beside its other measurements (`edges.json`; `--refresh` measures
// again, and so does any change to what is asked). Each step waits on STATE: the panel in the same box for
// three frames.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { root, screen } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

// What `hold` and `read` do to a page. Raised whenever that changes: Flux's kept measurements are then stale.
const HARNESS = 3;

/** A phone and a desktop. */
export const VIEWS = [[390, 844], [1920, 1080]];

// Where the host is held — its left and top, a negative number being its far edge's distance from the
// viewport's far edge, 'mid' the middle — and what is added to the panel (`fill`: width, height).
export const EDGES = [
  { name: 'in the middle', at: ['mid', 'mid'] },
  { name: 'at the left edge', at: [5, 'mid'] },
  { name: 'at the right edge', at: [-5, 'mid'] },
  { name: 'near the right edge', at: [-40, 'mid'] },
  { name: 'at the top', at: ['mid', 70] },
  { name: 'at the bottom', at: ['mid', -5] },
  { name: 'in the bottom right corner', at: [-5, -5] },
  { name: 'in the bottom left corner', at: [5, -5] },
  { name: 'in the top right corner', at: [-5, 70] },
  { name: 'taller than the room under it', at: ['mid', 'mid'], fill: [1, 1500] },
  { name: 'taller than the room, low in the viewport', at: ['mid', -120], fill: [1, 1500] },
  { name: 'taller than the room, high in the viewport', at: ['mid', 120], fill: [1, 1500] },
  { name: 'wider than the viewport', at: ['mid', 'mid'], fill: [2500, 1] },
  // Moved from the middle until the panel that opened there would end this far inside an edge: within the
  // 5 px a panel keeps from it, and just past it.
  { name: 'its bottom edge 3 px inside the viewport\'s', inside: ['bottom', 3] },
  { name: 'its bottom edge 6 px past the viewport\'s', inside: ['bottom', -6] },
  { name: 'its top edge 3 px inside the viewport\'s', inside: ['top', 3] },
  { name: 'its right edge 3 px inside the viewport\'s', inside: ['right', 3] },
  { name: 'its right edge 6 px past the viewport\'s', inside: ['right', -6] },
  { name: 'its left edge 3 px inside the viewport\'s', inside: ['left', 3] },
];

/**
 * Opens every host of `family` at every edge on both pages and prints one line per host and viewport.
 * Returns how many of them differ.
 *
 * family: { name, flux: side, rask: side, hosts: [index | { index, open, name }], views, edges, width, refresh, all }
 * side:   { url, host, panel, trigger, open, prepare } — `host` a selector matched in document order on that
 *         page, `panel` and `trigger` selectors inside the host (the trigger is its first child without one,
 *         and the host itself when the selector matches it), `open` a function (page, point) for a page that
 *         cannot open by the gesture itself, `prepare` a function (page) run once the page has loaded.
 */
export async function landings(browser, family) {
  const views = family.views ?? VIEWS;
  const edges = family.edges ?? EDGES;
  const hosts = family.hosts.map(host => (typeof host === 'number' ? { index: host } : host));
  const asked = JSON.stringify({ HARNESS, views, edges, hosts, width: family.width, host: family.flux.host, panel: family.flux.panel });
  const file = join(root, 'artifacts', 'flux-parity', 'flux', family.name, 'edges.json');
  let flux = existsSync(file) && !family.refresh ? JSON.parse(await readFile(file, 'utf8')) : null;
  if (!flux || flux.asked !== asked) {
    flux = { asked, ...await measure(browser, family, family.flux, views, edges, hosts) };
    await mkdir(join(root, 'artifacts', 'flux-parity', 'flux', family.name), { recursive: true });
    await writeFile(file, JSON.stringify(flux));
  }

  const rask = (await measure(browser, family, family.rask, views, edges, hosts, flux.inherited)).landed;
  let failures = 0;
  for (const [key, theirs] of Object.entries(flux.landed)) {
    const mine = rask[key] ?? [];
    const diffs = [];
    theirs.forEach((landed, i) => {
      const difference = differs(landed, mine[i]);
      if (difference) diffs.push(`${edges[i].name}: ${difference}`);
      else if (family.all) diffs.push(`${edges[i].name}: ${say(landed)}`);
    });
    const failed = diffs.some(d => d.includes(' vs '));
    failures += failed ? 1 : 0;
    console.log(`${failed ? 'FAIL' : 'ok  '} ${family.name} lands as Flux's, ${key} — ${edges.length} places${failed ? `, ${diffs.filter(d => d.includes(' vs ')).length} differ` : ''}`);
    for (const d of diffs) console.log(`       ${d}`);
  }

  return failures;
}

const round = value => Math.round(value * 100) / 100;

function say(landed) {
  if (!landed?.open) return 'not open';
  const [x, y, right, bottom] = landed.panel;
  return `x=${round(x)}..${round(right)} y=${round(y)}..${round(bottom)}${landed.cap === null ? '' : ` max-height ${round(landed.cap)}`}`;
}

// A pixel either way: the two pages lay the same panel out to within a rounding.
function differs(theirs, mine) {
  if (!mine) return `${say(theirs)} vs nothing measured`;
  const near = (a, b) => (a === null || b === null ? a === b : Math.abs(a - b) <= 1);
  const same = theirs.open === mine.open
    && (!theirs.open || (theirs.panel.every((v, i) => near(v, mine.panel[i])) && near(theirs.cap, mine.cap)));
  if (!same) return `${say(theirs)} vs ${say(mine)}`;
  // The harness's own check: a trigger that is not in the same place was not asked the same question.
  return theirs.trigger.every((v, i) => near(v, mine.trigger[i])) ? null
    : `the triggers are not in the same place: ${theirs.trigger.map(round)} vs ${mine.trigger.map(round)}`;
}

// `inherited`: Flux's side of each host, which makes this the Rask page — given the runtime's hooks, and the
// text its docs page hands down (font, size, line height), without which a panel is a pixel or two another size.
async function measure(browser, family, side, views, edges, hosts, inherited) {
  const hooks = Boolean(inherited);
  const landed = {};
  const found = {};
  for (const [width, height] of views) {
    for (const host of hosts) {
      const context = await browser.newContext(screen(width, height));
      // The day lib.mjs pins: a calendar that opens on today is another height in a month of six weeks.
      await context.clock.setFixedTime(new Date('2026-01-15T12:00:00Z'));
      const page = await context.newPage();
      await page.goto(side.url, { waitUntil: 'networkidle', timeout: 120000 });
      await page.evaluate(() => document.fonts.ready);
      // A parity page is static markup: what Flux does in script the kit asks the runtime's hooks for.
      if (hooks && !await page.evaluate(() => Boolean(globalThis.__raskHookSeam))) await withRuntime(page, 'rask-hooks.ts');
      if (side.prepare) await side.prepare(page);
      const key = `${width}x${height} #${host.index}${host.name ? ` ${host.name}` : ''}`;
      landed[key] = [];
      found[key] = await page.evaluate(inherit, { host: side.host, index: host.index, from: inherited?.[key] });
      for (const edge of edges) {
        landed[key].push(await one(page, family, side, host, edge, landed[key][0]));
      }

      await context.close();
    }
  }

  return { landed, inherited: found };
}

// Runs in the page. What a host inherits — read on Flux's page, written on Rask's.
function inherit({ host, index, from }) {
  const el = [...document.querySelectorAll(host)][index];
  const names = ['fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];
  if (!el) return null;
  if (from) Object.assign(el.style, from);
  const style = getComputedStyle(el);
  return Object.fromEntries(names.map(name => [name, style[name]]));
}

// `middle`: where this host's panel landed in the first place asked for, which an `inside` edge moves from.
async function one(page, family, side, host, edge, middle) {
  const held = await page.evaluate(hold, { side: { host: side.host, panel: side.panel, trigger: side.trigger }, index: host.index, edge, width: family.width, middle });
  if (!held) return { open: false, panel: [0, 0, 0, 0], trigger: [0, 0, 0, 0], cap: null };
  const point = [held[0] + held[2] / 2, held[1] + held[3] / 2];
  const how = host.open ?? 'click';
  await page.mouse.move(2, 2);
  if (side.open) await side.open(page, point, how);
  else if (how === 'hover') await page.mouse.move(...point);
  else if (how === 'context') await page.mouse.click(...point, { button: 'right' });
  else await page.mouse.click(...point);
  const result = await page.evaluate(read);
  await page.keyboard.press('Escape');
  await page.mouse.move(2, 2);
  await page.evaluate(shut);
  return result;
}

// Runs in the page. Holds one host at a place in the viewport; answers its trigger's box.
function hold({ side, index, edge, width, middle }) {
  const el = [...document.querySelectorAll(side.host)][index];
  if (!el) return null;
  const panel = side.panel ? el.querySelector(side.panel) : null;
  const trigger = !side.trigger ? el.firstElementChild : el.matches(side.trigger) ? el : el.querySelector(side.trigger);
  if (!panel || !trigger) return null;
  window.__placed = { panel, trigger };
  // Property by property: the host's own style attribute may be what names it an anchor.
  Object.assign(el.style, { position: 'fixed', margin: '0', zIndex: '2147483647', width: width ? `${width}px` : 'max-content', left: '0', top: '0' });
  panel.querySelector('[data-placement-fill]')?.remove();
  if (edge.fill) {
    // Into what scrolls: the list inside a panel that holds a search field over it, or the panel.
    const into = panel.querySelector('[role=listbox]') ?? panel;
    const fill = document.createElement('div');
    fill.setAttribute('data-placement-fill', '');
    fill.style.cssText = `width:${edge.fill[0]}px;height:${edge.fill[1]}px;flex:none`;
    into.append(fill);
  }

  const box = el.getBoundingClientRect();
  const room = [document.documentElement.clientWidth, document.documentElement.clientHeight];
  const place = (at, i) => (at === 'mid' ? Math.round((room[i] - [box.width, box.height][i]) / 2) : at < 0 ? room[i] + at - [box.width, box.height][i] : at);
  let [left, top] = (edge.at ?? ['mid', 'mid']).map(place);
  if (edge.inside && middle?.open) {
    const [to, by] = edge.inside;
    const [x, y, right, bottom] = middle.panel;
    if (to === 'bottom') top += room[1] - by - bottom;
    if (to === 'top') top += by - y;
    if (to === 'right') left += room[0] - by - right;
    if (to === 'left') left += by - x;
  }

  el.style.left = `${left}px`;
  el.style.top = `${top}px`;
  const at = trigger.getBoundingClientRect();
  return [at.x, at.y, at.width, at.height];
}

// Runs in the page. The panel once it has been in one box for three frames, or after two seconds of not being.
function read() {
  const { panel, trigger } = window.__placed;
  const shown = () => {
    try {
      return panel.matches(':popover-open') || (panel.tagName === 'DIALOG' && panel.open);
    } catch {
      return panel.getBoundingClientRect().width > 0;
    }
  };

  return new Promise(done => {
    let last = '';
    let same = 0;
    let frames = 0;
    const look = () => {
      const box = panel.getBoundingClientRect();
      const now = [shown(), box.x, box.y, box.width, box.height].join();
      same = now === last ? same + 1 : 0;
      last = now;
      if ((same >= 3 && frames >= 6 && shown()) || ++frames > 120) {
        const cap = parseFloat(getComputedStyle(panel).maxHeight);
        const at = trigger.getBoundingClientRect();
        // A max-height the panel does not reach holds it to nothing.
        done({ open: shown(), panel: [box.left, box.top, box.right, box.bottom], trigger: [at.left, at.top, at.right, at.bottom],
          cap: Number.isNaN(cap) || cap > box.height + 1 ? null : cap });
      } else requestAnimationFrame(look);
    };
    requestAnimationFrame(look);
  });
}

// Runs in the page. Waits for the panel to be closed, and closes one that Escape did not.
function shut() {
  const { panel } = window.__placed;
  const shown = () => {
    try {
      return panel.matches(':popover-open') || (panel.tagName === 'DIALOG' && panel.open);
    } catch {
      return false;
    }
  };

  return new Promise(done => {
    let frames = 0;
    const look = () => {
      if (!shown() && frames >= 3) done();
      else if (++frames > 60) {
        try { panel.hidePopover(); } catch { /* not a popover */ }
        done();
      } else requestAnimationFrame(look);
    };
    requestAnimationFrame(look);
  });
}

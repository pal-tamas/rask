#!/usr/bin/env node
// A sidebar narrowed to its rail, walked with a pointer: what parity.mjs cannot see, because nothing of it is
// on the page as loaded.
//
// 1. Flux's live demo (fluxui.dev/demo/sidebar-collapsible) and the Rask page of the same demo
//    (artifacts/flux-parity/rask/sidebar/collapsible-sidebar.html, given the runtime's hooks), light and dark:
//    every step must land the same box in the same colours — an item's tooltip while the sidebar is wide (opened,
//    not drawn) and in the rail, the collapse control's tooltip in both, the menu a group opens under the
//    pointer, its heading and rows, a row under the pointer, and the menu gone when the pointer leaves.
// 2. An application's menu (long-menu.html: twelve groups, eighty items, three folded, one current): the
//    sidebar scrolls inside itself and the page does not; what the rail does to it. Printed, and failed only
//    where the kit would be wrong whatever Flux does (a menu or a tooltip outside the viewport).
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/rail.mjs
//
// Exit code 1 on any difference.

import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

const pages = join(root, 'artifacts', 'flux-parity', 'rask', 'sidebar');
const sides = {
  flux: { url: 'https://fluxui.dev/demo/sidebar-collapsible', mark: 'flux', collapse: '[data-flux-sidebar-collapse] button' },
  rask: { url: pathToFileURL(join(pages, 'collapsible-sidebar.html')).href, mark: 'ui', collapse: '[data-ui-sidebar-collapse] label' },
};

const browser = await chromium().launch();
let failed = 0;

// Every popover showing: its words, its box, its ground and its edge. One that is open and not drawn has no box.
const showing = page => page.evaluate(() => [...document.querySelectorAll('[popover]')].filter(p => p.matches(':popover-open')).map(p => {
  const box = p.getBoundingClientRect();
  const style = getComputedStyle(p);
  const drawn = style.display !== 'none';
  return [p.getAttribute('role') ?? 'menu', drawn ? [box.x, box.y, box.width, box.height].map(Math.round).join(',') : 'not drawn',
    // The colour of an edge 0px wide is nobody's: not said.
    drawn ? `${style.backgroundColor} ${style.color} ${style.borderTopWidth}${style.borderTopWidth === '0px' ? '' : ` ${style.borderTopColor}`}` : ''].join(' ').replaceAll(' none)', ' 0)');
}).sort());

const look = (page, selector, index = 0) => page.locator(selector).nth(index).evaluate(node => {
  const box = node.getBoundingClientRect();
  const style = getComputedStyle(node);
  // `oklch(… none)` is `oklch(… 0)`: a hue that is missing and one that is zero draw the same grey.
  return `${[box.x, box.y, box.width, box.height].map(Math.round).join(',')} ${style.color} ${style.backgroundColor} ${style.padding}`.replaceAll(' none)', ' 0)');
});

async function open(side, scheme, width = 1280) {
  const context = await browser.newContext({ viewport: { width, height: 900 }, colorScheme: scheme });
  const page = await context.newPage();
  await page.goto(side.url, { waitUntil: 'networkidle', timeout: 90000 });
  if (side.mark === 'ui') await withRuntime(page, 'rask-hooks.ts');
  await page.evaluate(() => document.fonts.ready);
  return page;
}

const settle = page => page.waitForTimeout(350);
const away = async page => { await page.mouse.move(900, 500); await settle(page); };

// One side's walk, as lines. The same steps on both; the selectors differ only by the marker's prefix.
async function walk(side, scheme) {
  const page = await open(side, scheme);
  const m = side.mark;
  const item = `[data-${m}-sidebar-nav] > * > [data-${m}-sidebar-item]`;
  const group = `[data-${m}-sidebar-group-dropdown]`;
  const row = `${group} [data-${m}-menu] [data-${m}-sidebar-item]`;
  const lines = [];
  const say = (step, value) => lines.push(`${step}: ${Array.isArray(value) ? value.join(' | ') || 'nothing' : value}`);

  await page.locator(item).nth(1).hover(); await settle(page);
  say('wide, pointer on an item', await showing(page));
  await page.locator(side.collapse).locator('visible=true').first().hover(); await settle(page);
  say('wide, pointer on the collapse control', await showing(page));

  await page.locator(side.collapse).locator('visible=true').first().click();
  await away(page);
  for (const index of [0, 1, 2]) {
    await page.locator(item).nth(index).hover(); await settle(page);
    say(`rail, pointer on item ${index}`, await showing(page));
  }

  // From beside it, level with it: no item is crossed on the way, so no item's tooltip is left behind.
  const button = await page.locator(`${group} > button`).boundingBox();
  await page.mouse.move(400, button.y + button.height / 2); await settle(page);
  await page.mouse.move(button.x + button.width / 2, button.y + button.height / 2, { steps: 4 }); await settle(page);
  say('rail, pointer on the group', await showing(page));
  // A menu the pointer opened takes no focus and locks nothing: Flux leaves both as they were.
  say('focus and the page behind the group\'s menu', await page.evaluate(() => {
    const html = getComputedStyle(document.documentElement);
    const inMenu = [...document.querySelectorAll('[popover]')].some(p => p.matches(':popover-open') && p.contains(document.activeElement));
    return `focus ${inMenu ? 'in the menu' : 'where it was'}, ${html.overflowY} ${html.pointerEvents}`;
  }));
  say('the group button under the pointer', await look(page, `${group} > button`));
  say('the heading', await look(page, `${group} [data-${m}-menu-heading]`));
  say('row 0', await look(page, row, 0));
  // Straight across the gap, then down the menu: a diagonal would leave both, and close it on either side.
  await page.mouse.move(button.x + button.width + 20, button.y + button.height / 2); await settle(page);
  const second = await page.locator(row).nth(1).boundingBox();
  await page.mouse.move(second.x + 40, second.y + second.height / 2, { steps: 5 }); await settle(page);
  say('row 1 under the pointer', await look(page, row, 1));
  say('the rows lit under the pointer', await page.locator(row).evaluateAll(rows => rows.map((r, i) => (r.hasAttribute('data-active') ? i : -1)).filter(i => i >= 0).join(',') || 'none'));
  say('pointer on row 1', await showing(page));
  await away(page);
  say('pointer gone', await showing(page));

  await page.locator(`[data-${m}-sidebar-header]`).hover(); await settle(page);
  say('rail, pointer on the header', await showing(page));
  await page.locator(`[data-${m}-sidebar-profile]`).first().hover(); await settle(page);
  say('rail, pointer on the profile', await showing(page));
  await page.context().close();
  return lines;
}

for (const scheme of ['light', 'dark']) {
  const [flux, rask] = [await walk(sides.flux, scheme), await walk(sides.rask, scheme)];
  flux.forEach((line, index) => {
    const same = line === rask[index];
    if (!same) failed++;
    console.log(same ? `ok   ${scheme} ${line.split(':')[0]}` : `FAIL ${scheme} ${line.split(':')[0]}\n       flux ${line}\n       rask ${rask[index]}`);
  });
}

// ----- An application's menu ------------------------------------------------------------------------
{
  const page = await open({ url: pathToFileURL(join(pages, 'long-menu.html')).href, mark: 'ui' }, 'light');
  const check = (ok, text) => { if (!ok) failed++; console.log(`${ok ? 'ok  ' : 'FAIL'} long menu: ${text}`); };
  const note = text => console.log(`note long menu: ${text}`);
  const sidebar = () => page.locator('[data-ui-sidebar]').evaluate(node => {
    const box = node.getBoundingClientRect();
    return { width: box.width, height: box.height, client: node.clientHeight, scroll: node.scrollHeight, top: node.scrollTop,
      page: document.documentElement.scrollHeight, view: innerHeight };
  });
  const y = selector => page.locator(selector).first().evaluate(node => Math.round(node.getBoundingClientRect().y));

  const counts = await page.evaluate(() => ({
    groups: document.querySelectorAll('details[data-ui-sidebar-group]').length,
    folded: document.querySelectorAll('details[data-ui-sidebar-group]:not([open])').length,
    items: document.querySelectorAll('details[data-ui-sidebar-group] [data-ui-sidebar-item], [data-ui-sidebar-nav] > * > [data-ui-sidebar-item]').length,
    current: document.querySelectorAll('details [aria-current=page]').length,
  }));
  check(counts.groups === 12 && counts.items === 80 && counts.folded === 3 && counts.current === 1,
    `${counts.groups} groups, ${counts.items} items, ${counts.folded} folded, ${counts.current} current`);

  const wide = await sidebar();
  check(wide.height === wide.view && wide.scroll > wide.client, `the sidebar is the viewport's height (${wide.height}) and scrolls inside itself (${wide.scroll} of content)`);
  check(wide.page === wide.view, 'the page itself does not scroll');

  const current = 'details [aria-current=page]';
  note(`the current item is at y=${await y(current)} as loaded (viewport ${wide.view}): ${await y(current) < wide.view ? 'in view' : 'below the fold — neither Flux nor the kit scrolls to it'}`);
  await page.locator(current).scrollIntoViewIfNeeded();
  const scrolled = await sidebar();
  note(`scrolled to it (scrollTop ${scrolled.top}): the header is at y=${await y('[data-ui-sidebar-header]')}, the profile at y=${await y('[data-ui-sidebar-profile]')} — the whole sidebar scrolls, as Flux's does`);

  // The first top-level item must not move when the rail is toggled; what is below the groups does.
  await page.locator('[data-ui-sidebar]').evaluate(node => { node.scrollTop = 0; });
  const before = { home: await y('[data-ui-sidebar-nav] [data-ui-sidebar-item]'), main: await page.locator('[data-ui-main]').evaluate(node => node.getBoundingClientRect().x) };
  await page.locator('[data-ui-sidebar-collapse] label').locator('visible=true').first().click();
  await away(page);
  const rail = await sidebar();
  const after = { home: await y('[data-ui-sidebar-nav] [data-ui-sidebar-item]'), main: await page.locator('[data-ui-main]').evaluate(node => node.getBoundingClientRect().x) };
  check(rail.width === 56 && before.home === after.home, `the rail is 56px and the first item stays at y=${after.home}`);
  note(`the page's column moves from x=${before.main} to x=${after.main}; the rail holds ${rail.scroll}px of icons in ${rail.client}px`);
  const icons = await page.locator('[data-ui-sidebar-group-dropdown] > button').locator('visible=true').count();
  check(icons === 12, `${icons} group icons in the rail`);

  // The last group's menu and the last item's tooltip must be drawn inside the viewport.
  for (const [name, selector] of [['the last group', '[data-ui-sidebar-group-dropdown] > button'], ['the last item', '[data-ui-sidebar-nav] > * > [data-ui-sidebar-item]']]) {
    const target = page.locator(selector).last();
    await target.scrollIntoViewIfNeeded();
    const box = await target.boundingBox();
    await away(page);
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2, { steps: 3 }); await settle(page);
    const shown = await page.evaluate(() => [...document.querySelectorAll('[popover]')].filter(p => p.matches(':popover-open') && getComputedStyle(p).display !== 'none').map(p => {
      const b = p.getBoundingClientRect();
      return { x: b.x, y: b.y, bottom: b.bottom, right: b.right, words: p.textContent.trim().replace(/\s+/g, ' ').slice(0, 24) };
    }));
    check(shown.length === 1 && shown[0].y >= 0 && shown[0].bottom <= 900 && shown[0].x >= 48,
      `${name} (y=${Math.round(box.y)}) opens "${shown[0]?.words}" at ${shown[0] ? [shown[0].x, shown[0].y].map(Math.round) : 'nowhere'}, inside the viewport`);
  }

  // A phone: the same menu slid over the page scrolls inside the overlay.
  await page.context().close();
  const phone = await open({ url: pathToFileURL(join(pages, 'long-menu.html')).href, mark: 'ui' }, 'light', 390);
  await phone.locator('[data-ui-sidebar-toggle]').first().click(); await settle(phone);
  const overlay = await phone.locator('[data-ui-sidebar]').evaluate(node => ({ x: node.getBoundingClientRect().x, client: node.clientHeight, scroll: node.scrollHeight, overflow: getComputedStyle(node).overflowY }));
  check(overlay.x === 0 && overlay.scroll > overlay.client && overlay.overflow === 'auto', `on a phone the overlay is on screen and scrolls inside itself (${overlay.scroll} in ${overlay.client})`);
  await phone.context().close();
}

await browser.close();
console.log(failed ? `\nflux rail: ${failed} difference(s).` : '\nflux rail: the rail behaves as Flux\'s.');
process.exit(failed ? 1 : 0);

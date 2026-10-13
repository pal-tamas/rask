#!/usr/bin/env node
// What Flux tells its SERVER while a reader uses a component: nothing, for anything that only changes what is drawn.
//
// Flux handles a pointer over a row, a cursor moving, a popup opening or closing and a view being paged in its
// own script; Livewire hears a bound value changing and an action the page wrote. This walks Flux's live docs
// page of each component with a real pointer and keyboard and counts the Livewire update requests each kind of
// gesture caused. It is the reference the kit's frame budget is held to
// (tests/Rask.Server.E2E.Tests/KitDrawsWithoutAskingTests.cs): a gesture that costs Flux no request may cost
// the kit no frame.
//
// Usage:  node scripts/flux/requests.mjs [slug ...]         # every component page when none is named
//
// Prints one line per (page, gesture) with the number of requests, and writes
// artifacts/flux-parity/requests.tsv. Only the public docs pages are read.

import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { chromium, root } from './lib.mjs';

const SLUGS = [
  'accordion', 'autocomplete', 'avatar', 'badge', 'breadcrumbs', 'button', 'calendar', 'callout', 'card', 'chart',
  'checkbox', 'color-picker', 'command', 'context', 'date-picker', 'dropdown', 'editor', 'field', 'file-upload',
  'input', 'kanban', 'modal', 'navbar', 'otp-input', 'pagination', 'pillbox', 'popover', 'profile', 'progress',
  'radio', 'select', 'separator', 'skeleton', 'slider', 'switch', 'table', 'tabs', 'textarea', 'time-picker',
  'timeline', 'toast', 'tooltip',
];

const slugs = process.argv.slice(2).length ? process.argv.slice(2) : SLUGS;
const browser = await chromium().launch();
const rows = [];

// The centre of every part a pointer or the keyboard can act on, inside the page's content, in document order.
const parts = (selector, most) => {
  const seen = [];
  for (const el of document.querySelectorAll(selector)) {
    if (el.closest('header, footer, aside, [data-flux-sidebar], [data-flux-header]')) continue;
    const box = el.getBoundingClientRect();
    if (box.width < 2 || box.height < 2 || getComputedStyle(el).visibility === 'hidden') continue;
    seen.push([box.left + scrollX + box.width / 2, box.top + scrollY + box.height / 2]);
    if (seen.length === most) break;
  }
  return seen;
};

async function at(page, [x, y]) {
  await page.evaluate(top => scrollTo(0, top), Math.max(0, y - 400));
  const top = await page.evaluate(() => scrollY);
  return [x, y - top];
}

const GESTURES = {
  'pointer over every part': async page => {
    const all = await page.evaluate(([f, s, n]) => new Function('return ' + f)()(s, n),
      [parts.toString(), 'main a, main button, main input, main select, main textarea, main summary, main label, main th, main [role], main [tabindex], main svg', 120]);
    for (const part of all) {
      const [x, y] = await at(page, part);
      await page.mouse.move(x, y, { steps: 3 });
    }
  },
  'focus and blur by Tab': async page => {
    await page.evaluate(() => { scrollTo(0, 0); document.querySelector('main')?.setAttribute('tabindex', '-1'); document.querySelector('main')?.focus(); });
    for (let i = 0; i < 40; i++) await page.keyboard.press('Tab');
    for (let i = 0; i < 10; i++) await page.keyboard.press('Shift+Tab');
  },
  'open a popup, walk it with the pointer and the keys, close it': async page => {
    const triggers = await page.evaluate(([f, s, n]) => new Function('return ' + f)()(s, n),
      [parts.toString(), 'main [aria-haspopup], main button[aria-expanded], main [role=combobox], main ui-select > button, main ui-tooltip > button', 14]);
    for (const trigger of triggers) {
      const [x, y] = await at(page, trigger);
      await page.mouse.click(x, y);
      await page.waitForTimeout(150);
      for (const key of ['ArrowDown', 'ArrowDown', 'ArrowUp', 'End', 'Home']) await page.keyboard.press(key);
      const inside = await page.evaluate(() => [...document.querySelectorAll(':popover-open [role], :popover-open button, dialog[open] [role], dialog[open] button')]
        .slice(0, 8).map(el => { const b = el.getBoundingClientRect(); return [b.left + b.width / 2, b.top + b.height / 2]; }));
      for (const [px, py] of inside) await page.mouse.move(px, py, { steps: 3 });
      await page.keyboard.press('Escape');
      await page.waitForTimeout(100);
      await page.keyboard.press('Escape');
    }
  },
  'page or switch a view': async page => {
    const all = await page.evaluate(([f, s, n]) => new Function('return ' + f)()(s, n),
      [parts.toString(), 'main [role=tab], main summary, main [data-flux-accordion-heading], main button[aria-label*="month" i], main button[aria-label*="next" i], main button[aria-label*="previous" i]', 24]);
    for (const part of all) {
      const [x, y] = await at(page, part);
      await page.mouse.click(x, y);
      await page.keyboard.press('ArrowRight');
      await page.keyboard.press('ArrowLeft');
    }
  },
  'typing in a field, focus left in it': async page => {
    const all = await page.evaluate(([f, s, n]) => new Function('return ' + f)()(s, n),
      [parts.toString(), 'main input:not([type=checkbox]):not([type=radio]):not([type=range]):not([type=file]):not([disabled]):not([readonly]), main textarea:not([disabled])', 8]);
    for (const part of all) {
      const [x, y] = await at(page, part);
      await page.mouse.click(x, y);
      await page.keyboard.type('al');
      await page.keyboard.press('Escape');
    }
  },
  'drag, still held, then let go': async page => {
    const all = await page.evaluate(([f, s, n]) => new Function('return ' + f)()(s, n),
      [parts.toString(), 'main [role=slider], main input[type=range], main ui-chart, main svg[data-flux-chart-svg]', 6]);
    for (const part of all) {
      const [x, y] = await at(page, part);
      await page.mouse.move(x - 30, y);
      await page.mouse.down();
      await page.mouse.move(x + 60, y, { steps: 12 });
      await page.mouse.up();
    }
  },
};

// Four pages at a time: each is a page of someone else's site, and the walk is mostly waiting on it.
const queue = [...slugs];
await Promise.all([0, 1, 2, 3].map(async () => {
  for (let slug = queue.shift(); slug; slug = queue.shift()) {
    await walk(slug);
  }
}));

async function walk(slug) {
  for (const [gesture, run] of Object.entries(GESTURES)) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
    const page = await context.newPage();
    let requests = 0;
    let posts = 0;
    let counting = false;
    page.on('request', request => {
      if (counting && request.method() === 'POST') { posts++; if (/livewire[^/]*\/update/.test(request.url())) requests++; }
    });
    try {
      await page.goto(`https://fluxui.dev/components/${slug}`, { waitUntil: 'networkidle' });
      counting = true;
      await run(page);
      await page.waitForTimeout(1200);
      rows.push([slug, gesture, requests, posts]);
    } catch (error) {
      rows.push([slug, gesture, 'failed: ' + String(error.message).split('\n')[0]]);
    }
    console.log(rows.at(-1).join('\t'));
    await context.close();
  }
}

await browser.close();
const out = join(root, 'artifacts', 'flux-parity');
await mkdir(out, { recursive: true });
rows.sort((a, b) => a[0].localeCompare(b[0]));
await writeFile(join(out, 'requests.tsv'), rows.map(row => row.join('\t')).join('\n') + '\n');

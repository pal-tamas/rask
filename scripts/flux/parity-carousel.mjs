#!/usr/bin/env node
// Walks Flux UI's carousel and the kit's with the same pointer and the same keys, and holds every step equal.
//
// `parity.mjs carousel` compares the page as it loads: every carousel at its start. What a carousel IS only
// shows once it moves, so this drives each of Flux's eight live carousels and its Rask twin — the static parity
// page with the runtime's carousel hook added (runtime.mjs), or a running site with `--live <url>` — and writes
// one line per step: where the track stands, which slide and which indicator are selected, what the root and
// each arrow say (data-at-start / data-at-end, disabled, how visible the arrow is). The two transcripts must
// be equal line for line.
//
// It waits on STATE, never on a delay: a step is over when the track has stood still for a few frames.
// Autoplay is the exception — a clock is what it is — and is held to the half second.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages   # writes the Rask page
//         node scripts/flux/parity-carousel.mjs
//         node scripts/flux/parity-carousel.mjs --live http://localhost:5000/ui/carousel/
//         node scripts/flux/parity-carousel.mjs --record                # Flux alone, every line printed
//         node scripts/flux/parity-carousel.mjs --view 390x844          # on a phone: taps and a swipe
//
// On a phone there is no pointer to hover with and no keyboard for a button: a press is a tap, the drag is a
// swipe (a native scroller scrolls under a finger on both sides), and the hover that holds autoplay is a tap
// on the slide. The key steps, the tab stops and the focus that holds autoplay are a desktop's and are skipped.

import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, press as pressAt, root, viewOf } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

const args = process.argv.slice(2);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
const record = args.includes('--record');
const live = option('live');
const { screen, phone } = viewOf(args);

const FLUX = {
  url: 'https://fluxui.dev/components/carousel',
  root: '[data-flux-carousel]', track: '[data-flux-carousel-track]', slide: '[data-flux-carousel-slide]',
  arrow: direction => `ui-carousel-button[direction="${direction}"]`, dots: 'ui-carousel-indicators', controls: '[data-flux-carousel-controls]',
};
const raskPage = join(root, 'artifacts', 'flux-parity', 'rask', 'carousel.html');
const RASK = {
  url: live ?? pathToFileURL(raskPage).href,
  root: '[data-ui-carousel]', track: '[data-ui-carousel-track]', slide: '[data-ui-carousel-slide]',
  arrow: direction => `[data-direction="${direction}"]`, dots: '[data-rask-carousel-indicators]', controls: '[data-ui-carousel-controls]',
};
if (!record && !live && !existsSync(raskPage)) {
  console.error(`flux carousel: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

// Flux's page, in the order it draws them. The kit's page and the site's demo keep the same order.
const BASIC = 0, DOTS = 1, AUTO = 2, EXTERNAL = 3, OVERLAP = 5, PAGED = 7;

const browser = await chromium().launch();
const flux = await walk(FLUX);
const rask = record ? flux : await walk(RASK);
await browser.close();

let failed = 0;
for (let i = 0; i < Math.max(flux.length, rask.length); i++) {
  const same = flux[i] === rask[i];
  if (!same) failed++;
  if (record || !same) console.log(same ? `     ${flux[i]}` : `FAIL flux: ${flux[i]}\n     rask: ${rask[i]}`);
}

console.log(record
  ? `flux carousel: ${flux.length} steps recorded.`
  : failed ? `flux carousel: ${failed} of ${flux.length} steps differ.` : `flux carousel: all ${flux.length} steps match Flux.`);
process.exit(failed ? 1 : 0);

async function walk(side) {
  const context = await browser.newContext(screen);
  const page = await context.newPage();
  await page.goto(side.url, { waitUntil: 'networkidle' });
  if (side === RASK && !live) await withRuntime(page, 'rask-carousel.ts');
  await page.locator(side.root).first().waitFor({ timeout: live ? 60000 : 15000 });
  await page.evaluate(() => document.fonts.ready);

  const lines = [];
  const carousel = index => page.locator(side.root).nth(index);
  // The arrows of a carousel are its own, or — with none of its own — the first external controls on the page.
  const arrow = async (index, direction) => {
    const own = carousel(index).locator(side.arrow(direction)).locator('button');
    return await own.count() ? own.first() : page.locator(side.controls).locator(side.arrow(direction)).locator('button').first();
  };

  const state = (index, selectors) => carousel(index).evaluate((el, s) => {
    const track = el.querySelector(s.track);
    const slides = [...track.querySelectorAll(s.slide)];
    const dots = [...el.querySelectorAll(`${s.dots} button`)].filter(b => !b.closest('template'));
    const flags = node => (node.hasAttribute('data-at-start') ? 'S' : '-') + (node.hasAttribute('data-at-end') ? 'E' : '-');
    const arrowOf = direction => {
      const name = el.getAttribute('data-name') ?? el.getAttribute('name');
      const wrapper = el.querySelector(s[direction])
        ?? [...document.querySelectorAll(`${s.controls} ${s[direction]}`)].find(w => !name || (w.closest(s.controls).getAttribute('data-name') ?? w.getAttribute('name')) === name);
      if (!wrapper) return 'none';
      const button = wrapper.querySelector('button');
      const style = getComputedStyle(wrapper);
      return `${flags(wrapper)} ${button.disabled ? 'off' : 'on'} opacity=${style.opacity} pointer=${style.pointerEvents}`;
    };
    const max = track.scrollWidth - track.clientWidth;
    return [
      `at=${max ? Math.round(100 * track.scrollLeft / max) : 0}%`,
      `slide=${slides.findIndex(x => x.hasAttribute('data-selected')) + 1}/${slides.length}`,
      `root=${flags(el)}`,
      `prev[${arrowOf('previous')}]`,
      `next[${arrowOf('next')}]`,
      dots.length ? `dot=${dots.findIndex(d => d.hasAttribute('data-selected')) + 1} current=${dots.findIndex(d => d.getAttribute('aria-current') === 'true') + 1}` : 'dots=none',
    ].join(' ');
  }, { track: selectors.track, slide: selectors.slide, dots: selectors.dots, controls: selectors.controls, previous: selectors.arrow('previous'), next: selectors.arrow('next') });

  // Over when the track has stood on one spot for six frames and no transition is running: an arrow that
  // fades may stand outside the carousel, among the external controls. A spinner turns for ever and a
  // scroll-driven fade runs for as long as its track exists; neither is waited for.
  const settle = index => carousel(index).evaluate((el, selector) => new Promise(resolve => {
    const track = el.querySelector(selector);
    let last = track.scrollLeft, still = 0, frames = 0;
    const tick = () => {
      still = track.scrollLeft === last ? still + 1 : 0;
      last = track.scrollLeft;
      if (still >= 6 && document.getAnimations().every(a => a.playState !== 'running' || !(a.timeline instanceof DocumentTimeline) || a.effect?.getComputedTiming().iterations === Infinity)) return resolve();
      if (++frames > 600) return resolve();
      requestAnimationFrame(tick);
    };
    requestAnimationFrame(tick);
  }), side.track);

  // Acts, then waits for the track to leave its place: a scroll starts a frame or more after what asked for it,
  // and a track that has not started yet looks exactly like one that has come to rest. One that never leaves
  // (an arrow at its end, a key that is not the carousel's) is given a second and a half to show it.
  const moving = async (index, act) => {
    const from = await carousel(index).locator(side.track).evaluate(t => t.scrollLeft);
    await act();
    await page.waitForFunction(([selector, at, left]) => document.querySelectorAll(selector)[at].scrollLeft !== left,
      [side.track, index, from], { timeout: 1500 }).catch(() => {});
  };
  const step = async (index, name, act) => {
    if (act) await act();
    await settle(index);
    lines.push(`#${index} ${name.padEnd(34)} ${await state(index, side)}`);
  };
  // A click, or a tap on a phone.
  const hit = (target, options) => (phone ? target.tap(options) : target.click(options));
  // An arrow with nowhere to go is disabled, or lets the pointer through: the press is made and nothing follows.
  const press = async (index, direction) => {
    const button = await arrow(index, direction);
    if (await button.isDisabled()) return;
    await moving(index, () => hit(button, { timeout: 2000 }).catch(() => {}));
  };
  // A phone has no pointer to park.
  const park = () => (phone ? undefined : page.mouse.move(2, 2));

  // The plain carousel: both arrows, each end, the keyboard on an arrow, a scroll by hand and a drag over the track.
  await carousel(BASIC).scrollIntoViewIfNeeded();
  await park();
  await step(BASIC, 'at rest');
  await step(BASIC, 'next', () => press(BASIC, 'next'));
  await step(BASIC, 'next again (the end)', () => press(BASIC, 'next'));
  await step(BASIC, 'previous', () => press(BASIC, 'previous'));
  if (!phone) await step(BASIC, 'Enter on next', async () => { await (await arrow(BASIC, 'next')).focus(); await moving(BASIC, () => page.keyboard.press('Enter')); });
  await step(BASIC, 'previous to the start', async () => { await press(BASIC, 'previous'); await settle(BASIC); await press(BASIC, 'previous'); });
  if (!phone) {
    await step(BASIC, 'Space on next', async () => { await (await arrow(BASIC, 'next')).focus(); await moving(BASIC, () => page.keyboard.press('Space')); });
    await step(BASIC, 'ArrowRight on next', () => page.keyboard.press('ArrowRight'));
    await step(BASIC, 'ArrowLeft on next', () => page.keyboard.press('ArrowLeft'));
    await step(BASIC, 'End on next', () => page.keyboard.press('End'));
    await step(BASIC, 'Home on next', () => page.keyboard.press('Home'));
    lines.push(`#${BASIC} focus after the keys              ${await page.evaluate(() => document.activeElement?.getAttribute('aria-label') ?? document.activeElement?.tagName)}`);
  }
  // A wheel and a swipe are the browser's own scrolling of a native scroller on both sides, and a headless
  // wheel lands differently from one run to the next: the track is scrolled by hand instead, which is what
  // either ends in, and the step holds what follows a scroll nobody pressed a button for.
  await step(BASIC, 'scrolled to the end by hand', () => moving(BASIC, () =>
    carousel(BASIC).locator(side.track).evaluate(t => t.scrollTo({ left: t.scrollWidth, behavior: 'instant' }))));
  await step(BASIC, 'scrolled to the start by hand', () => moving(BASIC, () =>
    carousel(BASIC).locator(side.track).evaluate(t => t.scrollTo({ left: 0, behavior: 'instant' }))));
  await step(BASIC, phone ? 'a swipe across the track' : 'a pointer drag across the track', async () => {
    const box = await carousel(BASIC).locator(side.track).boundingBox();
    const [from, to, y] = [box.x + box.width * 0.8, box.x + box.width * 0.2, box.y + box.height / 2];
    if (phone) return moving(BASIC, () => swipe(page, from, to, y));
    await page.mouse.move(from, y);
    await page.mouse.down();
    await page.mouse.move(to, y, { steps: 8 });
    await page.mouse.up();
  });
  if (!phone) lines.push(`#${BASIC} tab stops inside                  ${await tabStops(page, carousel(BASIC))}`);

  // Indicators: each goes to its slide, and follows a move made any other way.
  await carousel(DOTS).scrollIntoViewIfNeeded();
  await park();
  const dot = n => carousel(DOTS).locator(`${side.dots} button`).nth(n);
  await step(DOTS, 'at rest');
  await step(DOTS, 'third indicator', () => moving(DOTS, () => hit(dot(2))));
  await step(DOTS, 'first indicator', () => moving(DOTS, () => hit(dot(0))));
  if (!phone) await step(DOTS, 'second indicator by Enter', async () => { await dot(1).focus(); await moving(DOTS, () => page.keyboard.press('Enter')); });
  await step(DOTS, 'scrolled to the end by hand', () => carousel(DOTS).locator(side.track).evaluate(t => t.scrollTo({ left: t.scrollWidth, behavior: 'instant' })));

  // External controls: they dim instead of leaving, and move the carousel that carries their name.
  await carousel(EXTERNAL).scrollIntoViewIfNeeded();
  await park();
  await step(EXTERNAL, 'at rest');
  await step(EXTERNAL, 'external next', () => press(EXTERNAL, 'next'));
  await step(EXTERNAL, 'external next (the end)', () => press(EXTERNAL, 'next'));
  await step(EXTERNAL, 'external previous', () => press(EXTERNAL, 'previous'));

  // Overlap: the same carousel with its arrows half over the edge.
  await carousel(OVERLAP).scrollIntoViewIfNeeded();
  await park();
  await step(OVERLAP, 'at rest');
  await step(OVERLAP, 'next', () => press(OVERLAP, 'next'));
  await step(OVERLAP, 'previous', () => press(OVERLAP, 'previous'));

  // Page advance: an arrow moves by the slides in view.
  await carousel(PAGED).scrollIntoViewIfNeeded();
  await park();
  await step(PAGED, 'at rest');
  await step(PAGED, 'next page', () => press(PAGED, 'next'));
  await step(PAGED, 'next page again', () => press(PAGED, 'next'));
  await step(PAGED, 'next page to the end', async () => { await press(PAGED, 'next'); await settle(PAGED); await press(PAGED, 'next'); });
  await step(PAGED, 'previous page', () => press(PAGED, 'previous'));

  // Autoplay, on its own clock: from a fresh load, pointer parked, then held under the pointer, then let go.
  await page.reload({ waitUntil: 'networkidle' });
  if (side === RASK && !live) await withRuntime(page, 'rask-carousel.ts');
  await page.locator(side.root).first().waitFor({ timeout: live ? 60000 : 15000 });
  await carousel(AUTO).scrollIntoViewIfNeeded();
  await park();
  const selected = () => carousel(AUTO).evaluate((el, s) => [...el.querySelectorAll(s)].findIndex(x => x.hasAttribute('data-selected')) + 1, side.slide);
  // A move is over when the track rests again: going back to the first slide passes over the second on its way.
  let resting = await selected(), moved = Date.now();
  const move = async ms => {
    const started = Date.now();
    while (await selected() === resting) {
      if (Date.now() - started > ms) return 'never';
      await page.waitForTimeout(40);
    }
    const since = Date.now() - moved;
    moved = Date.now();
    await settle(AUTO);
    resting = await selected();
    return `${Math.round(since / 500) / 2}s to slide ${resting}`;
  };
  await move(12000);
  lines.push(`#${AUTO} autoplay: the next move            ${await move(12000)}`);
  lines.push(`#${AUTO} autoplay: and the one after       ${await move(12000)}`);
  lines.push(`#${AUTO} autoplay: and the one after that  ${await move(12000)}`);
  // A phone's tap stands in for the hover: the browser makes a mouse that stays where the finger was.
  const box = await carousel(AUTO).locator(side.track).boundingBox();
  if (phone) await pressAt(page, box.x + box.width / 2, box.y + box.height / 2, phone);
  else await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  lines.push(`#${AUTO} autoplay: ${phone ? 'after a tap on it, 7s ' : 'under the pointer, 7s'}    ${await move(7000)}`);
  if (phone) await pressAt(page, 2, 2, phone);
  else await park();
  moved = Date.now();
  lines.push(`#${AUTO} autoplay: ${phone ? 'after a tap outside ' : 'after the pointer left'}  ${await move(12000)}`);
  if (!phone) {
    await (await arrow(AUTO, 'previous')).focus();
    lines.push(`#${AUTO} autoplay: focus on an arrow, 7s    ${await move(7000)}`);
    await page.evaluate(() => document.activeElement?.blur());
    moved = Date.now();
    lines.push(`#${AUTO} autoplay: after focus left        ${await move(12000)}`);
    // A press on a slide ends it for good, as the tap above does on a phone.
    await page.mouse.click(box.x + box.width / 2, box.y + box.height / 2);
    await park();
    moved = Date.now();
    lines.push(`#${AUTO} autoplay: pressed, then left, 7s  ${await move(7000)}`);
  }
  await context.close();

  const calm = await browser.newContext({ ...screen, reducedMotion: 'reduce' });
  const still = await calm.newPage();
  await still.goto(side.url, { waitUntil: 'networkidle' });
  if (side === RASK && !live) await withRuntime(still, 'rask-carousel.ts');
  await still.locator(side.root).first().waitFor({ timeout: live ? 60000 : 15000 });
  await still.locator(side.root).nth(AUTO).scrollIntoViewIfNeeded();
  await still.waitForTimeout(8000);
  lines.push(`#${AUTO} autoplay: reduced motion, 8s      slide ${await still.locator(side.root).nth(AUTO).evaluate((el, s) => [...el.querySelectorAll(s)].findIndex(x => x.hasAttribute('data-selected')) + 1, side.slide)}`);
  await calm.close();
  return lines;
}

// A finger drawn slowly across a track from one x to another, and held still before it lifts, so the browser
// flings nothing: where the track ends is where its snapping puts it. Playwright's touchscreen only taps.
async function swipe(page, from, to, y) {
  const cdp = await page.context().newCDPSession(page);
  const touch = (type, x) => cdp.send('Input.dispatchTouchEvent', { type, touchPoints: x === undefined ? [] : [{ x, y }] });
  await touch('touchStart', from);
  for (let i = 1; i <= 10; i++) {
    await touch('touchMove', from + (to - from) * i / 10);
    await page.waitForTimeout(16);
  }

  await page.waitForTimeout(100);
  await touch('touchEnd');
  await cdp.detach();
}

// What Tab reaches inside a carousel, in order: each stop by its tag and its name.
async function tabStops(page, scope) {
  await scope.evaluate(el => {
    const before = document.createElement('button');
    before.id = 'parity-before';
    el.before(before);
    before.focus();
  });
  const stops = [];
  for (let i = 0; i < 8; i++) {
    await page.keyboard.press('Tab');
    const stop = await scope.evaluate(el => (el.contains(document.activeElement)
      ? `${document.activeElement.tagName.toLowerCase()}${document.activeElement.getAttribute('aria-label') ? `(${document.activeElement.getAttribute('aria-label')})` : ''}`
      : null));
    if (!stop) break;
    stops.push(stop);
  }

  await page.evaluate(() => document.getElementById('parity-before')?.remove());
  return stops.join(' > ') || 'none';
}

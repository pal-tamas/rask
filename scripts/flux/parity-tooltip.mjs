#!/usr/bin/env node
// Holds Ui.Tooltip to Flux's tooltip in the state parity.mjs cannot see: SHOWN.
//
// parity.mjs measures a page as it loads, and a tooltip that has just loaded is closed. This shows each
// tooltip — hovers its trigger, or clicks it when it is toggleable — on Flux's live page and on the page
// TooltipShownParity wrote (artifacts/flux-parity/rask/tooltip-shown.html), and compares the box that
// appears: its size, where it sits against its trigger, every computed style lib.mjs lists, and its
// children (the kbd, the paragraphs). Then it walks both through the same pointer and keyboard script
// and compares what shows and hides it.
//
// Cases pair by name. Flux's page shows seven of them; the props it documents without an example
// (align, gap, offset, interactive, disabled, a trigger that is not a button) are built in Flux's page
// from its own <ui-tooltip>, around the same trigger the Rask page uses, so its own script places them.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-tooltip.mjs [--all]
//
// Exit code 1 on any difference that is not listed in KNOWN below. Screenshots land in
// artifacts/flux-parity/shown/.

import { mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root, STYLES } from './lib.mjs';

const FLUX = 'https://fluxui.dev/components/tooltip';
const out = join(root, 'artifacts', 'flux-parity');
const raskPage = join(out, 'rask', 'tooltip-shown.html');
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

const SAVE = '<button type="button" style="width:120px;height:40px;border:1px solid gray">Save file</button>';
const SMALL = '<button type="button" style="width:40px;height:40px;border:1px solid gray">o</button>';
const WIDE = 'A fairly wide tooltip text here';

// name · where Flux has it (an example on its page, or attributes to build one from) · how it is shown.
const CASES = [
  { name: 'intro', example: 0 },
  { name: 'position top', example: 2 },
  { name: 'position right', example: 3 },
  { name: 'position bottom', example: 4 },
  { name: 'position left', example: 5 },
  { name: 'kbd', header: true },
  { name: 'info', example: 1, click: true },
  ...['top', 'right', 'bottom', 'left'].flatMap(side => ['center', 'start', 'end'].map(align =>
    ({ name: `${side} ${align}`, build: `position="${side} ${align}"` }))),
  { name: 'gap 12', build: 'position="top center" gap="12"' },
  { name: 'offset 20', build: 'position="top center" offset="20"' },
  { name: 'offset 20 start', build: 'position="top start" offset="20"' },
  { name: 'offset 8 end', build: 'position="top end" offset="8"' },
  { name: 'right gap 12 offset 20', build: 'position="right center" gap="12" offset="20"' },
  { name: 'left start offset 8', build: 'position="left start" offset="8"' },
  { name: 'interactive', build: 'position="top center" interactive' },
  { name: 'disabled', build: 'position="top center" disabled', closed: true },
  { name: 'span', build: 'position="top center"', trigger: '<span tabindex="0">plain text</span>' },
  { name: 'disabled button', build: 'position="top center"', trigger: SAVE.replace('<button ', '<button disabled ') },
  // The same four tooltips, pushed against an edge of the viewport: each has to flip, or slide, or both.
  ...[
    ['top edge, top', 'wide top', 'top:4px;left:600px'], ['bottom edge, bottom', 'wide bottom', 'bottom:4px;left:600px'],
    ['left edge, left', 'wide left', 'top:300px;left:4px'], ['right edge, right', 'wide right', 'top:300px;right:4px'],
    ['left edge, top', 'wide top', 'top:300px;left:4px'], ['right edge, bottom', 'wide bottom', 'top:300px;right:4px'],
    ['corner, top', 'wide top', 'top:4px;left:4px'],
  ].map(([name, rask, edge]) => ({ name, rask, edge, build: `position="${rask.slice(5)} center"`, trigger: SMALL, text: WIDE })),
];

// How it is placed rather than what it is: Flux's script writes `position:absolute` and page coordinates,
// Rask.Ui has the browser anchor a fixed box. Where the box ENDS UP is compared, as `at`.
const MECHANISM = new Set(['position', 'width', 'height']);

// Differences in behaviour that are known and reported, each with what would close it.
const KNOWN = {
  'rask:hover-path/Escape hides it': 'no script: :hover cannot be dismissed. Needs a runtime hook (see docs/ui-kit.md).',
  'rask:interest/shown again after the press is released': 'no script: :active hides it only while pressed. Same hook.',
  'rask:hover-path/shown again after the press is released': 'no script: :active hides it only while pressed. Same hook.',
  'rask:hover-path/Escape hides it while focused': 'no script: :focus-visible cannot be dismissed either. Same hook.',
  'rask:interest/stays while focused, pointer gone': 'the browser drops interest when the pointer leaves, focused or not. Same hook.',
};

// Where the pointer rests between steps: the page's left edge, where neither page has a trigger.
const AWAY = [2, 450];

const flag = name => process.argv.includes(`--${name}`);
const browser = await chromium().launch();
let failures = 0;
for (const scheme of ['light', 'dark']) {
  const flux = await open(scheme, FLUX);
  await prepareFlux(flux);
  const rask = await open(scheme, pathToFileURL(raskPage).href);

  for (const test of CASES) {
    const theirs = await show(flux, test, 'flux', scheme);
    const mine = await show(rask, test, 'rask', scheme);
    const diffs = compare(theirs, mine, test);
    failures += diffs.length ? 1 : 0;
    const where = theirs.shown ? `${theirs.at[2]}x${theirs.at[3]} at ${theirs.at[0]},${theirs.at[1]}` : 'closed';
    const top = mine.shown ? (mine.topLayer ? ' · top layer' : ' · in place') : '';
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${scheme} ${test.name.padEnd(24)} ${where}${top}`);
    for (const d of diffs.slice(0, flag('all') ? Infinity : 10)) console.log(`       ${d}`);
  }

  if (scheme === 'light') failures += await behaviour(flux, rask);
  await flux.context().close();
  await rask.context().close();
}

await browser.close();
console.log(failures ? `\nflux parity: the shown tooltip differs in ${failures} case(s).` : '\nflux parity: the shown tooltip matches Flux.');
process.exit(failures ? 1 : 0);

async function open(scheme, url) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  await page.evaluate(() => document.fonts.ready);
  return page;
}

// Builds, inside Flux's own page, the cases its page does not show. The content's classes are taken from a
// live tooltip at run time, so the box is Flux's and only the props under test are ours.
async function prepareFlux(page) {
  await page.evaluate(({ cases, SAVE }) => {
    const live = document.querySelector('[data-preview-wrapper] ui-tooltip');
    const look = live.querySelector('[data-flux-tooltip-content]').className;
    const stage = document.createElement('div');
    stage.style.cssText = 'display:flex;flex-wrap:wrap;gap:100px 150px;justify-content:center;padding:60px 120px';
    live.closest('[data-preview-wrapper]').appendChild(stage);
    for (const test of cases) {
      const host = document.createElement('div');
      host.dataset.case = test.name;
      host.innerHTML = `<ui-tooltip data-flux-tooltip ${test.build}>${test.trigger ?? SAVE}`
        + `<div popover="manual" data-flux-tooltip-content role="tooltip" class="${look}">${test.text ?? 'Settings'}</div></ui-tooltip>`;
      stage.appendChild(host);
    }
  }, { cases: CASES.filter(test => test.build), SAVE });
  await page.waitForTimeout(300);
}

function locate(page, test, side) {
  if (side === 'rask') return page.locator(`[data-case="${test.rask ?? test.name}"] [data-ui-tooltip]`);
  if (test.header) return page.locator('[data-flux-tooltip]:has(> [tooltip\\:kbd])').first();
  if (test.build) return page.locator(`[data-case="${test.name}"] [data-flux-tooltip]`);
  return page.locator('[data-preview-wrapper] [data-flux-tooltip]:not([data-case] *)').nth(test.example);
}

async function show(page, test, side, scheme) {
  const tooltip = locate(page, test, side);
  const trigger = tooltip.locator(':scope > :first-child');
  const host = page.locator(`[data-case="${side === 'rask' ? test.rask ?? test.name : test.name}"]`);
  // Mid-viewport, so nothing flips that was not pushed against an edge on purpose.
  if (test.edge) await host.evaluate((el, css) => { el.style.cssText = `position:fixed;z-index:99999;${css}`; }, test.edge);
  else await trigger.evaluate(el => el.scrollIntoView({ block: 'center' }));

  await page.mouse.move(...AWAY);
  await page.waitForTimeout(60);
  await (test.click ? trigger.click() : trigger.hover({ force: true }));
  await page.waitForTimeout(200);
  const measured = await tooltip.evaluate(measure, { STYLES: STYLES.filter(key => !MECHANISM.has(key)) });
  if (measured.shown) {
    const dir = join(out, 'shown', side, scheme);
    await mkdir(dir, { recursive: true });
    const { clip } = measured;
    await page.screenshot({ path: join(dir, `${test.name.replace(/[ ,]+/g, '-')}.png`), clip }).catch(() => {});
  }

  if (test.click) await trigger.click();
  if (test.edge) await host.evaluate(el => { el.style.cssText = ''; });
  await page.mouse.move(...AWAY);
  return measured;
}

// Runs in the page. The shown content against its trigger.
function measure(tooltip, { STYLES }) {
  const trigger = tooltip.firstElementChild;
  const content = tooltip.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
  const computed = getComputedStyle(content);
  const fix = v => Math.round(v * 100) / 100;
  const rect = el => el.getBoundingClientRect();
  const t = rect(trigger);
  const c = rect(content);
  const style = el => Object.fromEntries(STYLES.map(key => [key, getComputedStyle(el)[key]]));
  const shown = computed.display !== 'none' && computed.visibility !== 'hidden' && c.width > 0;
  const x = Math.max(0, Math.min(t.x, c.x) - 12);
  const y = Math.max(0, Math.min(t.y, c.y) - 12);
  return {
    shown,
    topLayer: content.matches(':popover-open'),
    // left and top against the trigger's, then the size.
    at: [fix(c.x - t.x), fix(c.y - t.y), fix(c.width), fix(c.height)],
    inViewport: [fix(c.x), fix(c.y)],
    // Without whitespace: what sits between two paragraphs is Blade's indentation, and draws nothing.
    text: content.textContent.replace(/\s+/g, ''),
    style: style(content),
    children: [...content.children].map(child => {
      const k = rect(child);
      return { tag: child.tagName.toLowerCase(), at: [fix(k.x - c.x), fix(k.y - c.y), fix(k.width), fix(k.height)], style: style(child) };
    }),
    clip: { x, y, width: Math.max(t.right, c.right) + 12 - x, height: Math.max(t.bottom, c.bottom) + 12 - y },
  };
}

function compare(theirs, mine, test) {
  const diffs = [];
  if (theirs.shown === !!test.closed) diffs.push(`Flux ${theirs.shown ? 'shows' : 'does not show'} it — the case is wrong`);
  if (theirs.shown !== mine.shown) return [...diffs, `shown: ${theirs.shown} vs ${mine.shown}`];
  if (!theirs.shown) return diffs;

  if (theirs.text !== mine.text) diffs.push(`text "${theirs.text}" vs "${mine.text}"`);
  box(theirs.at, mine.at, 'against its trigger', diffs);
  if (test.edge) box(theirs.inViewport, mine.inViewport, 'in the viewport', diffs);
  styles(theirs.style, mine.style, 'content', diffs);
  if (theirs.children.length !== mine.children.length) {
    diffs.push(`children <${theirs.children.map(c => c.tag).join(' ')}> vs <${mine.children.map(c => c.tag).join(' ')}>`);
    return diffs;
  }

  theirs.children.forEach((child, i) => {
    if (child.tag !== mine.children[i].tag) diffs.push(`child ${i}: <${child.tag}> vs <${mine.children[i].tag}>`);
    box(child.at, mine.children[i].at, `${child.tag}[${i}] inside it`, diffs);
    styles(child.style, mine.children[i].style, `${child.tag}[${i}]`, diffs);
  });
  return diffs;
}

// The tolerances parity.mjs holds a loaded page to.
function box(a, b, what, diffs) {
  if (a.some((v, i) => Math.abs(v - b[i]) > 0.6)) diffs.push(`${what}: ${a.join(',')} vs ${b.join(',')}`);
}

function styles(x, y, where, diffs) {
  for (const key of Object.keys(x)) {
    if (same(x[key], y[key])) continue;
    const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
    if (side && x[`border${side}Width`] === '0px' && y[`border${side}Width`] === '0px') continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

function same(x, y) {
  if (x === y) return true;
  const round = v => v.replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000));
  return round(x) === round(y);
}

// What shows it and what hides it, walked the same way on both pages. Rask.Ui has two ways of showing a
// hover tooltip — the browser's interest invoker on a button or a link, and :hover on anything else — so
// both are walked.
async function behaviour(flux, rask) {
  const hover = [
    ['flux', flux, CASES.find(test => test.name === 'top center'), 'flux'],
    ['rask:interest', rask, CASES.find(test => test.name === 'top center'), 'rask'],
    ['rask:hover-path', rask, CASES.find(test => test.name === 'span'), 'rask'],
  ];
  const walked = {};
  for (const [label, page, test, side] of hover) walked[label] = await walkHover(page, locate(page, test, side));
  const info = CASES.find(test => test.name === 'info');
  const toggled = { flux: await walkToggle(flux, locate(flux, info, 'flux')), rask: await walkToggle(rask, locate(rask, info, 'rask')) };

  let unexpected = 0;
  console.log('\nbehaviour (true = the tooltip is showing after the step)');
  for (const [title, runs] of [['hover', walked], ['toggleable', toggled]]) {
    const labels = Object.keys(runs);
    console.log(`  ${title}: ${labels.join(' | ')}`);
    for (const step of Object.keys(runs.flux)) {
      const values = labels.map(label => runs[label][step]);
      const off = labels.filter(label => String(runs[label][step]) !== String(runs.flux[step]) && typeof runs.flux[step] === 'boolean');
      const known = off.every(label => KNOWN[`${label}/${step}`]);
      unexpected += off.length && !known ? 1 : 0;
      const mark = off.length ? (known ? 'KNOWN' : 'FAIL ') : 'ok   ';
      console.log(`  ${mark} ${step.padEnd(46)} ${values.join(' | ')}`);
      for (const label of off) if (KNOWN[`${label}/${step}`]) console.log(`           ${label}: ${KNOWN[`${label}/${step}`]}`);
    }
  }

  return unexpected;
}

async function walkHover(page, tooltip) {
  const trigger = tooltip.locator(':scope > :first-child');
  const showing = () => tooltip.evaluate(el => {
    const content = el.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
    const computed = getComputedStyle(content);
    return computed.display !== 'none' && computed.visibility !== 'hidden';
  });
  // Milliseconds from the pointer arriving (or leaving) to the frame the tooltip has changed in.
  const timed = async (act, want) => {
    await tooltip.evaluate((el, want) => {
      const content = el.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
      const visible = () => getComputedStyle(content).display !== 'none' && getComputedStyle(content).visibility !== 'hidden';
      window.__timed = null;
      let from = 0;
      const frame = () => {
        if (visible() === want) window.__timed = Math.round(performance.now() - from);
        else requestAnimationFrame(frame);
      };
      const arm = () => { from = performance.now(); frame(); };
      el.firstElementChild.addEventListener(want ? 'pointerenter' : 'pointerleave', arm, { once: true });
    }, want);
    await act();
    await page.waitForTimeout(900);
    return page.evaluate(() => (window.__timed === null ? 'never' : `${window.__timed}ms`));
  };
  const away = async () => { await page.mouse.move(...AWAY); await page.waitForTimeout(150); };
  const steps = {};

  await trigger.evaluate(el => el.scrollIntoView({ block: 'center' }));
  await away();
  steps['delay before it shows'] = await timed(() => trigger.hover({ force: true }), true);
  steps['hover shows it'] = await showing();
  steps['delay before it hides'] = await timed(() => page.mouse.move(...AWAY), false);
  steps['pointer leaving hides it'] = !(await showing());

  // Scrolled by script, whichever way the page can go: the pointer does not move, so nothing leaves.
  await trigger.hover({ force: true });
  await page.waitForTimeout(150);
  const gap = () => tooltip.evaluate(el => {
    const content = el.querySelector('[data-flux-tooltip-content], [data-ui-tooltip-content]');
    return [el.firstElementChild.getBoundingClientRect().y, content.getBoundingClientRect().y].map(Math.round);
  });
  const before = await gap();
  await page.evaluate(() => { const y = scrollY; scrollBy(0, 8); if (scrollY === y) scrollBy(0, -8); });
  await page.waitForTimeout(250);
  const after = await gap();
  steps['follows its trigger on scroll'] = after[0] !== before[0] && after[0] - after[1] === before[0] - before[1];
  await page.evaluate(() => scrollBy(0, 0));
  await away();

  await trigger.hover({ force: true });
  await page.waitForTimeout(150);
  const box = await trigger.boundingBox();
  const content = await tooltip.locator('[data-flux-tooltip-content], [data-ui-tooltip-content]').boundingBox();
  for (let y = box.y + 3; y > content.y + content.height / 2; y -= 1) {
    await page.mouse.move(box.x + box.width / 2, y);
    await page.waitForTimeout(8);
  }
  await page.waitForTimeout(200);
  steps['pointer resting on the tooltip keeps it'] = await showing();
  await away();

  await trigger.hover({ force: true });
  await page.waitForTimeout(150);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  steps['Escape hides it'] = !(await showing());
  await away();
  await trigger.hover({ force: true });
  await page.waitForTimeout(150);
  steps['hovering again after Escape shows it'] = await showing();

  await page.mouse.down();
  await page.waitForTimeout(100);
  steps['pressing the trigger hides it'] = !(await showing());
  await page.mouse.up();
  await page.waitForTimeout(150);
  steps['shown again after the press is released'] = await showing();
  await away();
  await page.evaluate(() => document.activeElement?.blur());

  // A key first, so the focus that follows is the keyboard's (:focus-visible).
  await page.keyboard.press('Shift');
  await trigger.focus();
  await page.waitForTimeout(150);
  steps['keyboard focus shows it'] = await showing();
  await trigger.hover({ force: true });
  await away();
  steps['stays while focused, pointer gone'] = await showing();
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  steps['Escape hides it while focused'] = !(await showing());
  await trigger.focus();
  await trigger.evaluate(el => el.blur());
  await page.waitForTimeout(150);
  steps['blur hides it'] = !(await showing());

  await away();
  return steps;
}

async function walkToggle(page, tooltip) {
  const trigger = tooltip.locator(':scope > :first-child');
  const content = tooltip.locator('[data-flux-tooltip-content], [data-ui-tooltip-content]');
  const showing = () => content.evaluate(el => getComputedStyle(el).display !== 'none');
  const settle = () => page.waitForTimeout(200);
  const steps = {};

  await trigger.evaluate(el => el.scrollIntoView({ block: 'center' }));
  await page.mouse.move(...AWAY);
  await trigger.hover();
  await settle();
  steps['hover shows it'] = await showing();
  await trigger.click();
  await settle();
  steps['a click shows it'] = await showing();
  await page.mouse.move(...AWAY);
  await settle();
  steps['stays when the pointer leaves'] = await showing();
  const box = await content.boundingBox();
  await page.mouse.click(box.x + 20, box.y + 20);
  await settle();
  steps['stays on a click inside it'] = await showing();
  await trigger.click();
  await settle();
  steps['a second click hides it'] = !(await showing());
  await trigger.click();
  await settle();
  await page.keyboard.press('Escape');
  await settle();
  steps['Escape hides it'] = !(await showing());
  await trigger.click();
  await settle();
  await page.mouse.click(20, 20);
  await settle();
  steps['a click outside hides it'] = !(await showing());
  await trigger.focus();
  await page.keyboard.press('Enter');
  await settle();
  steps['Enter on the trigger shows it'] = await showing();
  await page.keyboard.press('Escape');
  await settle();
  return steps;
}

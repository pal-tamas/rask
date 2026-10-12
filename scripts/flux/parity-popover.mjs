#!/usr/bin/env node
// Holds the OPEN popover to Flux UI's: how it looks and where it lands, then what a pointer and the keys do.
//
// parity.mjs measures a page as it loads, and a popover loads closed. The popup is the component, so this
// measures Flux's page once per dropdown with that dropdown open — lib.mjs opens whatever carries
// `data-parity-open`, by a real press or a real hover — and files the results as the page `popover-open`.
// PopoverOpenParity writes the Rask page of the same name, one example per dropdown with the same mark on its
// trigger, and parity.mjs compares the two as it compares any page: the panel's box, look and contents, and
// where it sits against the dropdown it belongs to. It is not an open.mjs config: that module opens ONE list
// of rows per example and compares it by its own minimum of parity.mjs's rules; this page has up to four
// dropdowns in an example, and what a panel holds is other pages' components.
//
// Then the walk: the same presses, pointer moves and keys on Flux's first two examples — the pressed popover
// and the hovered one — and on the Rask page, each step writing down whether the panel is open, where focus
// is, what the trigger says and whether the page behind is locked. Every line must agree, but for what a walk
// names as `open`: a difference that is the runtime's hooks' to close, printed as OPEN and not failed.
//
// The Rask page is static markup. What Flux does in script the kit asks the runtime for (data-rask-hover,
// data-rask-lock, the popover's focus rules), so the page is given the runtime's hooks (runtime.mjs).
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-popover.mjs [--refresh] [--all]     # --refresh re-measures Flux
//         node scripts/flux/parity-popover.mjs --record                # Flux's walk alone, every step
//
// Exit code 1 on any difference.

import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium, measurePage, root } from './lib.mjs';
import { runtime } from './runtime.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const fluxUrl = 'https://fluxui.dev/components/popover';
const out = join(root, 'artifacts', 'flux-parity', 'flux', 'popover-open');
const file = join(out, 'measurements.json');
const raskPage = join(root, 'artifacts', 'flux-parity', 'rask', 'popover-open.html');
const HOOKS = '<!-- runtime hooks -->';

// What a reader does to a popover that a press opens, and to one the pointer opens. Every pointer step is ONE
// move, and the pointer crosses the gap between a trigger and its panel in one too ('hoverpanel'): both
// close a panel the pointer is over neither part of.
const PRESSED = ['click', 'wheel', 'clickpanel', 'Tab', 'Escape', 'click', 'click', 'click', 'outside', 'focus', 'Enter', 'Escape',
  'focus', 'Space', 'Tab', 'Shift+Tab', 'Shift+Tab', 'focus', 'ArrowDown', 'hover', 'leave'];
const HOVERED = ['hover', 'hoverpanel', 'leave', 'hover', 'click', 'leave', 'focus', 'Enter', 'Tab', 'Escape', 'focus', 'Space', 'Escape'];
// Measured on Flux (2026-10-12): the pointer leaving closes its panel from a pointer-move check that runs at
// most once in 100 ms, so a move made straight after another one is not looked at — a pointer gliding off the
// trigger keeps the panel for up to that long, and reaches it across a narrow gap ('glide': two moves with
// nothing between them, the second into the gap). A pointer that rested and then goes into the gap in one
// move ('gap') closes it at once. The runtime's hover hook looks the same way (rask-hover.ts).
const GLIDED = ['hover', 'gap', 'hover', 'glide', 'hoverpanel', 'glide', 'gap', 'hover', 'leave'];
// A panel the pointer left takes focus nowhere on Flux, where the runtime's menu keys hand it to the
// trigger of whatever closed with focus on nothing (rask-menu-keys.ts).
const LEFT = {
  reason: 'the runtime hands focus to the trigger of a panel the pointer left; Flux leaves it where it was',
  as: line => line.replace('focus on nothing', 'focus on the trigger'),
};
const WALKS = [
  { name: 'a press opens it', dropdown: 0, steps: PRESSED },
  { name: 'the pointer opens it', dropdown: 1, steps: HOVERED, open: LEFT },
  { name: 'the pointer glides across the gap', dropdown: 1, steps: GLIDED, open: LEFT },
];

const browser = await chromium().launch();
if (flag('record')) {
  for (const walk of WALKS) print(walk, await walkOne(fluxUrl, walk, 'flux'));
  await browser.close();
  process.exit(0);
}

if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

// A page that is opened by hover and walked gets the runtime, once: FluxParityPages writes it without.
const html = await readFile(raskPage, 'utf8');
if (!html.includes(HOOKS)) {
  await writeFile(raskPage, html.replace('</body>', () => `${HOOKS}<script>${runtime('rask-hooks.ts').replaceAll('</script', '<\\/script')}</script></body>`));
}

if (!existsSync(file) || flag('refresh')) {
  const open = { light: [], dark: [] };
  for (let dropdown = 0; ; dropdown++) {
    let found = false;
    const measured = await measurePage(browser, fluxUrl, join(out, String(dropdown)), async page => {
      found = await page.evaluate(mark, dropdown);
    });
    if (!found) break;

    for (const scheme of ['light', 'dark']) {
      open[scheme].push({ ...measured[scheme][0], section: 'open', ordinal: dropdown });
    }

    console.log(`flux: dropdown ${dropdown} measured open`);
  }

  await mkdir(out, { recursive: true });
  await writeFile(file, JSON.stringify(open));
}

let differs = 0;
for (const walk of WALKS) {
  const theirs = await walkOne(fluxUrl, walk, 'flux');
  const mine = await walkOne(pathToFileURL(raskPage).href, walk, 'ui');
  const off = walk.steps.filter((_, i) => theirs[i] !== mine[i]).length;
  // A walk's open difference, and nothing else: with `as`, every line has to agree once Flux's is read that way.
  const known = off && walk.open && (!walk.open.as || walk.steps.every((_, i) => theirs[i] === mine[i] || walk.open.as(theirs[i]) === mine[i]));
  differs += off && !known ? 1 : 0;
  console.log(`${!off ? 'ok  ' : known ? 'OPEN' : 'FAIL'} ${walk.name} — ${walk.steps.length} steps${off ? `, ${off} differ` : ''}${known ? ` (${walk.open.reason})` : ''}`);
  if (off || flag('all')) print(walk, theirs, mine);
}

await browser.close();
console.log(differs ? `\nflux parity: the popover walks differently in ${differs} walk(s).\n` : '\nflux parity: the popover walks as Flux\'s does.\n');

const rest = args.filter(a => a !== '--refresh');
const parity = spawnSync(process.execPath, [join(fileURLToPath(new URL('.', import.meta.url)), 'parity.mjs'), 'popover-open', ...rest], { stdio: 'inherit' });
process.exit(differs || parity.status ? 1 : 0);

function print(walk, theirs, mine) {
  walk.steps.forEach((step, i) => {
    console.log(`       ${step.padEnd(11)} Flux: ${theirs[i]}`);
    if (mine && mine[i] !== theirs[i]) console.log(`       ${''.padEnd(11)} Rask: ${mine[i]}`);
  });
}

// One dropdown of the page, walked. Each step waits on STATE — the panel open or closed for three frames in
// a row — and then writes down what it left behind.
async function walkOne(url, walk, prefix) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 120000 });
  await page.evaluate(() => document.fonts.ready);
  // Flux's page has links above its first example; a parity page begins with it, and Shift+Tab off the first
  // trigger would land on nothing.
  await page.evaluate(() => document.body.prepend(Object.assign(document.createElement('button'), { style: 'position:absolute;opacity:0' })));
  const dropdown = (await page.$$(`[data-preview-wrapper] [data-${prefix}-dropdown]`))[walk.dropdown];
  await dropdown.evaluate(el => el.scrollIntoView({ block: 'center' }));
  const trigger = (await dropdown.evaluateHandle(el => el.firstElementChild)).asElement();
  const panel = await dropdown.$(`[data-${prefix}-popover]`);
  // A pointer comes from somewhere: Flux's page now and then does not open under a pointer whose first move
  // ever lands on the trigger.
  await page.mouse.move(4, 450);
  await page.waitForTimeout(200);
  const centre = async el => {
    const box = await el.boundingBox();
    return [box.x + box.width / 2, box.y + box.height / 2];
  };

  const states = [];
  // Where the panel was when it was last open: a step that goes there says so on both pages alike.
  let onPanel = null;
  for (const step of walk.steps) {
    const scrolled = await page.evaluate(() => scrollY);
    if (await panel.boundingBox()) onPanel = await centre(panel);
    if (step === 'click') await page.mouse.click(...await centre(trigger));
    else if (step === 'hover') await page.mouse.move(...await centre(trigger));
    else if (step === 'hoverpanel') await page.mouse.move(...onPanel);
    // Two moves with nothing between them: one that stays on the trigger, one into the gap under it.
    else if (step === 'glide') {
      const from = await centre(trigger);
      await page.mouse.move(from[0] + 6, from[1] + 10);
      await page.mouse.move(from[0] + 12, from[1] + 21);
    }
    else if (step === 'gap') {
      const from = await centre(trigger);
      await page.mouse.move(from[0] + 12, from[1] + 21);
    }
    else if (step === 'clickpanel') await page.mouse.click(...onPanel.map((v, i) => (i ? v : v - 80)));
    else if (step === 'outside') await page.mouse.click(4, 450);
    else if (step === 'leave') await page.mouse.move(4, 450);
    else if (step === 'focus') await trigger.focus();
    else if (step === 'wheel') await page.mouse.wheel(0, 120);
    else await page.keyboard.press(step);
    await settled(page, panel);
    states.push(await dropdown.evaluate(read, { prefix, scrolled }));
  }

  await context.close();
  return states;
}

// The same answer to "is it open" for three frames, and no transition running on the panel.
async function settled(page, panel) {
  await panel.evaluate(el => new Promise(done => {
    let last = null;
    let same = 0;
    let frames = 0;
    const look = () => {
      const now = el.matches(':popover-open');
      same = now === last ? same + 1 : 0;
      last = now;
      if ((same >= 3 && frames >= 12) || ++frames > 120) done();
      else requestAnimationFrame(look);
    };
    requestAnimationFrame(look);
  }));
  await page.waitForTimeout(60);
}

// Runs in the page. What a step left behind, in words both pages can be held to.
function read(dropdown, { prefix, scrolled }) {
  const trigger = dropdown.firstElementChild;
  const panel = dropdown.querySelector(`[data-${prefix}-popover]`);
  const active = document.activeElement;
  const focus = active === trigger ? 'on the trigger' : active === panel ? 'on the panel'
    : panel.contains(active) ? 'inside the panel' : active === document.body ? 'on nothing' : 'elsewhere';
  const html = getComputedStyle(document.documentElement);
  const locked = html.overflow === 'hidden' && html.pointerEvents === 'none';
  return [
    panel.matches(':popover-open') ? 'open  ' : 'closed',
    `focus ${focus}`,
    `aria-expanded=${trigger.getAttribute('aria-expanded')}`,
    locked ? 'page locked' : 'page free',
    `scrolled ${Math.round(scrollY - scrolled)}`,
  ].join(' · ');
}

// Runs in the page. Leaves the one example that holds this dropdown, and marks its trigger for lib.mjs to open.
function mark(dropdown) {
  const mine = [...document.querySelectorAll('[data-preview-wrapper] [data-flux-dropdown]')][dropdown];
  const wrapper = mine?.closest('[data-preview-wrapper]');
  for (const other of document.querySelectorAll('[data-preview-wrapper]')) {
    if (other !== wrapper) other.removeAttribute('data-preview-wrapper');
  }

  mine?.firstElementChild.setAttribute('data-parity-open', mine.hasAttribute('hover') ? 'hover' : '');
  return Boolean(mine);
}

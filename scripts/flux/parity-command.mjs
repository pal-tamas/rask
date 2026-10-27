#!/usr/bin/env node
// Holds Ui.Command to Flux UI's command palette ONCE IT IS USED — the half parity.mjs cannot see, since a palette
// as loaded has nothing typed, nothing lit and its modal shut.
//
// First the look: parity.mjs compares the page `command-open` (CommandOpenParity) with Flux's page brought to the
// same states — typed into, left with no match, opened as a modal and walked — by command-open.mjs, which drives
// both pages the same way.
//
// Then the behaviour: each walk below is done with real keys and a real pointer on Flux's live page and on the
// Rask parity page (`command`, given the runtime's hooks, as an app has them), and after every step both write down
// what they show — the commands shown, the lit one, what the box holds, whether the clear or close button and the
// "No results found" row show, whether the modal is open, and where focus is. The two transcripts must agree line
// for line. On the Rask page the box must also NAME the lit command (aria-activedescendant), as Flux's does.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity-command.mjs [--refresh] [--all]     # --all prints every step of every walk
//         node scripts/flux/parity-command.mjs --record                # Flux's walks alone, every step
//         node scripts/flux/parity-command.mjs --view 390x844          # the same on a phone: taps, and no pointer
//                                                                      # to rest on a command or take away
//
// Exit code 1 on any difference.

import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { chromium, root, viewOf } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

const args = process.argv.slice(2);
const record = args.includes('--record');
const every = args.includes('--all') || record;
const view = viewOf(args);
const here = fileURLToPath(new URL('.', import.meta.url));
const raskPage = join(root, 'artifacts', 'flux-parity', 'rask', 'command.html');
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

// Steps: `box` presses the search box, `trigger` the modal's trigger, `row:<n>` / `hover:<n>` the n-th command
// SHOWN, `clear` / `close` the box's buttons, `leave` takes the pointer off the palette, `type:<text>`, or a key.
const WALKS = [
  {
    name: 'keys', example: 0,
    steps: ['box', 'ArrowDown', 'ArrowDown', 'ArrowUp', 'ArrowUp', 'ArrowUp', 'End', 'Home', 'type:c', 'type:h', 'type:zz',
      'ArrowDown', 'Backspace', 'Backspace', 'Backspace', 'Backspace', 'ArrowDown', 'ArrowDown', 'ArrowDown', 'ArrowDown',
      'ArrowDown', 'ArrowDown', 'type:DOC', 'Enter', 'Escape', 'Tab'],
  },
  {
    name: 'pointer', example: 0,
    steps: ['box', 'hover:2', 'ArrowDown', 'leave', 'hover:4', 'ArrowUp', 'row:1', 'ArrowDown', 'box', 'type:doc', 'row:0', 'box', 'Tab'],
  },
  {
    name: 'clear', example: 0,
    steps: ['box', 'type:new', 'clear', 'type:x', 'Backspace', 'Tab'],
  },
  {
    name: 'modal', example: 1,
    steps: ['trigger', 'Tab', 'type:doc', 'ArrowDown', 'Enter', 'type:set', 'Escape', 'trigger', 'box', 'type:x', 'close', 'trigger', 'Escape'],
  },
  {
    name: 'back by Tab', example: 1,
    steps: ['trigger', 'Tab', 'type:set', 'Escape', 'trigger', 'Tab', 'type:x', 'Shift+Tab', 'Tab', 'Escape'],
  },
];

const look = spawnSync(process.execPath,
  [join(here, 'parity.mjs'), 'command-open', '--prepare', join(here, 'command-open.mjs'), ...args.filter(a => a === '--refresh' || a === '--all'),
    ...(view.sized ? ['--view', view.size.join('x')] : [])],
  { stdio: 'inherit' });

const browser = await chromium().launch();
let differs = 0;
console.log('\nwalks (Flux | Rask)');
for (const asked of WALKS) {
  const walk = view.phone ? { ...asked, steps: asked.steps.filter(step => !/^(hover|leave)\b/.test(step)) } : asked;
  const theirs = await walkOne('https://fluxui.dev/components/command', walk, false);
  const mine = record ? theirs : await walkOne(pathToFileURL(raskPage).href, walk, true);
  const off = walk.steps.filter((_, i) => theirs[i] !== mine[i]);
  differs += off.length ? 1 : 0;
  console.log(`${off.length ? 'FAIL' : 'ok  '} ${walk.name} — ${walk.steps.length} steps${off.length ? `, ${off.length} differ` : ''}`);
  walk.steps.forEach((step, i) => {
    if (!every && theirs[i] === mine[i]) return;
    console.log(`       ${step.padEnd(10)} Flux: ${theirs[i]}`);
    if (theirs[i] !== mine[i]) console.log(`       ${''.padEnd(10)} Rask: ${mine[i]}`);
  });
}

await browser.close();
console.log(differs ? `\nflux parity: the command palette walks differently in ${differs} walk(s).` : '\nflux parity: the command palette walks as Flux\'s does.');
process.exit(look.status || differs ? 1 : 0);

async function walkOne(url, walk, rask) {
  const context = await browser.newContext(view.screen);
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 120000 });
  await page.evaluate(() => document.fonts.ready);
  if (rask) {
    await withRuntime(page, 'rask-hooks.ts');
    await page.evaluate(() => { document.body.style.paddingBottom = '100vh'; });
  }

  const scope = (await page.$$('[data-preview-wrapper]'))[walk.example];
  await scope.evaluate(el => el.scrollIntoView({ block: 'center' }));
  const palette = '[data-flux-command], [data-ui-command]';
  // The palette a step acts on: the open modal's, else the example's.
  const current = async () => (await page.$(`dialog[open] :is(${palette})`)) ?? scope.$(palette);
  const shownRows = async () => (await current()).$$(':is([data-flux-command-item], [data-ui-command-item]):not([data-hidden])');
  // A press is a tap on a phone.
  const press = async el => (view.phone ? el.tap() : el.click());
  const lines = [];
  for (const step of walk.steps) {
    const [verb, arg] = step.split(':');
    if (verb === 'box') await press(await (await current()).$('input'));
    else if (verb === 'trigger') await press(await scope.$('[data-flux-modal-trigger] button, [data-ui-modal-trigger] button'));
    else if (verb === 'row') await press((await shownRows())[Number(arg)]);
    else if (verb === 'hover') await (await shownRows())[Number(arg)].hover();
    else if (verb === 'clear' || verb === 'close') await press(await (await current()).$(`button[aria-label^="${verb === 'clear' ? 'Clear' : 'Close'}"]`));
    else if (verb === 'leave') await page.mouse.move(2, 2);
    else if (verb === 'type') await page.keyboard.type(arg, { delay: 60 });
    else await page.keyboard.press(verb);
    await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
    await page.waitForTimeout(350);
    lines.push(await page.evaluate(read, { example: walk.example, palette, rask }));
  }

  await context.close();
  return lines;
}

// Runs in the page. What a step left behind, in words both pages can be held to.
function read({ example, palette, rask }) {
  const wrapper = document.querySelectorAll('[data-preview-wrapper]')[example];
  const dialog = wrapper.querySelector('dialog');
  const root = (dialog?.open ? dialog : wrapper).querySelector(palette);
  const box = root.querySelector('input');
  const words = el => (el.innerText ?? '').trim().split('\n')[0].trim();
  const seen = el => !!el && el.checkVisibility();
  const rows = [...root.querySelectorAll('[role=option]')].filter(seen);
  const lit = rows.find(row => row.hasAttribute('data-active'));
  const named = document.getElementById(box.getAttribute('aria-activedescendant') ?? '');
  const empty = [...root.querySelectorAll('[role=listbox] > :not([role=option])')].some(seen);
  const button = name => seen(root.querySelector(`button[aria-label^="${name}"]`));
  const focused = document.activeElement;
  const focus = focused === box ? 'box'
    : focused?.getAttribute('role') === 'listbox' && root.contains(focused) ? 'list'
      : wrapper.contains(focused) && focused.closest('[data-flux-modal-trigger], [data-ui-modal-trigger]') ? 'trigger'
        // Past the example: the next thing each page has there is the page's, not the palette's.
        : focused === document.body ? 'body' : 'elsewhere';
  return [
    `shows [${rows.map(words).join(', ')}]`,
    `lit ${lit ? `"${words(lit)}"` : '-'}`,
    rask && named !== (lit ?? null) ? `NAMES "${named ? words(named) : '-'}"` : '',
    `box "${box.value}"`,
    button('Clear') ? 'clear' : '',
    button('Close') ? 'close' : '',
    empty ? 'NO RESULTS' : '',
    dialog ? (dialog.open ? 'open' : 'shut') : '',
    `focus ${focus}`,
  ].filter(Boolean).join(' · ');
}

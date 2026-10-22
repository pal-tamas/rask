// Which side an OPEN date picker keeps when its calendar changes height.
//
// A month of six weeks is a row taller than one of five. A picker low in the viewport opens its calendar
// under the trigger on a five-week month; paged to a six-week one, the calendar no longer fits there. This
// opens Flux's live picker with the trigger's bottom at the same place as the app that reported it (scrolled
// there, as a form page is), pages month by month and writes down where the calendar is after each step, then
// what makes it choose again: a scroll, a resize of the window, closing and opening it.
//
//   node scripts/flux/date-flip.mjs [--rask <url> [--picker <css>]] [--view 1440x950] [--bottom 616]
//
// `--rask` measures a running Rask page the same way (its first `[data-ui-date-picker]`, or `--picker`) beside
// Flux's button picker, and compares the side of every step. The steps that page the OPEN calendar are DEPARTED
// (flux-component SKILL.md, 2026-10-21): the kit keeps the side it opened on where Flux turns it over, so there
// Rask must be on the side it opened on. Every other step must be on Flux's side. Exits 1 on a difference.

import { chromium } from './lib.mjs';

const args = process.argv.slice(2);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
const views = option('view') ? [option('view').split('x').map(Number)] : [[1440, 950], [1400, 900]];
const bottom = Number(option('bottom') ?? 616);

// The first picker of the page whose trigger is a button, opened on no value, and the typed one.
const fluxButton = { name: 'flux button', url: 'https://fluxui.dev/components/date-picker', picker: '[data-preview-wrapper] [data-flux-date-picker]', index: 9 };
const sides = option('rask')
  ? [fluxButton, { name: 'rask', url: option('rask'), picker: option('picker') ?? '[data-ui-date-picker]', index: 0 }]
  : [fluxButton, { name: 'flux input', url: 'https://fluxui.dev/components/date-picker', picker: '[data-preview-wrapper] [data-flux-date-picker]', index: 1 }];
const sidesOf = {};

const browser = await chromium().launch();
for (const side of sides) {
  for (const [width, height] of views) {
    const context = await browser.newContext({ viewport: { width, height } });
    // October 2026 has five weeks, September five, August six, July five.
    await context.clock.setFixedTime(new Date('2026-10-21T12:00:00Z'));
    const page = await context.newPage();
    await page.goto(side.url, { waitUntil: 'networkidle', timeout: 120000 });
    await page.evaluate(() => document.fonts.ready);
    console.log(`\n${side.name} at ${width}x${height}, the trigger's bottom at ${bottom}`);
    const rows = sidesOf[`${side.name} ${width}x${height}`] = [];
    const note = async (step, paged = false) => {
      const at = { ...await page.evaluate(read, side), step, paged };
      rows.push(at);
      console.log(`  ${step.padEnd(34)} ${at.month.padEnd(16)} ${at.weeks} weeks  trigger ${at.trigger}  popup ${at.open ? `${at.side} top ${at.top} bottom ${at.bottom}` : 'closed'}`);
    };

    await page.evaluate(scroll, { ...side, bottom });
    await opener(page, side).click();
    await settle(page);
    await page.evaluate(clear, side);
    await settle(page);
    await page.evaluate(scroll, { ...side, bottom });
    await settle(page);
    await note('opened');
    for (const step of ['previous', 'previous', 'previous', 'next', 'next', 'next']) {
      await pager(page, step).click();
      await settle(page);
      await note(`${step} month`, true);
    }

    // Back on a six-week month: what makes it choose again.
    for (const step of ['previous', 'previous']) {
      await pager(page, step).click();
      await settle(page);
    }

    await note('previous ×2 (back to six weeks)', true);
    await page.evaluate(() => window.scrollBy(0, 1));
    await settle(page);
    await note('scrolled the page by 1 px');
    await page.evaluate(() => window.scrollBy(0, -1));
    await settle(page);
    await note('scrolled back');
    await page.setViewportSize({ width, height: height + 1 });
    await settle(page);
    await page.setViewportSize({ width, height });
    await settle(page);
    await note('window resized by 1 px and back');
    await page.keyboard.press('Escape');
    await settle(page);
    await opener(page, side).click();
    await settle(page);
    await note('closed and opened again');
    await context.close();
  }
}

await browser.close();

if (option('rask')) {
  let failed = 0;
  console.log('\nrask against flux');
  for (const [width, height] of views) {
    const flux = sidesOf[`flux button ${width}x${height}`];
    const rask = sidesOf[`rask ${width}x${height}`];
    flux.forEach((theirs, i) => {
      const mine = rask[i];
      // Paged while open: the side it opened on (DEPARTED); anything else chooses afresh, as Flux's does.
      const wanted = theirs.paged ? rask[0].side : theirs.side;
      const ok = mine?.side === wanted;
      failed += ok ? 0 : 1;
      const tag = !ok ? 'FAIL' : theirs.paged && wanted !== theirs.side ? 'DEPARTED' : 'ok';
      console.log(`  ${tag.padEnd(8)} ${width}x${height} ${theirs.step.padEnd(34)} flux ${theirs.side ?? 'closed'}, rask ${mine?.side ?? 'closed'}`);
    });
  }
  process.exit(failed ? 1 : 0);
}

function opener(page, side) {
  // The button that opens it: the trigger itself, or the one beside a typed field (Flux's is the chevron).
  const picker = page.locator(side.picker).nth(side.index);
  return picker.locator('button[role=combobox], ui-date-picker-trigger button, ui-date-picker-trigger > svg:last-child, [data-ui-date-picker-trigger] button').first();
}

function pager(page, step) {
  const label = step === 'previous' ? /previous/i : /next/i;
  return page.locator('dialog[open], [popover]:popover-open').getByRole('button', { name: label }).first();
}

async function settle(page) {
  await page.evaluate(() => new Promise(done => {
    let n = 0;
    const tick = () => (++n >= 12 ? done() : requestAnimationFrame(tick));
    requestAnimationFrame(tick);
  }));
}

// Runs in the page. Scrolls so the picker's trigger ends `bottom` px down the viewport.
function scroll({ picker, index, bottom }) {
  const el = document.querySelectorAll(picker)[index];
  window.scrollBy(0, el.getBoundingClientRect().bottom - bottom);
}

// Runs in the page. A picker that holds a value opens on its month: emptied, it opens on today.
function clear({ picker, index }) {
  const el = document.querySelectorAll(picker)[index];
  if (!el.hasAttribute('data-empty') && 'value' in el) el.value = '';
}

// Runs in the page. Where the calendar is, which month it shows and how many weeks that month has.
function read({ picker, index }) {
  const el = document.querySelectorAll(picker)[index];
  const t = el.getBoundingClientRect();
  const panel = document.querySelector('dialog[open]') ?? document.querySelector('[popover]:popover-open');
  const round = v => Math.round(v);
  if (!panel) return { open: false, month: '', weeks: 0, trigger: `${round(t.top)}..${round(t.bottom)}` };
  const p = panel.getBoundingClientRect();
  // The month is the one the day in the middle of the grid belongs to: "Thursday, October 15, 2026".
  const days = [...panel.querySelectorAll('[aria-label]')].map(d => d.getAttribute('aria-label')).filter(l => /\d{4}$/.test(l));
  const middle = (days[Math.floor(days.length / 2)] ?? '').split(', ');
  const rows = [...panel.querySelectorAll('tbody tr, [role=row]')].filter(r => r.querySelector('td, [role=gridcell]'));
  return {
    open: true,
    month: middle.length > 2 ? `${middle[1].split(' ')[0]} ${middle[2]}` : '',
    weeks: rows.length,
    side: p.top >= t.bottom - 1 ? 'below' : p.bottom <= t.top + 1 ? 'above' : 'over',
    top: round(p.top),
    bottom: round(p.bottom),
    trigger: `${round(t.top)}..${round(t.bottom)}`,
  };
}

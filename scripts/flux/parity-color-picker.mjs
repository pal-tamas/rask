#!/usr/bin/env node
// Holds the OPEN color picker to Flux UI's, example by example, and then walks both.
//
// parity.mjs measures a page as it loads, and a picker loads closed: its popover is in the tree and not
// displayed. The popover is most of the component, so this measures Flux's page again with each picker open —
// lib.mjs opens whatever carries `data-parity-open`, by a real press at its centre, and closes it again before
// the next — and files the result as the page `color-picker-open`, under the same section ids.
// ColorPickerOpenParity writes the Rask page of that name with the same mark, and parity.mjs compares the two
// as it compares any page: the popover's box, look and contents, and where it sits against its trigger.
//
// `--live <url>` then walks a running site's showcase (the URL of /docs/ui/color-picker) beside Flux's page:
// the same keys and the same presses on both, and after each step what it left behind — whether the popover
// is open, the value, what the inputs say, the preview, where the thumbs stand, which swatch is picked and
// which holds the tab stop, what was announced and where focus is. `--phone` walks the sheet at 390 px.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages        # writes the Rask page
//         node scripts/flux/parity-color-picker.mjs [--refresh] [--all]    # --refresh re-measures Flux
//         node scripts/flux/parity-color-picker.mjs --section dropper
//         node scripts/flux/parity-color-picker.mjs --live http://localhost:5000/docs/ui/color-picker [--phone] [--wait 2200]

import { spawnSync } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium, measurePage, root } from './lib.mjs';

const FLUX = 'https://fluxui.dev/components/color-picker';
const args = process.argv.slice(2);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);

if (option('live')) {
  const browser = await chromium().launch();
  const differs = await walkBoth(browser, option('live'), args.includes('--phone'), Number(option('wait') ?? 400));
  await browser.close();
  console.log(differs ? `\nflux parity: the color picker behaves differently in ${differs} walk(s).` : '\nflux parity: the color picker walks as Flux does.');
  process.exit(differs ? 1 : 0);
}

const out = join(root, 'artifacts', 'flux-parity', 'flux', 'color-picker-open');
const file = join(out, 'measurements.json');
if (!existsSync(file) || args.includes('--refresh')) {
  const browser = await chromium().launch();
  const open = await measurePage(browser, FLUX, out, page => page.evaluate(mark));
  await browser.close();
  await mkdir(out, { recursive: true });
  await writeFile(file, JSON.stringify(open));
  console.log(`flux: ${open.light.length} color-picker example(s) measured open`);
}

const rest = args.filter(a => a !== '--refresh');
const parity = spawnSync(process.execPath, [join(fileURLToPath(new URL('.', import.meta.url)), 'parity.mjs'), 'color-picker-open', ...rest], { stdio: 'inherit' });
process.exit(parity.status ?? 1);

// Runs in the page. Marks what a reader presses to open each example's first picker: the swatch button of an
// input trigger, or the button trigger itself.
function mark() {
  for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) {
    wrapper.querySelector('[data-flux-color-picker-swatch-button], [data-flux-color-picker-button]')?.setAttribute('data-parity-open', '');
  }
}

// ----- The keyboard and the pointer, on a running app --------------------------------------------------

// `flux` is the picker's place among Flux's; `rask` the id of the showcase's example.
function walks(phone) {
  if (phone) {
    return [
      {
        name: 'the sheet holds until Select', flux: 0, rask: 'intro',
        steps: ['press:opener', 'at:area:0.2:0.2', 'at:hue:0.5:0.5', 'fill:popover-input:#00ff00', 'key:Enter', 'press:select', 'press:opener', 'at:area:0.6:0.6', 'press:cancel',
          'press:opener', 'at:area:0.8:0.3', 'key:Escape', 'press:opener', 'at:area:0.3:0.8', 'outside', 'press:opener', 'swatch:3', 'press:opener', 'focus:area-thumb', 'key:ArrowRight', 'key:Escape',
          'fill:input:#ff0000', 'key:Enter'],
      },
      {
        name: 'the sheet, button trigger', flux: 5, rask: 'button',
        steps: ['press:opener', 'at:hue:0.25:0.5', 'press:select', 'press:opener', 'swatch:1'],
      },
    ];
  }

  return [
    {
      name: 'area keys', flux: 0, rask: 'intro',
      steps: ['press:opener', 'key:ArrowRight', 'key:ArrowDown', 'key:Shift+ArrowLeft', 'key:Shift+ArrowUp', 'key:PageUp', 'key:PageDown', 'key:Home', 'key:End', 'key:Escape'],
    },
    {
      name: 'hue keys', flux: 0, rask: 'intro',
      steps: ['press:opener', 'key:Tab', 'key:ArrowRight', 'key:ArrowLeft', 'key:Shift+ArrowRight', 'key:Shift+ArrowLeft', 'key:Home', 'key:End', 'key:Shift+ArrowRight', 'key:PageDown', 'key:PageUp', 'key:Escape'],
    },
    {
      name: 'the trigger input', flux: 0, rask: 'intro',
      steps: ['focus:input', 'key:ArrowDown', 'key:Escape', 'fill:input:#f00', 'key:Enter', 'fill:input:rgb(1 2 3)', 'key:Tab',
        'fill:input:hsl(120 50% 50% / 0.5)', 'key:Enter', 'fill:input:blue', 'key:Enter', 'fill:input:oklch(0.7 0.1 200)', 'key:Enter', 'fill:input:lab(50 40 30)', 'key:Enter',
        'fill:input:', 'key:Enter', 'press:opener', 'key:ArrowRight', 'key:Escape'],
    },
    {
      name: 'the popover input', flux: 0, rask: 'intro',
      steps: ['press:opener', 'fill:popover-input:#00f', 'key:Enter', 'fill:popover-input:lime', 'key:Tab', 'fill:popover-input:', 'key:Enter', 'key:Escape'],
    },
    {
      name: 'swatches', flux: 0, rask: 'intro',
      steps: ['press:opener', 'key:Tab', 'key:Tab', 'key:Tab', 'key:ArrowDown', 'key:ArrowDown', 'key:ArrowRight', 'key:ArrowUp', 'key:ArrowLeft', 'key:End', 'key:ArrowRight', 'key:Space',
        'key:Home', 'key:Enter', 'swatch:5', 'key:Escape', 'press:opener', 'swatch:3', 'outside'],
    },
    {
      name: 'pointer', flux: 0, rask: 'intro',
      steps: ['press:opener', 'at:area:0.5:0.5', 'at:area:0.2:0.9', 'drag:area:0.2:0.2:0.9:0.9', 'drag:area:0.5:0.5:1.5:-0.5', 'at:hue:0.5:0.5', 'at:hue:0.02:0.5', 'drag:hue:0.2:0.5:0.8:0.5',
        'key:ArrowRight', 'key:Escape'],
    },
    {
      name: 'alpha', flux: 1, rask: 'format',
      steps: ['press:opener', 'key:Tab', 'key:Tab', 'key:ArrowRight', 'key:Shift+ArrowLeft', 'key:Home', 'key:End', 'at:alpha:0.5:0.5', 'swatch:0', 'fill:popover-input:#ff000080', 'key:Enter', 'key:Escape'],
    },
    {
      name: 'button trigger', flux: 5, rask: 'button',
      steps: ['press:opener', 'key:ArrowRight', 'key:Escape', 'key:Enter', 'key:Escape', 'key:Space', 'key:Escape', 'key:ArrowDown', 'swatch:1', 'key:Escape'],
    },
    {
      name: 'custom layout', flux: 8, rask: 'layout',
      steps: ['press:opener', 'key:Tab', 'key:ArrowLeft', 'key:Tab', 'key:ArrowRight', 'key:Tab', 'key:Escape'],
    },
    {
      name: 'clearable', flux: 10, rask: 'clearable',
      steps: ['press:clear', 'press:opener', 'key:ArrowRight', 'key:Tab', 'key:ArrowRight', 'swatch:2', 'key:Escape', 'press:clear', 'fill:input:#123', 'key:Enter'],
    },
    {
      name: 'copyable', flux: 11, rask: 'copyable',
      steps: ['press:copy', 'clipboard', 'press:opener', 'swatch:4', 'key:Escape', 'press:copy', 'clipboard'],
    },
    {
      name: 'disabled', flux: 12, rask: 'disabled',
      steps: ['force:opener', 'force:input'],
    },
  ];
}

async function walkBoth(browser, liveUrl, phone, wait) {
  const view = phone ? { viewport: { width: 390, height: 844 }, hasTouch: true } : { viewport: { width: 1280, height: 900 } };
  let differs = 0;
  for (const walk of walks(phone)) {
    const sides = [];
    for (const side of ['flux', 'rask']) {
      const context = await browser.newContext({ ...view, permissions: ['clipboard-read', 'clipboard-write'] });
      const page = await context.newPage();
      page.setDefaultTimeout(5000);
      await page.goto(side === 'flux' ? FLUX : liveUrl, { waitUntil: 'networkidle' });
      const prefix = side === 'flux' ? 'data-flux-' : 'data-ui-';
      const picker = side === 'flux'
        ? page.locator('ui-color-picker').nth(walk.flux)
        : page.locator(`[data-color-picker-example="${walk.rask}"] [data-ui-color-picker]`).first();
      await picker.scrollIntoViewIfNeeded();
      const lines = [];
      for (const step of walk.steps) {
        await act(page, picker, prefix, step, phone).catch(e => lines.push(`${step}: could not (${String(e.message).split('\n')[0].slice(0, 80)})`));
        await page.waitForTimeout(side === 'flux' ? 250 : wait);
        lines.push(`${step} -> ${step === 'clipboard' ? await page.evaluate(() => navigator.clipboard.readText()) : await observe(picker, prefix)}`);
      }
      sides.push(lines);
      await context.close();
    }

    const [flux, rask] = sides;
    const bad = flux.map((line, i) => (line === rask[i] ? null : `       ${walk.steps[i]}\n         flux ${line}\n         rask ${rask[i]}`)).filter(Boolean);
    differs += bad.length ? 1 : 0;
    console.log(`${bad.length ? 'FAIL' : 'ok  '} ${walk.name} (${walk.steps.length} steps)${bad.length ? ` — ${bad.length} differ` : ''}`);
    for (const line of bad) console.log(line);
  }

  return differs;
}

const PARTS = {
  opener: p => `[${p}color-picker-swatch-button], [${p}color-picker-button]`,
  input: p => `[${p}color-picker-input]`,
  'popover-input': p => `[${p}color-picker-popover-input]`,
  area: p => `[${p}color-picker-area]`,
  'area-thumb': p => `[${p}color-picker-area-thumb]`,
  hue: p => `[${p}color-picker-slider][data-channel=hue]`,
  alpha: p => `[${p}color-picker-slider][data-channel=alpha]`,
  clear: () => '[aria-label="Clear color"]',
  copy: p => `[${p}color-picker-copy]`,
  // The box Flux hears its Select through, and the one around its Cancel: the kit's are the same two, in that order.
  cancel: p => (p === 'data-flux-' ? 'ui-close button' : 'dialog [data-ui-button]:not([data-ui-group-target])'),
  select: p => (p === 'data-flux-' ? 'ui-color-picker-select' : 'dialog [role=button]'),
};

async function act(page, picker, prefix, step, phone) {
  const [verb, ...rest] = step.split(':');
  const part = name => picker.locator(PARTS[name](prefix)).first();
  const press = locator => (phone ? locator.tap() : locator.click());
  const point = async (name, x, y) => {
    const box = await part(name).boundingBox();
    return [box.x + box.width * Number(x), box.y + box.height * Number(y)];
  };
  switch (verb) {
    case 'press': return press(part(rest[0]));
    case 'force': return part(rest[0]).click({ force: true, timeout: 1000 }).catch(() => {});
    case 'focus': return part(rest[0]).focus();
    case 'fill': return part(rest[0]).fill(rest.slice(1).join(':'));
    case 'key': return page.keyboard.press(rest[0]);
    case 'swatch': return press(picker.locator(`[${prefix}color-picker-swatch]`).nth(Number(rest[0])));
    case 'outside': return phone ? page.touchscreen.tap(195, 10) : page.mouse.click(5, 5);
    case 'clipboard': return undefined;
    case 'at': {
      const [x, y] = await point(rest[0], rest[1], rest[2]);
      return phone ? page.touchscreen.tap(x, y) : page.mouse.click(x, y);
    }
    case 'drag': {
      const from = await point(rest[0], rest[1], rest[2]);
      const to = await point(rest[0], rest[3], rest[4]);
      await page.mouse.move(...from);
      await page.mouse.down();
      await page.mouse.move((from[0] + to[0]) / 2, (from[1] + to[1]) / 2, { steps: 4 });
      await page.mouse.move(...to, { steps: 4 });
      return page.mouse.up();
    }
    default: throw new Error(`unknown step ${step}`);
  }
}

// What a step left behind, in words both pages can say. A thumb's place is read from where it is DRAWN — Flux
// writes a percentage, the kit a custom property — and the value from what the inputs and the preview show.
function observe(picker, prefix) {
  return picker.evaluate((root, p) => {
    const one = name => root.querySelector(`[${p}${name}]`);
    const dialog = root.querySelector('dialog');
    const area = one('color-picker-area');
    const thumb = one('color-picker-area-thumb');
    const place = (el, box) => (el && box ? (() => {
      const a = el.getBoundingClientRect(); const b = box.getBoundingClientRect();
      return b.width ? `${Math.round((a.x + a.width / 2 - b.x) / b.width * 100)}/${Math.round((a.y + a.height / 2 - b.y) / b.height * 100)}` : '-';
    })() : '-');
    const swatches = [...root.querySelectorAll(`[${p}color-picker-swatch]`)];
    const active = document.activeElement;
    const focus = !active || active === document.body ? 'body'
      : swatches.includes(active) ? `swatch ${swatches.indexOf(active)}`
      : active.getAttribute('aria-label') || active.tagName.toLowerCase();
    const input = one('color-picker-input');
    const popoverInput = one('color-picker-popover-input');
    const shown = one('color-picker-value');
    return [
      dialog.open ? 'open' : 'shut',
      `input=${input ? input.value : shown ? shown.textContent.trim() : ''}`,
      `popover=${popoverInput ? popoverInput.value : ''}`,
      `preview=${getComputedStyle(one('color-picker-preview')).backgroundColor}`,
      `area=${dialog.open ? place(thumb, area) : '-'}`,
      `ranges=${[...root.querySelectorAll('input[type=range]')].map(r => r.value).join('/')}`,
      `picked=${swatches.findIndex(s => s.getAttribute('aria-selected') === 'true')}`,
      `stop=${swatches.findIndex(s => s.getAttribute('tabindex') === '0')}`,
      `said=${one('color-picker-announce').textContent.trim()}`,
      `expanded=${[...root.querySelectorAll('[aria-expanded]')].map(e => e.getAttribute('aria-expanded')).join(',')}`,
      `focus=${focus}`,
    ].join(' | ');
  }, prefix);
}

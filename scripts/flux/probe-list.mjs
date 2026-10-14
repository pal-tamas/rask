#!/usr/bin/env node
// Reads what Flux UI's live page leaves behind, step by step, in a control that opens a list: every attribute
// of the control, its trigger, its popover and its rows, where focus is, and how many requests the step sent.
// open.mjs compares a walk in words; this prints the markup those words are read from, which is how a
// behaviour table is written down before the hook that follows it (rask-listbox.ts).
//
// Usage:  node scripts/flux/probe-list.mjs <slug> <example index> <steps> [--brief]
//         node scripts/flux/probe-list.mjs select 3 click,hover:2,ArrowDown,type:le,Escape
// Steps:  focus · click · click:<row> · hover:<row> · leave · outside · type:<text> · wait:<ms> · any key name.
// `--brief` prints one line a step — the rows shown, lit and picked, the active descendant, the focus — and
// everything after the last step.

import { chromium } from './lib.mjs';

const [slug = 'select', example = '3', walk = 'click'] = process.argv.slice(2);
const brief = process.argv.includes('--brief');
const steps = walk.split(',');
const browser = await chromium().launch();
const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await context.newPage();
let requests = 0;
page.on('request', request => {
  if (['xhr', 'fetch'].includes(request.resourceType())) requests++;
});
await page.goto(`https://fluxui.dev/components/${slug}`, { waitUntil: 'networkidle', timeout: 120000 });
const scope = (await page.$$('[data-preview-wrapper]'))[Number(example)];
await scope.evaluate(el => el.scrollIntoView({ block: 'center' }));
const trigger = await scope.$('button[role=combobox], input[role=combobox], [data-flux-pillbox-trigger]');
const rows = () => scope.$$('ui-option, ui-option-create');

// Runs in the page.
function read(wrapper) {
  const root = wrapper.querySelector('[data-flux-select],[data-flux-pillbox],[data-flux-autocomplete]') ?? wrapper;
  const attrs = el => [...el.attributes].filter(a => !/^(class|style|wire:|x-)/.test(a.name))
    .map(a => (a.value === '' ? a.name : `${a.name}=${a.value}`)).join(' ');
  const words = el => el.textContent.trim().replace(/\s+/g, ' ').slice(0, 24);
  const out = [`<${root.localName}> ${attrs(root)}`];
  for (const el of root.querySelectorAll('button[role=combobox], input, [data-flux-pillbox-trigger], [role=listbox], [popover], ui-selected')) {
    out.push(`  <${el.localName}> ${attrs(el)}${el.localName === 'input' ? ` value="${el.value}"` : ''}${el.localName === 'ui-selected' ? ` says "${words(el)}"` : ''}`);
  }

  for (const row of root.querySelectorAll('ui-option, ui-option-create, ui-option-empty, ui-empty, ui-option-group, [role=presentation]')) {
    out.push(`    <${row.localName}> "${words(row)}" ${attrs(row)} display=${getComputedStyle(row).display}`);
  }

  const focused = document.activeElement;
  const popup = root.querySelector('[popover]');
  const list = root.querySelector('[role=listbox]');
  out.push(`  FOCUS <${focused.localName}> ${attrs(focused)} · open=${popup ? popup.matches(':popover-open') : '?'} · scrollTop=${list ? list.scrollTop : '-'}`);
  return out.join('\n');
}

// Runs in the page. The same, in one line.
function line(wrapper) {
  const root = wrapper.querySelector('[data-flux-select],[data-flux-pillbox],[data-flux-autocomplete]') ?? wrapper;
  const words = el => el.textContent.trim().replace(/\s+/g, ' ').slice(0, 24);
  const rows = [...root.querySelectorAll('ui-option, ui-option-create, ui-option-empty, ui-empty')].filter(row => getComputedStyle(row).display !== 'none');
  const named = [...root.querySelectorAll('[aria-activedescendant]')].map(el => `${el.localName}→"${words(document.getElementById(el.getAttribute('aria-activedescendant')) ?? el)}"`);
  const focused = document.activeElement;
  const popup = root.querySelector('[popover]');
  const input = root.querySelector('input:not([type=hidden])');
  return [
    popup?.matches(':popover-open') ? 'open' : 'closed',
    `lit [${rows.filter(row => row.hasAttribute('data-active')).map(words).join(', ')}]`,
    `picked [${rows.filter(row => row.getAttribute('aria-selected') === 'true').map(words).join(', ')}]`,
    `shows ${rows.length}${rows.length < 4 ? ` [${rows.map(row => `${row.localName}:${words(row)}`).join(', ')}]` : ''}`,
    `names ${named.join(' ') || '-'}`,
    `focus <${focused.localName}${focused.getAttribute('role') ? ` role=${focused.getAttribute('role')}` : ''}>`,
    input ? `input "${input.value}"` : '',
    `says "${words(root.querySelector('ui-selected') ?? root)}"`,
  ].filter(Boolean).join(' · ');
}

for (const [at, step] of steps.entries()) {
  const [verb, arg] = step.split(':');
  const before = requests;
  if (verb === 'focus') await trigger.focus();
  else if (verb === 'click' && arg === undefined) await trigger.click();
  else if (verb === 'click') await (await rows())[Number(arg)].click();
  else if (verb === 'hover') await (await rows())[Number(arg)].hover();
  else if (verb === 'leave') await page.mouse.move(2, 2);
  else if (verb === 'outside') await page.mouse.click(5, 5);
  else if (verb === 'type') await page.keyboard.type(arg, { delay: 60 });
  else if (verb === 'wait') await page.waitForTimeout(Number(arg));
  else await page.keyboard.press(verb);
  await page.waitForTimeout(350);
  const state = await scope.evaluate(read);
  console.log(`=== ${step} (requests +${requests - before})`);
  console.log(brief && at < steps.length - 1 ? await scope.evaluate(line) : state);
}

console.log(`requests in all: ${requests}`);
await browser.close();

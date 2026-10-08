#!/usr/bin/env node
// Holds Ui.Editor IN USE to Flux's live editor: what parity.mjs cannot see, because it only exists once
// somebody types, presses a toolbar button or opens a list.
//
// One scenario is driven on both pages with real keys and a real pointer — fluxui.dev/components/editor,
// and the page FluxParityPages wrote (artifacts/flux-parity/rask/editor.html) with the kit's engine
// (src/Rask.Ui/Resources/ui-editor.js) mounted on it — and each step writes down what it observes: the
// HTML value, the events the editor raised, a control's states, where a popover opened, what has focus.
// The two transcripts must be the same, line for line, in light and in dark:
//   - the look of every kind of node a document can hold (headings, lists, quote, code, links, rule…);
//   - the placeholder, the empty value, the disabled editor;
//   - every toolbar toggle, by button and by shortcut, and the pressed state it shows;
//   - the heading and align lists: where they open, the active option by key and by pointer, picking;
//   - the link panel: where it opens, Enter, the insert and unlink buttons, ⌘K, Escape;
//   - the toolbar's arrow keys (they wrap) and its single tab stop; undo and redo; Markdown input rules.
//   - the marks no example shows a button for (code, subscript, superscript, highlight), by a control of that name.
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask page
//         node scripts/flux/parity-editor.mjs [--all]
// Exit code 1 on any difference.

import { readFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root } from './lib.mjs';

const raskPage = join(root, 'artifacts', 'flux-parity', 'rask', 'editor.html');
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

const engine = await readFile(join(root, 'src', 'Rask.Ui', 'Resources', 'ui-editor.js'), 'utf8');
// Where each page keeps the same thing: Flux's custom elements, and the native ones the kit writes.
const FLUX = { editor: 'ui-editor', list: 'ui-options', option: 'ui-option', value: 'value', shown: 'ui-selected > div', link: '[data-flux-editor-link]', disabled: ['disabled', ''] };
const RASK = { editor: '[data-ui-editor]', list: '[role=listbox]', option: '[role=option]', value: 'data-value', shown: 'button[role=combobox] [data-value]', link: '[data-ui-editor-link]', disabled: ['aria-disabled', 'true'] };
const DOCUMENT = '<h1>One</h1><h2>Two</h2><h3>Three</h3><p>Para <strong>b</strong> <em>i</em> <s>s</s> <u>u</u> <code>c</code> <mark>m</mark> x<sub>sub</sub> y<sup>sup</sup> <a href="https://a.b">link</a></p>'
  + '<ul><li><p>a</p><ul><li><p>nested</p></li></ul></li><li><p>b</p></li></ul><ol><li><p>one</p></li><li><p>two</p></li></ol>'
  + '<blockquote><p>quote</p><p>quote2</p></blockquote><pre><code class="language-js">let a = 1;</code></pre><p style="text-align:center">centered</p><p>last</p>';
const INHERITED = ['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];

const browser = await chromium().launch();
let failures = 0;
for (const scheme of ['light', 'dark']) {
  const theirs = await transcript(scheme, 'https://fluxui.dev/components/editor', FLUX);
  const mine = await transcript(scheme, pathToFileURL(raskPage).href, RASK, theirs.inherited);
  const length = Math.max(theirs.lines.length, mine.lines.length);
  let differing = 0;
  for (let i = 0; i < length; i++) {
    if (theirs.lines[i] === mine.lines[i]) continue;
    if (differing++ < (process.argv.includes('--all') ? Infinity : 12)) console.log(`FAIL ${scheme} step ${i}\n   Flux: ${theirs.lines[i]}\n   Rask: ${mine.lines[i]}`);
  }

  failures += differing;
  console.log(`${differing ? 'FAIL' : 'ok  '} ${scheme}: ${length} observations${differing ? `, ${differing} differ` : ''}`);
}

await browser.close();
console.log(failures ? `\nflux parity: the editor in use differs in ${failures} observation(s).` : '\nflux parity: the editor in use matches Flux.');
process.exit(failures ? 1 : 0);

async function transcript(scheme, url, at, inherited) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
  await context.addInitScript(`window.__look = ${look}; window.__states = ${states}`);
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  await page.evaluate(() => document.fonts.ready);
  if (inherited) {
    // The Rask page: the surroundings Flux's example sits in, then the engine, as an app would have loaded it.
    await page.evaluate(async ({ engine, inherited }) => {
      const wrapper = document.querySelector('[data-preview-wrapper]');
      for (const [key, value] of Object.entries(inherited)) wrapper.style[key] = value;
      const module = await import(URL.createObjectURL(new Blob([engine], { type: 'text/javascript' })));
      for (const editor of document.querySelectorAll('[data-ui-editor]')) module.mount(editor);
      window.__engine = module;
    }, { engine, inherited });
  } else {
    inherited = await page.evaluate(keys => Object.fromEntries(keys.map(key => [key, getComputedStyle(document.querySelector('[data-preview-wrapper]'))[key]])), INHERITED);
  }

  const lines = [];
  const editor = page.locator(at.editor).first();
  await editor.scrollIntoViewIfNeeded();
  const content = editor.locator('[data-slot=content]');
  // A gray's hue means nothing at zero chroma, and prints as `none` once a minifier has been at the sheet.
  const note = (what, value) => lines.push(`${what}: ${value}`.replaceAll(' none)', ' 0)'));
  const value = () => editor.evaluate(el => el.value);
  // Both editors settle a frame or two after a change or a click: an observation waits for that.
  const settle = () => page.waitForTimeout(100);
  const set = async html => {
    await editor.evaluate((el, html) => el.editor.commands.setContent(html, true), html);
    await settle();
  };
  const state = selector => editor.evaluate((el, selector) => window.__states(el.querySelector(selector)), selector);
  const focus = () => page.evaluate(() => { const a = document.activeElement; return a.dataset.slot ?? a.dataset.editor ?? a.closest('[data-editor]')?.dataset.editor ?? a.tagName; });
  const events = () => page.evaluate(() => window.__events.splice(0).join(','));
  // A popover's box against the button that opened it, and whether it is in the top layer.
  const placed = (panel, trigger) => editor.evaluate((el, [panel, trigger]) => {
    const p = el.querySelector(panel), a = p.getBoundingClientRect(), b = el.querySelector(trigger).getBoundingClientRect();
    return `${getComputedStyle(p).display} top-layer=${p.matches(':popover-open')} dx=${Math.round(a.x - b.x)} dy=${Math.round(a.y - b.bottom)} ${Math.round(a.width)}x${Math.round(a.height)}`;
  }, [panel, trigger]);
  const display = panel => editor.evaluate((el, panel) => getComputedStyle(el.querySelector(panel)).display, panel);
  const options = name => editor.evaluate((el, [name, at]) => [...el.querySelectorAll(`[data-editor=${name}] ${at.option}`)].map(o =>
    `${o.getAttribute(at.value)}${o.hasAttribute('data-active') ? '@' : ''}${o.hasAttribute('data-selected') ? '*' : ''}[${o.getAttribute('aria-selected')}] bg=${getComputedStyle(o).backgroundColor} svg=${getComputedStyle(o.querySelector('svg')).color}`).join(' | '), [name, at]);
  const shown = name => editor.evaluate((el, [name, at]) => el.querySelector(`[data-editor=${name}] ${at.shown}`)?.dataset.value, [name, at]);
  const select = name => `[data-editor=${name}] button`;
  const linkButton = '[data-editor=link] button:not([data-editor])';
  const linkPanel = '[data-editor=link] > [popover]';
  await editor.evaluate(el => {
    window.__events = [];
    for (const type of ['input', 'change']) el.addEventListener(type, event => window.__events.push(`${type}${event.bubbles ? '^' : ''}`));
  });

  // The document: every kind of node, as it computes.
  await set(DOCUMENT);
  note('events of a programmatic change', await events());
  for (const line of await content.evaluate(c => {
    const seen = new Set(), out = [];
    for (const node of c.querySelectorAll('*')) {
      const parent = node.parentElement, own = parent === c ? '' : `${parent.tagName.toLowerCase()}>`;
      const key = `${own}${node.tagName.toLowerCase()}${node === parent.firstElementChild ? ':first' : ''}${node === parent.lastElementChild ? ':last' : ''}`;
      if (seen.has(key)) continue;
      seen.add(key);
      out.push(`${key} ${window.__look(node)}`);
      for (const pseudo of ['::before', '::after', '::marker']) {
        const drawn = getComputedStyle(node, pseudo).content;
        if (pseudo === '::marker' ? node.tagName === 'LI' : drawn !== 'none' && drawn !== 'normal') out.push(`${key}${pseudo} ${window.__look(node, pseudo)}`);
      }
    }
    return out;
  })) lines.push(line.replaceAll(' none)', ' 0)'));
  note('area', await content.evaluate(c => window.__look(c)));
  note('value', await value());
  await set('');
  note('empty value', JSON.stringify(await value()));
  note('empty paragraph', await content.evaluate(c => { const p = c.querySelector('p'); return `${p.className} placeholder=${p.getAttribute('data-placeholder')} ${window.__look(p, '::before')}`; }));
  note('placeholder of the second editor', await page.locator(at.editor).nth(1).evaluate(el => { const p = el.querySelector('[data-slot=content] p'); return `${p.getAttribute('data-placeholder')} ${window.__look(p, '::before')}`; }));

  // Toggles: by button, by shortcut, and what the button says.
  await set('<p>Hello world</p>');
  await content.click();
  await settle();
  await events();
  note('focused area', await editor.evaluate(el => window.__look(el.querySelector('[data-slot=content]')) + ' root ' + getComputedStyle(el).boxShadow.slice(-40)));
  await page.keyboard.press('ControlOrMeta+a');
  await settle();
  note('bold at rest', await state('[data-editor=bold]'));
  await editor.locator('[data-editor=bold]').click();
  await settle();
  note('bold pressed', `${await state('[data-editor=bold]')} value=${await value()} events=${await events()} focus=${await focus()}`);
  await page.keyboard.press('ControlOrMeta+b');
  note('bold by shortcut', `${await state('[data-editor=bold]')} value=${await value()}`);
  for (const [name, keys] of [['italic', 'ControlOrMeta+i'], ['strike', 'ControlOrMeta+Shift+s'], ['bullet', 'ControlOrMeta+Shift+8'], ['ordered', 'ControlOrMeta+Shift+7'], ['blockquote', 'ControlOrMeta+Shift+b']]) {
    await set('<p>Hello world</p><p>second</p>');
    await content.locator('p').first().click();
    await settle();
    for (let press = 1; press <= 2; press++) {
      await editor.locator(`[data-editor=${name}]`).click();
      await page.waitForTimeout(120);
      note(`${name} click ${press}`, `${await state(`[data-editor=${name}]`)} value=${await value()} focus=${await focus()}`);
    }

    await page.keyboard.press(keys);
    note(`${name} by shortcut`, `${await state(`[data-editor=${name}]`)} value=${await value()}`);
  }

  for (const keys of ['ControlOrMeta+u', 'ControlOrMeta+e', 'ControlOrMeta+Shift+h', 'ControlOrMeta+,', 'ControlOrMeta+.', 'ControlOrMeta+Alt+2', 'ControlOrMeta+Alt+0', 'ControlOrMeta+Shift+e', 'ControlOrMeta+Shift+r']) {
    await set('<p>Hello world</p>');
    await content.click();
    await settle();
    await page.keyboard.press('ControlOrMeta+a');
    await settle();
    await page.waitForTimeout(60);
    await page.keyboard.press(keys);
    await page.waitForTimeout(60);
    note(`shortcut ${keys}`, await value());
  }

  // Typing, undo, redo, and the Markdown rules.
  await set('<p>Hello world</p>');
  await content.click();
  await settle();
  await page.keyboard.press('ControlOrMeta+a');
  await settle();
  await events();
  await page.keyboard.type('ab');
  note('two characters typed', `${await value()} events=${await events()}`);
  await page.keyboard.press('ControlOrMeta+z');
  note('undo', await value());
  await page.keyboard.press('ControlOrMeta+Shift+z');
  note('redo', await value());
  await page.keyboard.press('ControlOrMeta+a');
  await settle();
  await page.keyboard.press('Backspace');
  for (const text of ['# Title\n', '## Second\n', '### Third\n', '- item\n\n', '1. one\n\n', '> quote\n\n', '**bold** *italic* ~~struck~~ `code` \n', '---\n', '```js\nlet a = 1;']) await page.keyboard.type(text, { delay: 5 });
  note('markdown input rules', await value());
  await page.keyboard.press('ControlOrMeta+Shift+Enter');

  // The heading list.
  await set('<p>Hello world</p>');
  await content.click();
  await settle();
  note('heading closed', await state(select('heading')));
  await editor.locator(select('heading')).click();
  await page.waitForTimeout(300);
  note('heading open', `${await state(select('heading'))} list ${await placed(`[data-editor=heading] ${at.list}`, select('heading'))} focus=${await focus()}`);
  note('heading options', await options('heading'));
  await page.keyboard.press('ArrowDown');
  note('heading ArrowDown', await options('heading'));
  await page.keyboard.press('ArrowUp');
  await page.keyboard.press('ArrowUp');
  note('heading ArrowUp at the top', await options('heading'));
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(200);
  note('heading Enter', `${await value()} ${await state(select('heading'))} list ${await display(`[data-editor=heading] ${at.list}`)} shown=${await shown('heading')} focus=${await focus()}`);
  await editor.locator(select('heading')).click();
  await page.waitForTimeout(300);
  const third = await editor.locator(`[data-editor=heading] ${at.option}`).nth(3).evaluate(o => { const box = o.getBoundingClientRect(); return [box.x + 20, box.y + 10]; });
  await page.mouse.move(third[0], third[1], { steps: 4 });
  note('heading pointer over an option', await options('heading'));
  await page.mouse.down();
  await page.mouse.up();
  await page.waitForTimeout(200);
  note('heading option clicked', `${await value()} shown=${await shown('heading')} focus=${await focus()}`);
  await editor.locator(select('heading')).click();
  await page.waitForTimeout(300);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  note('heading Escape', `${await state(select('heading'))} focus=${await focus()}`);
  await editor.locator(select('heading')).click();
  await page.waitForTimeout(300);
  await page.mouse.click(5, 5);
  await page.waitForTimeout(150);
  note('heading press outside', `${await state(select('heading'))} list ${await display(`[data-editor=heading] ${at.list}`)} focus=${await focus()}`);
  await set('<h2>Head</h2><p>para</p>');
  await content.locator('h2').click();
  await settle();
  note('caret in a heading', `shown=${await shown('heading')} ${await options('heading')}`);

  // The align list.
  await set('<p>Hello world</p>');
  await content.click();
  await settle();
  await editor.locator(select('align')).click();
  await page.waitForTimeout(300);
  note('align open', `${await state(select('align'))} list ${await placed(`[data-editor=align] ${at.list}`, select('align'))}`);
  await page.keyboard.press('ArrowDown');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(200);
  note('align center', `${await value()} shown=${await shown('align')}`);
  await page.keyboard.press('ControlOrMeta+Shift+l');
  note('align left by shortcut', `${await value()} shown=${await shown('align')}`);

  // The link panel.
  await set('<p>Hello world</p>');
  await content.click();
  await settle();
  await page.keyboard.press('ControlOrMeta+a');
  await settle();
  await editor.locator(linkButton).click();
  await page.waitForTimeout(300);
  note('link open', `${await state(linkButton)} panel ${await placed(linkPanel, linkButton)} focus=${await focus()}`);
  note('link panel look', await editor.evaluate((el, at) => {
    const q = selector => getComputedStyle(el.querySelector(selector));
    const input = q('[data-editor="link:url"]'), insert = q('[data-editor="link:insert"]'), row = q(at.link), panel = q('[data-editor=link] > [popover]');
    return `panel bg=${panel.backgroundColor} border=${panel.borderTopColor} input ${input.color} ${input.fontSize}/${input.lineHeight} h=${input.height} outline=${input.outlineStyle} insert ${insert.color} ${insert.width}x${insert.height} row h=${row.height}`;
  }, at));
  await page.keyboard.type('example.com');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(200);
  await page.mouse.move(640, 4);
  await settle();
  note('link Enter', `${await value()} ${await state(linkButton)} panel ${await display(linkPanel)} focus=${await focus()}`);
  await editor.locator(linkButton).click();
  await page.waitForTimeout(300);
  note('link reopened', await editor.locator('[data-editor="link:url"]').inputValue());
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  note('link Escape, opened by its button', `${await state(linkButton)} panel ${await display(linkPanel)} focus=${await focus()}`);
  await editor.locator(linkButton).click();
  await page.waitForTimeout(300);
  await editor.locator('[data-editor="link:unlink"]').click();
  await page.waitForTimeout(200);
  note('unlink', `${await value()} ${await state(linkButton)} focus=${await focus()}`);
  await content.click();
  await settle();
  await page.keyboard.press('ControlOrMeta+a');
  await settle();
  await page.keyboard.press('ControlOrMeta+k');
  await page.waitForTimeout(300);
  note('link by shortcut', `panel ${await placed(linkPanel, linkButton)} focus=${await focus()}`);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(150);
  note('link Escape, opened by the shortcut', `${await state(linkButton)} panel ${await display(linkPanel)} focus=${await focus()}`);
  await editor.locator(linkButton).click();
  await page.waitForTimeout(300);
  await editor.locator('[data-editor="link:url"]').fill('https://x.y');
  await editor.locator('[data-editor="link:insert"]').click();
  await page.waitForTimeout(200);
  note('link insert button', `${await value()} focus=${await focus()}`);

  // The toolbar's keys: one tab stop, the arrows walk it.
  const stops = () => editor.evaluate(el => [...el.querySelectorAll('[role=toolbar] button')].filter(b => b.offsetParent).map(b => `${b.dataset.editor ?? b.closest('[data-editor]')?.dataset.editor}:${b.tabIndex}${b === document.activeElement ? '*' : ''}`).join(' '));
  await editor.locator(select('heading')).focus();
  note('toolbar focused', await stops());
  for (const key of ['ArrowRight', 'ArrowRight', 'ArrowRight', 'ArrowLeft', 'Home', 'End', 'ArrowDown']) {
    await page.keyboard.press(key);
    await page.waitForTimeout(120);
    note(`toolbar ${key}`, await stops());
  }

  await page.keyboard.press('Escape');
  await page.keyboard.press('Tab');
  note('toolbar Tab', await focus());
  await page.keyboard.press('Shift+Tab');
  note('area Shift+Tab', await focus());
  // One at a time, as a person presses them: Flux moves focus a frame after the key.
  const walk = async (key, presses) => {
    for (let press = 0; press < presses; press++) {
      await page.keyboard.press(key);
      await page.waitForTimeout(120);
    }
  };
  await page.waitForTimeout(200);
  await walk('ArrowLeft', 4);
  note('toolbar ArrowLeft past the start', await stops());
  await walk('ArrowRight', 12);
  note('toolbar ArrowRight past the end', await stops());

  // Disabled.
  await editor.evaluate((el, [name, value]) => el.setAttribute(name, value), at.disabled);
  await page.waitForTimeout(200);
  note('disabled', await editor.evaluate(el => {
    const area = el.querySelector('[data-slot=content]'), bold = el.querySelector('[data-editor=bold]');
    return `shadow=${getComputedStyle(el).boxShadow.slice(-36)} area ${getComputedStyle(area).color} editable=${area.getAttribute('contenteditable')} bold disabled=${bold.disabled} opacity=${getComputedStyle(bold).opacity} select disabled=${el.querySelector('[data-editor=heading] button').disabled}`;
  }));
  await editor.evaluate((el, [name]) => el.removeAttribute(name), at.disabled);
  await page.waitForTimeout(200);
  note('enabled again', await editor.evaluate(el => `editable=${el.querySelector('[data-slot=content]').getAttribute('contenteditable')} bold disabled=${el.querySelector('[data-editor=bold]').disabled}`));

  // The four marks no example's toolbar shows. Flux's element wires a control by its name, so a control of
  // that name is made — the bold button of a copy of the editor, renamed — and pressed over a selection: what
  // `code` does (the inline mark, not the block) is read here and nowhere else. Waits are on state.
  for (const name of ['code', 'subscript', 'superscript', 'highlight']) {
    note(`a control named ${name}`, await page.evaluate(async ([selector, name]) => {
      const frame = () => new Promise(resolve => requestAnimationFrame(resolve));
      const first = document.querySelector(selector), copy = first.cloneNode(true);
      const button = copy.querySelector('[data-editor=bold]');
      button.setAttribute('data-editor', name);
      for (const state of ['aria-pressed', 'data-match']) button.removeAttribute(state);
      first.parentElement.appendChild(copy);
      window.__engine?.mount(copy);
      for (let i = 0; i < 200 && !copy.editor; i++) await frame();
      copy.editor.commands.setContent('<p>Hello world</p>', true);
      copy.editor.commands.focus();
      copy.editor.commands.selectAll();
      // A control is wired once the editor has said its state on it: until then a press is nobody's.
      for (let i = 0; i < 200 && !button.hasAttribute('aria-pressed'); i++) await frame();
      button.click();
      for (let i = 0; i < 200 && copy.value === '<p>Hello world</p>'; i++) await frame();
      await frame();
      await frame();
      const said = `${copy.value} pressed=${button.getAttribute('aria-pressed')} match=${button.hasAttribute('data-match')}`;
      copy.remove();
      return said;
    }, [at.editor, name]));
  }

  await context.close();
  return { lines, inherited };
}

// Runs in the page. How one node of a document computes — what a reader of it would see.
function look(element, pseudo) {
  const c = getComputedStyle(element, pseudo);
  const keys = ['display', 'position', 'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'marginTop', 'marginBottom', 'marginLeft',
    'fontSize', 'lineHeight', 'fontWeight', 'fontStyle', 'color', 'backgroundColor', 'borderTopWidth', 'borderLeftWidth',
    'borderTopLeftRadius', 'textDecorationLine', 'listStyleType', 'minHeight', 'maxHeight', 'overflowY', 'whiteSpace', 'textAlign',
    'verticalAlign', 'outlineStyle', 'float', 'pointerEvents', 'cursor', 'overflowWrap', 'width'];
  const family = c.fontFamily.includes('mono') ? 'mono' : 'text';
  // A width to a tenth of a pixel: layout snaps to 1/64px, and where it snaps is not the component's.
  const said = key => (key === 'width' && c[key].endsWith('px') ? `${Math.round(parseFloat(c[key]) * 10) / 10}px` : c[key]);
  return keys.map(key => `${key}=${said(key)}`).join(';') + `;family=${family}` + (pseudo ? `;content=${c.content}` : '');
}

// Runs in the page. The states a toolbar control states, under names both pages use.
function states(control) {
  const c = getComputedStyle(control);
  const said = ['aria-pressed', 'aria-expanded', 'tabindex'].filter(name => control.hasAttribute(name)).map(name => `${name}=${control.getAttribute(name)}`);
  for (const flag of ['data-match', 'data-open', 'aria-activedescendant', 'disabled']) if (control.hasAttribute(flag)) said.push(flag);
  return `[${said.join(' ')}] ${c.color} on ${c.backgroundColor}`;
}

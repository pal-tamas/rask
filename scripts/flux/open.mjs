// Holds a control's OPEN list of options to Flux UI's live documentation. parity.mjs measures every example
// as loaded, where a drawn list is not displayed; this opens each one — on fluxui.dev and on the page
// FluxParityPages wrote — and compares what is then on screen:
//   - the control and the whole popup subtree: tag, box, computed styles, pseudo-elements;
//   - where the popup sits against its trigger (every box is measured from the trigger's corner);
//   - a row hovered, and a row pressed.
// `--live <url>` walks the keyboard and the pointer through Flux's examples and through a running site, and
// compares what each step left behind.
//
// One module for every control that opens a list: parity-select.mjs, parity-autocomplete.mjs and
// parity-pillbox.mjs each hand it what is theirs — which page, which selectors, which walks. The comparison
// is parity.mjs's, copied to its minimum: that file runs on import and exports nothing yet (see "Open work"
// in .claude/skills/flux-component/SKILL.md).
//
// A control (`kind`) is:
//   slug       Flux's page, and the Rask parity page of the same name
//   raskPage   the Rask parity page, when the open state has one of its own (pillbox-picked)
//   focused    selectors, the first that matches being what Flux's script has focused once the list is open
//   pick       rows clicked on Flux's page once the list is open; the Rask page is written with them picked
//   name       what the verdict calls it ("select")
//   root       selector of the control's root, Flux's marker and Rask's
//   trigger    selector of what a click opens it from, inside the root
//   popup      selector of the popover, inside the root
//   rows       selector of an option row; `allRows` adds the create row
//   search     selector of a search field inside the popup, when there is one
//   native     Flux's custom elements and the native element Rask.Ui writes in their place
//   says       runs in the page: (root, trigger) => what the control shows as its answer
//   walks      the --live walks: { name, flux: example index, rask: selector, keys: [...], accepted? }
//   liveRoot   runs in the page: the element a walk's `rask` selector found => the example around its control

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, root, STYLES } from './lib.mjs';

const IGNORED = new Set(['width', 'height']);

export async function openParity(kind) {
  const args = process.argv.slice(2);
  const flag = name => args.includes(`--${name}`);
  const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
  const out = join(root, 'artifacts', 'flux-parity');
  const fluxUrl = `https://fluxui.dev/components/${kind.slug}`;
  const raskPage = join(out, 'rask', `${kind.raskPage ?? kind.slug}.html`);
  if (!existsSync(raskPage) && !flag('record') && !option('live')) {
    console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
    process.exit(1);
  }

  const browser = await chromium().launch();
  if (option('live') || flag('record')) {
    // `--walk <name>` runs one walk. `--wait <ms>` between steps: a Debug WASM site takes over a second to draw a page of demos again.
    const differs = await walkBoth(browser, kind, fluxUrl, option('live'), flag('all') || flag('record'), Number(option('wait') ?? 450), option('walk'));
    await browser.close();
    console.log(differs ? `\nflux parity: the ${kind.name}'s keyboard differs in ${differs} walk(s).` : `\nflux parity: the ${kind.name} walks as Flux does.`);
    process.exit(differs ? 1 : 0);
  }

  const folder = `${kind.slug}-open`;
  const fluxFile = join(out, 'flux', folder, 'measurements.json');
  let flux;
  if (existsSync(fluxFile) && !flag('refresh')) {
    flux = JSON.parse(await readFile(fluxFile, 'utf8'));
  } else {
    flux = await measureOpen(browser, kind, fluxUrl, join(out, 'flux', folder));
    await writeFile(fluxFile, JSON.stringify(flux));
  }

  const rask = await measureOpen(browser, kind, pathToFileURL(raskPage).href, join(out, 'rask', folder), flux);
  await writeFile(join(out, 'rask', folder, 'measurements.json'), JSON.stringify(rask));
  await browser.close();

  const limit = flag('all') ? Infinity : 12;
  let failures = 0;
  for (const scheme of ['light', 'dark']) {
    for (const [nth, theirs] of flux[scheme].entries()) {
      if (option('section') && option('section') !== theirs.section) continue;
      const label = `${scheme} ${theirs.section || '(intro)'}#${theirs.ordinal} (${nth})`;
      // By position: a Livewire example that answered a click is re-rendered, and loses the mark its
      // ordinal is counted by.
      const mine = rask[scheme][nth];
      if (!mine) {
        console.log(`MISSING ${label}`);
        failures++;
        continue;
      }

      const diffs = [];
      if (!theirs.open) diffs.push('Flux did not open');
      if (!mine.open) diffs.push('Rask did not open');
      compareTree(kind, theirs, theirs.nodes[0], mine, mine.nodes[0], kind.name, diffs);
      for (const state of ['hover', 'pressed']) {
        const x = theirs.rows[state] ?? {};
        const y = mine.rows[state] ?? {};
        for (const key of new Set([...Object.keys(x), ...Object.keys(y)])) {
          if (!same(x[key], y[key])) diffs.push(`row:${state} ${key}: ${x[key] ?? '(unchanged)'} vs ${y[key] ?? '(unchanged)'}`);
        }
      }

      failures += diffs.length ? 1 : 0;
      console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ''}`);
      for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
      if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
    }
  }

  console.log(failures ? `\nflux parity: the open ${kind.name} differs in ${failures} example(s).` : `\nflux parity: the open ${kind.name} matches Flux.`);
  process.exit(failures ? 1 : 0);
}

// ----- The keyboard, on a running app ------------------------------------------------------------------
// The parity page is static: it has no runtime, so its lists open (the popover is the browser's) and
// nothing more. `--live <url>` walks the same keys through Flux's examples and through the showcase of a
// running site (`dotnet run --project src/Rask.Site`, then the URL of the demo's page) and compares what
// each key left behind: whether the list is open, the active row, the picked rows, what the control says
// and where focus is. `--record` walks Flux's page alone and prints every step: how a behaviour table is
// written down before the component is.
async function walkBoth(browser, kind, fluxUrl, live, every, wait, only) {
  let differs = 0;
  for (const walk of kind.walks.filter(each => !only || each.name === only)) {
    const theirs = await walkOne(browser, kind, fluxUrl, walk, 450, wrappers => wrappers[walk.flux]);
    const mine = live
      ? await walkOne(browser, kind, live, walk, wait, async (_, page) => (await page.waitForSelector(walk.rask, { timeout: 120000 })).evaluateHandle(kind.liveRoot))
      : theirs;
    const lines = walk.keys.map((key, i) => ({ key, theirs: theirs[i], mine: mine[i] }));
    const off = lines.filter(line => line.theirs !== line.mine);
    differs += off.length && !walk.accepted ? 1 : 0;
    const verdict = !off.length ? 'ok  ' : walk.accepted ? 'DIFF' : 'FAIL';
    console.log(`${verdict} ${walk.name} — ${walk.keys.length} steps${off.length ? `, ${off.length} differ` : ''}${off.length && walk.accepted ? ` (accepted: ${walk.accepted})` : ''}`);
    for (const line of every ? lines : off) {
      console.log(`       ${line.key.padEnd(10)} Flux: ${line.theirs}`);
      if (line.theirs !== line.mine) console.log(`       ${''.padEnd(10)} Rask: ${line.mine}`);
    }
  }

  return differs;
}

async function walkOne(browser, kind, url, walk, wait, find) {
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 120000 });
  await page.waitForSelector(kind.root, { timeout: 120000 });
  const scope = await find(await page.$$('[data-preview-wrapper]'), page);
  await scope.evaluate(el => el.scrollIntoView({ block: 'center' }));
  const trigger = await scope.$(kind.trigger);
  const rows = () => scope.$$(kind.rows);
  const selectors = { root: kind.root, trigger: kind.trigger, popup: kind.popup, rows: kind.allRows ?? kind.rows, search: kind.search ?? null, says: String(kind.says) };
  const states = [];
  for (const key of walk.keys) {
    const [verb, arg] = key.split(':');
    if (verb === 'focus') await trigger.focus();
    else if (verb === 'click' && arg === undefined) await trigger.click();
    else if (verb === 'click') await (await rows())[Number(arg)].click();
    else if (verb === 'hover') await (await rows())[Number(arg)].hover();
    else if (verb === 'remove') await (await scope.$$(kind.remove))[Number(arg)].click();
    else if (verb === 'leave') await page.mouse.move(2, 2);
    else if (verb === 'type') await page.keyboard.type(arg, { delay: 60 });
    else await page.keyboard.press(verb);
    await page.waitForTimeout(wait);
    states.push(await scope.evaluate(read, selectors));
  }

  await context.close();
  return states;
}

// Runs in the page. What a key left behind, in words both pages can be held to.
function read(scope, selectors) {
  const control = scope.querySelector(selectors.root);
  const trigger = control.querySelector(selectors.trigger);
  const popup = control.querySelector(selectors.popup);
  const words = el => (el.innerText ?? '').trim().replace(/\s+/g, ' ');
  const rows = [...control.querySelectorAll(selectors.rows)].filter(row => getComputedStyle(row).display !== 'none');
  const open = popup.matches(':popover-open');
  const focused = document.activeElement;
  const focus = focused === trigger ? 'trigger'
    : selectors.search && focused?.closest?.(selectors.search) ? 'search'
      : popup.contains(focused) ? 'list'
        : control.contains(focused) ? `${focused.localName} in the control` : 'elsewhere';
  const active = open ? rows.find(row => row.hasAttribute('data-active')) : undefined;
  // eslint-disable-next-line no-new-func
  const says = new Function(`return (${selectors.says})`)()(control, trigger, words);
  return [
    open ? 'open' : 'closed',
    `says "${says}"`,
    `active ${active ? `"${words(active)}"` : '-'}`,
    `picked [${rows.filter(row => row.getAttribute('aria-selected') === 'true').map(words).join(', ')}]`,
    open ? `shows ${rows.length}` : '',
    `focus ${focus}`,
  ].filter(Boolean).join(' · ');
}

// Every example of the control on `url`, opened one at a time and measured, in light and in dark. `like`
// is Flux's measurement when the page is Rask's: each example is given the text its Flux twin inherits from
// the docs page (parity.mjs's INHERITED rule), and the page the room under its last example that Flux's
// has, so a list at the foot of it opens downwards on both.
async function measureOpen(browser, kind, url, shots, like) {
  const schemes = {};
  const selectors = { root: kind.root, trigger: kind.trigger, popup: kind.popup };
  for (const scheme of ['light', 'dark']) {
    await mkdir(join(shots, scheme), { recursive: true });
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    await page.evaluate(() => document.fonts.ready);
    const examples = [];
    const wrappers = await page.$$('[data-preview-wrapper]');
    if (like) await page.evaluate(() => { document.body.style.paddingBottom = '900px'; });
    for (const [index, wrapper] of wrappers.entries()) {
      if (!(await wrapper.$(kind.popup))) continue;
      const twin = like?.[scheme][examples.length];
      if (twin) await wrapper.evaluate((w, inherited) => Object.assign(w.style, inherited), twin.inherited);
      await wrapper.evaluate(w => w.scrollIntoView({ block: 'center' }));
      const trigger = await wrapper.$(kind.trigger);
      await trigger.click();
      await page.waitForTimeout(350);
      // A static page has no runtime to pick with: Rask's is written with these rows already picked.
      for (const row of like ? [] : kind.pick ?? []) {
        await (await wrapper.$$(kind.rows))[row].click();
        await page.waitForTimeout(350);
      }

      // A pointer under a hand never rests on one pixel: a hover that a re-render dropped (Livewire answers
      // the click on a backend-search example) is back with the next move.
      // Off the control altogether where rows were picked: Flux's page takes no pointer while a list is
      // open, so nothing on its trigger hovers, and a static page's trigger would.
      const at = await trigger.boundingBox();
      if (kind.pick) await page.mouse.move(2, 2);
      else await page.mouse.move(at.x + at.width / 2 + 1, at.y + at.height / 2);
      await page.waitForTimeout(100);
      // A static page has no runtime to open a list from an input: the popover is shown as the runtime
      // shows it.
      await wrapper.evaluate((w, popup) => {
        const el = w.querySelector(popup);
        if (el && Object.keys(el.dataset).some(k => k.startsWith('ui')) && !el.matches(':popover-open')) el.showPopover();
      }, kind.popup);
      // …none to hear the pointer leave the list after those picks, which is where Flux's cursor goes out…
      if (like && kind.pick) await wrapper.evaluate(w => w.querySelectorAll('[data-active]').forEach(row => row.removeAttribute('data-active')));
      // …and none to hand focus where Flux's script hands it.
      for (const selector of like ? kind.focused ?? [] : []) {
        const target = await wrapper.$(selector);
        if (target) {
          await target.focus();
          break;
        }
      }

      const example = await wrapper.evaluate(collect, { STYLES, index, selectors });
      await page.screenshot({ path: join(shots, scheme, `${String(index).padStart(2, '0')}-${example.section || 'intro'}.png`) });

      example.rows = {};
      // The second option: the first is where the cursor already is.
      const row = (await wrapper.$$(kind.rows))[1];
      if (row) {
        const before = await row.evaluate(look, STYLES);
        await row.hover();
        await page.waitForTimeout(150);
        example.rows.hover = changed(before, await row.evaluate(look, STYLES));
        await page.mouse.down();
        await page.waitForTimeout(100);
        example.rows.pressed = changed(before, await row.evaluate(look, STYLES));
        // Released off the row, so nothing is picked.
        await page.mouse.move(2, 2);
        await page.mouse.up();
      }

      await page.keyboard.press('Escape');
      await wrapper.evaluate((w, popup) => {
        const el = w.querySelector(popup);
        if (el && Object.keys(el.dataset).some(k => k.startsWith('ui')) && el.matches(':popover-open')) el.hidePopover();
      }, kind.popup);
      await page.mouse.click(2, 2);
      await page.waitForTimeout(150);
      examples.push(example);
    }

    schemes[scheme] = examples;
    await context.close();
  }

  return schemes;
}

function changed(before, after) {
  return Object.fromEntries(Object.keys(after).filter(k => after[k] !== before[k]).map(k => [k, after[k]]));
}

// Runs in the page. One node's computed look.
function look(el, STYLES) {
  const computed = getComputedStyle(el);
  return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
}

// Runs in the page. The control of one example, open: every element under its root, measured from the
// trigger's corner. lib.mjs's collect(), from another origin.
function collect(wrapper, { STYLES, index, selectors }) {
  for (const animation of document.getAnimations()) animation.finish?.();
  const headings = [...document.querySelectorAll('h2[id]')];
  const section = wrapper.dataset.section
    ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
  const ordinal = [...document.querySelectorAll('[data-preview-wrapper]')].slice(0, index)
    .filter(w => (w.getAttribute('data-o-section') ?? '') === section).length;
  wrapper.setAttribute('data-o-section', section);
  const control = wrapper.querySelector(selectors.root);
  const trigger = control.querySelector(selectors.trigger);
  const popup = control.querySelector(selectors.popup);
  const origin = trigger.getBoundingClientRect();
  const keep = name => !/^(class|style|wire:|x-|@|:|data-o$|data-m$|id$)/.test(name);
  const style = (el, pseudo) => {
    const computed = getComputedStyle(el, pseudo);
    return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
  };

  const nodes = [];
  [control, ...control.querySelectorAll('*')].slice(0, 600).forEach((el, i) => {
    const id = `${index}-${i}`;
    el.setAttribute('data-o', id);
    const box = el.getBoundingClientRect();
    const node = {
      id, parent: el === control ? null : el.parentElement.getAttribute('data-o'), tag: el.tagName.toLowerCase(),
      attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
      text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
      box: [box.x - origin.x, box.y - origin.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
      style: style(el),
    };
    for (const pseudo of ['::before', '::after']) {
      const content = getComputedStyle(el, pseudo).content;
      if (content && content !== 'none' && content !== 'normal') node[pseudo] = { content, ...style(el, pseudo) };
    }

    nodes.push(node);
  });

  const inherited = Object.fromEntries(['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing']
    .map(key => [key, getComputedStyle(wrapper)[key]]));
  return { index, section, ordinal, open: popup.matches(':popover-open'), inherited, nodes };
}

function compareTree(kind, theirs, a, mine, b, where, diffs) {
  if ((kind.native[a.tag] ?? a.tag) !== b.tag) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);

  const differs = (x, y) => Math.abs(x - y) > 0.6;
  const boxless = n => n.box[2] === 0 && n.box[3] === 0;
  const displayed = !(boxless(a) && boxless(b));
  if (differs(a.box[2], b.box[2]) || differs(a.box[3], b.box[3])) diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  if (displayed && [0, 1].some(i => differs(a.box[i], b.box[i]))) {
    diffs.push(`${where}: offset from the trigger ${a.box[0]},${a.box[1]} vs ${b.box[0]},${b.box[1]}`);
  }

  // Flux's script lays the open popup out with an inline `position: absolute` and pixel insets; Rask.Ui
  // anchors it in CSS, where a popover in the top layer is `fixed`. Where it ends up is the offset above.
  const placed = 'popover' in a.attrs;
  compareStyles(a.style, b.style, where, diffs, placed ? new Set(['position']) : undefined);
  for (const pseudo of ['::before', '::after']) {
    if (!a[pseudo] !== !b[pseudo]) diffs.push(`${where}${pseudo}: ${a[pseudo] ? 'only in Flux' : 'only in Rask'}`);
    else if (a[pseudo]) compareStyles(a[pseudo], b[pseudo], `${where}${pseudo}`, diffs);
  }

  // A <template> holds what Flux's script clones from; it draws nothing and Rask.Ui has no use for one.
  const kids = (example, n) => example.nodes.filter(c => c.parent === n.id && c.tag !== 'template');
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => compareTree(kind, theirs, child, mine, cb[i], `${where} > ${child.tag}[${i}]`, diffs));
}

function compareStyles(x, y, where, diffs, let_go) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || let_go?.has(key) || undrawn(key, x, y) || same(x[key], y[key])) continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

// The colour of a border or an outline neither side draws is each PAGE's reset, not the component's.
function undrawn(key, x, y) {
  const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
  if (side) return x[`border${side}Width`] === '0px' && y[`border${side}Width`] === '0px';
  return key === 'outlineColor' && x.outlineStyle === 'none' && y.outlineStyle === 'none';
}

// Colours and lengths print with float noise that differs between two pages computing the same value.
function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  const round = v => v.replace(/(okl(?:ch|ab)\([^)]*?) none\)/g, '$1 0)')
    .replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000));
  return round(x) === round(y);
}

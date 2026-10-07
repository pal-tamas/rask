#!/usr/bin/env node
// Holds a Rask.Ui component to Flux UI's live documentation, example by example.
//
// Two measurements of the same examples, taken the same way (lib.mjs): Flux's docs page, and the page
// FluxParityPages wrote for the Rask component (artifacts/flux-parity/rask/<slug>.html). Every marked
// node — `data-flux-*` there, `data-ui-*` here — is paired with the node of the same marker, in document
// order, and its whole subtree is compared: tag, box, computed styles, pseudo-elements, animations (timing and
// keyframes), and what hover / active / focus-visible change.
//
// What is NOT the component's is taken out of the comparison in the open, each by one rule below:
//   - the docs page's inherited text (ink, font, line height) is copied onto each Rask example first;
//   - a component from ANOTHER page is a stand-in until it is rebuilt: marked and `data-parity-skip`
//     (held to its place and size), or unmarked (a printed note, nothing compared);
//   - `data-parity-skip="self|width|height"` lets go of exactly that (see compareTree);
//   - a Flux custom element pairs with the native element that behaves that way (NATIVE).
//
// Usage:  dotnet test tests/Rask.Ui.Tests --filter FluxParityPages     # writes the Rask pages
//         node scripts/flux/parity.mjs button                           # components/button
//         node scripts/flux/parity.mjs layouts/sidebar --refresh        # re-measure Flux
//         node scripts/flux/parity.mjs button --section variants --all  # one section, every difference
//
// Exit code 1 on any difference. Screenshots of both sides land beside the measurements.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, measurePage, root, stateTargets, STYLES } from './lib.mjs';

const args = process.argv.slice(2);
const flag = name => args.includes(`--${name}`);
const option = name => (args.includes(`--${name}`) ? args[args.indexOf(`--${name}`) + 1] : undefined);
const page = args.find(a => !a.startsWith('--') && a !== option('section'));
if (!page) {
  console.error('usage: node scripts/flux/parity.mjs <page> [--refresh] [--section <id>] [--all]');
  process.exit(1);
}

const path = page.includes('/') ? page : `components/${page}`;
const slug = path.split('/')[1];
const out = join(root, 'artifacts', 'flux-parity');
const raskPage = join(out, 'rask', `${slug}.html`);
if (!existsSync(raskPage)) {
  console.error(`flux parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  process.exit(1);
}

const browser = await chromium().launch();
const fluxFile = join(out, 'flux', slug, 'measurements.json');
let flux;
if (existsSync(fluxFile) && !flag('refresh')) {
  flux = JSON.parse(await readFile(fluxFile, 'utf8'));
} else {
  flux = await measurePage(browser, `https://fluxui.dev/${path}`, join(out, 'flux', slug));
  await mkdir(join(out, 'flux', slug), { recursive: true });
  await writeFile(fluxFile, JSON.stringify(flux));
}

// The docs page hands each example its text: black or white by scheme, a 24px or 26px line by where the
// example sits in the prose. A component inherits those, so each Rask example is given what its Flux
// twin was given before anything is measured — the same surroundings, and only the component differs.
const INHERITED = ['color', 'fontFamily', 'fontSize', 'fontWeight', 'lineHeight', 'letterSpacing'];
const rask = await measurePage(browser, pathToFileURL(raskPage).href, join(out, 'rask', slug), (document, scheme) =>
  document.evaluate(({ examples, INHERITED }) => {
    const seen = {};
    for (const wrapper of document.querySelectorAll('[data-preview-wrapper]')) {
      const section = wrapper.dataset.section ?? '';
      const ordinal = seen[section] = (seen[section] ?? -1) + 1;
      const theirs = examples.find(example => example.section === section && example.ordinal === ordinal);
      for (const key of theirs ? INHERITED : []) wrapper.style[key] = theirs.nodes[0].style[key];
    }
  }, { examples: flux[scheme], INHERITED }));
await browser.close();

// Not compared: what the surrounding docs page decides rather than the component (where a top-level
// node sits, how wide a stretched one is), and the two the box already states.
const IGNORED = new Set(['width', 'height']);
// Flux's own custom elements, which its script upgrades, and the native element Rask.Ui writes in their
// place because it has that behaviour built in: a <label for> focuses its control with no script at all.
const NATIVE = {
  'ui-field': 'div', 'ui-label': 'label', 'ui-description': 'div', 'ui-legend': 'legend', 'ui-progress': 'div',
  'ui-table-scroll-area': 'div', 'ui-disclosure-group': 'div', 'ui-disclosure': 'details', 'ui-chart': 'div',
  // Flux's pressable that is not a <button> (a kanban card): focusable, pressed with Enter and Space.
  'ui-button': 'button',
  // The tooltip's wrapper: the kit wires the trigger at render and the browser shows the [popover].
  // A toggleable tooltip is a <ui-dropdown> on Flux's page, under the tooltip's marker.
  'ui-tooltip': 'div', 'ui-dropdown': 'div',
  // The select's, the autocomplete's and the pillbox's elements: a native popover and C# key handling in their place.
  'ui-select': 'div', 'ui-selected': 'div', 'ui-options': 'div', 'ui-option': 'div', 'ui-option-empty': 'div',
  'ui-option-create': 'div', 'ui-empty': 'div', 'ui-pillbox': 'div', 'ui-pillbox-trigger': 'div',
  'ui-selected-remove': 'div',
  // The modal's wrapper, and the one around a button that closes it: the kit's buttons are invoker commands.
  'ui-modal': 'div', 'ui-close': 'div',
};
// …and a Flux part, by its marker, that needs script to do what a native element does alone: a <label>
// opens the file input inside it when clicked, where Flux's <div> calls input.click().
const NATIVE_PART = { 'input-file': 'label' };
// The <button> Flux scripts to open a <ui-disclosure> is a <details>' own <summary>.
const sameTag = (a, b) => (NATIVE[a.tag] ?? NATIVE_PART[mark(a, 'data-flux-')] ?? a.tag) === b.tag || (a.tag === 'button' && b.tag === 'summary');
// Flux marks an accordion's root `data-flux-accordion-heading`, the marker its headings carry too.
// …and leaves a chart's root with no marker at all: its <ui-chart> is the chart.
const MISMARKED = { 'ui-disclosure-group': 'accordion', 'ui-chart': 'chart' };
// What the kit adds to do WITHOUT script what Flux does with it: a chart's hover strips, each carrying the
// cursor and tooltip Flux's script would move there. Flux has no such node, so there is nothing to pair it with.
const EXTRA = ['data-ui-chart-hover'];
// The markers of the parts this page documents: flux:button.group -> button-group, flux:icon.* -> icon.
const snapshot = JSON.parse(await readFile(join(root, 'tests', 'Rask.Ui.Tests', 'Flux', 'flux.snapshot.json'), 'utf8'));
const OWN = new Set([slug, ...(snapshot.pages.find(p => p.slug === slug)?.parts ?? [])
  .map(part => part.name.replace(/^flux:/, '').replace(/\.\*$/, '').replaceAll('.', '-'))]);
const limit = flag('all') ? Infinity : 12;
let failures = 0;
for (const scheme of ['light', 'dark']) {
  const sections = new Map();
  for (const example of flux[scheme]) (sections.get(example.section) ?? sections.set(example.section, []).get(example.section)).push(example);
  for (const mine of rask[scheme]) {
    if (option('section') && mine.section !== option('section')) continue;
    const theirs = sections.get(mine.section)?.[mine.ordinal ?? 0];
    const label = `${scheme} ${mine.section}#${mine.ordinal ?? 0}`;
    if (!theirs) {
      console.log(`?    ${label}: Flux has no such example`);
      failures++;
      continue;
    }

    const notes = [];
    const diffs = compareExample(theirs, mine, notes);
    failures += diffs.length ? 1 : 0;
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ''}`);
    for (const note of notes) console.log(`       (${note})`);
    for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
    if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
  }
}

console.log(failures ? `\nflux parity: ${slug} differs in ${failures} example(s).` : `\nflux parity: ${slug} matches Flux.`);
process.exit(failures ? 1 : 0);

function compareExample(theirs, mine, notes) {
  const a = tops(theirs, 'data-flux-');
  const b = tops(mine, 'data-ui-');
  const named = (nodes, prefix, name) => nodes.filter(n => mark(n, prefix) === name);
  const diffs = [];

  // Paired by marker, in document order. A part this page documents has to be there node for node. A
  // NEIGHBOUR from another page (the buttons beside a separator) with no marked node on the Rask side is
  // an unmarked stand-in: not compared, and said so.
  for (const name of new Set([...a.map(n => mark(n, 'data-flux-')), ...b.map(n => mark(n, 'data-ui-'))])) {
    const x = named(a, 'data-flux-', name);
    const y = named(b, 'data-ui-', name);
    if (!OWN.has(name) && !name.startsWith(`${slug}-`) && y.length === 0) {
      notes.push(`${name} ×${x.length}: a stand-in on the Rask page, not compared`);
      continue;
    }

    if (x.length !== y.length) {
      diffs.push(`marked nodes: Flux has ${x.length} ${name}, Rask has ${y.length}`);
      continue;
    }

    x.forEach((node, i) => compareTree(theirs, node, mine, y[i], node, y[i], `${name}[${i}]${node.text ? ` "${node.text.slice(0, 16)}"` : ''}`, diffs));
  }

  return diffs;
}

// Marked nodes with no marked ancestor: the components an example places.
function tops(example, prefix) {
  const byId = new Map(example.nodes.map(n => [n.id, n]));
  const marked = n => n.tag in MISMARKED || Object.keys(n.attrs).some(k => k.startsWith(prefix));
  const hasMarkedAncestor = n => {
    for (let p = byId.get(n.parent); p; p = byId.get(p.parent)) if (marked(p)) return true;
    return false;
  };
  return example.nodes.filter(n => marked(n) && !hasMarkedAncestor(n));
}

// The part's marker is the shortest: Flux writes `data-flux-card` beside `data-flux-card-body-variant`.
function mark(node, prefix) {
  if (MISMARKED[node.tag]) return MISMARKED[node.tag];
  const names = Object.keys(node.attrs).filter(k => k.startsWith(prefix)).map(k => k.slice(prefix.length));
  return names.sort((x, y) => x.length - y.length)[0] ?? node.tag;
}

// `data-parity-skip` on a Rask node says what of it is NOT this page's to match, and nothing more is let go:
//   (no value)  a stand-in for a component not rebuilt yet: held to its place and size, inside not compared;
//   "self"      its own look is another component's, its children are compared as usual;
//   "width" / "height"   that dimension is random on Flux's page (`rand()` in the docs), here and below it.
function compareTree(theirs, a, mine, b, rootA, rootB, where, diffs, free = '') {
  const skip = b.attrs['data-parity-skip'];
  const standIn = skip === '';
  const own = !standIn && skip !== 'self';
  if (skip === 'width' || skip === 'height') free = skip;
  if (!sameTag(a, b) && own) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text && a !== rootA && own) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);

  const differs = (x, y) => Math.abs(x - y) > 0.6;
  const held = [free !== 'width', free !== 'height'];
  if ((held[0] && differs(a.box[2], b.box[2])) || (held[1] && differs(a.box[3], b.box[3]))) {
    diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  }

  // A node that is not displayed — itself or by an ancestor — or that is 0×0 on both sides has no box:
  // its rectangle is the viewport's corner, which says how far each page is scrolled and nothing about it.
  const boxed = (shown(theirs, a) || shown(mine, b)) && [a, b].some(n => n.box[2] > 0 || n.box[3] > 0);
  // Nor has a `display: contents` root (a modal's trigger): nothing under it can be placed against it.
  const anchored = rootA.style.display !== 'contents' || rootB.style.display !== 'contents';
  if (a !== rootA && boxed && anchored) {
    const off = (n, r, i) => n.box[i] - r.box[i];
    if ([0, 1].some(i => held[i] && differs(off(a, rootA, i), off(b, rootB, i)))) {
      diffs.push(`${where}: offset ${fix(off(a, rootA, 0))},${fix(off(a, rootA, 1))} vs ${fix(off(b, rootB, 0))},${fix(off(b, rootB, 1))}`);
    }
  }

  if (standIn) return;
  if (own) compareLook(theirs, a, mine, b, where, diffs);

  // A <template> is never drawn — Flux keeps a prototype of every chart node in one, and inside an <svg> a
  // template's children are ordinary DOM children — so it is no child on either side. Nor is an EXTRA node.
  const kids = (example, n) => example.nodes.filter(c => c.parent === n.id && c.tag !== 'template' && !EXTRA.some(name => name in c.attrs));
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => compareTree(theirs, child, mine, cb[i], rootA, rootB, `${where} > ${child.tag}[${i}]`, diffs, free));
}

// What one node looks like: computed styles, pseudo-elements, animations, and what each forced state changes.
function compareLook(theirs, a, mine, b, where, diffs) {
  compareStyles(a.style, b.style, where, diffs);
  for (const pseudo of ['::before', '::after']) {
    if (!a[pseudo] !== !b[pseudo]) diffs.push(`${where}${pseudo}: ${a[pseudo] ? 'only in Flux' : 'only in Rask'}`);
    else if (a[pseudo]) compareStyles(a[pseudo], b[pseudo], `${where}${pseudo}`, diffs);
  }

  // Flux's keyframes are named `flux-*` as its markers are; Rask's are `ui-*`.
  const moves = n => JSON.stringify(n.animations ?? []).replaceAll('"name":"flux-', '"name":"ui-');
  if (moves(a) !== moves(b)) diffs.push(`${where}: animations: ${moves(a)} vs ${moves(b)}`);

  // A long example is measured for its first 60 controls only, and the two pages need not run out at the
  // same node: a state is compared where both sides measured it.
  if (!measured(theirs).has(a.id) || !measured(mine).has(b.id)) return;
  for (const state of ['hover', 'active', 'focus-visible']) {
    const x = theirs.states.find(s => s.node === a.id && s.state === state)?.changed ?? {};
    const y = mine.states.find(s => s.node === b.id && s.state === state)?.changed ?? {};
    for (const key of new Set([...Object.keys(x), ...Object.keys(y)])) {
      if (undrawn(key, { ...a.style, ...x }, { ...b.style, ...y })) continue;
      if (!same(x[key], y[key])) diffs.push(`${where}:${state} ${key}: ${x[key] ?? '(unchanged)'} vs ${y[key] ?? '(unchanged)'}`);
    }
  }
}

function measured(example) {
  return (example.measured ??= new Set(stateTargets(example.nodes)));
}

function shown(example, node) {
  for (let n = node; n; n = example.nodes.find(p => p.id === n.parent)) if (n.style.display === 'none') return false;
  return true;
}

function compareStyles(x, y, where, diffs) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || undrawn(key, x, y) || same(x[key], y[key])) continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

// The colour of a border or an outline neither side draws is whatever each PAGE's reset left there —
// Flux's docs default every border to gray-200, a bare preflight to the text colour — not the component's.
function undrawn(key, x, y) {
  const side = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
  if (side) return x[`border${side}Width`] === '0px' && y[`border${side}Width`] === '0px';
  return key === 'outlineColor' && x.outlineStyle === 'none' && y.outlineStyle === 'none';
}

// Colours and lengths print with float noise that differs between two pages computing the same value.
function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  // …and a gray's hue, which means nothing at zero chroma, prints as `none` once a minifier has been at the sheet.
  const round = v => v.replace(/(okl(?:ch|ab)\([^)]*?) none\)/g, '$1 0)')
    .replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000));
  if (round(x) === round(y)) return true;
  // …and two numbers a hair apart can still round to different thousandths (579.8294 and 579.8295, a
  // translate() Chromium keeps in single precision): the same text around numbers no further apart than that.
  const numbers = [];
  const shape = v => round(v).replace(/-?\d+(\.\d+)?(e-?\d+)?/g, n => (numbers.push(Number(n)), '#'));
  if (shape(x) !== shape(y)) return false;
  const half = numbers.length / 2;
  return numbers.slice(0, half).every((n, i) => Math.abs(n - numbers[half + i]) <= 0.0011);
}

function fix(v) {
  return Math.round(v * 10) / 10;
}

#!/usr/bin/env node
// Holds a Rask.Ui component to Flux UI's live documentation, example by example.
//
// Two measurements of the same examples, taken the same way (lib.mjs): Flux's docs page, and the page
// FluxParityPages wrote for the Rask component (artifacts/flux-parity/rask/<slug>.html). Every marked
// node — `data-flux-*` there, `data-ui-*` here — is paired in document order, and its whole subtree is
// compared: tag, box, computed styles, pseudo-elements, and what hover / active / focus-visible change.
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
import { chromium, measurePage, root, STYLES } from './lib.mjs';

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
// place because the platform already has the behaviour: a [popover] opens from `popovertarget` alone.
const NATIVE = {
  'ui-dropdown': 'div', 'ui-context': 'div', 'ui-menu': 'div', 'ui-submenu': 'div',
  'ui-menu-radio-group': 'div', 'ui-menu-checkbox-group': 'div', 'ui-menu-radio': 'button', 'ui-menu-checkbox': 'button',
};
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

    const diffs = compareExample(theirs, mine);
    failures += diffs.length ? 1 : 0;
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label}${diffs.length ? ` — ${diffs.length} difference(s)` : ''}`);
    for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
    if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
  }
}

console.log(failures ? `\nflux parity: ${slug} differs in ${failures} example(s).` : `\nflux parity: ${slug} matches Flux.`);
process.exit(failures ? 1 : 0);

function compareExample(theirs, mine) {
  const a = tops(theirs, 'data-flux-');
  const b = tops(mine, 'data-ui-');
  if (a.length !== b.length) {
    return [`marked nodes: Flux has ${a.length} [${a.map(n => mark(n, 'data-flux-')).join(' ')}], Rask has ${b.length} [${b.map(n => mark(n, 'data-ui-')).join(' ')}]`];
  }

  const diffs = [];
  a.forEach((node, i) => compareTree(theirs, node, mine, b[i], node, b[i], `${mark(node, 'data-flux-')}[${i}]${node.text ? ` "${node.text.slice(0, 16)}"` : ''}`, diffs));
  return diffs;
}

// Marked nodes with no marked ancestor: the components an example places.
function tops(example, prefix) {
  const byId = new Map(example.nodes.map(n => [n.id, n]));
  const marked = n => Object.keys(n.attrs).some(k => k.startsWith(prefix));
  const hasMarkedAncestor = n => {
    for (let p = byId.get(n.parent); p; p = byId.get(p.parent)) if (marked(p)) return true;
    return false;
  };
  return example.nodes.filter(n => marked(n) && !hasMarkedAncestor(n));
}

function mark(node, prefix) {
  return Object.keys(node.attrs).find(k => k.startsWith(prefix))?.slice(prefix.length) ?? node.tag;
}

function compareTree(theirs, a, mine, b, rootA, rootB, where, diffs) {
  // A stand-in for a component that is not rebuilt yet (`data-parity-skip` on the Rask side): it has to
  // take the same room in the same place, and what it looks like inside is that component's own page.
  const standIn = 'data-parity-skip' in b.attrs;
  if ((NATIVE[a.tag] ?? a.tag) !== b.tag && !standIn) diffs.push(`${where}: tag <${a.tag}> vs <${b.tag}>`);
  if (a.text !== b.text && a !== rootA && !standIn) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);

  // A node that is not displayed has no box: its rectangle is the viewport's corner, which says how far
  // each page is scrolled and nothing about the node.
  const displayed = a.box[2] > 0 || a.box[3] > 0 || b.box[2] > 0 || b.box[3] > 0;
  const size = (n, i) => Math.abs(a.box[i] - b.box[i]) > 0.6;
  if (size(a, 2) || size(a, 3)) diffs.push(`${where}: size ${a.box[2]}x${a.box[3]} vs ${b.box[2]}x${b.box[3]}`);
  if (a !== rootA && displayed) {
    const off = (n, r, i) => n.box[i] - r.box[i];
    if (Math.abs(off(a, rootA, 0) - off(b, rootB, 0)) > 0.6 || Math.abs(off(a, rootA, 1) - off(b, rootB, 1)) > 0.6) {
      diffs.push(`${where}: offset ${fix(off(a, rootA, 0))},${fix(off(a, rootA, 1))} vs ${fix(off(b, rootB, 0))},${fix(off(b, rootB, 1))}`);
    }
  }

  if (standIn) return;
  compareStyles(a.style, b.style, where, diffs);
  for (const pseudo of ['::before', '::after']) {
    if (!a[pseudo] !== !b[pseudo]) diffs.push(`${where}${pseudo}: ${a[pseudo] ? 'only in Flux' : 'only in Rask'}`);
    else if (a[pseudo]) compareStyles(a[pseudo], b[pseudo], `${where}${pseudo}`, diffs);
  }

  // A node with no box cannot be hovered, pressed or focused, so a state forced onto it says nothing — and
  // inside a closed menu it would say something false: Flux lights a row from script (`data-active`), where
  // Rask.Ui has `:hover`. scripts/flux/parity-menu.mjs compares the rows of an OPEN menu under a real pointer.
  for (const state of displayed ? ['hover', 'active', 'focus-visible'] : []) {
    const x = theirs.states.find(s => s.node === a.id && s.state === state)?.changed ?? {};
    const y = mine.states.find(s => s.node === b.id && s.state === state)?.changed ?? {};
    for (const key of new Set([...Object.keys(x), ...Object.keys(y)])) {
      if (!same(x[key], y[key])) diffs.push(`${where}:${state} ${key}: ${x[key] ?? '(unchanged)'} vs ${y[key] ?? '(unchanged)'}`);
    }
  }

  const kids = (example, n) => example.nodes.filter(c => c.parent === n.id);
  const ca = kids(theirs, a);
  const cb = kids(mine, b);
  if (ca.length !== cb.length) {
    diffs.push(`${where}: children <${ca.map(c => c.tag).join(' ')}> vs <${cb.map(c => c.tag).join(' ')}>`);
    return;
  }

  ca.forEach((child, i) => compareTree(theirs, child, mine, cb[i], rootA, rootB, `${where} > ${child.tag}[${i}]`, diffs));
}

function compareStyles(x, y, where, diffs) {
  for (const key of STYLES) {
    if (IGNORED.has(key) || same(x[key], y[key])) continue;
    // The colour of a border neither side draws: Flux's page gives every element a grey one, a Tailwind
    // app leaves it the text colour, and at no width it is nothing at all.
    const width = /^border(Top|Right|Bottom|Left)Color$/.exec(key)?.[1];
    if (width && x[`border${width}Width`] === '0px' && y[`border${width}Width`] === '0px') continue;
    diffs.push(`${where}: ${key}: ${x[key]} vs ${y[key]}`);
  }
}

// Colours and lengths print with float noise that differs between two pages computing the same value.
function same(x, y) {
  if (x === y) return true;
  if (x === undefined || y === undefined) return false;
  // `none` is a hue that has no say (a gray's): a minifier writes it for the 0 the source had.
  const round = v => v.replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 1000) / 1000)).replace(/ none\)/g, ' 0)');
  return round(x) === round(y);
}

function fix(v) {
  return Math.round(v * 10) / 10;
}

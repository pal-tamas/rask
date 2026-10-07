#!/usr/bin/env node
// Generates Rask.Ui's icon set from Heroicons — the collection Flux UI's `flux:icon.*` is.
//
// Heroicons (https://github.com/tailwindlabs/heroicons) is MIT-licensed, Copyright (c) Tailwind Labs, Inc.
// The npm tarball is PINNED below by version and integrity, read in memory, and never unpacked to disk.
// Out of its four folders come:
//
//   src/Rask.Ui/Ui.IconName.cs     every icon, in PascalCase of Heroicons' name (arrow-down-tray -> ArrowDownTray)
//   src/Rask.Ui/UiIconPaths.g.cs   the path data of all four variants (outline, solid, mini, micro)
//
// Usage:  node scripts/flux/icons.mjs            # regenerate
//         node scripts/flux/icons.mjs --check    # exit 1 when the committed files are stale
//
// To take a new Heroicons release: change VERSION and INTEGRITY (`npm view heroicons@<v> dist.integrity`),
// run this, then record the new enum members in src/Rask.Ui/PublicAPI (scripts/public-api/record.py src/Rask.Ui).

import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { gunzipSync } from 'node:zlib';
import { root } from './lib.mjs';

const VERSION = '2.2.0';
const INTEGRITY = 'sha512-yOwvztmNiBWqR946t+JdgZmyzEmnRMC2nxvHFC90bF1SUttwB6yJKYeme1JeEcBfobdOs827nCyiWBS2z/brog==';
const TARBALL = `https://registry.npmjs.org/heroicons/-/heroicons-${VERSION}.tgz`;

// Folder in the package -> the Ui.IconVariant it draws, and the root attributes every file in it must
// carry. UiIcon writes those attributes itself, so a release that changes one has to fail here.
const VARIANTS = [
  { name: 'Outline', folder: '24/outline', svg: { viewBox: '0 0 24 24', fill: 'none', 'stroke-width': '1.5', stroke: 'currentColor' } },
  { name: 'Solid', folder: '24/solid', svg: { viewBox: '0 0 24 24', fill: 'currentColor' } },
  { name: 'Mini', folder: '20/solid', svg: { viewBox: '0 0 20 20', fill: 'currentColor' } },
  { name: 'Micro', folder: '16/solid', svg: { viewBox: '0 0 16 16', fill: 'currentColor' } },
];

// Names Heroicons deprecated in 2.1 and still ships at 24px and 20px only. They have no micro drawing and
// are not on heroicons.com, so they are not icons here: every name left has all four variants.
const DEPRECATED = [
  'arrow-left-on-rectangle', 'arrow-right-on-rectangle', 'arrow-small-down', 'arrow-small-left',
  'arrow-small-right', 'arrow-small-up', 'minus-small', 'plus-small',
];

const files = untar(gunzipSync(await download()));
const sets = VARIANTS.map(variant => ({ ...variant, icons: read(variant) }));
const names = [...sets[0].icons.keys()].filter(name => !DEPRECATED.includes(name)).sort();
for (const set of sets) {
  const odd = [
    ...names.filter(name => !set.icons.has(name)),
    ...[...set.icons.keys()].filter(name => !names.includes(name) && !DEPRECATED.includes(name)),
  ];
  if (odd.length) fail(`${set.folder} does not hold the same icons as ${sets[0].folder}: ${odd.join(', ')}`);
}

const outputs = [
  [join(root, 'src', 'Rask.Ui', 'Ui.IconName.cs'), enumSource()],
  [join(root, 'src', 'Rask.Ui', 'UiIconPaths.g.cs'), pathsSource()],
];

let stale = 0;
for (const [path, text] of outputs) {
  if (process.argv.includes('--check')) {
    stale += (await readFile(path, 'utf8').catch(() => '')) === text ? 0 : 1;
  } else {
    await writeFile(path, text);
  }
}

if (stale) fail(`${stale} generated file(s) are stale — run: node scripts/flux/icons.mjs`);
console.log(`flux icons: heroicons ${VERSION} — ${names.length} icons x ${sets.length} variants, `
  + `${sets.reduce((sum, set) => sum + blob(set).length, 0)} bytes of path data.`);

async function download() {
  const response = await fetch(TARBALL);
  if (!response.ok) fail(`${TARBALL}: HTTP ${response.status}`);
  const bytes = Buffer.from(await response.arrayBuffer());
  const digest = `sha512-${createHash('sha512').update(bytes).digest('base64')}`;
  if (digest !== INTEGRITY) fail(`${TARBALL} is not the pinned tarball (${digest})`);
  return bytes;
}

// A ustar archive, read in place: 512-byte headers, each followed by its file rounded up to 512.
function untar(archive) {
  const entries = new Map();
  const field = (at, start, length) => archive.subarray(at + start, at + start + length).toString('utf8').replace(/\0.*$/s, '');
  for (let at = 0; at + 512 <= archive.length;) {
    const name = field(at, 0, 100);
    if (!name) break;
    const size = parseInt(field(at, 124, 12).trim() || '0', 8);
    const prefix = field(at, 345, 155);
    if (field(at, 156, 1) === '0' || field(at, 156, 1) === '') {
      entries.set(prefix ? `${prefix}/${name}` : name, archive.subarray(at + 512, at + 512 + size).toString('utf8'));
    }

    at += 512 + Math.ceil(size / 512) * 512;
  }

  return entries;
}

// One folder: icon name -> its shapes, each encoded. Anything UiIcon does not draw fails here.
function read(variant) {
  const icons = new Map();
  const base = `package/${variant.folder}/`;
  for (const [path, svg] of files) {
    if (!path.startsWith(base) || !path.endsWith('.svg')) continue;
    const name = path.slice(base.length, -4);
    const [, rootAttrs, body] = /^<svg([^>]*)>(.*)<\/svg>\s*$/s.exec(svg) ?? fail(`${path}: not one <svg>`);
    const expected = { xmlns: 'http://www.w3.org/2000/svg', ...variant.svg, 'aria-hidden': 'true', 'data-slot': 'icon' };
    same(attributes(rootAttrs), expected) || fail(`${path}: unexpected root attributes ${rootAttrs}`);

    const paths = [];
    const rest = body.replace(/<(path|rect)([^>]*?)\/>/g, (_, tag, raw) => {
      paths.push(tag === 'rect' ? rect(path, attributes(raw)) : shape(path, variant, attributes(raw)));
      return '';
    });
    if (rest.trim() || !paths.length) fail(`${path}: something other than <path> elements: ${rest.trim()}`);
    icons.set(name, paths);
  }

  if (!icons.size) fail(`${variant.folder}: no icons in the tarball`);
  return icons;
}

// A <path>, with the one mark its attributes amount to: an outline path is stroked with round caps and
// round joins, or — marked 'J' — with a round join alone; a solid path is filled by the non-zero rule or,
// marked 'E', by the even-odd one.
function shape(file, variant, { d, ...others }) {
  const marks = variant.name === 'Outline'
    ? [['', { 'stroke-linecap': 'round', 'stroke-linejoin': 'round' }], ['J', { 'stroke-linejoin': 'round' }]]
    : [['', {}], ['E', { 'fill-rule': 'evenodd', 'clip-rule': 'evenodd' }]];
  const mark = marks.find(([, attrs]) => same(others, attrs));
  if (!d || !mark) fail(`${file}: unexpected <path> attributes ${JSON.stringify(others)}`);
  if (!/^[Mm][-\d.\sA-Za-z]*$/.test(d) || /[EJR|]/.test(d)) fail(`${file}: path data outside the encoding: ${d}`);
  return mark[0] + d;
}

// A <rect> ('R'), as "x y width height rx".
function rect(file, { x, y, width, height, rx, ...others }) {
  const numbers = [x, y, width, height, rx];
  if (Object.keys(others).length || numbers.some(n => !/^\d+(\.\d+)?$/.test(n ?? ''))) fail(`${file}: unexpected <rect>`);
  return `R${numbers.join(' ')}`;
}

function attributes(raw) {
  return Object.fromEntries([...raw.matchAll(/([\w:-]+)="([^"]*)"/g)].map(([, key, value]) => [key, value]));
}

function same(a, b) {
  const keys = Object.keys(a).sort();
  return keys.join() === Object.keys(b).sort().join() && keys.every(key => a[key] === b[key]);
}

function pascal(name) {
  return name.split('-').map(word => word[0].toUpperCase() + word.slice(1)).join('');
}

// One icon in a variant's blob: its shapes joined by '|'. Path data opens with M or m and holds no E, J
// or R, so a mark cannot be mistaken for it.
function encode(shapes) {
  return shapes.join('|');
}

function blob(set) {
  return names.map(name => encode(set.icons.get(name))).join('');
}

function header() {
  return [
    '// <auto-generated />',
    `// Generated by scripts/flux/icons.mjs from heroicons ${VERSION} (${TARBALL}).`,
    '// Heroicons is MIT-licensed, Copyright (c) Tailwind Labs, Inc. — https://github.com/tailwindlabs/heroicons',
    '// Do not edit: change the script and run it again.',
    '#nullable enable',
    '',
  ];
}

function enumSource() {
  const lines = [
    ...header(),
    'namespace Rask;',
    '',
    'public static partial class Ui',
    '{',
    '    /// <summary>',
    `    ///     Every icon <see cref="UiIcon" /> draws: the whole of <see href="https://heroicons.com">Heroicons</see> ${VERSION}`,
    '    ///     under Heroicons\' own names (<c>arrow-down-tray</c> is <see cref="ArrowDownTray" />), plus Flux\'s',
    '    ///     <see cref="Loading" /> spinner. Each comes in the four <see cref="IconVariant" />s.',
    '    /// </summary>',
    '    public enum IconName',
    '    {',
  ];
  for (const name of names) {
    lines.push(`        /// <summary>Heroicons' <c>${name}</c>.</summary>`, `        ${pascal(name)},`, '');
  }

  lines.push(
    '        /// <summary>Flux\'s loading spinner (<c>flux:icon.loading</c>): not a Heroicon, and it spins.</summary>',
    '        Loading,',
    '    }',
    '}',
    '');
  return lines.join('\n');
}

function pathsSource() {
  const lines = [
    ...header(),
    'namespace Rask;',
    '',
    '/// <summary>',
    '///     The path data behind every <see cref="Ui.IconName" />, one table per <see cref="Ui.IconVariant" />.',
    '/// </summary>',
    '/// <remarks>',
    '///     UTF-8 literals, so each table is bytes in the assembly\'s image rather than objects on the heap: nothing is',
    '///     allocated until an icon is drawn. An icon is <c>Data[Offsets[i]..Offsets[i + 1]]</c> with <c>i</c> its',
    '///     enum value; its shapes are separated by <c>|</c>. A shape is path data — led by <c>E</c> when it is filled',
    '///     by the even-odd rule, by <c>J</c> when its stroke has a round join and no round cap — or <c>R</c> and a',
    '///     rectangle\'s <c>x y width height rx</c>.',
    '/// </remarks>',
    'internal static partial class UiIconPaths',
    '{',
    `    /// <summary>How many icons each table holds: every <see cref="Ui.IconName" /> before <see cref="Ui.IconName.Loading" />.</summary>`,
    `    internal const int Count = ${names.length};`,
  ];
  for (const set of sets) {
    const encoded = names.map(name => encode(set.icons.get(name)));
    const offsets = [0];
    for (const icon of encoded) offsets.push(offsets.at(-1) + icon.length);

    lines.push('', `    /// <summary>Heroicons' <c>${set.folder}</c>.</summary>`, `    internal static ReadOnlySpan<byte> ${set.name}Data =>`);
    encoded.forEach((icon, i) => lines.push(`        "${icon}"u8${i === encoded.length - 1 ? ';' : ' +'} // ${names[i]}`));
    lines.push('', `    /// <summary>Where each icon starts in <see cref="${set.name}Data" />, and where the last one ends.</summary>`,
      `    internal static ReadOnlySpan<int> ${set.name}Offsets =>`, '    [');
    for (let i = 0; i < offsets.length; i += 16) lines.push(`        ${offsets.slice(i, i + 16).join(', ')},`);
    lines.push('    ];');
  }

  lines.push('}', '');
  return lines.join('\n');
}

function fail(message) {
  console.error(`flux icons: ${message}`);
  process.exit(1);
}

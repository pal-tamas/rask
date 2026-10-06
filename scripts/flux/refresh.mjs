#!/usr/bin/env node
// Refreshes tests/Rask.Ui.Tests/Flux/flux.snapshot.json from Flux UI's public documentation.
//
// Rask.Ui mirrors Flux's component catalogue: its names, its props and the values each prop takes. Flux
// serves every docs page as markdown (`<page>.md`, indexed by /llms.txt) and ends each one with a
// "Reference" section in one regular shape, so the catalogue can be read rather than retyped.
//
// What is kept is NAMES AND VALUES ONLY — the component, its props/slots/events, the options a prop
// accepts and its default. No description is copied: those are Flux's prose. And this reads the DOCS,
// never the `livewire/flux` repository — that source is proprietary and Rask.Ui is written without it.
//
// Usage:  node scripts/flux/refresh.mjs            (fetches fluxui.dev)
//         node scripts/flux/refresh.mjs <dir>      (reads <dir>/llms.txt and the pages saved beside it)

import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const origin = 'https://fluxui.dev';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');
const output = join(root, 'tests', 'Rask.Ui.Tests', 'Flux', 'flux.snapshot.json');
const cache = process.argv[2];

// A page saved by hand is named after its path: components/button.md -> components-button.md.
const read = async path => cache
  ? readFile(join(cache, path === 'llms.txt' ? 'idx-llms.txt' : path.replace('/', '-')), 'utf8')
  : (await fetch(`${origin}/${path}`)).text();

const index = await read('llms.txt');
const pages = [...index.matchAll(/\((?:https:\/\/fluxui\.dev)\/((components|layouts)\/([a-z0-9-]+)\.md)\)/g)]
  .map(([, path, kind, slug]) => ({ path, kind, slug }));

const snapshot = { source: origin, pages: [] };
for (const page of pages) {
  const markdown = await read(page.path);
  snapshot.pages.push({ kind: page.kind, slug: page.slug, title: title(markdown), parts: reference(markdown) });
}

await mkdir(dirname(output), { recursive: true });
await writeFile(output, JSON.stringify(snapshot, null, 2) + '\n');
const parts = snapshot.pages.reduce((n, p) => n + p.parts.length, 0);
console.log(`flux snapshot: ${snapshot.pages.length} pages, ${parts} parts -> ${output}`);

function title(markdown) {
  return /^# (.+)$/m.exec(markdown)?.[1].trim() ?? '';
}

// The Reference section: `### flux:button`, then `**Prop:**` / `**Slot:**` / … headings, each followed
// by a list of "- `name` - description. Options: `a`, `b`. Default: `a`." entries.
function reference(markdown) {
  const start = markdown.search(/^## Reference\s*$/m);
  if (start < 0) return [];

  const parts = [];
  let part, section;
  for (const line of markdown.slice(start).split('\n').slice(1)) {
    if (/^## /.test(line)) break;

    const heading = /^### (.+)$/.exec(line);
    if (heading) {
      part = { name: heading[1].trim() };
      parts.push(part);
      section = undefined;
      continue;
    }

    const kind = /^\*\*([A-Za-z ]+):\*\*\s*$/.exec(line);
    if (kind && part) {
      section = key(kind[1]);
      part[section] ??= [];
      continue;
    }

    const entry = /^- `([^`]+)`(?: - (.*))?$/.exec(line);
    if (entry && part && section) part[section].push(describe(entry[1], entry[2] ?? ''));
  }

  return parts;
}

// Flux spells a few of these two ways ("CSS Variable", "CSS variables"); a heading this table does not
// know stops the refresh rather than inventing a key nothing reads.
function key(heading) {
  const keys = {
    'prop': 'props', 'slot': 'slots', 'event': 'events', 'attribute': 'attributes',
    'data attributes': 'attributes', 'css': 'css', 'class': 'classes', 'size': 'sizes',
    'css variable': 'cssVariables', 'css variables': 'cssVariables',
    'method': 'methods', 'static method': 'staticMethods', 'parameter': 'parameters',
    'component': 'components', 'livewire directive': 'livewireDirectives',
  };
  const known = keys[heading.trim().toLowerCase()];
  if (!known) throw new Error(`flux snapshot: unknown reference heading "${heading}"`);
  return known;
}

function describe(name, text) {
  const entry = { name };

  // Options run to the end of their sentence; a value tagged "(default)" is the default.
  const options = /Options?: (.*?)(?:\. |\.$|$)/.exec(text)?.[1];
  if (options) {
    entry.options = [...options.matchAll(/`([^`]+)`/g)].map(m => m[1]);
    const tagged = /`([^`]+)` \(default\)/.exec(options)?.[1];
    if (tagged) entry.default = tagged;
  }

  const stated = /Default: `([^`]+)`/.exec(text)?.[1];
  if (stated) entry.default = stated;
  if (/If `(?:true|false)`/.test(text) && !entry.options) entry.boolean = true;

  return entry;
}

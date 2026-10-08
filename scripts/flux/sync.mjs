#!/usr/bin/env node
// Has Flux UI changed since Rask.Ui last matched it? Answers without anyone asking.
//
// Three things can move upstream, and each is checked against what is committed:
//   - the CATALOGUE  — a component, prop or value added, removed or renamed  -> flux.snapshot.json
//   - the LOOK       — any example on any docs page measuring differently    -> flux.lock.json
//   - the RELEASE    — the version Flux publishes                             -> flux.lock.json
//
// It rewrites both files and prints what moved, so `git diff` is the report and the next steps are the
// usual ones: FluxConformanceTests names the props Rask.Ui now lacks, and `parity.mjs <slug>` names the
// pixels. The fresh measurements stay in artifacts/flux-parity/flux/, where parity.mjs reads them.
//
// A look is measured in pixels and text measures differently on every OS, so the lock belongs to ONE
// platform: `measuredOn`, the CI runner's. Anywhere else the looks are measured but neither compared
// nor locked. `--baseline` takes the lock over: every look is locked as measured here, none reported.
// It measures each page twice, and an example Flux draws differently each time (a chart's data is made
// up per request) is locked as `unstable`: never compared, never reported.
//
// Usage:  node scripts/flux/sync.mjs              # everything
//         node scripts/flux/sync.mjs button card  # these pages only (the lock keeps the others)
//         node scripts/flux/sync.mjs --baseline   # lock every look as this platform measures it
//         node scripts/flux/sync.mjs --comparable # exit 0 when the lock was taken here, by this code
// Exit:   0 nothing moved · 2 something did · 1 it could not tell

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { chromium, layoutDemos, measureLayouts, measurePage, root } from './lib.mjs';

const flux = join(root, 'tests', 'Rask.Ui.Tests', 'Flux');
const snapshotFile = join(flux, 'flux.snapshot.json');
const lockFile = join(flux, 'flux.lock.json');
const UNSTABLE = 'unstable';
// A print is only as good as the code that took it: a change to how a page is measured moves every
// look it touches, and that is not Flux moving. The lock names the code, and is retaken when it changes.
const measuredWith = createHash('sha256')
  .update(await readFile(join(root, 'scripts', 'flux', 'lib.mjs')))
  .update(await readFile(join(root, 'scripts', 'flux', 'sync.mjs')))
  .digest('hex').slice(0, 16);
const baseline = process.argv.includes('--baseline');
const only = process.argv.slice(2).filter(arg => !arg.startsWith('--'));
if (baseline && only.length) {
  console.error('flux sync: --baseline locks every page on this platform, so it takes no page filter.');
  process.exit(1);
}

const lock = existsSync(lockFile) ? JSON.parse(await readFile(lockFile, 'utf8')) : { release: '', pages: {} };
const lockedOn = lock.measuredOn ?? 'another platform';
const comparable = lock.measuredOn === process.platform && lock.measuredWith === measuredWith;
if (process.argv.includes('--comparable')) process.exit(comparable ? 0 : 1);
const compare = !baseline && comparable;

const before = existsSync(snapshotFile) ? await readFile(snapshotFile, 'utf8') : '';
execFileSync(process.execPath, [join(root, 'scripts', 'flux', 'refresh.mjs')], { stdio: 'inherit' });
const snapshot = JSON.parse(await readFile(snapshotFile, 'utf8'));
const moved = [];   // the report: a line per catalogue or release move, and ONE per page whose look moved
const detail = [];  // every example behind those pages — printed above the report, for the log
if (before && before !== JSON.stringify(snapshot, null, 2) + '\n') moved.push(...catalogueChanges(JSON.parse(before), snapshot));

const release = await latestRelease();
if (release && release !== lock.release) {
  if (lock.release) moved.push(`release: ${lock.release} -> ${release}`);
  lock.release = release;
}

if (baseline) lock.pages = {};
const browser = await chromium().launch();
for (const page of snapshot.pages.filter(p => only.length === 0 || only.includes(p.slug))) {
  const known = compare && lock.pages[page.slug];
  let prints = await measure(page, known);
  // Moved means REPRODUCIBLY moved: Flux draws some examples at random, so a difference is measured twice.
  if (baseline) prints = twice(page.slug, prints, await measure(page), () => UNSTABLE);
  else if (known && !alike(known, prints)) prints = twice(page.slug, prints, await measure(page, known), key => known[key]);
  if (known) moved.push(...lookChanges(page.slug, known, prints));
  if (compare || baseline) lock.pages[page.slug] = prints;
  console.log(`flux sync: measured ${page.slug} (${Object.keys(prints).length / 2} examples)`);
}

await browser.close();
if (baseline) Object.assign(lock, { measuredOn: process.platform, measuredWith });
await writeFile(lockFile, JSON.stringify({ release: lock.release, measuredOn: lock.measuredOn, measuredWith: lock.measuredWith, pages: lock.pages }, null, 1) + '\n');

if (detail.length) console.log(`\nflux sync: every example that moved:\n  ${detail.join('\n  ')}`);
if (baseline) console.log(`\nflux sync: looks baselined on ${process.platform} (were locked on ${lockedOn}).`);
else if (!compare) console.log(`\nflux sync: looks are locked on ${lockedOn}${lock.measuredOn === process.platform ? ' by other measuring code' : `; this is ${process.platform}`}, so they were measured but not compared.`);
console.log(moved.length
  ? `\nflux sync: Flux moved in ${moved.length} place(s):\n  ${moved.join('\n  ')}`
  : `\nflux sync: ${compare ? 'Flux is where Rask.Ui last matched it' : 'the catalogue and the release are where Rask.Ui last matched them'}.`);
process.exit(moved.length ? 2 : 0);

// Measures a page, leaves what parity.mjs reads, and hands back its fingerprints — an example the
// lock knows as unstable keeps that name, so it compares equal whatever it measured this time.
async function measure(page, known) {
  const dir = join(root, 'artifacts', 'flux-parity', 'flux', page.slug);
  const url = `https://fluxui.dev/${page.kind}/${page.slug}`;
  // A layout's examples are the demos its page links to, each at every width and state (lib.mjs).
  const take = async () => page.kind === 'layouts'
    ? measureLayouts(browser, await layoutDemos(browser, url), demo => `https://fluxui.dev/demo/${demo.name}`, dir)
    : measurePage(browser, url, dir);
  // One more try: a docs page that never went idle once is the network, not Flux.
  const schemes = await take().catch(() => take());
  await mkdir(dir, { recursive: true });
  await writeFile(join(dir, 'measurements.json'), JSON.stringify(schemes));
  const prints = fingerprints(schemes);
  for (const key of Object.keys(prints)) if (known?.[key] === UNSTABLE) prints[key] = UNSTABLE;
  return prints;
}

function alike(a, b) {
  return Object.keys({ ...a, ...b }).every(key => a[key] === b[key]);
}

// Two fresh measurements of one page: where they agree, that is the look; where they do not, the
// example is unstable and is never reported — a compare keeps what the lock holds (`otherwise`), a
// baseline locks it as unstable.
function twice(slug, first, second, otherwise) {
  const prints = {};
  const keys = Object.keys({ ...first, ...second });
  const example = key => key.slice(key.indexOf('/') + 1);   // light and dark are one example
  const unstable = new Set(keys.filter(key => first[key] !== second[key]).map(example));
  for (const key of keys) {
    const print = unstable.has(example(key)) ? otherwise(key) : first[key];
    if (print) prints[key] = print;
  }

  if (unstable.size) console.log(`flux sync: ${slug} ${[...unstable].join(', ')} does not measure the same twice; ignored`);
  return prints;
}

// One line per page for the report; the examples behind it go to the log.
function lookChanges(slug, known, prints) {
  const counts = { changed: new Set(), new: new Set(), gone: new Set() };
  for (const key of new Set([...Object.keys(known), ...Object.keys(prints)])) {
    if (known[key] === prints[key]) continue;
    const how = !known[key] ? 'new' : !prints[key] ? 'gone' : 'changed';
    counts[how].add(key.slice(key.indexOf('/') + 1));   // light and dark are one example
    detail.push(`look: ${slug} ${key} ${how === 'changed' ? how : `is ${how}`}`);
  }

  const summary = Object.entries(counts).filter(([, examples]) => examples.size).map(([how, examples]) => `${examples.size} ${how}`);
  return summary.length ? [`look: ${slug} — example(s): ${summary.join(', ')}`] : [];
}

// One short hash per example and scheme: enough to say WHICH example moved; parity.mjs says how.
function fingerprints(schemes) {
  const prints = {};
  for (const [scheme, examples] of Object.entries(schemes)) {
    for (const example of examples) {
      const round = v => (typeof v === 'string' ? v.replace(/-?\d*\.\d+(e-?\d+)?/g, n => String(Math.round(Number(n) * 100) / 100)) : v);
      const facts = example.nodes.map(n => [
        n.tag, Object.keys(n.attrs).filter(a => a.startsWith('data-flux')).sort(), n.text,
        n.box.slice(2).map(v => Math.round(v)), Object.values(n.style).map(round),
        n['::before'] ? Object.values(n['::before']).map(round) : 0, n['::after'] ? Object.values(n['::after']).map(round) : 0,
        // What moves and how, only where something does: an example with nothing moving keeps its print.
        ...(n.animations ? [n.animations] : []),
      ]);
      const states = example.states.map(s => [s.state, Object.entries(s.changed).map(([k, v]) => [k, round(v)])]);
      prints[`${scheme}/${example.section || 'intro'}#${example.ordinal}`] =
        createHash('sha256').update(JSON.stringify([facts, states])).digest('hex').slice(0, 16);
    }
  }

  return prints;
}

function catalogueChanges(old, now) {
  const flat = snap => new Map(snap.pages.flatMap(p => p.parts.flatMap(part => [
    [`${part.name}`, ''],
    ...Object.entries(part).filter(([k]) => k !== 'name').flatMap(([kind, entries]) =>
      entries.map(e => [`${part.name} ${kind} ${e.name}`, JSON.stringify([e.options ?? [], e.default ?? ''])])),
  ])));
  const a = flat(old);
  const b = flat(now);
  const changes = [];
  for (const [key, value] of b) {
    if (!a.has(key)) changes.push(`catalogue: + ${key}`);
    else if (a.get(key) !== value) changes.push(`catalogue: ~ ${key} ${a.get(key)} -> ${value}`);
  }
  for (const key of a.keys()) if (!b.has(key)) changes.push(`catalogue: - ${key}`);
  return changes;
}

// Release METADATA only — the tag name. The repository's source is proprietary and is never read.
async function latestRelease() {
  try {
    const response = await fetch('https://api.github.com/repos/livewire/flux/releases/latest', { headers: { 'user-agent': 'rask-flux-sync' } });
    return response.ok ? (await response.json()).tag_name ?? '' : '';
  } catch {
    return '';
  }
}

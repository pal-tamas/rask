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
// Usage:  node scripts/flux/sync.mjs              # everything
//         node scripts/flux/sync.mjs button card  # these pages only (the lock keeps the others)
// Exit:   0 nothing moved · 2 something did · 1 it could not tell

import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { chromium, measurePage, root } from './lib.mjs';

const flux = join(root, 'tests', 'Rask.Ui.Tests', 'Flux');
const snapshotFile = join(flux, 'flux.snapshot.json');
const lockFile = join(flux, 'flux.lock.json');
const only = process.argv.slice(2);

const before = existsSync(snapshotFile) ? await readFile(snapshotFile, 'utf8') : '';
execFileSync(process.execPath, [join(root, 'scripts', 'flux', 'refresh.mjs')], { stdio: 'inherit' });
const snapshot = JSON.parse(await readFile(snapshotFile, 'utf8'));
const moved = [];
if (before && before !== JSON.stringify(snapshot, null, 2) + '\n') moved.push(...catalogueChanges(JSON.parse(before), snapshot));

const lock = existsSync(lockFile) ? JSON.parse(await readFile(lockFile, 'utf8')) : { release: '', pages: {} };
const release = await latestRelease();
if (release && release !== lock.release) {
  if (lock.release) moved.push(`release: ${lock.release} -> ${release}`);
  lock.release = release;
}

const browser = await chromium().launch();
for (const page of snapshot.pages.filter(p => only.length === 0 || only.includes(p.slug))) {
  const dir = join(root, 'artifacts', 'flux-parity', 'flux', page.slug);
  const schemes = await measurePage(browser, `https://fluxui.dev/${page.kind}/${page.slug}`, dir);
  await mkdir(dir, { recursive: true });
  await writeFile(join(dir, 'measurements.json'), JSON.stringify(schemes));

  const prints = fingerprints(schemes);
  const known = lock.pages[page.slug];
  if (known) {
    for (const key of new Set([...Object.keys(known), ...Object.keys(prints)])) {
      if (known[key] !== prints[key]) moved.push(`look: ${page.slug} ${key} ${!known[key] ? 'is new' : !prints[key] ? 'is gone' : 'changed'}`);
    }
  }

  lock.pages[page.slug] = prints;
  console.log(`flux sync: measured ${page.slug} (${Object.keys(prints).length / 2} examples)`);
}

await browser.close();
await writeFile(lockFile, JSON.stringify(lock, null, 1) + '\n');

console.log(moved.length ? `\nflux sync: Flux moved in ${moved.length} place(s):\n  ${moved.join('\n  ')}` : '\nflux sync: Flux is where Rask.Ui last matched it.');
process.exit(moved.length ? 2 : 0);

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

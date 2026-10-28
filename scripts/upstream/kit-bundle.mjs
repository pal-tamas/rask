#!/usr/bin/env node
// Follows the npm packages a Rask.Ui engine bundles, and rebuilds the engine with them:
//
//   editor   src/Rask.Ui/Resources/editor   Tiptap behind Ui.Editor: npm's latest, every @tiptap package at one
//                                           version (until they are, the pins stay)
//
// Every other dependency (esbuild) is npm's latest; package.json's `overrides` are holds a person made and never
// move. What moved is written to package.json, locked (`npm install --package-lock-only`), every locked package
// it names checked against the registry's integrity, installed (`npm ci`, no install scripts) and built by the
// engine's own build.mjs, which writes the bundle, its LICENSES and the versions the kit and its tests hold.
//
// Every visitor of a page with an engine downloads it, so a bigger one is the owner's call: when a bundle comes
// out bigger than the one committed — its budget — the run stops non-zero, naming both sizes, and the daily job
// opens its issue. Nothing here re-records a budget.
//
// Usage:  node scripts/upstream/kit-bundle.mjs editor            # follow and rebuild
//         node scripts/upstream/kit-bundle.mjs editor --check    # exit 1 when a pin is not what it follows
//
// scripts/upstream/follow.sh runs it every day.

import { spawnSync } from 'node:child_process';
import { readFile, stat, writeFile } from 'node:fs/promises';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { fail as failAs, latest, release } from './npm.mjs';

const ENGINES = {
  editor: { bundles: ['ui-editor.js'], family: '@tiptap/' },
};
const SECTIONS = ['dependencies', 'devDependencies'];

const name = process.argv[2];
const engine = Object.hasOwn(ENGINES, name) ? ENGINES[name] : failAs('kit bundle', `name an engine: ${Object.keys(ENGINES).join(' or ')}`);
const LABEL = `kit bundle ${name}`;
const resources = join(resolve(dirname(fileURLToPath(import.meta.url)), '..', '..'), 'src', 'Rask.Ui', 'Resources');
const here = join(resources, name);
const manifestPath = join(here, 'package.json');
const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
const pinned = dependency => SECTIONS.map(section => manifest[section]?.[dependency]).find(Boolean);

// Each dependency, and the release it follows as the registry describes it.
const dependencies = SECTIONS.flatMap(section => Object.keys(manifest[section] ?? {}));
const wanted = new Map(await Promise.all(dependencies.map(async dependency => {
  const follows = engine.follows?.[dependency];
  return [dependency, follows ? await release(dependency, await follows(), LABEL) : await latest(dependency, LABEL)];
})));

if (engine.family) {
  const versions = new Set([...wanted].filter(([dependency]) => dependency.startsWith(engine.family)).map(([, r]) => r.version));
  if (versions.size > 1) {
    console.log(`${LABEL}: npm's ${engine.family}* are at ${[...versions].join(', ')}, not one version yet; the pins stay.`);
    process.exit(0);
  }
}

const moved = [...wanted].filter(([dependency, r]) => pinned(dependency) !== r.version);
const named = list => list.map(([dependency, r]) => `${dependency} ${pinned(dependency)} -> ${r.version}`).join(', ');
if (process.argv.includes('--check')) {
  if (moved.length) fail(`not what it follows: ${named(moved)} — run: node scripts/upstream/kit-bundle.mjs ${name}`);
  await locked();
  console.log(`${LABEL}: every pin is what it follows.`);
  process.exit(0);
}

if (!moved.length) {
  await locked();
  console.log(`${LABEL}: already the newest.`);
  process.exit(0);
}

const budget = await sizes();
const summary = named(moved);
for (const [dependency, r] of moved) {
  for (const section of SECTIONS) if (manifest[section]?.[dependency]) manifest[section][dependency] = r.version;
}

await writeFile(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
run('npm', 'install', '--package-lock-only', '--ignore-scripts', '--no-audit', '--no-fund');
await locked();
run('npm', 'ci', '--ignore-scripts', '--no-audit', '--no-fund');
run(process.execPath, 'build.mjs');

const built = await sizes();
const grown = engine.bundles.filter(bundle => built.get(bundle) > budget.get(bundle));
if (grown.length) {
  fail(`${summary} grows ${grown.map(b => `${b} from ${budget.get(b)} to ${built.get(b)} bytes`).join(', ')}. `
    + 'A bigger engine is the owner\'s call, so it does not land by itself: look at the regenerated tree and land it by hand.');
}

console.log(`${LABEL}: ${summary}; ${engine.bundles.map(b => `${b} ${budget.get(b)} -> ${built.get(b)} bytes`).join(', ')}.`);

// The lockfile holds, for every dependency, the release it follows and the registry's integrity of that tarball.
async function locked() {
  const lock = JSON.parse(await readFile(join(here, 'package-lock.json'), 'utf8'));
  for (const [dependency, r] of wanted) {
    const entry = lock.packages?.[`node_modules/${dependency}`];
    if (entry?.version !== r.version || entry.integrity !== r.dist.integrity) {
      fail(`package-lock.json locks ${dependency} ${entry?.version} (${entry?.integrity}), not ${r.version} (${r.dist.integrity})`);
    }
  }
}

async function sizes() {
  return new Map(await Promise.all(engine.bundles.map(async bundle => [bundle, (await stat(join(resources, bundle))).size])));
}

function run(command, ...args) {
  const result = spawnSync(command, args, { cwd: here, stdio: 'inherit' });
  if (result.status !== 0) fail(`${command} ${args.join(' ')} failed (${result.error?.message ?? `exit ${result.status}`})`);
}

function fail(message) {
  failAs(LABEL, message);
}

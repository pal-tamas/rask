#!/usr/bin/env node
// Measures Flux UI's live documentation examples, so a Rask.Ui component can be held to them exactly.
//
// Rask.Ui must look and behave as Flux does, and is written without Flux's source. What makes that
// possible is that the docs render every example live: this opens a docs page in Chromium and records,
// for each example, what the browser computed — boxes, type, colours, borders, radii, shadows — in light
// and in dark, plus how each control changes when hovered, pressed and keyboard-focused. Numbers, not
// markup: no class attribute is recorded.
//
// Usage:  node scripts/flux/measure.mjs <out-dir> <page>...
//         node scripts/flux/measure.mjs /tmp/flux components/button layouts/sidebar
//
// Needs the Playwright driver the E2E project restores (dotnet build tests/Rask.Site.E2E.Tests -c Release).

import { mkdir, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { chromium, measurePage } from './lib.mjs';

const [out, ...pages] = process.argv.slice(2);
if (!out || pages.length === 0) {
  console.error('usage: node scripts/flux/measure.mjs <out-dir> <page>...   (e.g. components/button)');
  process.exit(1);
}

const browser = await chromium().launch();
for (const path of pages) {
  const dir = join(out, path.replace('/', '-'));
  const schemes = await measurePage(browser, `https://fluxui.dev/${path}`, dir);
  await mkdir(dir, { recursive: true });
  await writeFile(join(dir, 'measurements.json'), JSON.stringify({ page: path, schemes }, null, 1));
  console.log(`flux measure: ${path} — ${schemes.light.length} examples -> ${dir}`);
}

await browser.close();

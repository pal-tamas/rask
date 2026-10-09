#!/usr/bin/env node
// Holds Ui.Chart's GEOMETRY to Flux's, number by number — what parity.mjs cannot see.
//
// parity.mjs compares boxes and computed styles. A chart is mostly neither: it is path data, line ends,
// circle centres and translate()s, and two curves can share a bounding box. This compares those attributes
// for every SVG node of every chart, example by example, in light and dark.
//
// Flux's docs page draws its charts from data its server makes up on EVERY request (random values, dates
// counted back from now), so two loads never show the same chart. `--measure` therefore pins one load:
//   - the document is fetched once, kept in artifacts/flux-parity/flux/chart/document.html, and replayed
//     for both schemes, so light and dark draw the same numbers;
//   - each chart's data is written to tests/Rask.Ui.Tests/Flux/Parity/ChartParity.data.json, which
//     ChartParity.cs draws from (data, not code);
//   - the measurement lands where parity.mjs caches it, so `parity.mjs chart` (WITHOUT --refresh, which
//     would roll new numbers) compares styles against the very same load.
//
// Usage:  node scripts/flux/parity-chart.mjs --measure     # pin a load of Flux's page (rarely)
//         dotnet test tests/Rask.Ui.Tests --filter FluxParityPages
//         node scripts/flux/parity.mjs chart                # boxes and styles
//         node scripts/flux/parity-chart.mjs [--all]        # geometry; exit code 1 over the tolerance
//         node scripts/flux/parity-chart.mjs --pointer      # what follows the pointer, on the same pin
//
// `--pointer` walks a real pointer over every chart of the pinned load and of the Rask page (given the
// runtime's hooks, runtime.mjs) and compares, step by step, what each shows: where the tooltip is and what it
// says, where the cursor is, what a summary reads, which points and slices are marked active or inactive.

import { mkdir, readFile, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { chromium, measurePage, root } from './lib.mjs';
import { withRuntime } from './runtime.mjs';

const args = process.argv.slice(2);
const URL = 'https://fluxui.dev/components/chart';
const out = join(root, 'artifacts', 'flux-parity');
const fluxDir = join(out, 'flux', 'chart');
const dataFile = join(root, 'tests', 'Rask.Ui.Tests', 'Flux', 'Parity', 'ChartParity.data.json');
const TOLERANCE = 0.05;   // px, on every number of every geometry attribute
// Where the pointer goes, as shares of the drawing's box: across the plot, up and down it, and off it.
const ACROSS = [[0.03, 0.5], [0.2, 0.5], [0.35, 0.5], [0.5, 0.5], [0.65, 0.5], [0.8, 0.5], [0.97, 0.5], [0.5, 0.2], [0.5, 0.75], [0.5, 0.97]];
const ROUND = [[0.6, 0.3], [0.3, 0.7], [0.75, 0.6], [0.5, 0.5], [0.03, 0.03]];
const NEAR = 1;   // px: a tooltip's box depends on its text's width, which the two pages round apart
const limit = args.includes('--all') ? Infinity : 8;

const browser = await chromium().launch();
if (args.includes('--measure')) await measureFlux();
const flux = JSON.parse(await readFile(join(fluxDir, 'geometry.json'), 'utf8'));
const raskPage = join(out, 'rask', 'chart.html');
if (!existsSync(raskPage)) {
  console.error(`flux chart parity: ${raskPage} is missing — run: dotnet test tests/Rask.Ui.Tests --filter FluxParityPages`);
  await browser.close();
  process.exit(1);
}

if (args.includes('--pointer')) {
  const failed = await pointerWalk();
  await browser.close();
  console.log(failed ? `\nflux chart parity: the pointer is followed differently in ${failed} step(s).` : '\nflux chart parity: the pointer is followed as Flux follows it.');
  process.exit(failed ? 1 : 0);
}

const rask = {};
await measurePage(browser, pathToFileURL(raskPage).href, join(out, 'rask', 'chart'), async (page, scheme) => {
  rask[scheme] = await page.evaluate(geometry, '[data-ui-chart]');
});
await browser.close();

let failures = 0;
for (const scheme of ['light', 'dark']) {
  flux[scheme].forEach((theirs, index) => {
    const mine = rask[scheme][index];
    const label = `${scheme} #${index} ${theirs.section}`;
    const { diffs, worst } = compare(theirs, mine);
    failures += diffs.length ? 1 : 0;
    console.log(`${diffs.length ? 'FAIL' : 'ok  '} ${label} — max deviation ${worst.toFixed(4)}px${diffs.length ? `, ${diffs.length} difference(s)` : ''}`);
    for (const d of diffs.slice(0, limit)) console.log(`       ${d}`);
    if (diffs.length > limit) console.log(`       … ${diffs.length - limit} more (--all)`);
  });
}

console.log(failures ? `\nflux chart parity: geometry differs in ${failures} example(s).` : '\nflux chart parity: geometry matches Flux.');
process.exit(failures ? 1 : 0);

async function measureFlux() {
  await mkdir(fluxDir, { recursive: true });
  const document = await (await fetch(URL)).text();
  await writeFile(join(fluxDir, 'document.html'), document);
  // One document for both schemes: measurePage opens a context per scheme, and each is handed the same page.
  const replaying = {
    newContext: async options => {
      const context = await browser.newContext(options);
      await context.route(URL, route => route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: document }));
      return context;
    },
  };
  const found = {};
  const schemes = await measurePage(replaying, URL, fluxDir, async (page, scheme) => {
    // Flux sizes a chart's gutters by measuring its tick labels when it first draws, and that can be before
    // Inter has loaded: the same chart then comes out 2.5px narrower in whichever scheme lost the race. It
    // draws again when its box changes, so the box is changed and put back once the fonts are in.
    await page.setViewportSize({ width: 1180, height: 900 });
    await page.waitForTimeout(400);
    await page.setViewportSize({ width: 1280, height: 900 });
    await page.waitForTimeout(400);
    found[scheme] = await page.evaluate(geometry, 'ui-chart');
  });
  await writeFile(join(fluxDir, 'measurements.json'), JSON.stringify(schemes));
  await writeFile(join(fluxDir, 'geometry.json'), JSON.stringify(found));
  const data = found.light.map(example => ({ section: example.section, charts: example.charts.map(chart => chart.value) }));
  await writeFile(dataFile, JSON.stringify(data, null, 1) + '\n');
  console.log(`flux chart parity: pinned ${found.light.length} examples -> ${fluxDir}, data -> ${dataFile}`);
}

// Runs in the page. Every chart of every example: its data (Flux's element exposes it) and the geometry of
// each SVG node it drew. A <template> inside an <svg> is Flux's prototype for a node, never drawn: skipped.
function geometry(selector) {
  const ATTRS = ['d', 'points', 'x', 'y', 'width', 'height', 'x1', 'x2', 'y1', 'y2', 'cx', 'cy', 'r', 'rx', 'ry',
    'transform', 'viewBox', 'stroke-width', 'stroke-dasharray', 'dx', 'dy', 'text-anchor', 'dominant-baseline', 'opacity'];
  const headings = [...document.querySelectorAll('h2[id]')];
  return [...document.querySelectorAll('[data-preview-wrapper]')].map(wrapper => ({
    section: wrapper.dataset.section
      ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '',
    charts: [...wrapper.querySelectorAll(selector)].map(chart => ({
      value: chart.value ?? null,
      nodes: [...chart.querySelectorAll('svg, svg *')]
        .filter(node => !node.closest('template'))
        .map(node => {
          const style = getComputedStyle(node);
          return {
            tag: node.tagName.toLowerCase(),
            hidden: style.display === 'none',
            anchor: style.textAnchor,
            text: node.children.length ? '' : node.textContent.trim(),
            attrs: Object.fromEntries(ATTRS.filter(name => node.hasAttribute(name)).map(name => [name, node.getAttribute(name)])),
          };
        }),
    })),
  }));
}

function compare(theirs, mine) {
  const diffs = [];
  let worst = 0;
  if (!mine || mine.charts.length !== theirs.charts.length) {
    return { diffs: [`charts: Flux has ${theirs.charts.length}, Rask has ${mine?.charts.length ?? 0}`], worst };
  }

  theirs.charts.forEach((chart, c) => {
    const other = mine.charts[c];
    if (chart.nodes.length !== other.nodes.length) {
      diffs.push(`chart ${c}: ${chart.nodes.length} SVG nodes vs ${other.nodes.length} (<${chart.nodes.map(n => n.tag).join(' ')}> vs <${other.nodes.map(n => n.tag).join(' ')}>)`.slice(0, 600));
      return;
    }

    chart.nodes.forEach((a, i) => {
      const b = other.nodes[i];
      const where = `chart ${c} node ${i} <${a.tag}>${a.text ? ` "${a.text}"` : ''}`;
      if (a.tag !== b.tag) diffs.push(`${where}: tag <${b.tag}>`);
      if (a.text !== b.text) diffs.push(`${where}: text "${a.text}" vs "${b.text}"`);
      if (a.hidden !== b.hidden) diffs.push(`${where}: ${a.hidden ? 'hidden in Flux' : 'hidden in Rask'}`);
      if (!a.hidden && a.tag === 'text' && a.anchor !== b.anchor) diffs.push(`${where}: text-anchor ${a.anchor} vs ${b.anchor}`);
      for (const name of new Set([...Object.keys(a.attrs), ...Object.keys(b.attrs)])) {
        const x = numbers(a.attrs[name]);
        const y = numbers(b.attrs[name]);
        if (x.shape !== y.shape) {
          diffs.push(`${where}: ${name}: ${String(a.attrs[name]).slice(0, 70)} vs ${String(b.attrs[name]).slice(0, 70)}`);
          continue;
        }

        const off = Math.max(0, ...x.values.map((v, k) => Math.abs(v - y.values[k])));
        worst = Math.max(worst, off);
        if (off > TOLERANCE) diffs.push(`${where}: ${name} is off by ${off.toFixed(3)}px`);
      }
    });
  });

  return { diffs, worst };
}

// An attribute as its commands and its numbers: "M 1,2 C3 4" and "M1 2C3,4" are the same path.
function numbers(value) {
  if (value === undefined) return { shape: '(none)', values: [] };
  const values = [];
  const shape = String(value).replace(/-?\d*\.?\d+(?:e[-+]?\d+)?/gi, n => (values.push(Number(n)), '#'))
    .replace(/[\s,]+/g, ' ').replace(/ ?([a-zA-Z()]) ?/g, '$1').trim();
  return { shape, values };
}

// ---- The pointer walk ------------------------------------------------------------------------------------


async function pointerWalk() {
  const document = await readFile(join(fluxDir, 'document.html'), 'utf8');
  const context = await browser.newContext({ viewport: { width: 1280, height: 900 } });
  await context.route(URL, route => route.fulfill({ status: 200, contentType: 'text/html; charset=utf-8', body: document }));
  const theirs = await context.newPage();
  await theirs.goto(URL, { waitUntil: 'networkidle', timeout: 90000 });
  await theirs.evaluate(() => document.fonts.ready);
  // Drawn again once the fonts are in, as --measure does.
  await theirs.setViewportSize({ width: 1180, height: 900 });
  await theirs.waitForTimeout(400);
  await theirs.setViewportSize({ width: 1280, height: 900 });
  await theirs.waitForTimeout(400);
  const ours = await context.newPage();
  await ours.goto(pathToFileURL(raskPage).href, { waitUntil: 'networkidle' });
  await ours.evaluate(() => document.fonts.ready);
  await withRuntime(ours, 'rask-hooks.ts');

  const count = await theirs.evaluate(() => document.querySelectorAll('[data-preview-wrapper] ui-chart').length);
  let failed = 0;
  for (let chart = 0; chart < count; chart++) {
    const round = await theirs.evaluate(i => !!document.querySelectorAll('[data-preview-wrapper] ui-chart')[i].querySelector('template[name=pie], svg path[stroke-linejoin=round][stroke-width="3"]'), chart);
    const differences = [];
    for (const [fx, fy] of [...(round ? ROUND : ACROSS), [-0.2, -0.2]]) {
      const [a, b] = await Promise.all([step(theirs, 'ui-chart', chart, fx, fy), step(ours, '[data-ui-chart]', chart, fx, fy)]);
      const off = differing(a, b);
      if (off.length) differences.push(`at ${fx},${fy}: ${off.join('; ')}`);
    }

    failed += differences.length;
    console.log(`${differences.length ? 'FAIL' : 'ok  '} pointer chart ${chart}${differences.length ? ` — ${differences.length} step(s)` : ''}`);
    for (const d of differences.slice(0, limit)) console.log(`       ${d}`.slice(0, 500));
  }

  await context.close();
  return failed;
}

// Moves the pointer to a share of chart `index`'s drawing — in two moves, as a hand arrives — and reads the chart.
async function step(page, selector, index, fx, fy) {
  const box = await page.evaluate(({ selector, index }) => {
    const chart = document.querySelectorAll(`[data-preview-wrapper] ${selector}`)[index];
    chart.scrollIntoView({ block: 'center' });
    const svg = [...chart.querySelectorAll('svg')].find(s => !s.closest('template')).getBoundingClientRect();
    return { x: svg.left, y: svg.top, width: svg.width, height: svg.height };
  }, { selector, index });
  const x = Math.round(box.x + fx * box.width);
  const y = Math.round(box.y + fy * box.height);
  await page.mouse.move(x - 1, y);
  await page.mouse.move(x, y);
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
  return page.evaluate(read, { selector, index });
}

// Runs in the page: what the chart shows now, in terms both pages have.
function read({ selector, index }) {
  const chart = document.querySelectorAll(`[data-preview-wrapper] ${selector}`)[index];
  const frame = chart.getBoundingClientRect();
  const svg = [...chart.querySelectorAll('svg')].find(s => !s.closest('template'));
  const origin = svg.getBoundingClientRect();
  const tooltip = chart.querySelector(':scope > [data-rask-plot-tooltip]')
    ?? [...chart.children].find(e => e.tagName === 'DIV' && getComputedStyle(e).position === 'absolute' && !e.hasAttribute('data-ui-chart-hover') && !e.querySelector('svg'));
  const tip = tooltip?.getBoundingClientRect();
  const shown = tooltip ? getComputedStyle(tooltip).opacity === '1' : false;
  // Flux's cursor is a path in the drawing; the kit's a box over it.
  let cursor = null;
  const path = [...svg.querySelectorAll('path[stroke-dasharray][opacity]')].find(p => !p.closest('template'));
  const line = chart.querySelector('[data-rask-plot-area] > div');
  if (line) {
    const box = line.getBoundingClientRect();
    cursor = getComputedStyle(line).display === 'none' ? null : [box.left - origin.left, box.top - origin.top, box.right - origin.left, box.bottom - origin.top];
  } else if (path && Number(path.getAttribute('opacity')) > 0) {
    const n = (path.getAttribute('d').match(/-?\d*\.?\d+(?:e[-+]?\d+)?/gi) ?? []).map(Number);
    cursor = / h/.test(path.getAttribute('d'))
      ? [n[0], n[1], n[0] + n[2], n[1] + n[3]]
      : [n[0] - 0.5, Math.min(n[1], n[3]), n[0] + 0.5, Math.max(n[1], n[3])];
  }

  const mark = node => node.hasAttribute('data-active') ? 'A' : node.hasAttribute('data-inactive') ? 'i' : '.';
  return {
    tooltip: shown ? { at: [tip.left - frame.left, tip.top - frame.top], text: tooltip.innerText.replace(/\s+/g, ' ').trim() } : null,
    cursor,
    summary: [...chart.querySelectorAll('slot')].filter(s => !s.closest('template') && !tooltip?.contains(s)).map(s => s.textContent.trim()).join(' | '),
    points: [...svg.querySelectorAll('circle')].filter(c => !c.closest('template')).map(mark).join(''),
    slices: [...svg.querySelectorAll('g > path[stroke-linejoin]')].filter(p => !p.closest('template') && p.getAttribute('stroke-width') === '3').map(mark).join(''),
  };
}

function differing(a, b) {
  const off = [];
  const close = (x, y) => x.length === y.length && x.every((v, k) => Math.abs(v - y[k]) <= NEAR);
  if (!a.tooltip !== !b.tooltip) off.push(`tooltip ${a.tooltip ? 'shown' : 'hidden'} in Flux, ${b.tooltip ? 'shown' : 'hidden'} in Rask`);
  else if (a.tooltip) {
    if (a.tooltip.text !== b.tooltip.text) off.push(`tooltip says "${a.tooltip.text}" vs "${b.tooltip.text}"`);
    if (!close(a.tooltip.at, b.tooltip.at)) off.push(`tooltip at ${a.tooltip.at.map(v => v.toFixed(1))} vs ${b.tooltip.at.map(v => v.toFixed(1))}`);
  }

  if (!a.cursor !== !b.cursor) off.push(`cursor ${a.cursor ? 'shown' : 'hidden'} in Flux, ${b.cursor ? 'shown' : 'hidden'} in Rask`);
  else if (a.cursor && !close(a.cursor, b.cursor)) off.push(`cursor box ${a.cursor.map(v => v.toFixed(1))} vs ${b.cursor.map(v => v.toFixed(1))}`);
  for (const what of ['summary', 'points', 'slices']) if (a[what] !== b[what]) off.push(`${what} "${a[what]}" vs "${b[what]}"`);
  return off;
}

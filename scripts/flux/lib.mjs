// Shared by measure.mjs and parity.mjs: Chromium, and the one way an example is measured.
import { createRequire } from 'node:module';
import { mkdir } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const root = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..');

export function chromium() {
  // RASK_FLUX_PLAYWRIGHT names a driver directly: a fresh worktree has not built the E2E project yet.
  const driver = [process.env.RASK_FLUX_PLAYWRIGHT, ...['Release', 'Debug']
    .map(cfg => join(root, 'tests', 'Rask.Site.E2E.Tests', 'bin', cfg, 'net10.0', '.playwright', 'package'))]
    .find(path => path && existsSync(path));
  if (!driver) {
    console.error('flux: no Playwright driver — run `dotnet build tests/Rask.Site.E2E.Tests -c Release` first.');
    process.exit(1);
  }

  return createRequire(import.meta.url)(driver).chromium;
}

export const STYLES = [
  'display', 'position', 'boxSizing', 'width', 'height', 'minWidth', 'minHeight', 'maxWidth',
  'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft',
  'marginTop', 'marginRight', 'marginBottom', 'marginLeft', 'gap',
  'flexDirection', 'alignItems', 'justifyContent', 'flexGrow', 'flexShrink', 'gridTemplateColumns',
  'fontFamily', 'fontSize', 'lineHeight', 'fontWeight', 'letterSpacing', 'textAlign', 'textTransform',
  'textDecorationLine', 'whiteSpace', 'color', 'backgroundColor', 'backgroundImage', 'opacity',
  'borderTopWidth', 'borderRightWidth', 'borderBottomWidth', 'borderLeftWidth', 'borderTopStyle',
  'borderTopColor', 'borderRightColor', 'borderBottomColor', 'borderLeftColor',
  'borderTopLeftRadius', 'borderTopRightRadius', 'borderBottomRightRadius', 'borderBottomLeftRadius',
  'boxShadow', 'outlineStyle', 'outlineWidth', 'outlineColor', 'outlineOffset', 'cursor',
  'overflowX', 'overflowY', 'zIndex', 'transform', 'transitionProperty', 'transitionDuration',
  'fill', 'stroke', 'strokeWidth', 'backdropFilter',
];

// Every `[data-preview-wrapper]` on `url`, measured in light and in dark.
export async function measurePage(browser, url, shots) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    // A fixed clock: a calendar that opens on today would measure differently every morning.
    await context.clock.setFixedTime(new Date('2026-01-15T12:00:00Z'));
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    await page.evaluate(() => document.fonts.ready);
    schemes[scheme] = await measure(page, join(shots, scheme));
    await context.close();
  }

  return schemes;
}

async function measure(page, shots) {
  await mkdir(shots, { recursive: true });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('DOM.enable');
  await cdp.send('CSS.enable');

  // A running animation is measured at its first frame, not wherever the clock caught it: a shimmer
  // half-way across would measure differently on every run, and never the same on both pages.
  await page.evaluate(() => {
    for (const animation of document.getAnimations()) {
      if (animation instanceof CSSAnimation && animation.playState === 'running') {
        animation.pause();
        animation.currentTime = 0;
      }
    }
  });

  const wrappers = await page.$$('[data-preview-wrapper]');
  const examples = [];
  for (const [index, wrapper] of wrappers.entries()) {
    const example = await wrapper.evaluate(collect, { STYLES, index });
    await wrapper.scrollIntoViewIfNeeded();
    const name = `${String(index).padStart(2, '0')}-${example.section || 'intro'}`;
    await wrapper.screenshot({ path: join(shots, `${name}.png`) }).catch(() => {});

    // Forced pseudo-states rather than real pointer moves: a hover that opens a tooltip or a menu would
    // move the page under the next measurement, and :focus-visible cannot be reached by el.focus().
    example.states = [];
    for (const target of example.interactive) {
      const { root } = await cdp.send('DOM.getDocument', { depth: 0 });
      const { nodeId } = await cdp.send('DOM.querySelector', { nodeId: root.nodeId, selector: `[data-m="${target}"]` });
      if (!nodeId) continue;

      for (const state of ['hover', 'active', 'focus-visible']) {
        await cdp.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: state === 'focus-visible' ? ['focus', 'focus-visible'] : [state] });
        const changed = await page.evaluate(diff, { STYLES, target });
        if (Object.keys(changed).length) example.states.push({ node: target, state, changed });
      }

      await cdp.send('CSS.forcePseudoState', { nodeId, forcedPseudoClasses: [] });
    }

    delete example.interactive;
    examples.push(example);
  }

  return examples;
}

// Runs in the page. Every element of one example: where it is, what it is, how it computed.
function collect(wrapper, { STYLES, index }) {
  // Flux's page names a section with the heading above it; a Rask parity page states it on the wrapper.
  const headings = [...document.querySelectorAll('h2[id]')];
  const section = wrapper.dataset.section
    ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
  const ordinal = [...document.querySelectorAll('[data-preview-wrapper]')].slice(0, index)
    .filter(w => (w.getAttribute('data-m-section') ?? '') === section).length;
  wrapper.setAttribute('data-m-section', section);
  const origin = wrapper.getBoundingClientRect();
  const keep = name => !/^(class|style|wire:|x-|@|:|data-m$|data-m-section$|data-section$|data-preview-wrapper$)/.test(name);
  const style = (el, pseudo) => {
    const computed = getComputedStyle(el, pseudo);
    return Object.fromEntries(STYLES.map(k => [k, computed[k]]));
  };

  // What moves, and how: each CSS animation's timing and keyframes, on the element or pseudo it runs on.
  const animations = new Map();
  for (const animation of document.getAnimations()) {
    if (!(animation instanceof CSSAnimation)) continue;
    const { target, pseudoElement } = animation.effect;
    const { duration, delay, iterations, direction, fill } = animation.effect.getTiming();
    const keyframes = animation.effect.getKeyframes().map(({ composite, computedOffset, ...frame }) => frame);
    (animations.get(target) ?? animations.set(target, []).get(target)).push({
      on: pseudoElement ?? '', name: animation.animationName, duration, delay, iterations: String(iterations), direction, fill, keyframes,
    });
  }

  const nodes = [];
  const interactive = [];
  const elements = [wrapper, ...wrapper.querySelectorAll('*')].slice(0, 500);
  elements.forEach((el, i) => {
    const id = `${index}-${i}`;
    el.setAttribute('data-m', id);
    const box = el.getBoundingClientRect();
    const node = {
      id, parent: el === wrapper ? null : el.parentElement.getAttribute('data-m'), tag: el.tagName.toLowerCase(),
      attrs: Object.fromEntries([...el.attributes].filter(a => keep(a.name)).map(a => [a.name, a.value])),
      text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).filter(Boolean).join(' ').slice(0, 80),
      box: [box.x - origin.x, box.y - origin.y, box.width, box.height].map(v => Math.round(v * 100) / 100),
      style: style(el),
    };
    for (const pseudo of ['::before', '::after']) {
      const content = getComputedStyle(el, pseudo).content;
      if (content && content !== 'none' && content !== 'normal') node[pseudo] = { content, ...style(el, pseudo) };
    }

    if (animations.has(el)) node.animations = animations.get(el);
    nodes.push(node);
    // Marked on either page: `data-flux-*` on Flux's, `data-ui-*` on Rask's — or one side's states go unmeasured.
    const fluxed = [...el.attributes].some(a => a.name.startsWith('data-flux') || a.name.startsWith('data-ui-'));
    if (interactive.length < 60 && (fluxed || el.matches('button, a, input, select, textarea, summary, label, [role], [tabindex]'))) {
      interactive.push(id);
    }
  });

  // The baseline every forced state is compared against.
  window.__fluxBase ??= {};
  for (const id of interactive) {
    const el = document.querySelector(`[data-m="${id}"]`);
    window.__fluxBase[id] = Object.fromEntries(STYLES.map(k => [k, getComputedStyle(el)[k]]));
  }

  return { index, section, ordinal, size: [Math.round(origin.width), Math.round(origin.height)], nodes, interactive };
}

// Runs in the page. What a forced state changed on a control, and on its descendants.
function diff({ STYLES, target }) {
  const changed = {};
  const el = document.querySelector(`[data-m="${target}"]`);
  if (!el) return changed;
  const computed = getComputedStyle(el);
  const base = window.__fluxBase[target];
  for (const k of STYLES) if (computed[k] !== base[k]) changed[k] = computed[k];
  return changed;
}

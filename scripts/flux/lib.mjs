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
export async function measurePage(browser, url, shots, prepare) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    // A fixed clock: a calendar that opens on today would measure differently every morning.
    await context.clock.setFixedTime(new Date('2026-01-15T12:00:00Z'));
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    await page.evaluate(() => document.fonts.ready);
    if (prepare) await prepare(page, scheme);
    // A running animation measures wherever it happens to be — a spinner's angle differed on every run,
    // and between a node and its own forced states. Held a quarter of a second in, it measures the same
    // each time, and still says what the animation is: a spin of another speed or easing is another angle.
    await page.evaluate(() => document.getAnimations().forEach(animation => {
      animation.pause();
      animation.currentTime = 250;
    }));
    schemes[scheme] = await measure(page, join(shots, scheme));
    await context.close();
  }

  return schemes;
}

// `states`: how many controls have their hover, press and focus measured — a whole document has more than an example.
async function measure(page, shots, states = 60) {
  await mkdir(shots, { recursive: true });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('DOM.enable');
  await cdp.send('CSS.enable');

  const wrappers = await page.$$('[data-preview-wrapper]');
  const examples = [];
  for (const [index, wrapper] of wrappers.entries()) {
    const example = await wrapper.evaluate(collect, { STYLES, index, states });
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
function collect(wrapper, { STYLES, index, states }) {
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
    // Not rendered: display:none, or clipped to a pixel for a screen reader alone.
    const clipped = node.style.position === 'absolute' && box.width <= 1 && box.height <= 1 && node.style.overflowX === 'hidden';
    if (node.style.display === 'none' || clipped) node.hidden = true;
    for (const pseudo of ['::before', '::after']) {
      const content = getComputedStyle(el, pseudo).content;
      if (content && content !== 'none' && content !== 'normal') node[pseudo] = { content, ...style(el, pseudo) };
    }

    nodes.push(node);
    // Either side's marker: a Flux node's states were measured, so its Rask twin's have to be too.
    const fluxed = [...el.attributes].some(a => a.name.startsWith('data-flux') || a.name.startsWith('data-ui-'));
    if (interactive.length < states && !node.hidden && (fluxed || el.matches('button, a, input, select, textarea, summary, label, [role], [tabindex]'))) {
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

// ----- Layouts ---------------------------------------------------------------------------------------
// A layout is a whole document, so Flux's docs do not render it in a `[data-preview-wrapper]`: under each
// heading the page links to a full-screen demo (fluxui.dev/demo/<name>). One "example" of a layout page
// is therefore one demo, at one width, in one state — its <body> measured exactly as a wrapper is.

// Desktop and phone, and what a reader can do to the sidebar at each: narrow it to its rail, slide it in.
export const VIEWS = [
  { width: 1280, states: ['', 'rail'] },
  { width: 390, states: ['', 'open'] },
];

// What puts a layout into a state, on either side: Flux's control or Rask's.
const CONTROLS = {
  rail: '[data-flux-sidebar-collapse] button, [data-ui-sidebar-collapse] label',
  open: '[data-flux-sidebar-toggle], [data-ui-sidebar-toggle]',
};

// The demos a layout page links to, each under the id of the <h2> above it ('' for the first).
export async function layoutDemos(browser, url) {
  const page = await browser.newPage();
  await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
  const demos = await page.evaluate(() => {
    const headings = [...document.querySelectorAll('h2[id]')];
    const seen = new Set();
    return [...document.querySelectorAll('a[href*="/demo/"]')].filter(a => !seen.has(a.href) && seen.add(a.href)).map(a => ({
      name: new URL(a.href).pathname.split('/').pop(),
      section: headings.filter(h => h.compareDocumentPosition(a) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '',
    }));
  });
  await page.close();
  return demos;
}

// Every demo at every width and state, in light and in dark. `locate` turns a demo's name into its URL;
// `only` names the examples to take (a side is only asked for the states the other side has).
export async function measureLayouts(browser, demos, locate, shots, { prepare, only } = {}) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    schemes[scheme] = [];
    for (const demo of demos) {
      for (const view of VIEWS) {
        for (const state of view.states) {
          const section = `${demo.section || demo.name}@${view.width}${state && `-${state}`}`;
          if (only && !only.has(section)) continue;
          const context = await browser.newContext({ viewport: { width: view.width, height: 900 }, colorScheme: scheme });
          const page = await context.newPage();
          await page.goto(locate(demo), { waitUntil: 'networkidle', timeout: 90000 });
          await page.evaluate(() => document.fonts.ready);
          if (state) {
            const control = page.locator(CONTROLS[state]).locator('visible=true').first();
            if (!(await control.count())) {
              await context.close();
              continue;
            }

            await control.click();
            // Away from the sidebar, and off the control: its hover is a state of its own, and the tooltip a
            // focused control shows is not the layout.
            await page.mouse.move(view.width - 5, 895);
            await page.evaluate(() => document.activeElement?.blur());
            await page.waitForTimeout(400);
          }

          await page.evaluate(name => {
            document.body.setAttribute('data-preview-wrapper', '');
            document.body.setAttribute('data-section', name);
          }, section);
          if (prepare) await prepare(page, scheme, section);
          await page.evaluate(() => document.getAnimations().forEach(animation => {
            animation.pause();
            animation.currentTime = 250;
          }));
          schemes[scheme].push(...await measure(page, join(shots, scheme), 400));
          await context.close();
        }
      }
    }
  }

  return schemes;
}

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

// Every `[data-preview-wrapper]` on `url`, measured in light and in dark. `prepare(page, scheme)` runs on
// the loaded page before anything is measured.
export async function measurePage(browser, url, shots, prepare) {
  const schemes = {};
  for (const scheme of ['light', 'dark']) {
    const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: scheme });
    // A fixed clock: a calendar that opens on today would measure differently every morning.
    await context.clock.setFixedTime(new Date('2026-01-15T12:00:00Z'));
    // Motion at rest (rest, below), for Flux's page and Rask's alike: a look read mid-flight measures
    // differently every run.
    await context.addInitScript(`window.__fluxRest = ${rest}`);
    const page = await context.newPage();
    await page.goto(url, { waitUntil: 'networkidle', timeout: 90000 });
    await page.evaluate(() => document.fonts.ready);
    await page.evaluate(() => window.__fluxRest());
    if (prepare) await prepare(page, scheme);
    schemes[scheme] = await measure(page, join(shots, scheme));
    await context.close();
  }

  return schemes;
}

// `layout`: a whole document rather than one example (see Layouts, below) — more controls have their
// states measured, and only those that are rendered.
async function measure(page, shots, layout = false) {
  await mkdir(shots, { recursive: true });
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('DOM.enable');
  await cdp.send('CSS.enable');

  const wrappers = await page.$$('[data-preview-wrapper]');
  const examples = [];
  for (const [index, wrapper] of wrappers.entries()) {
    // A popup is closed as loaded, and the open popup is the component. An example says what opens it with
    // `data-parity-open` on that element — a press, or "hover" — and it is open while this example is measured.
    const opener = await wrapper.$('[data-parity-open]');
    if (opener) await open(page, wrapper, opener);
    const example = await wrapper.evaluate(collect, { STYLES, index, layout });
    const targets = stateTargets(example.nodes, layout);
    await page.evaluate(baseline, { STYLES, targets });
    await wrapper.scrollIntoViewIfNeeded();
    const name = `${String(index).padStart(2, '0')}-${example.section || 'intro'}`;
    await wrapper.screenshot({ path: join(shots, `${name}.png`) }).catch(() => {});

    // Forced pseudo-states rather than real pointer moves: a hover that opens a tooltip or a menu would
    // move the page under the next measurement, and :focus-visible cannot be reached by el.focus().
    example.states = [];
    for (const target of targets) {
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

    if (opener) await close(page);
    examples.push(example);
  }

  return examples;
}

// Opened the way a reader opens it — a real pointer, at the element's centre — with the example mid-screen, so
// the popup has the same room around it on both pages.
async function open(page, wrapper, opener) {
  await wrapper.evaluate(el => el.scrollIntoView({ block: 'center' }));
  const box = await opener.boundingBox();
  const at = [box.x + box.width / 2, box.y + box.height / 2];
  if (await opener.getAttribute('data-parity-open') === 'hover') await page.mouse.move(...at);
  else await page.mouse.click(...at);
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
}

async function close(page) {
  await page.keyboard.press('Escape');
  await page.mouse.move(0, 0);
  await page.evaluate(() => new Promise(done => requestAnimationFrame(() => requestAnimationFrame(done))));
}

// Runs in the page. Everything in motion, put where it rests: what ends (a transition, an entrance) at
// its end — the state Flux draws — and what never ends (a spinner, a shimmer) on its first frame. What
// either IS (duration, easing, keyframes) is recorded per node in collect() and compared on that.
// A scroll-driven animation runs on no clock: it is where the scroll position puts it, and is left there.
function rest() {
  for (const animation of document.getAnimations()) {
    if (!(animation.timeline instanceof DocumentTimeline)) continue;
    if (animation.effect?.getComputedTiming().endTime === Infinity) {
      animation.pause();
      animation.currentTime = 0;
    } else {
      animation.finish();
    }
  }
}

// Runs in the page. Every element of one example: where it is, what it is, how it computed.
function collect(wrapper, { STYLES, index, layout }) {
  window.__fluxRest();
  // Flux's page names a section with the heading above it; a Rask parity page states it on the wrapper.
  const headings = [...document.querySelectorAll('h2[id]')];
  const section = wrapper.dataset.section
    ?? headings.filter(h => h.compareDocumentPosition(wrapper) & Node.DOCUMENT_POSITION_FOLLOWING).pop()?.id ?? '';
  const ordinal = [...document.querySelectorAll('[data-preview-wrapper]')].slice(0, index)
    .filter(w => (w.getAttribute('data-m-section') ?? '') === section).length;
  wrapper.setAttribute('data-m-section', section);
  const origin = wrapper.getBoundingClientRect();
  const keep = name => !/^(class|style|wire:|x-|@|:|data-m$|data-m-section$|data-section$|data-preview-wrapper$)/.test(name);
  // An auto margin is recorded as declared. Chromium answers the space it took on one page load and 0px
  // on the next, for the same layout — and the box already states where it put the element.
  const auto = (declared, k) => k.startsWith('margin') && String(declared.get(k.replace('margin', 'margin-').toLowerCase())) === 'auto';
  const style = (el, pseudo) => {
    const computed = getComputedStyle(el, pseudo);
    const declared = pseudo ? undefined : el.computedStyleMap();
    return Object.fromEntries(STYLES.map(k => [k, declared && auto(declared, k) ? 'auto' : computed[k]]));
  };

  // What moves, and how: each CSS animation's timing and keyframes, on the element or pseudo it runs on.
  // Clock-driven ones only. A scroll-driven animation is a state, not a motion — Rask.Ui uses one where
  // Flux runs script (a sticky column's shadow) — and what it does is in the computed styles already.
  const animations = new Map();
  for (const animation of document.getAnimations()) {
    if (!(animation instanceof CSSAnimation) || !(animation.timeline instanceof DocumentTimeline)) continue;
    const { target, pseudoElement } = animation.effect;
    const { duration, delay, iterations, direction, fill } = animation.effect.getTiming();
    const keyframes = animation.effect.getKeyframes().map(({ composite, computedOffset, ...frame }) => frame);
    (animations.get(target) ?? animations.set(target, []).get(target)).push({
      on: pseudoElement ?? '', name: animation.animationName, duration, delay, iterations: String(iterations), direction, fill, keyframes,
    });
  }

  const nodes = [];
  // What is never drawn is not collected, so it does not count towards the 500 nodes an example is measured
  // to. A <template> and what is inside one: Flux's client-side components leave theirs in place (one per day
  // of a calendar), and what a script stamps from it is in the tree beside it. An <input type="hidden">: the
  // one bound field the runtime reads a group of typed segments through (data-rask-segments), where Flux's
  // script keeps the value in the element itself.
  const undrawn = el => el.closest('template') || (el.localName === 'input' && el.type === 'hidden');
  const elements = [wrapper, ...wrapper.querySelectorAll('*')].filter(el => !undrawn(el)).slice(0, 500);
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
    if (layout) {
      // Not rendered: display:none, or clipped to a pixel for a screen reader alone.
      const clipped = node.style.position === 'absolute' && box.width <= 1 && box.height <= 1 && node.style.overflowX === 'hidden';
      if (node.style.display === 'none' || clipped) node.hidden = true;
    }

    for (const pseudo of ['::before', '::after']) {
      const content = getComputedStyle(el, pseudo).content;
      if (content && content !== 'none' && content !== 'normal') node[pseudo] = { content, ...style(el, pseudo) };
    }

    if (animations.has(el)) node.animations = animations.get(el);
    nodes.push(node);
  });

  return { index, section, ordinal, size: [Math.round(origin.width), Math.round(origin.height)], nodes };
}

// The nodes of one example whose hover, active and focus-visible states are measured: every component
// root and part (`data-flux-*` on Flux's page, `data-ui-*` on a Rask one) and every native control, in
// document order, up to a limit that keeps a long example affordable. From the recorded nodes rather
// than the live page, so parity.mjs can ask the same question of a measurement taken earlier.
// A layout is a whole document: 400 controls rather than 60, and none that is not rendered.
export function stateTargets(nodes, layout = false) {
  const controls = new Set(['button', 'a', 'input', 'select', 'textarea', 'summary', 'label']);
  const marked = node => Object.keys(node.attrs).some(name => name.startsWith('data-flux') || name.startsWith('data-ui-'));
  return nodes
    .filter(node => marked(node) || controls.has(node.tag) || 'role' in node.attrs || 'tabindex' in node.attrs)
    .filter(node => !(layout && node.hidden))
    .slice(0, layout ? 400 : 60)
    .map(node => node.id);
}

// Runs in the page. The resting styles every forced state is compared against.
function baseline({ STYLES, targets }) {
  window.__fluxBase ??= {};
  for (const id of targets) {
    const el = document.querySelector(`[data-m="${id}"]`);
    if (!el) continue;   // an editor swaps its own nodes in after it is collected: no state to compare
    window.__fluxBase[id] = Object.fromEntries(STYLES.map(k => [k, getComputedStyle(el)[k]]));
  }
}

// Runs in the page. What a forced state changed on a control, and on its descendants.
function diff({ STYLES, target }) {
  const changed = {};
  const el = document.querySelector(`[data-m="${target}"]`);
  if (!el) return changed;
  window.__fluxRest();   // the state's own transition, taken to the end it settles on
  const computed = getComputedStyle(el);
  const base = window.__fluxBase[target];
  if (!base) return changed;
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
          // Motion at rest, as measurePage has it: the sidebar's slide is measured where it ends.
          await context.addInitScript(`window.__fluxRest = ${rest}`);
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
          await page.evaluate(() => window.__fluxRest());
          schemes[scheme].push(...await measure(page, join(shots, scheme), true));
          await context.close();
        }
      }
    }
  }

  return schemes;
}

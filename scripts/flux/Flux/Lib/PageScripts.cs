namespace Rask.Flux;

/// <summary>
///     The functions a measurement runs IN THE PAGE: browser JavaScript, handed to Playwright as text. Each is a
///     function expression, called with the arguments its doc names.
/// </summary>
public static class PageScripts
{
    /// <summary>
    ///     Everything in motion, put where it rests: what ends (a transition, an entrance) at its end — the state Flux
    ///     draws — and what never ends (a spinner, a shimmer) on its first frame. What either IS (duration, easing,
    ///     keyframes) is recorded per node in <see cref="Collect" /> and compared on that. A scroll-driven animation runs
    ///     on no clock: it is where the scroll position puts it, and is left there. Installed as <c>window.__fluxRest</c>.
    /// </summary>
    public const string Rest = """
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
        """;

    /// <summary>
    ///     (wrapper, { STYLES, PRESSABLE, HELD, index, layout }) — every element of one example: where it is, what it is,
    ///     how it computed.
    /// </summary>
    public const string Collect = """
        function collect(wrapper, { STYLES, PRESSABLE, HELD, index, layout }) {
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
          // A month of a calendar that is ITSELF not displayed: under 640 px Flux's script draws one month whatever
          // `months` says, and the kit's stylesheet hides the others until the page has heard how wide it is.
          const folded = el => { const month = el.closest('[data-month]'); return month && getComputedStyle(month).display === 'none'; };
          // And the empty box the kit adds for the runtime to read a width from (parity's EXTRA): Flux has no node
          // there, so counting it would end the kit's 500 one node early wherever an example runs past them.
          const probe = el => el.childElementCount === 0 && (el.hasAttribute('data-ui-calendar-narrow') || el.hasAttribute('data-ui-color-picker-sheet'));
          const undrawn = el => el.closest('template') || (el.localName === 'input' && el.type === 'hidden') || folded(el) || probe(el);
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
              pressable: el.closest(PRESSABLE) ? true : el.closest(HELD) ? 'held' : false,
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

          // `origin`: where the example was in the viewport as it was measured — a node's box is counted from there.
          return { index, section, ordinal, size: [Math.round(origin.width), Math.round(origin.height)], origin: [origin.x, origin.y], nodes };
        }
        """;

    /// <summary>({ STYLES, targets }) — the resting styles every forced state is compared against.</summary>
    public const string Baseline = """
        function baseline({ STYLES, targets }) {
          window.__fluxBase ??= {};
          for (const id of targets) {
            const el = document.querySelector(`[data-m="${id}"]`);
            if (!el) continue;   // an editor swaps its own nodes in after it is collected: no state to compare
            window.__fluxBase[id] = Object.fromEntries(STYLES.map(k => [k, getComputedStyle(el)[k]]));
          }
        }
        """;

    /// <summary>({ STYLES, target }) — what a forced state changed on a control.</summary>
    public const string Diff = """
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
        """;

    /// <summary>
    ///     (el) — on what a finger tapped: done once it has STAYED hovered for ten frames, or after 1.5 s. That hover comes
    ///     with the mouse events the browser makes of a tap, and those land when they land: two frames after it on a Mac,
    ///     150 ms on a loaded Linux runner, where one page was measured hovered and the other not (the chevron of an open
    ///     picker). The touch itself hovers it for a moment before, and the page may lose it until they land (measured on
    ///     the Rask page: hovered, then not for 130 ms, then hovered for good), so one hovered frame is not it.
    /// </summary>
    public const string HoverSettled = """
        function hoverSettled(el) {
          return new Promise(done => {
            let frames = 0;
            let held = 0;
            const look = () => {
              held = el?.isConnected && el.matches(':hover') ? held + 1 : 0;
              if (held >= 10 || ++frames > 90) done();
              else requestAnimationFrame(look);
            };
            look();
            setTimeout(done, 1500);
          });
        }
        """;

    /// <summary>
    ///     (at) — after a finger opened a popup by a press at <c>at</c>: makes the browser hit-test the place the finger
    ///     lifted from again, and waits until it has. A tap's hover is left where the finger was, and whether a browser
    ///     looks again once the popup is up — and its page lock takes the pointer from what is behind it — is up to the
    ///     browser and to whatever lays the page out next: on Linux the kit's open date picker laid out once more 35 ms
    ///     after the tap's hover landed and lost it, Flux's did not and kept it, so Flux's chevron was measured hovered
    ///     behind its sheet and the kit's not (2026-11-04, #1260); a Mac looks again on neither. A box laid under the
    ///     finger and taken away is content changing under the pointer, which the browser answers by hit-testing it: both
    ///     pages are then read as the browser has them once anything moves. Not for a popup the pointer holds open (a
    ///     hover trigger): the box would be a place the pointer went to.
    /// </summary>
    public const string Looked = """
        function looked(at) {
          const frames = n => new Promise(done => {
            const step = () => (--n <= 0 ? done() : requestAnimationFrame(step));
            requestAnimationFrame(step);
            setTimeout(done, 500);
          });
          return (async () => {
            const box = document.createElement('div');
            box.style.cssText = `position:fixed;left:${at[0] - 2}px;top:${at[1] - 2}px;width:4px;height:4px;pointer-events:auto;z-index:2147483647`;
            document.documentElement.append(box);
            await frames(3);
            box.remove();
            await frames(3);
          })();
        }
        """;

    /// <summary>
    ///     () — on a static page standing in for one a pick redrew: lays the page out once more, away from any finger, as
    ///     the render that answers a pick does, and waits until the browser has looked again. After a layout the browser
    ///     hit-tests the place it last knew a mouse at — on a Mac where the tap's mouse events were (the row tapped, so
    ///     nothing changes), on Linux the page's corner (0,0), so the row a tap lit is left for the root and goes dark
    ///     (measured on Flux's searchable pillbox, a mouseout to &lt;html&gt; at 0,0 ten ms after its pick laid out a pill;
    ///     the kit in an app lays out the same pill). A box added off the screen and taken away is what a render is to
    ///     both: a layout to the browser, nodes added to the kit's list (rask-list.ts looks at its rows again), and no
    ///     place a pointer could be.
    /// </summary>
    public const string Redrawn = """
        function redrawn() {
          const frames = n => new Promise(done => {
            const step = () => (--n <= 0 ? done() : requestAnimationFrame(step));
            requestAnimationFrame(step);
            setTimeout(done, 500);
          });
          return (async () => {
            const box = document.createElement('div');
            box.style.cssText = 'position:fixed;left:-20px;top:-20px;width:4px;height:4px';
            document.documentElement.append(box);
            await frames(3);
            box.remove();
            await frames(3);
          })();
        }
        """;

    /// <summary>
    ///     () — after a press opened a popup: done once what a page lock writes on the root (pointer-events, overflow, the
    ///     scrollbar gutter) has held for ten frames, or after 1.5 s. Flux's script and the kit's hook put it there a task
    ///     or two after the press, sooner or later by how loaded the machine is.
    /// </summary>
    public const string Locked = """
        function locked() {
          return new Promise(done => {
            const read = () => {
              const root = getComputedStyle(document.documentElement);
              return `${root.pointerEvents} ${root.overflow} ${root.scrollbarGutter}`;
            };
            let last = read();
            let held = 0;
            let frames = 0;
            const look = () => {
              const now = read();
              held = now === last ? held + 1 : 0;
              last = now;
              if (held >= 10 || ++frames > 90) done();
              else requestAnimationFrame(look);
            };
            requestAnimationFrame(look);
            setTimeout(done, 1500);
          });
        }
        """;

    /// <summary>
    ///     () — two of the page's frames, or a quarter of a second where it draws none. A headless page left alone may paint
    ///     nothing, and a frame waited on that never comes stops the whole run without a word (0% CPU, no timeout:
    ///     Playwright puts none on an evaluate). Every frame-counting wait in the harness carries a timer the same way.
    /// </summary>
    public const string Frames = """
        () => new Promise(done => {
          requestAnimationFrame(() => requestAnimationFrame(done));
          setTimeout(done, 250);
        })
        """;

    /// <summary>() — the hover test of <see cref="Measure" />'s open: until a popover shows in the wrapper, or a second.</summary>
    public const string PopoverShown = """
        el => new Promise(done => {
          let frames = 0;
          const look = () => (el.querySelector(':popover-open') || ++frames > 60 ? done() : requestAnimationFrame(look));
          look();
          setTimeout(done, 1000);
        })
        """;
}

// A carousel (data-rask-carousel): a scroll-snapping track, and everything around it that has to know where
// the track is.
//
// The scrolling is the browser's — a track with overflow and scroll-snap — so a swipe, a wheel and the
// keyboard all work with no script. What a stylesheet cannot do is REPORT the position, and that is this:
//
//   * on a scroll of the [data-rask-carousel-track] (once per frame) and on a resize, the root and every
//     [data-direction] wrapper get data-at-start / data-at-end; each wrapper's button is disabled where there
//     is nowhere to go; the slide whose start is nearest the track's (scroll-padded) start gets data-selected,
//     and so does its button inside [data-rask-carousel-indicators], with aria-current="true";
//   * a press on a [data-direction="next" | "previous"] button scrolls to the next or previous slide — by the
//     number of slides fully in view when the root says data-advance="page" — smoothly unless data-scroll=
//     "instant"; at the end, data-wrap="rewind" goes back to the first slide and keeps "next" enabled;
//   * a press on the Nth indicator scrolls to the Nth slide;
//   * data-autoplay="<ms>" advances on that interval and rewinds at the end. The pointer over the carousel
//     stops it and the interval starts afresh when it leaves; under prefers-reduced-motion it never starts.
//     (Flux UI's carousel, measured: 3500 ms between moves, nothing for 7 s under the pointer, the next move
//     3.5 s after it left, and no move at all in 16 s with reduced motion.)
//
// Wrappers and indicators may sit outside the root, in a [data-rask-carousel-controls] whose data-name is the
// root's. A root whose buttons were all rendered disabled is left exactly as rendered.
//
// Everything written is held against the morph (rask-owned.ts): the page rendered the first slide selected
// and "previous" disabled, and a render must not put that back over where the reader has scrolled to.

import {listen, near, own, page} from "./rask-owned.js";

const ROOT = "[data-rask-carousel]";
const TRACK = "[data-rask-carousel-track]";
const CONTROLS = "[data-rask-carousel-controls]";
const DOTS = "[data-rask-carousel-indicators]";

function flag(el: Element, name: string, on: boolean, value?: string): void {
    if (on === el.hasAttribute(name)) {
        return;
    }
    if (on) {
        own(el, name);
        el.setAttribute(name, value || "");
    } else {
        // Still held: the page may have rendered it, and that render must not bring it back.
        own(el, name);
        el.removeAttribute(name);
    }
}

function slidesOf(track: Element): HTMLElement[] {
    return Array.prototype.filter.call(track.children, function (c: Element) {
        return c instanceof HTMLElement && !c.hasAttribute("data-rask-managed");
    });
}

/** Where the track has to be scrolled for each slide to sit at its start, never past the end. */
function stops(track: HTMLElement, slides: HTMLElement[]): number[] {
    const box = track.getBoundingClientRect();
    const pad = parseFloat(getComputedStyle(track).scrollPaddingLeft) || 0;
    const max = track.scrollWidth - track.clientWidth;
    return slides.map(function (slide) {
        const at = slide.getBoundingClientRect().left - box.left - track.clientLeft + track.scrollLeft - pad;
        return Math.max(0, Math.min(max, Math.round(at)));
    });
}

/** The index of the stop nearest `left`. Exported for the fixture. */
export function nearest(positions: number[], left: number): number {
    let best = 0;
    for (let i = 1; i < positions.length; i++) {
        if (Math.abs(positions[i] - left) < Math.abs(positions[best] - left)) {
            best = i;
        }
    }
    return best;
}

function partsOf(doc: Document, root: Element, selector: string): Element[] {
    const found: Element[] = Array.prototype.slice.call(root.querySelectorAll(selector));
    const name = root.getAttribute("data-name");
    if (name) {
        const controls = doc.querySelectorAll(CONTROLS);
        for (let i = 0; i < controls.length; i++) {
            if (controls[i].getAttribute("data-name") === name) {
                found.push.apply(found, Array.prototype.slice.call(controls[i].querySelectorAll(selector)));
            }
        }
    }
    return found;
}

const seen = new WeakSet<Element>();
const frozen = new WeakSet<Element>();

function sync(doc: Document, root: Element): void {
    const track = root.querySelector<HTMLElement>(TRACK);
    if (!track) {
        return;
    }
    const slides = slidesOf(track);
    const max = track.scrollWidth - track.clientWidth;
    const atStart = track.scrollLeft <= 1;
    const atEnd = track.scrollLeft >= max - 1;
    const wrappers = partsOf(doc, root, "[data-direction]");

    if (!seen.has(root)) {
        seen.add(root);
        const buttons = wrappers.map(function (w) { return w.querySelector("button"); });
        if (buttons.length && max > 1 && buttons.every(function (b) { return !!b && b.disabled; })) {
            frozen.add(root); // rendered disabled: not ours to enable
        }
    }

    flag(root, "data-at-start", atStart);
    flag(root, "data-at-end", atEnd);
    const rewinds = root.getAttribute("data-wrap") === "rewind";
    for (const wrapper of wrappers) {
        flag(wrapper, "data-at-start", atStart);
        flag(wrapper, "data-at-end", atEnd);
        const button = wrapper.querySelector("button");
        if (button && !frozen.has(root)) {
            const next = wrapper.getAttribute("data-direction") === "next";
            flag(button, "disabled", next ? atEnd && !rewinds : atStart);
        }
    }

    const selected = nearest(stops(track, slides), track.scrollLeft);
    for (let i = 0; i < slides.length; i++) {
        flag(slides[i], "data-selected", i === selected);
    }
    for (const group of partsOf(doc, root, DOTS)) {
        const dots = group.querySelectorAll("button");
        for (let i = 0; i < dots.length; i++) {
            flag(dots[i], "data-selected", i === selected);
            flag(dots[i], "aria-current", i === selected, "true");
        }
    }
}

function scrollTo(root: Element, track: HTMLElement, left: number): void {
    track.scrollTo({left, behavior: root.getAttribute("data-scroll") === "instant" ? "instant" : "smooth"} as ScrollToOptions);
}

function move(root: Element, forward: boolean): void {
    const track = root.querySelector<HTMLElement>(TRACK);
    if (!track || frozen.has(root)) {
        return;
    }
    const slides = slidesOf(track);
    const positions = stops(track, slides);
    const max = track.scrollWidth - track.clientWidth;
    if (forward && track.scrollLeft >= max - 1) {
        if (root.getAttribute("data-wrap") === "rewind") {
            scrollTo(root, track, 0);
        }
        return;
    }
    let step = 1;
    if (root.getAttribute("data-advance") === "page") {
        // The slides wholly inside the track right now.
        const box = track.getBoundingClientRect();
        step = Math.max(1, slides.filter(function (s) {
            const r = s.getBoundingClientRect();
            return r.left >= box.left - 1 && r.right <= box.right + 1;
        }).length);
    }
    const from = nearest(positions, track.scrollLeft);
    const to = Math.max(0, Math.min(slides.length - 1, from + (forward ? step : -step)));
    scrollTo(root, track, positions[to]);
}

function rootOf(doc: Document, part: Element): Element | null {
    const inside = part.closest(ROOT);
    const controls = inside ? null : part.closest(CONTROLS);
    if (!controls) {
        return inside;
    }
    const roots = doc.querySelectorAll(ROOT);
    for (let i = 0; i < roots.length; i++) {
        if (roots[i].getAttribute("data-name") === controls.getAttribute("data-name")) {
            return roots[i];
        }
    }
    return null;
}

// ----- Autoplay --------------------------------------------------------------------------------------

const timers = new WeakMap<Element, number>();

function stop(root: Element): void {
    window.clearTimeout(timers.get(root));
    timers.delete(root);
}

function play(root: Element): void {
    const ms = Number(root.getAttribute("data-autoplay"));
    stop(root);
    if (!(ms > 0) || (typeof matchMedia === "function" && matchMedia("(prefers-reduced-motion: reduce)").matches)) {
        return;
    }
    timers.set(root, window.setTimeout(function () {
        timers.delete(root);
        if (!root.isConnected) {
            return;
        }
        const track = root.querySelector<HTMLElement>(TRACK);
        if (track && track.scrollLeft >= track.scrollWidth - track.clientWidth - 1) {
            scrollTo(root, track, 0);
        } else {
            move(root, true);
        }
        play(root);
    }, ms));
}

if (page) {
    const doc = page;
    const adopt = function (root: Element): void {
        sync(doc, root);
        if (!timers.has(root) && !root.matches(":hover")) {
            play(root);
        }
    };
    const scan = function (node: Node): void {
        if (node instanceof Element) {
            if (node.matches(ROOT)) adopt(node);
            node.querySelectorAll(ROOT).forEach(adopt);
        }
    };

    // Scroll does not bubble; it is caught on the way down, and answered once per frame per track.
    const pending = new Set<Element>();
    listen("scroll", function (e) {
        const root = e.target instanceof Element && e.target.matches(TRACK) ? e.target.closest(ROOT) : null;
        if (!root || pending.has(root)) {
            return;
        }
        pending.add(root);
        requestAnimationFrame(function () {
            pending.delete(root);
            sync(doc, root);
        });
    }, {capture: true, passive: true});

    window.addEventListener("resize", function () {
        doc.querySelectorAll(ROOT).forEach(function (root) { sync(doc, root); });
    }, {passive: true});

    listen("click", function (e) {
        const button = near(e.target, "button");
        if (!button) {
            return;
        }
        const wrapper = button.closest("[data-direction]");
        const dots = wrapper ? null : button.closest(DOTS);
        const root = wrapper || dots ? rootOf(doc, (wrapper || dots)!) : null;
        if (!root || frozen.has(root)) {
            return;
        }
        if (wrapper) {
            move(root, wrapper.getAttribute("data-direction") === "next");
            return;
        }
        const track = root.querySelector<HTMLElement>(TRACK);
        const index = Array.prototype.indexOf.call(dots!.querySelectorAll("button"), button);
        if (track && index >= 0) {
            const positions = stops(track, slidesOf(track));
            if (index < positions.length) scrollTo(root, track, positions[index]);
        }
    });

    listen("pointerover", function (e) {
        const root = near(e.target, ROOT + "[data-autoplay]");
        if (root) {
            stop(root);
        }
    }, {capture: true, passive: true});
    listen("pointerout", function (e) {
        const root = near(e.target, ROOT + "[data-autoplay]");
        const to = (e as PointerEvent).relatedTarget;
        if (root && !(to instanceof Node && root.contains(to))) {
            play(root);
        }
    }, {capture: true, passive: true});

    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            for (const record of records) {
                record.addedNodes.forEach(scan);
            }
        }).observe(doc.documentElement, {subtree: true, childList: true});
        scan(doc.documentElement);
    }
}

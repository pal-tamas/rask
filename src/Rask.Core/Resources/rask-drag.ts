// A surface the pointer drags a value across (data-rask-drag).
//
// A thumb has to be under the pointer in the frame the pointer moved, and a round trip per move is neither
// that nor affordable. So the surface is the BROWSER's while it is held: the runtime captures the pointer on
// the press, and on every move writes where it is — 0 to 1 along each axis the surface names — into
// `--rask-drag-x` / `--rask-drag-y` on the surface, which is what the thumb's CSS reads. The page hears about
// it through the surface's ONE bound field (rask-bound.ts), whose value is "x y" (or the one axis): an `input`
// at most once a frame while the pointer moves, and a `change` when it lets go. When the page writes that
// field itself — a key handled in C#, a colour typed as text — the properties follow it.
//
//   data-rask-drag="x y" | "x" | "y"   the axes; 0 is the left / top edge
//   data-rask-drag-inset="<px>"        the track is that much shorter at both ends: a thumb drawn centred on
//                                      the value stays inside the surface (0 by default)
//
// The surface's `style` is the runtime's from the first value on, so a re-render does not pull the thumb back
// to where the page last heard it was. `touch-action: none` on the surface is the page's CSS to write, or a
// touch scrolls instead of dragging.

import {announce, bind, settle} from "./rask-bound.js";
import {listen, near, own, page} from "./rask-owned.js";

const SURFACE = "[data-rask-drag]";

/** Where `at` falls along a track of `size` px from `start`, shortened by `inset` at both ends: 0 to 1. */
export function dragFraction(at: number, start: number, size: number, inset: number): number {
    const span = size - 2 * inset;
    return span > 0 ? Math.max(0, Math.min(1, (at - start - inset) / span)) : 0;
}

/** A fraction as the bound field and the custom properties carry it: at most four decimals. */
export function dragText(fraction: number): string {
    return "" + Math.round(Math.max(0, Math.min(1, fraction)) * 10000) / 10000;
}

function names(surface: Element): string[] {
    const axes = surface.getAttribute("data-rask-drag") || "";
    const out: string[] = [];
    if (axes.indexOf("x") >= 0) out.push("--rask-drag-x");
    if (axes.indexOf("y") >= 0) out.push("--rask-drag-y");
    return out;
}

function shown(surface: Element): string {
    const style = (surface as HTMLElement).style;
    return names(surface).map(function (n) { return style.getPropertyValue(n); }).join(" ").trim();
}

function show(surface: Element, value: string): void {
    const parts = value.split(" ");
    const props = names(surface);
    for (let i = 0; i < props.length; i++) {
        const n = Number(parts[i]);
        if (parts[i] && isFinite(n)) {
            own(surface, "style");
            (surface as HTMLElement).style.setProperty(props[i], dragText(n));
        }
    }
}

// The surface under the pointer, from the press to the release.
let held: HTMLElement | null = null;

// A value the page writes while the surface is held is an answer to an earlier move, however it is spelled.
bind(SURFACE, shown, function (surface, rendered) {
    if (surface !== held) show(surface, rendered);
});

if (page) {
    let pending = "";
    let frame = 0;

    const tell = function (): void {
        frame = 0;
        if (held) announce(held, pending, false);
    };

    const move = function (e: PointerEvent): void {
        if (!held) {
            return;
        }
        const box = held.getBoundingClientRect();
        const inset = Number(held.getAttribute("data-rask-drag-inset")) || 0;
        const axes = held.getAttribute("data-rask-drag") || "";
        const at: string[] = [];
        if (axes.indexOf("x") >= 0) at.push(dragText(dragFraction(e.clientX, box.left, box.width, inset)));
        if (axes.indexOf("y") >= 0) at.push(dragText(dragFraction(e.clientY, box.top, box.height, inset)));
        pending = at.join(" ");
        show(held, pending);
        if (!frame) frame = requestAnimationFrame(tell);
    };

    const release = function (e: PointerEvent): void {
        if (!held) {
            return;
        }
        if (e.type === "pointerup") move(e);
        if (frame) cancelAnimationFrame(frame);
        tell();
        settle(held);
        held = null;
    };

    listen("pointerdown", function (e) {
        const surface = near(e.target, SURFACE);
        if (!surface || e.button !== 0 || surface.closest("[disabled], [aria-disabled=true]")) {
            return;
        }
        held = surface;
        try {
            surface.setPointerCapture(e.pointerId);
        } catch (err) {
            // a synthetic pointer has nothing to capture — the document listeners below still follow it
        }
        move(e);
    }, true);
    listen("pointermove", move, {capture: true, passive: true});
    listen("pointerup", release, true);
    listen("pointercancel", release, true);
}

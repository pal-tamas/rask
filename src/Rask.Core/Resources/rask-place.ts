// A panel kept inside the viewport (data-rask-place).
//
// A popover that opens against a trigger — a menu, a popover, a list of options, a calendar — says where it
// wants to be: data-rask-place="<side> <align> <gap> <offset>" ("bottom start 5 0"), beside the CSS anchor
// rules that put it there with no script. Those rules can turn it to the other side and nothing else, so a
// panel near an edge hung off the viewport. This is what Flux UI does instead, measured on its live pages with
// the trigger held at every edge of a phone and of a desktop:
//
//   1. FLIP. The side asked for, then the same side aligned the other way, then the opposite side, then that
//      aligned the other way: the first that fits the viewport — edge to edge, no padding — is taken. When none
//      does: the one whose side fits and overflows least along it, and failing that the one that overflows
//      least in all (the roomier side).
//   2. SHIFT. Along the trigger's edge the panel is slid back to stay 5 px inside the viewport; one wider than
//      the viewport keeps its start edge at 5.
//   3. SIZE. A panel taller than what is left, 5 px from the edge, is given that as its max-height and scrolls
//      inside. Then it is placed again, with the height it now has. A panel that scrolls ITSELF and, held
//      to that height, overflows by less than two pixels is let go again: Flux leaves such a one be, and a
//      column of flex items takes up to fifteen pixels that way (measured on its first popover, which hangs
//      past the 5 px, and past the edge, by that much). One whose list scrolls inside it keeps the height.
//
// THE ANCHOR is the one the panel already names: the last element whose style says `anchor-name` with the
// panel's `position-anchor`. A panel whose style names none is placed against what shows it: the first
// element of the data-rask-tooltip or data-rask-toggle that names its id, or its `popovertarget` button. And
// one nothing names sits at the pointer (--rask-context-x / -y, which the context-menu opener sets) — flipped
// and slid but never held to a height: Flux's context menu is not, and one taller than the viewport runs past
// it there too.
//
// WHAT IT WRITES is one rule per open panel in a stylesheet of its own, keyed on data-rask-placed, which it
// holds against the morph: the panel's style attribute stays the render's, a strict Content-Security-Policy
// has nothing to object to, and a closed panel is the stylesheet's and the anchor rules' again. The panel is
// `position: absolute` at page coordinates while placed, so it scrolls with the page untouched, as Flux's does.
//
// STANDING DOWN. A panel whose `--rask-place` computes to `none` is the stylesheet's alone: nothing is written
// for it, for as long as that holds. It is how a panel that hangs from its trigger on a desktop is something
// else on a phone — Flux UI's date picker is a sheet centred over the page under 640 px — with the breakpoint
// stated once, in the stylesheet: `max-sm:[--rask-place:none]`. Asked again on every pass, so a window dragged
// across the breakpoint with the panel open changes hands as it goes.
//
// WHEN: in the task that opens it and again before the next frame is drawn — a panel is never painted where
// the anchor rules alone would have left it — then whenever something scrolls, the window is resized or the
// panel or its anchor changes size, once per frame: every open panel is measured in one pass and written in
// one. Nothing is sent to the page.

import {disown, isShown, listen, near, own, page} from "./rask-owned.js";

const PLACE = "data-rask-place";
const PLACED = "data-rask-placed";
/** How far a panel stays from the viewport's edge. */
const EDGE = 5;

/** A box in viewport coordinates. */
export interface Box {
    x: number;
    y: number;
    w: number;
    h: number;
}

/** Where a panel lands in the viewport, and the height it is held to — null when it fits. */
export interface Landing {
    x: number;
    y: number;
    cap: number | null;
}

const OTHER: {[name: string]: string} = {
    top: "bottom", bottom: "top", left: "right", right: "left", start: "end", end: "start", center: "center",
};

function upright(side: string): boolean {
    return side === "top" || side === "bottom";
}

// The panel's corner for one side and alignment, before anything keeps it in view.
function corner(side: string, align: string, a: Box, w: number, h: number, gap: number, offset: number, rtl: boolean): number[] {
    const up = upright(side);
    const from = up ? a.x : a.y;
    const span = up ? a.w : a.h;
    const size = up ? w : h;
    const mirrored = up && rtl;
    const along = align === "center"
        ? from + (span - size) / 2 + (mirrored ? -offset : offset)
        : (align === "end") !== mirrored ? from + span - size - offset : from + offset;
    const across = side === "bottom" ? a.y + a.h + gap
        : side === "top" ? a.y - gap - h
            : side === "right" ? a.x + a.w + gap : a.x - gap - w;
    return up ? [along, across] : [across, along];
}

// How far a box reaches past each edge of the viewport; negative is room to spare.
function past(x: number, y: number, w: number, h: number, vw: number, vh: number): {[side: string]: number} {
    return {top: -y, bottom: y + h - vh, left: -x, right: x + w - vw};
}

// Step 1: which side and alignment the panel takes.
function fit(side: string, align: string, a: Box, w: number, h: number, gap: number, offset: number,
    vw: number, vh: number, rtl: boolean): string[] {
    const tries = align === "center"
        ? [[side, align], [OTHER[side], align]]
        : [[side, align], [side, OTHER[align]], [OTHER[side], align], [OTHER[side], OTHER[align]]];
    let sided: string[] | null = null;
    let sidedBy = Infinity;
    let least = tries[0];
    let leastBy = Infinity;
    for (const t of tries) {
        const at = corner(t[0], t[1], a, w, h, gap, offset, rtl);
        const over = past(at[0], at[1], w, h, vw, vh);
        const up = upright(t[0]);
        // The edge the alignment pushes the panel towards comes first; a trigger longer than the panel turns it.
        let first = up ? ((t[1] === "start") !== rtl ? "right" : "left") : (t[1] === "start" ? "bottom" : "top");
        if ((up ? a.w > w : a.h > h)) {
            first = OTHER[first];
        }
        const main = over[t[0]];
        const one = over[first];
        const two = over[OTHER[first]];
        if (main <= 0 && one <= 0 && two <= 0) {
            return t;
        }
        if (main <= 0 && one < sidedBy) {
            sided = t;
            sidedBy = one;
        }
        const all = Math.max(main, 0) + Math.max(one, 0) + Math.max(two, 0);
        if (all < leastBy) {
            least = t;
            leastBy = all;
        }
    }
    return sided || least;
}

/**
 * Where a panel of `w` by `natural` lands against `a` in a viewport of `vw` by `vh`. `place` is the
 * attribute's value. `free` is a panel whose height is not to be held, whatever room there is.
 */
export function landing(place: string, a: Box, w: number, natural: number,
    vw: number, vh: number, rtl: boolean, free?: boolean): Landing {
    const words = place.split(" ");
    const gap = parseFloat(words[2]) || 0;
    const offset = parseFloat(words[3]) || 0;
    let h = natural;
    let x = 0;
    let y = 0;
    let cap: number | null = null;
    // Capped, the panel is another height and may fit somewhere it did not: placed again, until it settles.
    for (let pass = 0; pass < 4; pass++) {
        const taken = fit(words[0], words[1] || "start", a, w, h, gap, offset, vw, vh, rtl);
        const at = corner(taken[0], taken[1], a, w, h, gap, offset, rtl);
        x = at[0];
        y = at[1];
        let room = vh - 2 * EDGE;
        if (upright(taken[0])) {
            x = Math.max(EDGE, Math.min(x, vw - EDGE - w));
            room = Math.min(room, taken[0] === "bottom" ? vh - EDGE - y : y + h - EDGE);
        } else {
            y = Math.max(EDGE, Math.min(y, vh - EDGE - h));
        }
        cap = !free && natural >= room ? room : null;
        const next = cap === null ? natural : Math.min(natural, cap);
        if (next === h) {
            break;
        }
        h = next;
    }
    return {x, y, cap};
}

if (page) {
    const doc = page;
    const view = window;
    /** The open panels: the number each one's rule is keyed on, and what it is placed against. */
    const open = new Map<HTMLElement, {n: number; anchor: Element | null}>();
    const HELD = "position:absolute!important;margin:0!important;translate:none!important;justify-self:auto!important;"
        + "align-self:auto!important;position-area:none!important;position-try-fallbacks:none!important;inset:";
    let sheet: CSSStyleSheet | null = null;
    let written = "";
    let count = 0;
    let queued = false;

    const sizes = typeof ResizeObserver === "function" ? new ResizeObserver(function () { queue(); }) : null;

    function write(rules: string): void {
        if (rules === written) {
            return;
        }
        if (!sheet) {
            sheet = new CSSStyleSheet();
            (doc as Document & { adoptedStyleSheets: CSSStyleSheet[] }).adoptedStyleSheets = doc.adoptedStyleSheets.concat(sheet);
        }
        sheet.replaceSync(written = rules);
    }

    function rule(n: number, inset: string, cap: number | null): string {
        return "[" + PLACED + '="' + n + '"]{' + HELD + inset + "!important"
            + (cap === null ? "" : ";max-height:" + cap + "px!important") + "}";
    }

    // The anchor the panel names, or null for the pointer.
    function anchorOf(panel: Element): Element | null {
        const named = /position-anchor:\s*(--[\w-]+)/.exec(panel.getAttribute("style") || "");
        if (!named) {
            const id = panel.id && '="' + panel.id + '"]';
            const shows = id ? doc.querySelector("[data-rask-tooltip" + id + ",[data-rask-toggle" + id) : null;
            return shows ? shows.firstElementChild || shows : id ? doc.querySelector("[popovertarget" + id) : null;
        }
        const says = new RegExp("anchor-name:[^;]*" + named[1] + "(?![\\w-])");
        const anchors = doc.querySelectorAll('[style*="anchor-name"]');
        for (let i = anchors.length - 1; i >= 0; i--) {
            if (says.test(anchors[i].getAttribute("style")!)) {
                return anchors[i];
            }
        }
        return panel; // it names one that is not there: left where the stylesheet has it
    }

    // Where the right-click that opens `panel` was. Heard here as well as read from the opener's two
    // properties: the opener (rask-dom.ts, in the runtime every page loads) pulls a menu it finds past an edge
    // back by moving the point it wrote, and Flux turns such a menu over the pointer instead — which takes the
    // point the reader pressed.
    let pressed: Box | null = null;
    let pressedFor = "";

    function pointer(panel: Element): Box | null {
        if (pressed && pressedFor === panel.id) {
            return pressed;
        }
        const root = getComputedStyle(doc.documentElement);
        const x = parseFloat(root.getPropertyValue("--rask-context-x"));
        const y = parseFloat(root.getPropertyValue("--rask-context-y"));
        return isNaN(x) || isNaN(y) ? null : {x, y, w: 0, h: 0};
    }

    function forget(panel: HTMLElement): void {
        const held = open.get(panel);
        open.delete(panel);
        panel.removeAttribute(PLACED);
        disown(panel, PLACED);
        if (sizes) {
            sizes.unobserve(panel);
            if (held && held.anchor) {
                sizes.unobserve(held.anchor);
            }
        }
    }

    // What a pass reads of the page — the viewport, the page's scroll, and each panel's anchor, box and content
    // size: when none of it has changed since the last pass left it, run() has nothing to do.
    function state(panels: HTMLElement[]): string {
        let said = doc.documentElement.clientWidth + " " + doc.documentElement.clientHeight + " " + view.scrollX + " " + view.scrollY;
        for (const panel of panels) {
            const held = open.get(panel)!;
            const at = held.anchor ? held.anchor.getBoundingClientRect() : null;
            const a = at ? {x: at.left, y: at.top, w: at.width, h: at.height} : pointer(panel);
            const box = panel.getBoundingClientRect();
            said += "|" + held.n + " " + panel.getAttribute(PLACE) + " " + (a ? a.x + " " + a.y + " " + a.w + " " + a.h : "-")
                + " " + box.left + " " + box.top + " " + box.width + " " + box.height
                + " " + panel.scrollWidth + " " + panel.scrollHeight;
        }

        return said;
    }

    /** The state the last pass left the page in. */
    let settled = "";

    function run(): void {
        queued = false;
        const panels: HTMLElement[] = [];
        open.forEach(function (_, panel) {
            if (!panel.isConnected || !isShown(panel)) {
                forget(panel);
            } else if (getComputedStyle(panel).getPropertyValue("--rask-place").trim() !== "none") {
                panels.push(panel);
                const held = open.get(panel)!;
                // A render may have drawn the trigger again as another element.
                if (held.anchor && !held.anchor.isConnected) {
                    held.anchor = anchorOf(panel);
                }
            }
        });
        // Nothing has moved since the last pass: the panels are where it left them, and placing them again is
        // not free. Measuring moves each one to the page's corner and back, and on a phone that relayout under
        // the finger cancels the hover the browser hands the trigger a moment after a tap (Chromium's
        // deferred tap, ~150 ms): the trigger lost its hover colour while its list was open, Flux's keeps it.
        // The ResizeObserver reports every panel once as it is observed, which asked for exactly such a pass.
        if (panels.length && state(panels) === settled) {
            return;
        }
        // Measured where nothing squeezes it: at the page's corner, as tall as it wants to be.
        const scrolled = panels.map(function (panel) { return panel.scrollTop; });
        write(panels.map(function (panel) { return rule(open.get(panel)!.n, "0 auto auto 0", null); }).join(""));
        const vw = doc.documentElement.clientWidth;
        const vh = doc.documentElement.clientHeight;
        const tall: (((free: boolean) => string) | null)[] = [];
        const rules = panels.map(function (panel, i) {
            const held = open.get(panel)!;
            tall[i] = null;
            const anchor = held.anchor;
            const at = anchor ? anchor.getBoundingClientRect() : null;
            const a = at ? {x: at.left, y: at.top, w: at.width, h: at.height} : pointer(panel);
            const box = panel.getBoundingClientRect();
            if (!a || anchor === panel || (at && !at.width && !at.height)) {
                return "";
            }
            const rtl = getComputedStyle(panel).direction === "rtl";
            const lands = function (free: boolean): string {
                const to = landing(panel.getAttribute(PLACE)!, a, box.width, box.height, vw, vh, rtl, free || !anchor);
                tall[i] = to.cap === null ? null : lands;
                return rule(held.n, (to.y + view.scrollY) + "px auto auto " + (to.x + view.scrollX) + "px", to.cap);
            };
            return lands(false);
        });
        write(rules.join(""));
        // Held to a height it scrolls in by a pixel or none: the height comes off (step 3).
        panels.forEach(function (panel, i) {
            const lands = tall[i];
            if (lands && panel.scrollHeight - panel.clientHeight < 2 && /auto|scroll/.test(getComputedStyle(panel).overflowY)) {
                rules[i] = lands(true);
            }
        });
        write(rules.join(""));
        // Measuring let a capped panel grow, and a box that no longer overflows forgets where it was scrolled to.
        panels.forEach(function (panel, i) {
            if (scrolled[i]) {
                panel.scrollTop = scrolled[i];
            }
        });
        settled = panels.length ? state(panels) : "";
    }

    function queue(): void {
        if (!queued && open.size) {
            queued = true;
            view.requestAnimationFrame(run);
        }
    }

    function track(panel: HTMLElement): void {
        if (!open.has(panel) && typeof CSSStyleSheet === "function" && doc.adoptedStyleSheets) {
            const anchor = anchorOf(panel);
            open.set(panel, {n: ++count, anchor});
            own(panel, PLACED);
            panel.setAttribute(PLACED, String(count));
            if (sizes) {
                sizes.observe(panel);
                if (anchor) {
                    sizes.observe(anchor);
                }
            }
        }
    }

    function placed(target: EventTarget | null): HTMLElement | null {
        return target instanceof HTMLElement && target.hasAttribute(PLACE) ? target : null;
    }

    // The same task as the open, which `toggle` is not. A popover a script shows is open by the time that
    // script is done, and is placed then; one the browser shows for a press is open by the next frame.
    listen("beforetoggle", function (e) {
        const panel = placed(e.target);
        if (panel && (e as ToggleEvent).newState === "open") {
            track(panel);
            Promise.resolve().then(function () {
                if (isShown(panel)) {
                    run();
                }
            });
            queue();
        }
    }, true);

    listen("toggle", function (e) {
        const panel = placed(e.target);
        if (!panel) {
            return;
        }
        if (isShown(panel)) {
            track(panel); // open before the hooks arrived, or opened by something that raised no beforetoggle
        } else if (open.has(panel)) {
            forget(panel);
        }
        queue();
        if (!open.size) {
            write("");
        }
    }, true);

    // The ContextMenu key and Shift+F10 come with no position: the menu opens under what has focus.
    listen("contextmenu", function (e) {
        const area = near(e.target, "[data-rask-contextmenu]");
        if (area) {
            const box = (e.target as Element).getBoundingClientRect();
            const keyed = !e.clientX && !e.clientY;
            pressed = {x: keyed ? box.left : e.clientX, y: keyed ? box.bottom : e.clientY, w: 0, h: 0};
            pressedFor = area.getAttribute("data-rask-contextmenu") || "";
        }
    }, true);

    // What scrolls inside a panel moves nothing the panel is placed against.
    listen("scroll", function (e) {
        let inside = false;
        open.forEach(function (_, panel) {
            inside = inside || (e.target instanceof Node && panel.contains(e.target));
        });
        if (!inside) {
            queue();
        }
    }, {capture: true, passive: true});
    view.addEventListener("resize", queue);

    // One that was open before the hooks arrived: a render opened it with the page.
    const waiting = doc.querySelectorAll<HTMLElement>("[" + PLACE + "]");
    for (let i = 0; i < waiting.length; i++) {
        if (isShown(waiting[i])) {
            track(waiting[i]);
        }
    }
    queue();
}

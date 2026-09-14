// The boxes the devtools draw on the inspected page: one around the nodes of the tree row under the pointer, and, while
// picking, one around whatever the pointer is over on the page.
//
// Drawn inside the devtools' own shadow root, never in the page's DOM: nothing the app renders moves, and nothing the
// diff addresses gains a sibling. The box never takes the pointer. The picker's glass does — a transparent layer over the
// page for as long as a pick lasts, so the click that picks is never a click on the app — and it finds what is under
// the pointer by asking the document, stepping past the devtools' own element.
//
// Positions come from the nodes the place resolves to, through the same path walk the runtime patches by
// (rask-dom-path.ts), and are recomputed on every scroll and resize while a box is up.

import {nodePath, relevantChild, resolvePath} from "../../../Rask.Core/Resources/rask-dom-path.js";
import {type Anchor, findAnchor, parsePlace, type Place} from "./anchors.js";

export interface Overlay {
    /** Boxes the nodes at `at` under `label`; a place that resolves to nothing clears the box instead. */
    show(at: string | null, label: string | null): void;
    /** Clears the box, unless a pick is drawing its own. */
    hide(): void;
    /** Starts, or updates, a pick against `anchors`. `onPicked` receives the chosen anchor's id. */
    pick(anchors: readonly Anchor[], onPicked: (id: string) => void, onCancelled: () => void): void;
    /** Ends a pick without choosing anything, and without reporting it. */
    stopPicking(): void;
    isPicking(): boolean;
}

export const OVERLAY_CSS =
    ".hl{position:fixed;z-index:2147483646;pointer-events:none;box-sizing:border-box;border:2px solid #8b7cf6;" +
    "background:rgba(139,124,246,.18);border-radius:2px}" +
    ".hl[hidden],.hl-label[hidden],.glass[hidden]{display:none}" +
    ".hl-label{position:fixed;z-index:2147483646;pointer-events:none;padding:2px 6px;border-radius:4px;background:#1d1b26;" +
    "color:#fff;font:600 11px/1.4 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;white-space:nowrap}" +
    ".glass{position:fixed;inset:0;z-index:2147483645;cursor:crosshair;background:transparent}";

/** The viewport box around every node of a place, or null when the place resolves to nothing visible. */
export function measure(place: Place): DOMRect | null {
    const parent = resolvePath(place.path as number[]);
    if (!parent) return null;

    let left = Infinity, top = Infinity, right = -Infinity, bottom = -Infinity;
    for (let i = 0; i < place.count; i++) {
        const node = relevantChild(parent, place.first + i);
        if (!node) break;
        const rect = rectOf(node);
        if (!rect || (rect.width === 0 && rect.height === 0)) continue;
        left = Math.min(left, rect.left);
        top = Math.min(top, rect.top);
        right = Math.max(right, rect.right);
        bottom = Math.max(bottom, rect.bottom);
    }

    return left === Infinity ? null : new DOMRect(left, top, right - left, bottom - top);
}

function rectOf(node: Node): DOMRect | null {
    if (node.nodeType === 1) return (node as Element).getBoundingClientRect();
    if (node.nodeType === 3 && typeof document.createRange === "function") {
        const range = document.createRange();
        range.selectNodeContents(node);
        return range.getBoundingClientRect();
    }
    return null;
}

/** `TaskRow · 612×44`: the node, and the size of the box drawn around it. */
export function labelText(label: string, rect: DOMRect): string {
    return label + " · " + Math.round(rect.width) + "×" + Math.round(rect.height);
}

export function installOverlay(shadow: ShadowRoot, host: Element): Overlay {
    const style = document.createElement("style");
    style.textContent = OVERLAY_CSS;
    const box = document.createElement("div");
    box.className = "hl";
    box.hidden = true;
    const tag = document.createElement("div");
    tag.className = "hl-label";
    tag.hidden = true;
    const glass = document.createElement("div");
    glass.className = "glass";
    glass.hidden = true;
    shadow.appendChild(style);
    shadow.appendChild(glass);
    shadow.appendChild(box);
    shadow.appendChild(tag);

    // What is boxed: the row's place while hovering the tree, the anchor under the pointer while picking.
    let shown: {place: Place; label: string} | null = null;
    let anchors: readonly Anchor[] = [];
    let picked: ((id: string) => void) | null = null;
    let cancelled: (() => void) | null = null;
    let underPointer: Anchor | null = null;
    let frame = 0;

    const draw = () => {
        frame = 0;
        const rect = shown ? measure(shown.place) : null;
        if (!shown || !rect) {
            box.hidden = true;
            tag.hidden = true;
            return;
        }
        box.style.left = rect.left + "px";
        box.style.top = rect.top + "px";
        box.style.width = rect.width + "px";
        box.style.height = rect.height + "px";
        box.hidden = false;
        tag.textContent = labelText(shown.label, rect);
        // Above the box, or inside its top edge when the box starts at the top of the viewport.
        tag.style.left = Math.max(0, rect.left) + "px";
        tag.style.top = (rect.top >= 22 ? rect.top - 22 : Math.max(0, rect.top) + 2) + "px";
        tag.hidden = false;
    };

    const schedule = () => {
        if (frame === 0 && typeof requestAnimationFrame === "function") {
            frame = requestAnimationFrame(draw);
        } else if (typeof requestAnimationFrame !== "function") {
            draw();
        }
    };

    // Capture, so a scroll inside any container moves the box with its content.
    window.addEventListener("scroll", () => shown && schedule(), {capture: true, passive: true});
    window.addEventListener("resize", () => shown && schedule(), {passive: true});

    const pointAt = (x: number, y: number) => {
        let target: Element | null = null;
        for (const el of document.elementsFromPoint(x, y)) {
            if (el === host || host.contains(el)) continue;
            target = el;
            break;
        }
        const path = target ? nodePath(target) : null;
        underPointer = path ? findAnchor(anchors, path) : null;
        shown = underPointer ? {place: underPointer.place, label: underPointer.label} : null;
        schedule();
    };

    glass.addEventListener("pointermove", (e: PointerEvent) => pointAt(e.clientX, e.clientY));
    glass.addEventListener("click", (e: MouseEvent) => {
        e.preventDefault();
        e.stopPropagation();
        pointAt(e.clientX, e.clientY);
        const chosen = underPointer;
        const report = picked;
        overlay.stopPicking();
        if (chosen && report) report(chosen.id);
    });

    // Esc ends a pick wherever focus is on the page; capture, so the app never sees that keystroke either.
    window.addEventListener("keydown", (e: KeyboardEvent) => {
        if (!overlay.isPicking() || e.key !== "Escape") return;
        e.preventDefault();
        e.stopImmediatePropagation();
        const report = cancelled;
        overlay.stopPicking();
        report?.();
    }, true);

    const overlay: Overlay = {
        show(at, label) {
            if (overlay.isPicking()) return;
            const place = parsePlace(at);
            shown = place ? {place, label: label ?? ""} : null;
            schedule();
        },
        hide() {
            if (overlay.isPicking()) return;
            shown = null;
            schedule();
        },
        pick(next, onPicked, onCancelled) {
            anchors = next;
            picked = onPicked;
            cancelled = onCancelled;
            glass.hidden = false;
            if (underPointer && !next.some(a => a.id === underPointer!.id)) {
                underPointer = null;
                shown = null;
                schedule();
            }
        },
        stopPicking() {
            glass.hidden = true;
            anchors = [];
            picked = null;
            cancelled = null;
            underPointer = null;
            shown = null;
            schedule();
        },
        isPicking: () => !glass.hidden,
    };
    return overlay;
}

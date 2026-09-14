// The flashes the devtools draw on the inspected page while "Flash on the page" is on, in two colours: amber around a
// component that rendered (what the panel posts after each commit), teal around a node the patch changed (what this
// module sees for itself, watching the document).
//
// Two views of one interaction, and they differ exactly where it matters: a component that rendered and changed nothing
// on the page flashes amber with no teal inside it — work that bought nothing.
//
// Drawn in the devtools' shadow root from a small pool of boxes, each fading out on its own; a burst past the pool drops
// the rest rather than piling up. Positions are taken once, when the flash starts: a flash is too short to follow a scroll.

import {parsePlace} from "./anchors.js";
import {measure} from "./overlay.js";

/** Boxes on screen at once, both colours together. */
export const FLASH_POOL = 64;

/** How long a flash takes to fade, in milliseconds. */
export const FLASH_MS = 1000;

// The same colours as the Renders tab's legend (DevToolsRendersTab.RenderColour / DomColour). A render box is an outline
// only: components nest, and a fill per level would bury the page under its layout. A change is filled, lightly, since
// it is the leaf that matters.
export const FLASH_CSS =
    ".fl{position:fixed;z-index:2147483645;pointer-events:none;box-sizing:border-box;border-radius:2px;opacity:0}" +
    ".fl[hidden]{display:none}" +
    ".fl-render{border:2px solid #f59e0b}" +
    ".fl-dom{border:2px solid #14b8a6;background:rgba(20,184,166,.18)}" +
    ".fl-label{position:absolute;left:0;top:-18px;padding:0 4px;border-radius:3px;background:#f59e0b;color:#1d1b26;" +
    "font:600 10px/16px ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;white-space:nowrap}";

export interface Flash {
    /** Flashes the components one commit rendered, as `[at, label]` pairs. Ignored while off. */
    renders(boxes: readonly (readonly [string, string])[]): void;
    /** Starts or stops flashing: DOM changes are watched only while on. */
    setEnabled(on: boolean): void;
    isEnabled(): boolean;
}

/** The elements a batch of mutations changed, for the DOM flash: each at most once, never the devtools' own. */
export function changedElements(mutations: readonly MutationRecord[], host: Element): Element[] {
    const seen = new Set<Element>();
    const root = document.documentElement;
    for (const m of mutations) {
        const target = m.target.nodeType === 1 ? m.target as Element : m.target.parentElement;
        if (!target || target === host || host.contains(target)) continue;
        // The document and body toggle attributes of their own while a page is busy (inert, aria-busy); a whole-page box
        // for that would drown out everything else.
        if (m.type === "attributes" && (target === root || target === document.body)) continue;
        if (target.closest("head")) continue;

        if (m.type === "childList" && m.addedNodes.length > 0) {
            let added = false;
            m.addedNodes.forEach(node => {
                if (node === host) return;
                if (node.nodeType === 1) {
                    seen.add(node as Element);
                    added = true;
                }
            });
            // Text added, or only removals: the container is what changed.
            if (!added && target !== root) seen.add(target);
        } else if (target !== root) {
            seen.add(target);
        }
    }
    return [...seen];
}

export function installFlash(shadow: ShadowRoot, host: Element): Flash {
    const style = document.createElement("style");
    style.textContent = FLASH_CSS;
    shadow.appendChild(style);

    const free: HTMLDivElement[] = [];
    let live = 0;
    let enabled = false;
    let observer: MutationObserver | null = null;

    const take = (): HTMLDivElement | null => {
        if (free.length > 0) return free.pop()!;
        if (live >= FLASH_POOL) return null;
        live++;
        const box = document.createElement("div");
        box.hidden = true;
        shadow.appendChild(box);
        return box;
    };

    const release = (box: HTMLDivElement) => {
        box.hidden = true;
        box.textContent = "";
        free.push(box);
    };

    const draw = (rect: DOMRect, kind: "render" | "dom", label: string | null) => {
        if (rect.width === 0 && rect.height === 0) return;
        const box = take();
        if (!box) return;
        box.className = "fl fl-" + kind;
        box.style.left = rect.left + "px";
        box.style.top = rect.top + "px";
        box.style.width = rect.width + "px";
        box.style.height = rect.height + "px";
        if (label) {
            const tag = document.createElement("span");
            tag.className = "fl-label";
            tag.textContent = label;
            box.appendChild(tag);
        }
        box.hidden = false;
        if (typeof box.animate === "function") {
            const fade = box.animate([{opacity: 1}, {opacity: 1, offset: 0.5}, {opacity: 0}], {duration: FLASH_MS});
            fade.onfinish = () => release(box);
            fade.oncancel = () => release(box);
        } else {
            box.style.opacity = "1";
            setTimeout(() => {
                box.style.opacity = "0";
                release(box);
            }, FLASH_MS);
        }
    };

    const onMutations = (mutations: MutationRecord[]) => {
        for (const el of changedElements(mutations, host)) {
            draw(el.getBoundingClientRect(), "dom", null);
        }
    };

    const flash: Flash = {
        renders(boxes) {
            if (!enabled) return;
            for (const [at, label] of boxes) {
                const place = parsePlace(at);
                const rect = place ? measure(place) : null;
                if (rect) draw(rect, "render", label);
            }
        },
        setEnabled(on) {
            if (on === enabled) return;
            enabled = on;
            if (on && typeof MutationObserver === "function") {
                observer = new MutationObserver(onMutations);
                observer.observe(document.documentElement, {
                    subtree: true,
                    childList: true,
                    attributes: true,
                    characterData: true,
                });
            } else if (!on) {
                observer?.disconnect();
                observer = null;
            }
        },
        isEnabled: () => enabled,
    };
    return flash;
}

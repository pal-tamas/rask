// A control for something this browser may not have (data-rask-requires).
//
// A render cannot know whether the reader's browser has an EyeDropper: on the Server host it runs somewhere
// else, and a page rendered ahead of time runs before there is a browser at all. So the control names the
// global it needs — `data-rask-requires="EyeDropper"` — and when it arrives the runtime sets `hidden` on it
// where that global is missing and takes `hidden` off where it is there. Flux UI's colour picker does exactly
// that with its eyedropper button: it is not disabled where the API is missing, it is not there.
// Rendering the control `hidden` is therefore the safe default: it shows only where it can work, and never
// flashes first.
//
// The name is looked up as a property of the window and nothing else: an identifier, never evaluated.
// `hidden` is the runtime's from then on; a control the page ALSO wants hidden drops the attribute while
// that holds.

import {own, page} from "./rask-owned.js";

const ATTR = "data-rask-requires";

/** Whether `name` is a plain identifier and `scope` has a property by that name. */
export function provides(scope: object, name: string | null): boolean {
    return !!name && /^[A-Za-z_$][A-Za-z0-9_$]*$/.test(name) && name in scope;
}

function gate(el: Element): void {
    own(el, "hidden");
    if (provides(window, el.getAttribute(ATTR))) {
        el.removeAttribute("hidden");
    } else {
        el.setAttribute("hidden", "");
    }
}

if (page && typeof MutationObserver === "function") {
    new MutationObserver(function (records) {
        for (const record of records) {
            if (record.type === "attributes") {
                const t = record.target as Element;
                if (t.hasAttribute(ATTR)) gate(t);
                continue;
            }
            record.addedNodes.forEach(function (n) {
                if (n instanceof Element) {
                    if (n.hasAttribute(ATTR)) gate(n);
                    n.querySelectorAll("[" + ATTR + "]").forEach(gate);
                }
            });
        }
    }).observe(page.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: [ATTR]});
    page.querySelectorAll("[" + ATTR + "]").forEach(gate);
}

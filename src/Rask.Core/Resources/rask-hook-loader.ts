// The behaviour hooks load on demand.
//
// What an element asks of the runtime by carrying an attribute (docs/js-interop-runtime.md, "Behaviour hooks")
// is a third of the runtime, and a page that carries none of those attributes used to download all of it. The
// hooks are a bundle of their own now (rask-hooks.ts -> rask-hooks.js), and what every page loads is this
// file: the list of attributes that ask for a hook, one look at the page, and one observer that stops looking
// the moment it has found one.
//
// FOUR THINGS HAVE TO HOLD.
//
//  1. ONCE. The bundle is asked for at most once per document, and not at all when the page already carries
//     its tag: the Server host writes one after the runtime's own for a page it rendered WITH hooks, so on a
//     first visit they run where they always ran, straight after the runtime.
//  2. THE SAME PLACE IN LINE. The hooks used to be imported between the runtime's shared listeners and the
//     host's own, and two contracts lean on that: a field a `data-rask-clear` button empties is announced
//     BEFORE the click reaches the page's handler, and a click a hook cancelled is not sent at all. A listener
//     added a round trip later would be last. So a place is kept, here, at import time, for the three events
//     where it matters, and a hook that arrives late takes it (`listen` in rask-owned.ts).
//  3. NOTHING LOST ON THE WAY. Between "an attribute was seen" and "the bundle ran" a reader can hover, press
//     or type. Those events are kept (a bounded few) and handed to the hooks, and only to the hooks, when they
//     arrive.
//  4. NOTHING AT IDLE. One MutationObserver, filtered to the attributes below, on a page that has no hooks —
//     where eight unfiltered ones used to run — and none at all once the bundle has been asked for.

import {page, seam} from "./rask-owned.js";

/**
 * Every attribute that asks for a hook. An element carrying one is what loads the bundle; an attribute a hook
 * only reads ON or INSIDE such an element (`data-rask-hover-if`, `data-rask-segment`, `data-rask-plot-row`)
 * is not listed. `HookBundleContractTests` fails for a hook module that names an attribute neither listed
 * here nor declared there as one of those.
 */
export const HOOK_ATTRIBUTES: string[] = (
    // Each `data-rask-…`, written without the prefix: every page downloads this list.
    "tooltip hover"                                               // rask-hover
    + " modal modal-open"                                         // rask-overlay
    + " lock"                                                     // rask-lock
    + " menu-pointer safe-area"                                   // rask-menu
    + " copy clear clear-keys focus mask mask-money big-step"     // rask-field
    + " contain-keys listbox-button roving"                       // rask-keys
    + " focus-follows press-keeps-focus"                          // rask-focus
    + " toggle"                                                   // rask-toggle
    + " drag"                                                     // rask-drag
    + " requires"                                                 // rask-requires
    + " plot measure"                                             // rask-plot
    + " otp"                                                      // rask-otp
    + " segments"                                                 // rask-segments
    + " dismiss-scope stack"                                      // rask-toast
    + " confirm-leave"                                            // rask-leave
    + " persist uncheck-on-navigate"                              // rask-persist
    + " carousel carousel-controls"                               // rask-carousel
).split(" ").map(function (name) { return "data-rask-" + name; }).concat(
    // And the ones the platform named: any popover (rask-overlay closes it when focus leaves), an invoker
    // command (the fallback), a switch (rask-field), a listbox's active option (rask-focus).
    "popover commandfor role aria-activedescendant".split(" "));

// `role` asks for a hook on one element only: a checkbox that is a switch (Enter toggles it).
const SELECTOR = HOOK_ATTRIBUTES
    .map(function (name) { return name === "role" ? "input[role=switch]" : "[" + name + "]"; })
    .join(",");

/** The mark on the bundle's own <script>, whoever wrote it: the Server host into the page, or `load` below. */
const TAG = "data-rask-hooks";

// What a reader can do to a hooked element before its hook is there. Not the ones that fire per pixel
// (pointermove, scroll): a drag or a chart's cursor picks up at the next one.
const KEPT = [
    "pointerover", "pointerout", "pointerdown", "pointerup", "mousedown", "click", "focusin", "focusout",
    "keydown", "beforeinput", "input", "change", "paste", "beforetoggle", "toggle", "close",
];
const KEPT_AT_MOST = 64;

function keep(e: Event): void {
    const missed = seam.missed;
    if (missed && missed.length < KEPT_AT_MOST) {
        missed.push(e);
    }
}

function reserve(target: EventTarget, type: string): void {
    const place: ((e: Event) => void)[] = seam.reserved[type] = [];
    target.addEventListener(type, function (e) {
        for (let i = 0; i < place.length; i++) {
            place[i](e);
        }
    });
}

// At IMPORT time, which is where the hooks' own listeners used to be added: after the shared modules', before
// the host's.
if (page) {
    reserve(page, "click");
    reserve(page, "change");
    reserve(window, "popstate");
}

let asked = false;

function load(doc: Document, url: string, nonce: string | undefined): void {
    asked = true;
    if (doc.querySelector("script[" + TAG + "]")) {
        return; // the page came with it
    }
    seam.missed = [];
    for (const type of KEPT) {
        doc.addEventListener(type, keep, true);
    }
    const done = function (): void {
        for (const type of KEPT) {
            doc.removeEventListener(type, keep, true);
        }
        seam.missed = null; // the bundle took them as it ran; after a failed load there is nobody to hand them to
    };
    const script = doc.createElement("script");
    script.src = url;
    script.setAttribute(TAG, "");
    // The morph owns <head>; this is how a node tells it "not yours" (rask-morph.ts).
    script.setAttribute("data-rask-managed", "");
    if (nonce) {
        script.nonce = nonce;
    }
    script.onload = done;
    script.onerror = done;
    doc.head.appendChild(script);
}

function asks(node: Node): boolean {
    return node instanceof Element && (node.matches(SELECTOR) || node.querySelector(SELECTOR) !== null);
}

/**
 * Loads the hooks bundle from `url` when the page first carries an attribute that asks for a hook: now, if it
 * already does, and otherwise when a render, a navigation or a script adds one. `nonce` is the runtime's own
 * script nonce, for a page whose policy admits scripts by nonce.
 */
export function loadHooksOnDemand(url: string, nonce?: string): void {
    if (!page || asked) {
        return;
    }
    const doc = page;
    if (doc.readyState === "loading") {
        // The Server host's runtime runs while the page is still being read, and the tag the server wrote for
        // the hooks comes AFTER the runtime's own: it is not in the document yet. Looking now would miss it
        // and ask for the bundle a second time, so the look waits for the end of the page.
        doc.addEventListener("DOMContentLoaded", function () { loadHooksOnDemand(url, nonce); }, {once: true});
        return;
    }
    if (doc.querySelector(SELECTOR) || typeof MutationObserver !== "function") {
        load(doc, url, nonce);
        return;
    }
    const watcher = new MutationObserver(function (records) {
        for (const record of records) {
            const found = record.type === "attributes"
                ? asks(record.target)
                : Array.prototype.some.call(record.addedNodes, asks);
            if (found) {
                watcher.disconnect();
                load(doc, url, nonce);
                return;
            }
        }
    });
    watcher.observe(doc.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: HOOK_ATTRIBUTES});
}

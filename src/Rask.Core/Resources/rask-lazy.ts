// Load when in view (data-rask-lazy): Livewire's lazy component.
//
// `Lazy.Placeholder(…)[Widget]` renders its placeholder inside an element carrying data-rask-lazy and a
// data-rask-on-load handler, and mounts the widget only once that handler is heard. This hook raises it: the
// first time any part of the element is in the viewport, a `load` is dispatched on it — the element's content is
// to be loaded now — and the runtime sends it as it sends every `load`, as nobody's action (rask-events.ts): no
// bound value goes ahead of it. Once per handler: an element the morph reuses for another lazy is watched again.

import {page, unasked} from "./rask-owned.js";

const LAZY = "data-rask-lazy";
const ON_LOAD = "data-rask-on-load";

if (page && typeof IntersectionObserver === "function" && typeof MutationObserver === "function") {
    // The handler each element was last raised for: a render that gives it another one asks again.
    const raised = new WeakMap<Element, string | null>();

    const view = new IntersectionObserver(function (entries) {
        for (const entry of entries) {
            const el = entry.target;
            if (!entry.isIntersecting || !el.isConnected || !el.hasAttribute(LAZY)) {
                continue;
            }
            view.unobserve(el);
            raised.set(el, el.getAttribute(ON_LOAD));
            unasked(function () { el.dispatchEvent(new Event("load")); });
        }
    });

    const watch = function (el: Element): void {
        if (el.hasAttribute(LAZY) && (!raised.has(el) || raised.get(el) !== el.getAttribute(ON_LOAD))) {
            raised.delete(el);
            view.observe(el);
        }
    };

    const arrived = function (node: Node): void {
        if (node instanceof Element) {
            watch(node);
            node.querySelectorAll("[" + LAZY + "]").forEach(watch);
        }
    };

    new MutationObserver(function (records) {
        for (const record of records) {
            if (record.type === "attributes") {
                watch(record.target as Element);
            } else {
                record.addedNodes.forEach(arrived);
            }
        }
    }).observe(page.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: [LAZY, ON_LOAD]});

    page.querySelectorAll("[" + LAZY + "]").forEach(watch);
}

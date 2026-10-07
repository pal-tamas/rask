// A checkbox whose state is the READER's rather than the page's: kept across visits, or dropped on navigation.
//
// A sidebar that collapses, a drawer that opens over the page on a phone — both are a checkbox and a
// stylesheet, with no handler and no round trip. Two things about them need the runtime:
//
//   * data-rask-persist="<key>": the box is checked or not as the reader last left it, read from
//     localStorage[key] ("true" / "false") the moment the box arrives and written on every change. Flux UI's
//     sidebar keeps its collapsed state the same way (flux-sidebar-collapsed-desktop). From then on the state
//     is not a render's to reset — the server never knew it — so the morph leaves `checked` alone on that box.
//     This runs when the runtime does, which on a server-rendered page is before the first paint in practice
//     and in a WebAssembly app is after the app has booted: a page that must not flash on a cold load restores
//     the same key from a script of its own in <head>, and this then agrees with it.
//   * data-rask-uncheck-on-navigate: the box is unchecked when the app navigates to another path, so a drawer
//     opened to reach a link is closed on the page the link leads to. A client-side navigation replaces the
//     content, not the document, and the box would otherwise stay as it was.
//
// localStorage can throw (a private window, a blocked origin); every touch of it is guarded and a failure
// means the box behaves like any other.

import {listen, own, page, seam} from "./rask-owned.js";

const PERSIST = "data-rask-persist";
const UNCHECK = "[data-rask-uncheck-on-navigate]";

function read(key: string): string | null {
    try {
        return window.localStorage.getItem(key);
    } catch (e) {
        return null;
    }
}

function restore(box: Element): void {
    const key = box.getAttribute(PERSIST);
    if (!key || !(box instanceof HTMLInputElement)) {
        return;
    }
    own(box, "checked");
    const stored = read(key);
    if (stored === "true" || stored === "false") {
        box.checked = stored === "true";
    }
}

function scan(root: Element): void {
    if (root.hasAttribute(PERSIST)) restore(root);
    root.querySelectorAll("[" + PERSIST + "]").forEach(restore);
}

if (page) {
    const doc = page;

    listen("change", function (e) {
        const box = e.target;
        const key = box instanceof HTMLInputElement ? box.getAttribute(PERSIST) : null;
        if (!key) {
            return;
        }
        try {
            window.localStorage.setItem(key, (box as HTMLInputElement).checked ? "true" : "false");
        } catch (e2) {
            // not stored: it is still checked for this visit
        }
    }, true);

    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            for (const record of records) {
                record.addedNodes.forEach(function (n) {
                    if (n instanceof Element) scan(n);
                });
            }
        }).observe(doc.documentElement, {subtree: true, childList: true});
        scan(doc.documentElement);
    }

    // A navigation inside the app is a history entry with another path. Both hosts make one with pushState
    // (replaceState for a redirect), and Back and Forward arrive as popstate — so those three are the signal,
    // read here rather than threaded through each host's navigation code.
    let path = location.pathname;
    const navigated = function (): void {
        if (location.pathname === path) {
            return; // the query or the fragment moved: the same page
        }
        path = location.pathname;
        const boxes = doc.querySelectorAll<HTMLInputElement>(UNCHECK);
        for (let i = 0; i < boxes.length; i++) {
            if (boxes[i].checked) {
                boxes[i].checked = false;
                boxes[i].dispatchEvent(new Event("change", {bubbles: true}));
            }
        }
    };
    const wrap = function (name: "pushState" | "replaceState"): void {
        const original = history[name];
        history[name] = function (this: History) {
            const result = original.apply(this, arguments as unknown as Parameters<History["pushState"]>);
            navigated();
            return result;
        };
    };
    if (typeof history !== "undefined" && typeof history.pushState === "function") {
        wrap("pushState");
        wrap("replaceState");
        // Ahead of the host's own popstate listener, as it was when the hooks were part of the runtime.
        const place = seam.reserved.popstate;
        if (place) place.push(navigated); else window.addEventListener("popstate", navigated);
    }
}

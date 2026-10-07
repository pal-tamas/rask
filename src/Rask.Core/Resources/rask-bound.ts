// The ONE bound field of a group the browser owns.
//
// A control whose parts move faster than a round trip — one-time-code cells, the segments of a typed date, a
// thumb under the pointer, a box being resized — is the BROWSER's while the reader is in it: its parts are
// rendered with no value and no handler, and the group holds one <input type="hidden"> that the page binds
// the usual way. A hook keeps that field equal to what the group shows and announces it with `input` and
// `change`; when the page changes the field itself, the group follows.
//
// THE LATE ECHO. The page's answer to each value announced comes back as the hidden field's `value`
// attribute, and it comes back LATE: the echo of "12" arrives when the group already says "1234". An echo is
// therefore recognised (it is one of the values still waiting) and ignored, and the field is put back to what
// the group says. Anything else in that attribute is the page changing the value, and the group adopts it.

import {page, seam} from "./rask-owned.js";

interface Kind {
    selector: string;
    /** What the group shows now, as the bound field would carry it. */
    current(group: Element): string;
    /** The page wrote `rendered` into the bound field (or the group just arrived with it): show it. */
    adopt(group: Element, rendered: string): void;
}

const kinds: Kind[] = [];
const sent = new WeakMap<Element, string[]>();
let watcher: MutationObserver | null = null;

/** The group's bound field. */
export function boundOf(group: Element): HTMLInputElement | null {
    return group.querySelector<HTMLInputElement>("input[type=hidden]");
}

// A hidden input's `value` IS its attribute, so writing it here looks, to the observer below, exactly like the
// page writing it. The records this write caused are taken back out before the observer is handed them.
function write(bound: HTMLInputElement, value: string): void {
    if (bound.value === value) {
        return;
    }
    // What the page wrote and the observer has not been handed yet is sorted out FIRST, while the attribute
    // still says what the page wrote. A record names the attribute, not its value: read after the write
    // below, a late echo of "1" would look like the page setting the value to what was just typed — every
    // echo still waiting would be forgotten, and the next one to arrive would take the group back.
    if (watcher) {
        observed(watcher.takeRecords());
    }
    bound.value = value;
    if (watcher) {
        watcher.takeRecords();
    }
}

/**
 * Writes `value` into the group's bound field and tells the page: `input`, and `change` too unless the value
 * is still moving (`settled` false — a drag in flight). Nothing when the field already says so.
 */
export function announce(group: Element, value: string, settled: boolean = true): void {
    const bound = boundOf(group);
    if (!bound || bound.value === value) {
        return;
    }
    write(bound, value);
    let waiting = sent.get(bound);
    if (!waiting) {
        sent.set(bound, waiting = []);
    }
    waiting.push(value);
    if (waiting.length > 64) {
        waiting.shift(); // a page that never answers (it binds nothing back) must not grow this for ever
    }
    bound.dispatchEvent(new Event("input", {bubbles: true}));
    if (settled) {
        settle(group);
    }
}

/** Tells the page the value announced last is the final one: a `change` on the bound field. */
export function settle(group: Element): void {
    const bound = boundOf(group);
    if (bound) {
        bound.dispatchEvent(new Event("change", {bubbles: true}));
    }
}

function refill(group: Element, kind: Kind): void {
    const bound = boundOf(group);
    if (!bound) {
        return;
    }
    const rendered = bound.getAttribute("value") || "";
    const waiting = sent.get(bound);
    const echo = waiting ? waiting.indexOf(rendered) : -1;
    if (waiting && echo >= 0) {
        waiting.splice(0, echo + 1);
        write(bound, kind.current(group));
        return;
    }
    if (waiting) {
        waiting.length = 0;
    }
    if (kind.current(group) !== rendered) {
        kind.adopt(group, rendered);
    }
    write(bound, kind.current(group));
}

// ONCE per group, however many records name it. A record says the attribute changed, not what it changed to,
// and one render writes a field twice (the attribute, then the property, which on a hidden input is the same
// attribute): the second record would be read after the first one's answer — the group's own value, put back
// over the echo — and that value is waiting too, so it would pass for ITS echo, and every echo before it be
// forgotten. The next one to arrive would then take the group back.
function observed(records: MutationRecord[]): void {
    const seen = new Map<Kind, Set<Element>>();
    const once = function (group: Element, kind: Kind): void {
        let groups = seen.get(kind);
        if (!groups) {
            seen.set(kind, groups = new Set<Element>());
        }
        if (!groups.has(group)) {
            groups.add(group);
            refill(group, kind);
        }
    };
    const arrived = function (el: Element): void {
        for (const kind of kinds) {
            if (el.matches(kind.selector)) once(el, kind);
            el.querySelectorAll(kind.selector).forEach(function (g) { once(g, kind); });
        }
    };
    for (const record of records) {
        if (record.type === "attributes") {
            const t = record.target as Element;
            if (t instanceof HTMLInputElement && t.type === "hidden") {
                for (const kind of kinds) {
                    const group = t.closest(kind.selector);
                    if (group && boundOf(group) === t) once(group, kind);
                }
            }
            continue;
        }
        record.addedNodes.forEach(function (n) {
            if (n instanceof Element) arrived(n);
        });
    }
}

/** Registers a kind of group: from now on its bound field is watched, and every group on the page adopted. */
export function bind(selector: string, current: Kind["current"], adopt: Kind["adopt"]): void {
    const kind: Kind = {selector, current, adopt};
    kinds.push(kind);
    if (!page || typeof MutationObserver !== "function") {
        return;
    }
    if (!watcher) {
        // The page changed a value: a hidden field's `value` attribute moved, or a group just arrived.
        watcher = new MutationObserver(observed);
        watcher.observe(page.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: ["value"]});
    }
    // The hooks can arrive after the group did (they load on demand: rask-hook-loader.ts), and a reader may
    // have typed into it by then. What they typed is about to be announced, from the events kept for that
    // (`replayMissed`), so a group the page gave no value is left as the reader has it rather than emptied.
    const late = seam.missed !== null;
    page.querySelectorAll(selector).forEach(function (g) {
        const bound = boundOf(g);
        if (!late || (bound && bound.getAttribute("value"))) {
            refill(g, kind);
        }
    });
}

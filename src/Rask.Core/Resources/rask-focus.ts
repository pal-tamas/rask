// Where focus goes when a render moves the thing it was on, and where it must not go at all.
//
//   * data-rask-focus-follows on a container, data-rask-focus-target on the ONE element inside it that should
//     hold focus (a calendar's tab stop). When a render moves the target mark while focus is on the element
//     that carried it — or removes that element with the mark reappearing on a new one — the new target is
//     focused. Focus anywhere else is never taken: not from another control inside the container, not from
//     the rest of the page. A render that leaves NO target lets focus fall where the browser drops it, which
//     is how Flux UI's calendar behaves on Home, End, PageUp and PageDown (focus ends on <body>), while its
//     arrow keys — across a month boundary too — carry focus to the new day.
//   * aria-activedescendant on a role="combobox" or role="listbox": when it changes, the option it names is
//     brought into view inside its own scroller, by the least movement, and nothing else scrolls — not the
//     page. (A tree's is rask-dom.ts's, which also knows about virtualized rows.)
//   * data-rask-press-keeps-focus: a mouse press on it (or inside it) does not move focus. Flux UI's colour
//     area is dragged with focus left wherever it was.

import {near, page} from "./rask-owned.js";

const FOLLOWS = "[data-rask-focus-follows]";
const TARGET = "data-rask-focus-target";

/**
 * How far a scroller must move for [top, bottom] to be inside [viewTop, viewBottom]: negative is up, 0 when it
 * already is. Something taller than the view is aligned to its top.
 */
export function scrollBy(top: number, bottom: number, viewTop: number, viewBottom: number): number {
    if (top < viewTop || bottom - top > viewBottom - viewTop) {
        return top - viewTop;
    }
    return bottom > viewBottom ? bottom - viewBottom : 0;
}

function scrollerOf(el: Element): HTMLElement | null {
    for (let up = el.parentElement; up && up !== page!.body && up !== page!.documentElement; up = up.parentElement) {
        const overflow = getComputedStyle(up).overflowY;
        if ((overflow === "auto" || overflow === "scroll") && up.scrollHeight > up.clientHeight) {
            return up;
        }
    }
    return null;
}

function reveal(option: Element): void {
    const scroller = scrollerOf(option);
    if (!scroller) {
        return;
    }
    const box = option.getBoundingClientRect();
    const top = scroller.getBoundingClientRect().top + scroller.clientTop;
    const by = scrollBy(box.top, box.bottom, top, top + scroller.clientHeight);
    if (by) {
        scroller.scrollTop += by;
    }
}

if (page) {
    const doc = page;
    // The target that has focus, while it has it. Null whenever focus is anywhere else.
    let from: Element | null = null;

    doc.addEventListener("focusin", function (e) {
        const t = e.target;
        from = t instanceof Element && t.hasAttribute(TARGET) && near(t, FOLLOWS) ? t : null;
    }, true);

    // Focus left for nowhere. If a render removed the target, the observer below has already answered by the
    // time this timer runs; if the reader pressed on nothing, it is theirs to leave.
    doc.addEventListener("focusout", function (e) {
        const was = from;
        if (was && !(e.relatedTarget instanceof Node)) {
            setTimeout(function () {
                if (from === was && doc.activeElement !== was) {
                    from = null;
                }
            }, 0);
        }
    }, true);

    doc.addEventListener("mousedown", function (e) {
        if (near(e.target, "[data-rask-press-keeps-focus]")) {
            e.preventDefault();
        }
    }, true);

    function follow(): void {
        const was = from;
        if (!was) {
            return;
        }
        const container = was.isConnected ? was.closest(FOLLOWS) : null;
        // A target taken out of the page has no container to ask; the one that follows it is looked for among
        // those the page has now.
        const candidates = container ? [container] : Array.from(doc.querySelectorAll(FOLLOWS));
        let target: HTMLElement | null = null;
        for (const c of candidates) {
            const found = c.querySelector<HTMLElement>("[" + TARGET + "]");
            if (found && found.closest(FOLLOWS) === c) {
                target = found;
                break;
            }
        }
        const active = doc.activeElement;
        const lost = !was.isConnected && (!active || active === doc.body);
        if (!target) {
            if (!was.isConnected) {
                from = null;
            }
            return;
        }
        if (target !== was && (active === was || lost)) {
            target.focus();
        }
    }

    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            let moved = false;
            for (const record of records) {
                if (record.type !== "attributes" || record.attributeName === TARGET) {
                    moved = true;
                    continue;
                }
                const box = record.target;
                const role = box instanceof Element ? box.getAttribute("role") : null;
                const id = role === "combobox" || role === "listbox" ? (box as Element).getAttribute("aria-activedescendant") : null;
                const option = id ? doc.getElementById(id) : null;
                if (option) {
                    reveal(option);
                }
            }
            if (moved && from) {
                follow();
            }
        }).observe(doc.documentElement, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: [TARGET, "aria-activedescendant"],
        });
    }
}

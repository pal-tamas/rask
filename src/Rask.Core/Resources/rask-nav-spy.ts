// The section being read (data-rask-nav-spy): a NavLink to a section of its own page lights while it is read.
//
// A NavLink whose Href names a section — `/#contact` — cannot be marked active by the server: the fragment never
// reaches it. It carries data-rask-nav-spy, holding its active class, and this hook marks it while its section is
// the one being read on the page it points at: the class gets those tokens, and it says aria-current="true" (a
// part of the page, not the page itself).
//
// The section being read is the one whose top has passed a line a third of the way down the viewport most
// recently; at the very bottom of the page, the lowest one that shows, since a short closing section never
// reaches the line. Above the first section none is: the page's own link says where the reader is.
//
// The hook holds the class and aria-current only while it has marked them (rask-owned.ts), so a render around
// a marked link does not strip the mark, and an unmarked link is the render's alone.

import {disown, own, page} from "./rask-owned.js";

const SPY = "data-rask-nav-spy";

/** The element `a` points at, when it names a section of the page being shown. */
function sectionOf(doc: Document, a: HTMLAnchorElement): Element | null {
    const url = new URL(a.href, doc.baseURI);
    if (url.hash.length < 2 || url.origin !== doc.location.origin || url.pathname !== doc.location.pathname) {
        return null;
    }
    return doc.getElementById(decodeURIComponent(url.hash.slice(1)));
}

/** Of `sections`, the one being read; null above the first. */
function reading(doc: Document, sections: Element[]): Element | null {
    const view = doc.defaultView;
    const height = view ? view.innerHeight : 0;
    const root = doc.scrollingElement || doc.documentElement;
    const atBottom = root.scrollTop + root.clientHeight >= root.scrollHeight - 1;
    const line = atBottom ? height : height / 3;
    let best: Element | null = null;
    let bestTop = -Infinity;
    for (const section of sections) {
        const top = section.getBoundingClientRect().top;
        if (top <= line && top > bestTop) {
            best = section;
            bestTop = top;
        }
    }
    return best;
}

function mark(a: HTMLAnchorElement, on: boolean): void {
    const tokens = (a.getAttribute(SPY) || "").split(/\s+/).filter(Boolean);
    if (on) {
        own(a, "class");
        own(a, "aria-current");
        a.classList.add(...tokens);
        a.setAttribute("aria-current", "true");
    } else if (a.getAttribute("aria-current") === "true") {
        a.classList.remove(...tokens);
        a.removeAttribute("aria-current");
        disown(a, "class");
        disown(a, "aria-current");
    }
}

// A host with a document but no window to listen on — a test's stand-in DOM — has no reading to follow.
const view = page ? page.defaultView : null;
if (page && view) {
    const doc = page;
    let queued = false;

    const update = function (): void {
        queued = false;
        const links: [HTMLAnchorElement, Element | null][] = [];
        doc.querySelectorAll<HTMLAnchorElement>("a[" + SPY + "]").forEach(function (a) {
            links.push([a, sectionOf(doc, a)]);
        });
        if (links.length === 0) {
            return;
        }
        const sections = links.map(function (link) { return link[1]; })
            .filter(function (section): section is Element { return section !== null; });
        const current = reading(doc, sections);
        for (const [a, section] of links) {
            mark(a, section !== null && section === current);
        }
    };

    const schedule = function (): void {
        if (!queued) {
            queued = true;
            requestAnimationFrame(update);
        }
    };

    view.addEventListener("scroll", schedule, {passive: true});
    view.addEventListener("resize", schedule, {passive: true});
    view.addEventListener("hashchange", schedule);
    // A navigation or a render brings other links, other sections or another page; the hook's own marks are
    // neither an id, an href nor the spy attribute, so they do not wake it.
    new MutationObserver(schedule).observe(doc.documentElement,
        {subtree: true, childList: true, attributes: true, attributeFilter: [SPY, "href", "id"]});
    schedule();
}

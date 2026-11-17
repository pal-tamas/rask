// A measured virtual list's box, held steady (data-rask-virtual, on the spacer above the rows; VirtualSpacers.cs).
//
// The server draws a window of rows between two spacers as tall as the rows not drawn, and measures the rows as the
// box scrolls (rask-virtual-report.ts). Three things only the browser can do are done here, .NET's Virtualize.ts
// the model:
//
//  * THE ROWS IN VIEW STAY PUT. A spacer is drawn at the average row height, so when the window moves the rows the
//    reader looks at would jump by however far the rows above them differ from that average. Where the browser
//    anchors scrolling itself (`overflow-anchor`, not in a table) it holds a row in view as rows above it grow or
//    shrink, the spacers saying `overflow-anchor: none` so it never holds one of them. A window that MOVED is held
//    here instead — the browser would hold the element in view, and an element the morph refilled is another row —
//    by the row's index: where the first row in view stood before, it stands after. In a table, and in a browser
//    without anchoring, every change is held that way, a ResizeObserver hearing the rows above the view grow.
//  * THE TAIL. `data-rask-virtual-anchor` holding End (2): a box at the bottom stays there as rows arrive, from the
//    moment it opens, until the reader scrolls up; back at the bottom, it follows again. Holding Start (1, the
//    default, unwritten): a box at the top stays at the top.
//  * A SCROLL TO A ROW. `data-rask-virtual-to="<seq> <row>"`: that row, which the server has drawn, is scrolled to the
//    top of the box as the render arrives (virtualAim, rask-owned.ts), and the box says so with its next scroll (seq,
//    in `seam.virtualDone`): the server's window leaves the row then, and `await scroller.ScrollTo(row)`, waiting in
//    the runtime (rask-virtual-report.ts), finishes. InitialItemIndex comes the same way, on the first render.
//
// After a render, a box whose rows do not reach both of its edges says where it is again, so the server draws the
// rows it is missing: the rows were shorter than the average it had.

import {aboveIn, belowOf, page, startOf, VIRTUAL, VIRTUAL_TO, virtualAim, virtualBox} from "./rask-owned.js";

const ANCHOR = "data-rask-virtual-anchor";
const START = 1;
const END = 2;

// How near the end of its content a box may be and still be at it: a zoomed page's scrollTop is fractional.
const SLACK = 2;

interface Snapshot {
    index: number;
    at: number;
}

class List {
    private readonly native: boolean;
    private readonly changes: MutationObserver;
    private readonly sizes: ResizeObserver;
    private readonly watched = new Set<Element>();
    private readonly heard = (): void => this.scrolled();
    private start = -1;
    private following: boolean;
    private snapshot: Snapshot | null = null;
    private covered = "";

    constructor(private readonly box: HTMLElement, above: HTMLElement) {
        this.native = above.tagName !== "TR" && typeof CSS !== "undefined" && CSS.supports("overflow-anchor", "auto");
        if (!this.native) {
            // Held here, every change: the browser's own anchoring would hold it twice.
            box.style.overflowAnchor = "none";
        }
        this.following = (this.anchor(above) & END) !== 0 && !above.hasAttribute(VIRTUAL_TO);
        this.changes = new MutationObserver(() => this.changed());
        this.changes.observe(box, {childList: true, subtree: true, attributes: true, attributeFilter: [VIRTUAL, VIRTUAL_TO, ANCHOR]});
        this.sizes = new ResizeObserver(() => this.resized());
        box.addEventListener("scroll", this.heard, {passive: true});
        this.changed();
    }

    /** Whether this list still has its box and its spacers on the page. */
    get alive(): boolean {
        return this.box.isConnected && aboveIn(this.box) !== null;
    }

    dispose(): void {
        this.changes.disconnect();
        this.sizes.disconnect();
        this.box.removeEventListener("scroll", this.heard);
        lists.delete(this.box);
    }

    private anchor(above: Element): number {
        const said = above.getAttribute(ANCHOR);
        return said === null ? START : Number(said) || 0;
    }

    private atEnd(): boolean {
        const b = this.box;
        return b.scrollHeight - b.clientHeight - b.scrollTop < SLACK;
    }

    // The top of the box's view, in the page's pixels.
    private viewTop(): number {
        return this.box.getBoundingClientRect().top + this.box.clientTop;
    }

    // How far down the box's scrolled content `el` starts.
    private contentTop(el: Element): number {
        return el.getBoundingClientRect().top - this.viewTop() + this.box.scrollTop;
    }

    private rows(above: Element): Element[] {
        const below = belowOf(above);
        const rows: Element[] = [];
        for (let el = above.nextElementSibling; el && el !== below; el = el.nextElementSibling) rows.push(el);
        return rows;
    }

    // Where the first row in view stands: the row the next change is held by.
    private remember(above: Element): void {
        const rows = this.rows(above);
        const top = this.viewTop();
        this.snapshot = null;
        for (let i = 0; i < rows.length; i++) {
            if (rows[i].getBoundingClientRect().bottom > top) {
                this.snapshot = {index: this.start + i, at: this.contentTop(rows[i])};
                return;
            }
        }
    }

    // Puts the remembered row back where it stood.
    private hold(above: Element): void {
        const snap = this.snapshot;
        const row = snap ? this.rows(above)[snap.index - this.start] : undefined;
        if (!snap || !row) return;
        const moved = this.contentTop(row) - snap.at;
        if (Math.abs(moved) >= 1) this.box.scrollTop += moved;
    }

    private pin(): void {
        this.box.scrollTop = this.box.scrollHeight;
    }

    // `data-rask-virtual-to`: the row to the top of the box, then said done with a scroll (rask-owned.ts).
    private aim(above: Element): boolean {
        if (!virtualAim(this.box, above)) return false;
        this.following = (this.anchor(above) & END) !== 0 && this.atEnd();
        return true;
    }

    private watch(above: Element): void {
        const now = new Set<Element>([above, ...this.rows(above)]);
        const below = belowOf(above);
        if (below) now.add(below);
        for (const el of this.watched) {
            if (!now.has(el)) this.sizes.unobserve(el);
        }
        for (const el of now) {
            if (!this.watched.has(el)) this.sizes.observe(el);
        }
        this.watched.clear();
        now.forEach((el) => this.watched.add(el));
    }

    // A render changed the rows: hold the view, follow the tail, or carry out a scroll to a row.
    private changed(): void {
        const above = aboveIn(this.box);
        if (!above) {
            this.dispose();
            return;
        }
        const start = startOf(above);
        const moved = this.start >= 0 && start !== this.start;
        this.start = start;
        this.watch(above);
        if (moved && this.native) {
            // This change is held by index below; the browser would hold the refilled element, another row.
            this.box.style.overflowAnchor = "none";
            requestAnimationFrame(() => { this.box.style.overflowAnchor = ""; });
        }
        if (!this.aim(above) && !this.followed()) {
            const keepsTop = (this.anchor(above) & START) !== 0 && this.box.scrollTop < 1;
            if (!keepsTop && (moved || !this.native)) {
                this.hold(above);
            }
        }
        this.remember(above);
        requestAnimationFrame(() => this.cover());
    }

    private followed(): boolean {
        if (!this.following) return false;
        this.pin();
        return true;
    }

    // A row grew or shrank: hold the view (where the browser does not), or follow the tail.
    private resized(): void {
        const above = aboveIn(this.box);
        if (!above) return;
        if (!this.followed() && !this.native) {
            this.hold(above);
        }
        this.remember(above);
    }

    private scrolled(): void {
        const above = aboveIn(this.box);
        if (!above) return;
        this.following = (this.anchor(above) & END) !== 0 && this.atEnd();
        this.remember(above);
    }

    // Rows that leave either edge of the box bare: the box says where it is again, once for each window and place.
    private cover(): void {
        const above = aboveIn(this.box);
        const below = above && belowOf(above);
        if (!above || !below || !this.box.isConnected) return;
        const view = this.box.getBoundingClientRect();
        const top = this.viewTop();
        const bottom = top + this.box.clientHeight;
        const a = above.getBoundingClientRect();
        const b = below.getBoundingClientRect();
        const bare = (a.height > 0 && a.bottom > top + 1) || (b.height > 0 && b.top < bottom - 1);
        const stamp = above.getAttribute(VIRTUAL) + " " + Math.round(this.box.scrollTop) + " " + Math.round(view.height);
        if (bare && stamp !== this.covered) {
            this.covered = stamp;
            this.box.dispatchEvent(new Event("scroll"));
        }
    }
}

const lists = new Map<Element, List>();

// Every measured list on the page, each held once by its box: the box that scrolls the spacers' parent.
function find(root: ParentNode): void {
    const spacers = root.querySelectorAll<HTMLElement>("[" + VIRTUAL + "]");
    for (let i = 0; i < spacers.length; i++) {
        const box = virtualBox(spacers[i]);
        if (box && !lists.has(box)) lists.set(box, new List(box, spacers[i]));
    }
}

if (page) {
    new MutationObserver(function (records) {
        for (const [box, list] of lists) {
            if (!list.alive) list.dispose();
            else if (!box.isConnected) lists.delete(box);
        }
        for (const record of records) {
            record.addedNodes.forEach(function (node) {
                if (node instanceof Element) {
                    if (node.hasAttribute(VIRTUAL) && node.parentElement) find(node.parentElement);
                    else find(node);
                }
            });
        }
    }).observe(page.documentElement, {childList: true, subtree: true});
    find(page);
}

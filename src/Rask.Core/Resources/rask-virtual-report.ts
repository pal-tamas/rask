// What a scroll of a measured virtual list's box tells its server beside MDN's scroll state (`raskVirtual` on the
// event's target, VirtualReport.cs): where the list starts in the box, how tall the rows between its two spacers
// stand, which rows those are, and the last scroll to a row the box carried out (rask-virtual.ts).
//
// A list is measured when its render draws the context's spacers (ctx.Above / ctx.Below, VirtualSpacers.cs). The
// server takes one running average of those heights, as .NET's Virtualize does, and finds the rows in view from
// where they stand — so rows of any height scroll true. In the runtime every page loads (rask-input.ts sends the
// scroll), so a list measures before the behaviour hooks have arrived.

import {aboveIn, belowOf, seam, startOf, virtualAim, virtualBox} from "./rask-owned.js";

// How long ScrollTo waits for its row to be drawn before it gives up waiting: a render lost, or a page gone.
const PATIENCE = 5000;

// ScrollTo's answer (VirtualizeModel.WaitForTheBrowserAsync): resolves once the box has carried out scroll `seq` —
// this carries it out itself when the hook has not yet, or has not arrived — or once there is no box to scroll.
window.__raskVirtual = window.__raskVirtual || {
    to(above: Element | null, seq: number): Promise<void> {
        const until = Date.now() + PATIENCE;
        return new Promise<void>(function (resolve) {
            const look = function (): void {
                const box = above && above.isConnected ? virtualBox(above) : null;
                const current = box && aboveIn(box);
                if (current) virtualAim(box!, current);
                if (!box || (seam.virtualDone!.get(box) ?? 0) >= seq || Date.now() > until) {
                    resolve();
                    return;
                }
                requestAnimationFrame(look);
            };
            look();
        });
    },
};

/**
 * `[listTop, separation, rows, start, done, topRow, bottomRow]` for the measured list `box` scrolls — the last two the
 * drawn rows the box's top and bottom edges cut, -1 where an edge cuts none — or undefined for a box that holds none.
 */
export function virtualReport(box: Element): number[] | undefined {
    const above = aboveIn(box);
    const below = above && belowOf(above);
    if (!above || !below) return undefined;
    const start = startOf(above);
    const viewTop = box.getBoundingClientRect().top + box.clientTop;
    const viewBottom = viewTop + box.clientHeight;
    let rows = 0, topRow = -1, bottomRow = -1;
    for (let el = above.nextElementSibling; el && el !== below; el = el.nextElementSibling, rows++) {
        const r = el.getBoundingClientRect();
        if (r.top <= viewTop && viewTop < r.bottom) topRow = start + rows;
        if (r.top < viewBottom && viewBottom <= r.bottom) bottomRow = start + rows;
    }
    const top = above.getBoundingClientRect();
    const done = seam.virtualDone!.get(box);
    return [top.top - viewTop + box.scrollTop, below.getBoundingClientRect().top - top.bottom, rows, start,
        done === undefined ? -1 : done, topRow, bottomRow];
}

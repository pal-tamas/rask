// A textarea that grows with its text (data-rask-grow="<most lines>").
//
// The textarea is rendered with `rows` — the lines it starts at — and keeps no scrollbar. As the reader types,
// it is made as tall as its text, never shorter than those rows nor taller than the most lines the attribute
// names; there it stops and scrolls. The height is written in its `style`, which the morph then leaves alone,
// and nothing is sent. (Flux UI's composer, measured: one line taller per line typed from `rows` to `max-rows`,
// `overflow-y` hidden until the text passes the last line and auto after; a box emptied by the page keeps the
// height it had.)

import {listen, own, page} from "./rask-owned.js";

const GROW = "data-rask-grow";

function px(value: string): number {
    return parseFloat(value) || 0;
}

/**
 * The `height` a textarea whose text is `content` px tall (its scrollHeight, padding included) is given, and
 * whether it scrolls: padding and border are part of that height under `border-box` and not otherwise.
 * Exported for the fixture.
 */
export function grown(content: number, line: number, most: number, padding: number, border: number, borderBox: boolean): [number, boolean] {
    const cap = most * line + padding + border;
    const want = content + border;
    const outer = Math.min(want, cap);
    return [borderBox ? outer : outer - padding - border, want > cap];
}

/** Fits `t` to its text. */
export function fit(t: HTMLTextAreaElement): void {
    const most = Number(t.getAttribute(GROW));
    if (!(most > 0)) {
        return;
    }
    const cs = getComputedStyle(t);
    own(t, "style");
    t.style.height = "auto";
    const [height, scrolls] = grown(t.scrollHeight, px(cs.lineHeight), most,
        px(cs.paddingTop) + px(cs.paddingBottom), px(cs.borderTopWidth) + px(cs.borderBottomWidth), cs.boxSizing === "border-box");
    t.style.height = height + "px";
    t.style.overflowY = scrolls ? "auto" : "hidden";
}

if (page) {
    listen("input", function (e) {
        const t = e.target;
        if (t instanceof HTMLTextAreaElement && t.hasAttribute(GROW)) {
            fit(t);
        }
    });
}

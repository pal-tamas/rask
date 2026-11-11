// The thin bar along the top of the viewport that says the page is working on what the reader asked: a handler
// whose answer is slow, a page that takes a moment to arrive. Both hosts draw it from here.
//
// It shows only once something has been waited on for PROGRESS_DELAY_MS — Livewire's measured value — so an
// answer that comes quickly never flashes it. On the WASM host the page renders in the tab, so it is rarely seen
// there at all; that is the point.
//
// Its colour is the app's accent (`--color-accent`, which Rask UI's theme sets on the root), with Rask's purple
// where an app has none. A reader who asked for less motion sees a still bar instead of a sliding one.
//
// The bar and its stylesheet are siblings of the rendered tree marked `data-rask-managed`, so the morph and the
// diff treat them as invisible and never trim them — the reconnect overlay's convention.

/** How long something must be waited on before the bar shows. */
export const PROGRESS_DELAY_MS = 150;

const BAR = "rask-pending";
const SHOW = "data-show";
const ACCENT = "var(--color-accent,#7C3AED)";

let bar: HTMLElement | null = null;

function install(): HTMLElement {
    const style = document.createElement("style");
    style.setAttribute("data-rask-pending", "");
    style.setAttribute("data-rask-managed", "");
    style.textContent =
        "." + BAR + "{position:fixed;top:0;left:0;right:0;height:2px;" +
        "z-index:2147483646;pointer-events:none;overflow:hidden;display:none;}" +
        "." + BAR + "[data-show]{display:block;}" +
        "." + BAR + "__bar{position:absolute;top:0;left:0;height:100%;width:40%;" +
        "background:linear-gradient(90deg,transparent," + ACCENT + ",transparent);" +
        "animation:rask-pending-slide 1s ease-in-out infinite;}" +
        "@keyframes rask-pending-slide{0%{transform:translateX(-100%);}" +
        "100%{transform:translateX(350%);}}" +
        "@media (prefers-reduced-motion:reduce){." + BAR + "__bar{animation:none;width:100%;" +
        "background:" + ACCENT + ";opacity:.6;}}";
    document.head.appendChild(style);

    const el = document.createElement("div");
    el.className = BAR;
    el.setAttribute("data-rask-managed", "");
    el.setAttribute("data-rask-pending", "");
    el.setAttribute("aria-hidden", "true");
    const slider = document.createElement("div");
    slider.className = BAR + "__bar";
    el.appendChild(slider);
    document.documentElement.appendChild(el);
    return el;
}

/** Shows the bar now. */
export function showProgress(): void {
    if (!bar || !bar.isConnected) bar = install();
    bar.setAttribute(SHOW, "");
}

/** Takes the bar away. */
export function hideProgress(): void {
    if (bar) bar.removeAttribute(SHOW);
}

// What is being waited on now, each with the timer that shows the bar once it has waited long enough.
let waits = 0;

/**
 * One more thing the reader waits on. The bar shows if it is still waited on after {@link PROGRESS_DELAY_MS},
 * and goes once nothing is. Call what this returns when it is answered; a second call does nothing.
 */
export function waitOn(): () => void {
    waits++;
    let timer: ReturnType<typeof setTimeout> | null = setTimeout(function () {
        timer = null;
        showProgress();
    }, PROGRESS_DELAY_MS);
    let done = false;
    return function () {
        if (done) return;
        done = true;
        if (timer !== null) clearTimeout(timer);
        waits--;
        if (waits === 0) hideProgress();
    };
}

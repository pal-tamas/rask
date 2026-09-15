// A control that is waiting on its own handler says so.
//
// Press "Save" on a slow link and nothing on the button changes: the top-of-viewport bar appears after
// 300 ms, far from the thumb that pressed, and the button stays live — so the reader presses it again and
// the handler runs twice. Flux UI answers this on every button bound to a server action: a spinner, and no
// second press while the first is in flight. This is that, for every host, with no per-call-site wiring.
//
// The RUNTIME owns it rather than a component library, because only the runtime knows when a dispatch
// starts and when it is done: the Server host hears an ack per handler seq, the WASM host awaits the
// dispatch promise. Each host calls beginLoading when it sends and endLoading when that dispatch is done;
// this module decides what to mark and how.
//
// What it writes, on the element that owns the handler:
//   data-loading   — a styling hook. Rask UI's `.btn[data-loading]` draws the spinner from it.
//   aria-busy=true — what assistive tech reads. NOT `disabled`, which would drop keyboard focus mid-press,
//                    and not `aria-disabled`, which daisyUI greys the button out for.
// Both after LOADING_DELAY_MS, so a handler that finishes in a frame or two never flashes a spinner. The
// delay is below the pending bar's 300 ms on purpose: the control nearest the reader answers first.
//
// Which element: a <button>, an <input type=button|submit>, or any element carrying `data-rask-loading`.
// `data-rask-loading="off"` on the element or any ancestor opts out — a stepper whose presses are meant to
// queue, or a whole toolbar of them.
//
// No DOM access at import: shared by both host bundles and imported by a Node fixture with a stub DOM.

export const LOADING_DELAY_MS = 200;

// Backstop for a dispatch that never reports back — a lost frame, a hung handler. Deliberately far longer
// than the pending bar's 10 s: a real export that takes 15 s should keep its spinner, and the bar's own
// timeout is about a bar that has stopped meaning anything, not about the control.
export const LOADING_HARD_TIMEOUT_MS = 30000;

export const LOADING_ATTR = "data-loading";
export const BUSY_ATTR = "aria-busy";
export const OPT_ATTR = "data-rask-loading";

export interface LoadingTicket {
    readonly el: Element;
    show: ReturnType<typeof setTimeout> | null;
    hard: ReturnType<typeof setTimeout> | null;
    done: boolean;
}

// Open tickets per element: two quick presses before the delay elapses are two dispatches, and the mark
// comes off when the LAST of them is done, not the first.
const openCount = new WeakMap<Element, number>();

// Elements whose attributes this module wrote. The morph asks, so a full-document repaint that arrives
// mid-dispatch — the render the handler itself caused, typically — does not strip a mark the server never
// rendered. Only what the runtime added is protected; a server-rendered `data-loading` stays the server's.
const stamped = new WeakSet<Element>();

// Every ticket not yet ended, so a disconnect can settle them all at once.
const live = new Set<LoadingTicket>();

function attr(el: Element, name: string): string | null {
    return typeof el.getAttribute === "function" ? el.getAttribute(name) : null;
}

/** The element to mark for a dispatch that `owner` (the element carrying the handler) starts, or null. */
export function loadingTarget(owner: Element | null): Element | null {
    if (!owner) return null;

    for (let n: Node | null = owner; n && n.nodeType === 1; n = n.parentNode) {
        if (attr(n as Element, OPT_ATTR) === "off") return null;
    }

    if (attr(owner, OPT_ATTR) !== null) return owner;

    const tag = owner.tagName;
    if (tag === "BUTTON") return owner;
    if (tag === "INPUT") {
        const type = (attr(owner, "type") || "").toLowerCase();
        return type === "button" || type === "submit" ? owner : null;
    }
    return null;
}

/** Starts marking `el` as waiting on a dispatch. Pair every call with {@link endLoading}. */
export function beginLoading(el: Element): LoadingTicket {
    const ticket: LoadingTicket = {el, show: null, hard: null, done: false};
    openCount.set(el, (openCount.get(el) ?? 0) + 1);
    live.add(ticket);

    ticket.show = setTimeout(() => {
        ticket.show = null;
        // Already marked by the render (`UiButton.Loading(true)`): the mark is the server's, and taking it
        // off when this dispatch ends would contradict the render until the next one.
        if (ticket.done || stamped.has(el) || el.hasAttribute(LOADING_ATTR)) return;
        stamped.add(el);
        el.setAttribute(LOADING_ATTR, "");
        el.setAttribute(BUSY_ATTR, "true");
    }, LOADING_DELAY_MS);
    ticket.hard = setTimeout(() => endLoading(ticket), LOADING_HARD_TIMEOUT_MS);

    return ticket;
}

/** Ends one dispatch's claim on its element. Safe to call twice, and on an element already detached. */
export function endLoading(ticket: LoadingTicket | null | undefined): void {
    if (!ticket || ticket.done) return;
    ticket.done = true;
    live.delete(ticket);
    if (ticket.show !== null) clearTimeout(ticket.show);
    if (ticket.hard !== null) clearTimeout(ticket.hard);
    ticket.show = ticket.hard = null;

    const el = ticket.el;
    const remaining = (openCount.get(el) ?? 1) - 1;
    if (remaining > 0) {
        openCount.set(el, remaining);
        return;
    }

    openCount.delete(el);
    if (stamped.has(el)) {
        stamped.delete(el);
        el.removeAttribute(LOADING_ATTR);
        el.removeAttribute(BUSY_ATTR);
    }
}

/** Settles every open dispatch — the connection dropped, and nothing in flight will report back. */
export function endAllLoading(): void {
    for (const ticket of [...live]) endLoading(ticket);
}

/**
 * Whether `el` is visibly waiting — past the delay, spinner showing. A press on it then is the double
 * submit this exists to stop; a press before the delay is a fast double-click on a fast handler, and goes
 * through.
 */
export function isVisiblyLoading(el: Element | null): boolean {
    return !!el && stamped.has(el);
}

/** Whether the runtime, not the render, put attribute `name` on `el` — so a morph must leave it alone. */
export function runtimeOwnsAttr(el: Element, name: string): boolean {
    return (name === LOADING_ATTR || name === BUSY_ATTR) && stamped.has(el);
}

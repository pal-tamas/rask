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
//   data-loading   — a styling hook. Rask UI's button fades its label out and its spinner in on it.
//   aria-busy=true — what assistive tech reads. NOT `disabled`, which would drop keyboard focus mid-press,
//                    and not `aria-disabled`, which daisyUI greys the button out for.
// Both after LOADING_DELAY_MS, so a handler that finishes in a frame or two never flashes a spinner. The
// delay is below the pending bar's 300 ms on purpose: the control nearest the reader answers first.
//
// Which element: a <button>, an <input type=button|submit>, or any element carrying `data-rask-loading`.
// `data-rask-loading="off"` on the element or any ancestor opts out — a stepper whose presses are meant to
// queue, or a whole toolbar of them.
//
// And what is around it: `data-rask-loading="within"` marks an element for every dispatch that starts INSIDE
// it. A pager's pressed page number is replaced by the render that answers the press, so a mark on it lands
// on a node the page no longer has; the pager's root outlives every frame, and is what a table dims its rows
// by. A scope gets `data-loading` alone — it is not a control, so it is not busy to assistive tech and a
// press inside it is never refused: only the control that was pressed is.
//
// And a field whose event was sent: Livewire puts `data-loading` on ANY element that triggered a request, a
// `wire:model.live` input included, so a typed or chosen value that left (`.Live()`, `.Debounce()`, a server
// `OnInput` search, a change) marks its field until that event's answer, released where a pressed button's is.
// Only `data-loading`: a field being typed into is not `aria-busy`, and nothing around it is marked.
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

export const SCOPE = "within";

export interface LoadingTicket {
    /** The control that waits, or null when the dispatch came from something that is not one. */
    readonly el: Element | null;
    /** The `data-rask-loading="within"` element around where the dispatch started, or null. */
    readonly scope: Element | null;
    /** Whether the control also says `aria-busy`: a button that waits does, a field being typed into does not. */
    readonly busy: boolean;
    show: ReturnType<typeof setTimeout> | null;
    hard: ReturnType<typeof setTimeout> | null;
    done: boolean;
}

// What the runtime marked, and how many open dispatches hold each mark: two quick presses before the delay
// elapses are two dispatches, and the mark comes off when the LAST of them is done, not the first.
//
// `stamped` is also what the morph asks, so a full-document repaint that arrives mid-dispatch — the render
// the handler itself caused, typically — does not strip a mark the server never rendered. Only what the
// runtime added is protected; a server-rendered `data-loading` stays the server's.
interface Marks {
    readonly open: WeakMap<Element, number>;
    readonly stamped: WeakSet<Element>;
}

const controls: Marks = {open: new WeakMap(), stamped: new WeakSet()};
const scopes: Marks = {open: new WeakMap(), stamped: new WeakSet()};

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

/**
 * The field a sent `input` or `change` event came from — the element carrying that handler's id — or null: for an
 * event the runtime sent on its own (`unasked`), any other kind, an id no longer on the page, or a field that says
 * `data-rask-loading="off"` or sits inside one that does.
 */
export function fieldTarget(payload: unknown): Element | null {
    const sent = payload as { id?: unknown; type?: unknown; unasked?: unknown } | null;
    if (!sent || sent.unasked || typeof sent.id !== "string" || (sent.type !== "input" && sent.type !== "change")) return null;

    const field = document.querySelector("[data-rask-on-" + sent.type + "=\"" + sent.id + "\"]");
    return field && !isOptedOut(field) ? field : null;
}

function isOptedOut(el: Element): boolean {
    for (let n: Node | null = el; n && n.nodeType === 1; n = n.parentNode) {
        if (attr(n as Element, OPT_ATTR) === "off") return true;
    }
    return false;
}

/**
 * The scope a dispatch from `owner` marks: the nearest element above it carrying
 * `data-rask-loading="within"`, or null — also when anything from `owner` up says "off".
 */
export function loadingScope(owner: Element | null): Element | null {
    let scope: Element | null = null;
    for (let n: Node | null = owner; n && n.nodeType === 1; n = n.parentNode) {
        const opt = attr(n as Element, OPT_ATTR);
        if (opt === "off") return null;
        if (opt === SCOPE && !scope && n !== owner) scope = n as Element;
    }
    return scope;
}

function hold(marks: Marks, el: Element | null): void {
    if (el) marks.open.set(el, (marks.open.get(el) ?? 0) + 1);
}

// Already marked by the render (`Ui.Button.Loading(true)`): the mark is the server's, and taking it off when
// this dispatch ends would contradict the render until the next one.
function stamp(marks: Marks, el: Element | null, busy: boolean): void {
    if (!el || marks.stamped.has(el) || el.hasAttribute(LOADING_ATTR)) return;
    marks.stamped.add(el);
    el.setAttribute(LOADING_ATTR, "");
    if (busy) el.setAttribute(BUSY_ATTR, "true");
}

function release(marks: Marks, other: Marks, el: Element | null, busy: boolean): void {
    if (!el) return;
    const remaining = (marks.open.get(el) ?? 1) - 1;
    if (remaining > 0) {
        marks.open.set(el, remaining);
        return;
    }

    marks.open.delete(el);
    if (!marks.stamped.has(el)) return;
    marks.stamped.delete(el);
    if (busy) el.removeAttribute(BUSY_ATTR);
    // An opted-in control inside which another dispatch started holds the mark as a scope too.
    if (!other.stamped.has(el)) el.removeAttribute(LOADING_ATTR);
}

/**
 * Starts marking what waits on a dispatch: `el`, the control (from {@link loadingTarget}), and the scope
 * around `from`, the element the dispatch started at. Null when there is neither. Pair every ticket with
 * {@link endLoading}.
 */
export function beginLoading(el: Element | null, from: Element | null = el, busy = true): LoadingTicket | null {
    const scope = loadingScope(from);
    if (!el && !scope) return null;

    const ticket: LoadingTicket = {el, scope, busy, show: null, hard: null, done: false};
    hold(controls, el);
    hold(scopes, scope);
    live.add(ticket);

    ticket.show = setTimeout(() => {
        ticket.show = null;
        if (ticket.done) return;
        stamp(controls, el, busy);
        stamp(scopes, scope, false);
    }, LOADING_DELAY_MS);
    ticket.hard = setTimeout(() => endLoading(ticket), LOADING_HARD_TIMEOUT_MS);

    return ticket;
}

/** Ends one dispatch's claim on what it marked. Safe to call twice, and on an element already detached. */
export function endLoading(ticket: LoadingTicket | null | undefined): void {
    if (!ticket || ticket.done) return;
    ticket.done = true;
    live.delete(ticket);
    if (ticket.show !== null) clearTimeout(ticket.show);
    if (ticket.hard !== null) clearTimeout(ticket.hard);
    ticket.show = ticket.hard = null;

    release(controls, scopes, ticket.el, ticket.busy);
    release(scopes, controls, ticket.scope, false);
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
    return !!el && controls.stamped.has(el);
}

/** Whether the runtime, not the render, put attribute `name` on `el` — so a morph must leave it alone. */
export function runtimeOwnsAttr(el: Element, name: string): boolean {
    if (name === LOADING_ATTR) return controls.stamped.has(el) || scopes.stamped.has(el);
    return name === BUSY_ATTR && controls.stamped.has(el);
}

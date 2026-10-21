// What the runtime's behaviour hooks share: the attributes they own, and the three lines every one of them
// would otherwise repeat.
//
// OWNED ATTRIBUTES. A hook that writes an attribute no render carries — `data-open` on a dialog the browser
// opened, `aria-expanded` on a tooltip's trigger while it shows, `data-copied` on a button — is writing on an
// element the morph reconciles back to the rendered output, so the first re-render of anything around it
// would strip the mark (or put a rendered `aria-expanded="false"` back over a popover that is still open).
// A hook says so here and the morph leaves that one attribute of that one element alone until the hook lets
// go. Per element and per name, in a WeakMap: nothing to clean up when the element leaves the page.
//
// TWO BUNDLES, ONE STATE. The morph is in the runtime every page loads; the hooks arrive in a bundle of their
// own, when a page first carries an attribute that asks for one (rask-hook-loader.ts). Each bundle has its own
// copy of this module, so what the two must agree on is not module state: it hangs off one object on the
// global, made by whichever copy runs first.

type Heard = (e: Event) => void;

interface Seam {
    /** The attributes the hooks hold against the morph. */
    owned: WeakMap<Element, Set<string>>;
    /** Places kept in the document's listener order, by event type, for hooks that arrive later. */
    reserved: {[type: string]: Heard[] | undefined};
    /** What happened while the hooks were on their way, oldest first; null when nothing is being kept. */
    missed: Event[] | null;
    /** Asks the reader about a guarded form's unsaved edits; false when they stay. Set by rask-leave.ts. */
    leave?: (again: () => void) => boolean;
    /**
     * Asks `.Confirm`'s question in the kit's dialog (Ui.Confirm): true when `el` was already said yes to and is
     * being made again, false once the dialog is asking, null when the layout placed none. Set by
     * rask-confirm-dialog.ts.
     */
    confirm?: (el: Element, question: string, expect: string | null, again: () => void) => boolean | null;
    /** What the reader last opened or shut, until the page has answered it. */
    said: WeakMap<Element, Word>;
    /** The elements in `said`: a WeakMap cannot be walked. */
    waiting: Set<Element>;
    /** The events still to come from what the runtime opened or shut itself. */
    ours: WeakMap<Element, Ours>;
    /** The element the runtime is opening or shutting right now. */
    acting?: Element | null;
    /** How many things the runtime is doing right now that no reader asked for (`unasked`). */
    unasked?: number;
    /** What each field that waits for a pause last told its handler (`data-rask-on-pause`, rask-input.ts). */
    told?: WeakMap<Element, string>;
    /** Ends the pause a field is counting: its handler hears the field now (`now`), or nothing. Set by rask-input.ts. */
    hear?: (el: Element, now: boolean) => void;
    /** How many events and navigations the page is waiting on the answer of (`awaiting`). */
    waits?: number;
    /** What runs each time the last of them is answered (`whenQuiet`). */
    quiet?: (() => void)[];
}

/**
 * The reader's last word on one element: the seq of the event that carries it — not `told` yet while that
 * event is still to be raised — and what a render was refused meanwhile.
 */
interface Word {
    until: number;
    told: boolean;
    late: ((el: Element) => void) | null;
}

/** What the runtime's own show or hide will raise on one element. */
interface Ours {
    toggle?: string;
    close?: boolean;
}

const scope = globalThis as typeof globalThis & {__raskHookSeam?: Seam};

/** What the runtime and the hooks bundle share. */
export const seam: Seam = scope.__raskHookSeam
    || (scope.__raskHookSeam = {
        owned: new WeakMap<Element, Set<string>>(), reserved: {}, missed: null,
        said: new WeakMap<Element, Word>(), waiting: new Set<Element>(), ours: new WeakMap<Element, Ours>(),
    });

// A page that stays open across a deploy may get the two bundles from different builds, and the seam is made
// by the one that ran first: what an older copy did not put on it is put on it here.
seam.said ||= new WeakMap<Element, Word>();
seam.waiting ||= new Set<Element>();
seam.ours ||= new WeakMap<Element, Ours>();
seam.told ||= new WeakMap<Element, string>();
seam.quiet ||= [];

const owned = seam.owned;

/** Marks attribute `name` of `el` as the runtime's: the morph neither removes nor rewrites it. */
export function own(el: Element, name: string): void {
    let names = owned.get(el);
    if (!names) {
        owned.set(el, names = new Set());
    }
    names.add(name);
}

/** Hands attribute `name` of `el` back to the render. */
export function disown(el: Element, name: string): void {
    const names = owned.get(el);
    if (names) {
        names.delete(name);
    }
}

/** Whether a hook holds attribute `name` of `el` — asked by the morph for every attribute it would touch. */
export function ownsAttr(el: Element, name: string): boolean {
    const names = owned.get(el);
    return names !== undefined && names.has(name);
}

/**
 * Whether the checked state of `el` is not a render's to set: a hook holds it, or the box says
 * `data-rask-persist` — its state is the READER's, kept across visits.
 *
 * The attribute is asked directly, and that is the point. The hook that restores and stores such a box
 * (rask-persist.ts) arrives in a bundle of its own, some time after the runtime; a page that must not flash
 * restores the box from a script in its `<head>` before the first paint. Between the two, a WebAssembly app's
 * first render used to put the box back to the unchecked one it rendered — nothing held it yet — and a
 * collapsed sidebar was drawn wide for the frames until the hooks came (2 to 12 of them, measured). The
 * runtime every page loads therefore knows this one thing itself.
 */
export function ownsChecked(el: Element): boolean {
    return ownsAttr(el, "checked") || el.hasAttribute("data-rask-persist");
}

/**
 * Whether a navigation the reader started inside the app may go ahead NOW: false when a form guarded with
 * `data-rask-confirm-leave` holds unsaved edits and the reader stays — or has yet to answer. A dialog in the
 * page cannot answer inside this call the way `confirm` does, so the caller hands over `again`, the same
 * navigation, and it is run (and let through) if the reader chooses to leave. Asked by both hosts, answered
 * by rask-leave.ts — which is only there on a page that has such a form, so until then nothing is asked.
 */
export function mayLeave(again: () => void): boolean {
    return !seam.leave || seam.leave(again);
}

// ----- Whose doing an event is ------------------------------------------------------------------------
//
// A bound control waits for the reader's NEXT ACTION, and what waits goes ahead of whatever the page sends
// (rask-input.ts). Not everything the page sends is the reader's doing: a toast whose time is up presses its
// own close button, a chart reports the box it was measured at, a video plays on, the server is answered. Sent
// as an action, any of those wrote a field the reader was still typing in, and its rules spoke under it.
//
// So what the runtime does BY ITSELF says so, here, at the place it does it. Everything raised or sent inside
// `unasked` — a click, a dispatched event, a frame — carries nothing ahead of it and is marked on the wire, so
// the page does not write what its own controls hold for the next action either (PendingBinds.cs). Anything
// else is the reader's: a press, a key, a script pressing for them. On the seam, since the hooks bundle marks
// what the runtime's bundle sends.

/** Runs `act`, something the runtime does with no reader behind it: a timer running out, a box measured. */
export function unasked(act: () => void): void {
    seam.unasked = (seam.unasked || 0) + 1;
    try {
        act();
    } finally {
        seam.unasked--;
    }
}

/** Whether what is being sent right now is the runtime's own doing rather than the reader's. */
export function isUnasked(): boolean {
    return !!seam.unasked;
}

// A CONTROL THAT DRAWS ITSELF ON THE PAGE'S SIDE hears the reader too: a list opened, a row the pointer is
// over, the next month. The page hears those only to draw the control again, and in Livewire, whose rule a
// bound control follows, they are no request at all. They are therefore no action either: the element says
// which of its handlers only draw — `data-rask-draws="toggle keydown"`, by event name — and an event for one
// of them is sent as `unasked` is, with nothing ahead of it and nothing written for it. What reaches code the
// app wrote, or writes a value now, is not named there, and is an action as before.

/** Whether the handler `el` has for `name` only draws its control. */
export function draws(el: Element, name: string): boolean {
    const names = el.getAttribute("data-rask-draws");
    return !!names && (" " + names + " ").indexOf(" " + name + " ") >= 0;
}

/** Sends what `el` heard as `name`: the reader's action, or nobody's where the handler only draws. */
export function tell<T>(el: Element, name: string, send: () => T): T {
    if (!draws(el, name)) {
        return send();
    }
    let answer: T;
    unasked(function () { answer = send(); });
    return answer!;
}

// ----- What the page is waiting on --------------------------------------------------------------------
//
// A hook that shows something the moment the reader asks — a table's skeleton rows (rask-skeleton.ts) — takes
// it away when the page has answered, and only the hosts know when that is: the Server host when the render
// acking a seq, or the navigation's, is on the page; the WASM host when a dispatch's promise and its reads have
// settled. Each host says so here, the same way, for every handler event and every navigation it sends.

/** The page now waits on one more answer. Call what this returns once the render that answers it is on the page. */
export function awaiting(): () => void {
    seam.waits = (seam.waits || 0) + 1;
    let done = false;
    return function () {
        if (done) return;
        done = true;
        seam.waits = Math.max(0, (seam.waits || 1) - 1);
        if (seam.waits === 0) {
            for (const heard of seam.quiet!) heard();
        }
    };
}

/** Whether an event or a navigation the page sent is still unanswered. */
export function isAwaiting(): boolean {
    return !!seam.waits;
}

/** Runs `heard` every time the page stops waiting on anything. */
export function whenQuiet(heard: () => void): void {
    seam.quiet!.push(heard);
}

// ----- Shared helpers ---------------------------------------------------------------------------------

/** The document, or null where there is none to listen on: the Node fixtures import these modules too. */
export const page: Document | null =
    typeof document !== "undefined" && typeof document.addEventListener === "function" ? document : null;

interface Listening {
    type: string;
    heard: Heard;
    capture: boolean;
}

const listening: Listening[] = [];

/**
 * A hook's listener on the document. Where the runtime kept a place for its event type (a `click`, a `change`:
 * rask-hook-loader.ts) the listener takes that place, so it still runs AHEAD of the host's own listener however
 * late the hooks arrived; anywhere else it is an ordinary listener.
 */
export function listen<K extends keyof DocumentEventMap>(
    type: K, heard: (e: DocumentEventMap[K]) => void, options?: boolean | AddEventListenerOptions): void {
    if (!page) {
        return;
    }
    const capture = options === true || (typeof options === "object" && options.capture === true);
    const place = capture ? undefined : seam.reserved[type];
    if (place) {
        place.push(heard as Heard);
    } else {
        page.addEventListener(type, heard, options);
    }
    listening.push({type, heard: heard as Heard, capture});
}

/**
 * Hands the hooks what they missed while their bundle was on its way: each event kept by the loader, oldest
 * first, to the hooks' listeners only (the page already heard it) — the ones that listen on the way down, then
 * the rest. An event that is over cannot be cancelled any more; everything else a hook does with one it still
 * can, which is what makes a first hover show its tooltip and a first character move on.
 */
export function replayMissed(): void {
    const missed = seam.missed;
    seam.missed = null;
    if (!missed) {
        return;
    }
    for (const e of missed) {
        for (const capture of [true, false]) {
            for (const l of listening) {
                if (l.type === e.type && l.capture === capture) {
                    try {
                        l.heard(e);
                    } catch (error) {
                        // one hook failing on an old event must not cost the others theirs
                    }
                }
            }
        }
    }
}

/** The nearest element from an event's target that matches `selector`. */
export function near(target: EventTarget | null, selector: string): HTMLElement | null {
    return target instanceof Element ? target.closest<HTMLElement>(selector) : null;
}

/** The element an attribute names by id, or null. */
export function named(host: Element, attribute: string): HTMLElement | null {
    const id = host.getAttribute(attribute);
    return id && page ? page.getElementById(id) : null;
}

type Popover = HTMLElement & { showPopover?: () => void; hidePopover?: () => void };

/** Whether `el` is a popover that is showing. False on an engine without the popover API. */
export function isShown(el: Element | null): boolean {
    try {
        return !!el && el.matches(":popover-open");
    } catch (e) {
        return false;
    }
}

/** Shows or hides a popover to match `open`; a no-op when it already does, or cannot. */
export function setShown(el: Element | null, open: boolean): void {
    const popover = el as Popover | null;
    if (!popover || typeof popover.showPopover !== "function" || isShown(popover) === open) {
        return;
    }
    try {
        if (open) popover.showPopover(); else popover.hidePopover!();
    } catch (e) {
        // not connected, or mid-transition — the next event reconciles
    }
}

// ----- Open and shut: the reader's last word ----------------------------------------------------------
//
// A popover, a dialog and a <details> are opened and shut by two hands. The reader's — a press, Escape — acts
// in the browser at once, and the page hears of it as an event. The page's is a render that says open or
// shut, which the runtime carries out. A render made BEFORE the page heard the reader's last toggle is older
// than what the reader is looking at: carried out, it opened a list the reader had just shut, over the fields
// being filled in.
//
//   * `begun`: the browser is opening or shutting it, and not because the runtime asked. The `toggle` that
//     tells the page is a task away, and a render carried out in between undid an Escape nobody had heard of.
//   * `said`: the reader toggled it. Until the page has answered THAT event, no render drives it.
//   * `stands`: asked by whoever carries a render out. What it is refused, it does once the answer is in.
//   * `drive`: the runtime carries a render out. The toggle this raises is no news to the page that asked for
//     it, and `isOurs` keeps it from being sent: followed as the reader's, a late one opened the list again,
//     whose own toggle shut it, and so on for as long as the page was up. (A dialog's `close` is still sent:
//     OnClose hears every close. It is only not held as the reader's.)

/** How long a toggle the browser began may go untold before it stops holding anything. */
const UNTOLD_MS = 1000;

function isOpen(el: Element): boolean {
    return el.hasAttribute("open") || isShown(el);
}

function release(el: Element, word: Word): void {
    seam.said.delete(el);
    seam.waiting.delete(el);
    if (word.late) {
        word.late(el);
    }
}

/**
 * The reader opened or shut `el`, and the page is being told. `answer` is how the host says when the page has
 * answered: the event's seq (the Server host, see `answered`) or a promise (WASM). Anything else is an event
 * nobody will answer, and nothing is held for it.
 */
export function said(el: Element, answer: unknown): void {
    const before = seam.said.get(el);
    const promised = answer as PromiseLike<unknown> | null | undefined;
    const waits = typeof answer !== "number" && !!promised && typeof promised.then === "function";
    if (typeof answer !== "number" && !waits) {
        unsaid(el);
        return;
    }
    const word: Word = {until: waits ? Infinity : answer as number, told: true, late: before ? before.late : null};
    if (waits) {
        const done = function () {
            if (seam.said.get(el) === word) release(el, word);
        };
        promised!.then(done, done);
    }
    seam.said.set(el, word);
    seam.waiting.add(el);
}

/**
 * The browser is about to open (or shut) `el`. When that is not the runtime's doing it is the reader's, and
 * holds from here: `said` takes over when the `toggle` is sent, `unsaid` lets go when nobody is told.
 */
export function begun(el: Element): void {
    if (seam.acting === el) {
        return;
    }
    const before = seam.said.get(el);
    const word: Word = {until: Infinity, told: false, late: before ? before.late : null};
    seam.said.set(el, word);
    seam.waiting.add(el);
    // A word nobody was told of in a second was never going to be: an opening a listener refused changes
    // nothing and raises no `toggle` to let go at.
    setTimeout(function () {
        if (seam.said.get(el) === word) release(el, word);
    }, UNTOLD_MS);
}

/** The `toggle` of `el` has been raised. A word no event carried to the page is not waited on. */
export function unsaid(el: Element): void {
    const word = seam.said.get(el);
    if (word && !word.told) {
        release(el, word);
    }
}

/** Whether the reader's word on `el` still stands. `again` is run once the page has answered it. */
export function stands(el: Element, again: (el: Element) => void): boolean {
    const word = seam.said.get(el);
    if (!word) {
        return false;
    }
    word.late = again;
    return true;
}

/** Every event up to `seq` is answered, and the render that answers it is on the page. */
export function answered(seq: number): void {
    seam.waiting.forEach(function (el) {
        const word = seam.said.get(el);
        if (word && word.until <= seq) release(el, word);
    });
}

// Only a change raises an event, so only a change is waited for: an expectation nothing meets would swallow
// the reader's next toggle. A dialog's `close` may still be on its way from an earlier one.
function expect(el: Element, before: boolean, after: boolean): void {
    if (before === after) {
        return;
    }
    const earlier = seam.ours.get(el);
    seam.ours.set(el, {
        toggle: after ? "open" : "closed",
        close: (!after && el.tagName === "DIALOG") || (!!earlier && !!earlier.close),
    });
}

/** Runs `act`, the runtime opening or shutting `el` to match a render. */
export function drive(el: Element, act: () => void): void {
    const before = isOpen(el);
    seam.acting = el;
    try {
        act();
    } finally {
        seam.acting = null;
        expect(el, before, isOpen(el));
    }
}

/**
 * Whether `e` on `el` is the runtime's own doing. Asked once per event: the browser raises one `toggle` for
 * all that happened since the last, so one the reader had a hand in says something else and is theirs.
 */
export function isOurs(el: Element, e: Event): boolean {
    if (e.type === "beforetoggle") {
        return seam.acting === el;
    }
    const mine = seam.ours.get(el);
    if (!mine) {
        return false;
    }
    if (e.type === "close") {
        const closed = !!mine.close;
        mine.close = false;
        return closed;
    }
    const expected = mine.toggle;
    mine.toggle = undefined;
    return expected === ((e as ToggleEvent).newState || (isOpen(el) ? "open" : "closed"));
}

/**
 * Whether a render may not write `open` on this <details> yet. Its `open` attribute IS its state, so the
 * render that is refused is remembered and written once the page has answered the reader.
 */
export function keepsOpen(el: Element, name: string, open: boolean): boolean {
    if (name !== "open" || el.tagName !== "DETAILS") {
        return false;
    }
    if (stands(el, function (details) { setDetails(details, open); })) {
        return true;
    }
    expect(el, el.hasAttribute("open"), open);
    return false;
}

function setDetails(details: Element, open: boolean): void {
    expect(details, details.hasAttribute("open"), open);
    if (open) details.setAttribute("open", ""); else details.removeAttribute("open");
}

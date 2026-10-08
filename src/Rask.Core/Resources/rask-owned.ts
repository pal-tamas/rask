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
    leave?: () => boolean;
}

const scope = globalThis as typeof globalThis & {__raskHookSeam?: Seam};

/** What the runtime and the hooks bundle share. */
export const seam: Seam = scope.__raskHookSeam
    || (scope.__raskHookSeam = {owned: new WeakMap<Element, Set<string>>(), reserved: {}, missed: null});

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

/** Whether a hook holds the checked state of `el` (a checkbox restored from storage), so a render does not. */
export function ownsChecked(el: Element): boolean {
    return ownsAttr(el, "checked");
}

/**
 * Whether a navigation the reader started inside the app may go ahead: false when a form guarded with
 * `data-rask-confirm-leave` holds unsaved edits and the reader chose to stay. Asked by both hosts, answered
 * by rask-leave.ts — which is only there on a page that has such a form, so until then nothing is asked.
 */
export function mayLeave(): boolean {
    return !seam.leave || seam.leave();
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

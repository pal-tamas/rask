// What the runtime's behaviour hooks share: the attributes they own, and the three lines every one of them
// would otherwise repeat.
//
// OWNED ATTRIBUTES. A hook that writes an attribute no render carries — `data-open` on a dialog the browser
// opened, `aria-expanded` on a tooltip's trigger while it shows, `data-copied` on a button — is writing on an
// element the morph reconciles back to the rendered output, so the first re-render of anything around it
// would strip the mark (or put a rendered `aria-expanded="false"` back over a popover that is still open).
// A hook says so here and the morph leaves that one attribute of that one element alone until the hook lets
// go. Per element and per name, in a WeakMap: nothing to clean up when the element leaves the page.

const owned = new WeakMap<Element, Set<string>>();

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

// ----- Shared helpers ---------------------------------------------------------------------------------

/** The document, or null where there is none to listen on: the Node fixtures import these modules too. */
export const page: Document | null =
    typeof document !== "undefined" && typeof document.addEventListener === "function" ? document : null;

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

// `autofocus` on a page that was put on screen by a render instead of by the browser.
//
// A browser acts on the attribute when it loads a document. A page reached in place is a morph or a handful of
// ops over the document that is already open, where the attribute is only an attribute — so a form opened by a
// link left its first field unfocused, and the same address typed in focused it. Both hosts ask here after
// every frame they apply:
//
//   * a NAVIGATION that arrived (a pushed history entry): the first `[autofocus]` element in document order
//     that is on screen and enabled takes focus, as it does on a load, whether the frame made it or kept it;
//   * any other frame — a handler's render, Back, a replaced address: an element that ARRIVED with the
//     attribute takes focus once. One that merely keeps it is never focused again, and nothing is taken from a
//     field the reader is typing in.
//
// What is not on screen is not focused and is not owed focus later: a closed <dialog> or popover hands focus to
// its own `autofocus` element when the browser opens it. Nor is a page reached at a `#fragment`, which a browser
// loading it would have focused at the fragment instead.

const doc = typeof document === "undefined" ? null : document;

// Every `[autofocus]` element a frame has already been asked about. What the document was served with was the
// browser's to focus, so it starts here.
const seen = new WeakSet<Element>();

function marked(visit: (el: HTMLElement) => void): void {
    doc!.querySelectorAll<HTMLElement>("[autofocus]").forEach(visit);
}

function takesFocus(el: HTMLElement): boolean {
    return !(el as HTMLInputElement).disabled && !el.closest("[inert]") && el.getClientRects().length > 0;
}

// A field that takes text, or a list being arrowed through: the reader's next key belongs to it.
function typing(): boolean {
    const el = doc!.activeElement as HTMLInputElement | null;
    return !!el && (el.isContentEditable || /^(TEXTAREA|SELECT)$/.test(el.tagName)
        || (el.tagName === "INPUT" && !/^(button|checkbox|radio|submit|reset|file|image|range|color)$/.test(el.type)));
}

if (doc) {
    const serve = function () { marked(function (el) { seen.add(el); }); };
    serve();
    doc.addEventListener("DOMContentLoaded", serve);
}

/**
 * Gives focus to the `autofocus` element the frame just applied calls for, if there is one; true when it did.
 * `history` is the frame's own: a pushed entry is a navigation, anything else is a render of the page.
 * `elsewhere` says focus is already owed to something else, so the frame's elements are only taken note of.
 */
export function focusArrived(history?: {action?: string} | null, elsewhere?: boolean): boolean {
    if (!doc) {
        return false;
    }
    const navigated = !!history && history.action !== "replace";
    let target: HTMLElement | null = null;
    marked(function (el) {
        const arrived = !seen.has(el);
        seen.add(el);
        if (!target && (navigated || arrived) && takesFocus(el)) {
            target = el;
        }
    });
    const found = target as HTMLElement | null;
    if (!found || elsewhere || (!navigated && typing())) {
        return false;
    }
    // A replaced entry (Back, Forward) leaves the scroll to the browser's own restoration.
    found.focus({preventScroll: !!history && !navigated});
    return doc.activeElement === found;
}

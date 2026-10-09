// A form that asks before the reader leaves it unsaved (data-rask-confirm-leave="<message>", written by
// `Form.Model(m).ConfirmLeave("…")`).
//
// UNSAVED IS DECIDED HERE, not on the server: a value typed a moment ago may not have been sent yet, and
// that is exactly the one the reader would lose. Every `input` and `change` inside a guarded form counts as
// an edit, whatever the value ends up being.
//
// SAVED IS THE SERVER'S TO SAY. The form carries `data-rask-saved`, a number its render changes after every
// submit that passed validation and ran its handler. When it has changed, the edits made before the last
// `submit` event are saved; one typed after that submit left is not. Read when somebody asks, so there is no
// observer, and a refused submit — which changes nothing — leaves the form unsaved.
//
// HOW IT ASKS. With `confirm`, unless the layout placed Ui.ConfirmLeave: then in that dialog, which is a
// Ui.Modal, and the exit waits for the answer.
//
// WHO ASKS. A navigation the reader starts inside the app goes through `mayLeave` in rask-owned.ts, from both
// hosts (a nav link, `__raskHost.navigate`); Back and Forward arrive here as `popstate`, ahead of the host;
// closing the tab, a reload and a link out of the app are `beforeunload`, whose dialog is the browser's own.
// A navigation the server makes from a handler takes none of those paths, so a save that ends in
// `Go.To(…)` is never asked about. A form that has left the document is not found, and guards nothing.

import {listen, near, page, seam} from "./rask-owned.js";

const MESSAGE = "data-rask-confirm-leave";
const GUARDED = "form[" + MESSAGE + "]";
const SAVED = "data-rask-saved";

interface Edits {
    /** Edits made since the form arrived. */
    made: number;
    /** How many of them the last `submit` carried. */
    sent: number;
    /** How many of them a submit the server accepted carried. */
    saved: number;
    /** `data-rask-saved` as it was when `saved` was last brought up to date. */
    at: string | null;
}

const forms = new WeakMap<Element, Edits>();

function edits(form: Element): Edits {
    const at = form.getAttribute(SAVED);
    let known = forms.get(form);
    if (!known) {
        forms.set(form, known = {made: 0, sent: 0, saved: 0, at});
    } else if (known.at !== at) {
        known.at = at;
        known.saved = known.sent;
    }
    return known;
}

/** The message of the first guarded form in `doc` holding edits no accepted submit carried, or null. */
export function unsavedMessage(doc: Document): string | null {
    const guarded = doc.querySelectorAll(GUARDED);
    for (let i = 0; i < guarded.length; i++) {
        const known = edits(guarded[i]);
        if (known.made > known.saved) {
            return guarded[i].getAttribute(MESSAGE);
        }
    }
    return null;
}

interface HistoryEntry {
    index: number;
    url: string | null;
}

/** The Navigation API, where the browser has it: the only thing that says which WAY a traversal went. */
interface Navigation {
    currentEntry: HistoryEntry | null;
    addEventListener(type: "currententrychange", heard: (e: {from: HistoryEntry | null}) => void): void;
}

function sameDocument(a: string, b: string): boolean {
    return a.split("#")[0] === b.split("#")[0];
}

// The kit's dialog (Ui.ConfirmLeave), when the layout placed one: its parts carry data-rask-leave.
const PART = "data-rask-leave";

if (page) {
    const doc = page;

    // THE QUESTION. Without Ui.ConfirmLeave it is `confirm`, answered before this returns. With it the
    // question is a modal dialog in the page, which answers later: the exit is refused now, kept as `asked`,
    // and run again — let through by `leaving` — if the reader presses the button that leaves. Every other
    // way the dialog closes (its other button, the X, Escape, a press outside) is staying, and needs nothing.
    let asked: (() => void) | null = null;
    let leaving = false;

    const dialogFor = function (message: string): HTMLDialogElement | null {
        const text = doc.querySelector("[" + PART + "=message]");
        const dialog = text ? text.closest("dialog") : null;
        if (text && dialog) text.textContent = message;
        return dialog;
    };

    const ask = function (again: () => void): boolean {
        const message = leaving ? null : unsavedMessage(doc);
        if (message === null) {
            return true;
        }
        const dialog = dialogFor(message);
        if (!dialog) {
            return window.confirm(message);
        }
        asked = again;
        // The platform's own call, which is what the dialog's invoker buttons make: rask-overlay.ts marks it
        // open and dismisses it as it does any Ui.Modal, and the browser moves focus in and hands it back.
        if (!dialog.open) dialog.showModal();
        return false;
    };
    seam.leave = ask;

    listen("click", function (e) {
        const again = asked;
        if (!again || !near(e.target, "[" + PART + "=go]")) {
            return;
        }
        asked = null;
        leaving = true;
        try {
            again();
        } finally {
            leaving = false;
        }
    });
    // `close` does not bubble. Closed any other way, the exit that was asked about is forgotten.
    listen("close", function () { asked = null; }, true);

    const unloading = function (e: BeforeUnloadEvent): void {
        if (unsavedMessage(doc) === null) {
            // Held only while there is something to lose: a page with this listener is kept out of some
            // browsers' back/forward cache.
            window.removeEventListener("beforeunload", unloading);
            return;
        }
        e.preventDefault();
        // What browsers asked for before they honoured preventDefault here.
        e.returnValue = true;
    };

    // Where the reader was before the history moved, and how many entries from where it moved to.
    let here = location.href;
    let back = 0;
    const navigation = (window as {navigation?: Navigation}).navigation;
    if (navigation) {
        navigation.addEventListener("currententrychange", function (e) {
            const now = navigation.currentEntry;
            if (e.from && e.from.url && now) {
                here = e.from.url;
                back = e.from.index - now.index;
            }
        });
    }

    const edited = function (e: Event): void {
        const form = near(e.target, GUARDED);
        if (form) {
            edits(form).made++;
            if (!navigation) here = location.href;
            window.addEventListener("beforeunload", unloading);
        }
    };
    // On the way down: a control that stops its own `input` from bubbling has still been typed into.
    listen("input", edited, true);
    listen("change", edited, true);
    listen("submit", function (e) {
        const form = near(e.target, GUARDED);
        if (form) {
            const known = edits(form);
            known.sent = known.made;
        }
    }, true);

    // Back and Forward. By the time `popstate` fires the URL has already moved, so staying means moving it
    // back, and the move back fires a `popstate` of its own that nobody is to hear. With the dialog the
    // history is moved back BEFORE the reader answers — staying is then nothing at all — and leaving makes
    // the same move again, which `passing` lets through to the host.
    let returning = false;
    let passing = false;
    const moved = function (e: Event): void {
        if (passing) {
            passing = false;
            return; // the reader chose to leave: the host's listener is next
        }
        if (returning) {
            returning = false;
            if (sameDocument(location.href, here)) {
                e.stopImmediatePropagation();
            }
            return; // back where the reader was; or somewhere else, and asking twice would not help
        }
        if (sameDocument(location.href, here)) {
            return; // a fragment of this page
        }
        // Only a browser that says which way the history went can make the move a second time; any other
        // is asked with `confirm`, dialog or not.
        const forth = navigation ? -back : 0;
        const again = function (): void {
            passing = true;
            history.go(forth);
        };
        const message = unsavedMessage(doc);
        if (message === null || (forth ? ask(again) : window.confirm(message))) {
            return; // nothing to lose, or the reader goes
        }
        e.stopImmediatePropagation();
        if (forth) {
            returning = true;
            history.go(back);
        } else {
            // No way to tell Back from Forward here: the address is put back, as a new entry.
            history.pushState(history.state, "", here);
        }
    };
    // Ahead of the host's own popstate listener, in the place the runtime kept (rask-hook-loader.ts).
    const place = seam.reserved.popstate;
    if (place) place.push(moved); else window.addEventListener("popstate", moved);
}

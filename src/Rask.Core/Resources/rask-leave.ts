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

if (page) {
    const doc = page;

    const ask = function (): boolean {
        const message = unsavedMessage(doc);
        return message === null || window.confirm(message);
    };
    seam.leave = ask;

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
    // back, and the move back fires a `popstate` of its own that nobody is to hear.
    let returning = false;
    const moved = function (e: Event): void {
        if (returning) {
            returning = false;
            if (sameDocument(location.href, here)) {
                e.stopImmediatePropagation();
            }
            return; // back where the reader was; or somewhere else, and asking twice would not help
        }
        if (sameDocument(location.href, here) || ask()) {
            return; // a fragment of this page, or the reader goes: the host's listener is next
        }
        e.stopImmediatePropagation();
        if (navigation && back) {
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

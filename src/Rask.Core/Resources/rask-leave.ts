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
// WHAT ONLY THE PAGE HEARD. A control drawn in C# — a listbox, a calendar, a colour sheet — changes its value
// on a press the browser raises no `input` for. The page hears that press, and says so on the form: it carries
// `data-rask-edited` while a value the reader chose in it has not been carried by an accepted submit. A render
// writes it and a render takes it away, so there is nothing to count here, and a value the page wrote by
// itself never puts it there.
//
// WHAT IS NOT THE FORM'S. A list's search field, a calendar's month, a box the kit measures into: typed or
// chosen in, and no data of the form. Such a field, or anything around it, says `data-rask-unguarded`, and
// what happens in it is not counted. Nor is an `input` the runtime raises by itself (`unasked`,
// rask-owned.ts): a chart writing the box it was measured at is nobody's edit.
//
// HOW IT ASKS. With `confirm`, unless the layout placed Ui.ConfirmLeave: then in that dialog, which is a
// Ui.Modal, and the exit waits for the answer (rask-ask.ts, shared with Ui.Confirm).
//
// WHO ASKS. A navigation the reader starts inside the app goes through `mayLeave` in rask-owned.ts, from both
// hosts (a nav link, `__raskHost.navigate`); Back and Forward arrive here as `popstate`, ahead of the host;
// closing the tab, a reload and a link out of the app are `beforeunload`, whose dialog is the browser's own.
// A navigation the server makes from a handler takes none of those paths, so a save that ends in
// `Go.To(…)` is never asked about. A form that has left the document is not found, and guards nothing.
//
// A DIALOG THAT HOLDS ONE. A form in a Ui.Modal is left when the modal closes, and no navigation is made: so
// every way the READER closes one asks first — Escape, a press outside (both a `cancel`), and a button that
// closes it (`command="close" | "request-close"`: the corner's cross, Ui.ModalClose). Escape is asked about on
// its way back up, so a listbox or a popover inside that closes on it first is left to do so. A close the page
// makes (Open false after a save) is the server's, as `Go.To(…)` is, and never asked about. Once the reader
// lets the dialog go, what its forms held is let go with it; and a form in a closed dialog is on no screen, so
// it holds nothing to lose.

import {dialogAsking} from "./rask-ask.js";
import {isUnasked, listen, near, page, seam} from "./rask-owned.js";

const MESSAGE = "data-rask-confirm-leave";
const GUARDED = "form[" + MESSAGE + "]";
const SAVED = "data-rask-saved";
const EDITED = "data-rask-edited";
const UNGUARDED = "[data-rask-unguarded]";

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

/** Whether `el` is a <dialog> that is showing. */
function isOpenDialog(el: Element | null): el is HTMLDialogElement {
    return !!el && el.matches("dialog") && (el as HTMLDialogElement).open;
}

// A form in a dialog that is closed is on no screen.
function onScreen(form: Element): boolean {
    const dialog = form.closest("dialog");
    return !dialog || isOpenDialog(dialog);
}

/**
 * The message of the first guarded form in `within` — the page, or a dialog — holding edits no accepted
 * submit carried, or null.
 */
export function unsavedMessage(within: Document | Element): string | null {
    const guarded = within.querySelectorAll(GUARDED);
    for (let i = 0; i < guarded.length; i++) {
        const known = edits(guarded[i]);
        if ((known.made > known.saved || guarded[i].hasAttribute(EDITED)) && onScreen(guarded[i])) {
            return guarded[i].getAttribute(MESSAGE);
        }
    }
    return null;
}

// The reader let the dialog go, and what its forms held with it.
function letGo(dialog: Element): void {
    const guarded = dialog.querySelectorAll(GUARDED);
    for (let i = 0; i < guarded.length; i++) {
        const known = edits(guarded[i]);
        known.saved = known.sent = known.made;
    }
}

// Whether something inside `dialog` that Escape closes before the dialog is showing.
function holdsAPopover(dialog: Element): boolean {
    try {
        return dialog.querySelector(":popover-open") !== null;
    } catch (e) {
        return false; // an engine without popovers
    }
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
    // question is a modal dialog in the page, which answers later (rask-ask.ts): the exit is refused now, and run
    // again — let through by `leaving` — if the reader leaves by the dialog's button that says so.
    let leaving = false;
    const inDialog = dialogAsking(PART);

    const ask = function (again: () => void): boolean {
        const message = leaving ? null : unsavedMessage(doc);
        if (message === null) {
            return true;
        }
        const dialog = inDialog(message, function () {
            leaving = true;
            try {
                again();
            } finally {
                leaving = false;
            }
        });
        return dialog ? false : window.confirm(message);
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

    // There is something to lose from here on: where the reader is, and the tab asks before it closes.
    const guard = function (): void {
        if (!navigation) here = location.href;
        window.addEventListener("beforeunload", unloading);
    };

    const edited = function (e: Event): void {
        const form = near(e.target, GUARDED);
        if (form && !isUnasked() && !near(e.target, UNGUARDED)) {
            edits(form).made++;
            guard();
        }
    };
    // What the page says of a form arrives in a render, which is no event: watched for, and looked for once
    // in a page that was drawn before this script came.
    const marked = function (): void {
        if (doc.querySelector(GUARDED + "[" + EDITED + "]")) guard();
    };
    if (typeof MutationObserver === "function") {
        new MutationObserver(marked).observe(doc.documentElement, {subtree: true, attributes: true, attributeFilter: [EDITED]});
    }
    marked();
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

    // A dialog the reader is closing, asked about first; `close` is the same close again, which `closing`
    // lets through once the reader says to let the dialog go.
    let closing = false;
    const closes = function (e: Event, dialog: Element | null, close: () => void): void {
        if (closing || !isOpenDialog(dialog)) {
            return;
        }
        const message = unsavedMessage(dialog);
        if (message === null) {
            return;
        }
        e.preventDefault();
        e.stopImmediatePropagation();
        const yes = function (): void {
            letGo(dialog);
            closing = true;
            try {
                close();
            } finally {
                closing = false;
            }
        };
        if (!inDialog(message, yes) && window.confirm(message)) {
            yes();
        }
    };
    // Escape and a press outside, as rask-overlay.ts dismisses a dialog: a `cancel` the page may refuse, then the close.
    const dismiss = function (dialog: HTMLDialogElement): void {
        if (dialog.dispatchEvent(new Event("cancel", {cancelable: true}))) {
            dialog.close();
        }
    };
    listen("keydown", function (e) {
        const dialog = e.key === "Escape" && !e.defaultPrevented ? near(e.target, "dialog") : null;
        if (dialog && !holdsAPopover(dialog)) {
            closes(e, dialog, function () { dismiss(dialog as HTMLDialogElement); });
        }
    });
    // A press outside, and any close request the key above did not see (a phone's back gesture). One the browser
    // will not let be refused is over already.
    listen("cancel", function (e) {
        const dialog = e.target instanceof Element ? e.target : null;
        if (e.cancelable && dialog) {
            closes(e, dialog, function () { dismiss(dialog as HTMLDialogElement); });
        }
    }, true);
    // A button that closes it, whether the engine runs its command or rask-overlay.ts does: refused before either.
    listen("click", function (e) {
        const button = near(e.target, "button[commandfor]");
        const command = button ? button.getAttribute("command") : null;
        if (button && (command === "close" || command === "request-close")) {
            closes(e, doc.getElementById(button.getAttribute("commandfor") || ""), function () { button.click(); });
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

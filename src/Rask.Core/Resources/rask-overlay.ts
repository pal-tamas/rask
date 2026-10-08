// What a popover and a modal dialog need that neither a render nor the platform gives them.
//
//   * FOCUS. Tab out of an open popover and it stays open behind the page, with focus somewhere it no longer
//     covers; close one by pressing outside and focus is on <body>, so a keyboard user is back at the top of the
//     document. An auto popover therefore closes when focus leaves it for anything but its own invoker, and
//     one that an outside press closed hands focus to the button that opens it. (Escape is the browser's, and
//     it already hands focus back.)
//   * A DIALOG A RENDER OPENS (data-rask-modal-open). A render can only write attributes, and `open` on a
//     <dialog> shows it non-modally: no top layer, no ::backdrop, no inert page. So the dialog says
//     data-rask-modal-open="true" | "false" and this calls showModal() / close() to match — on a CHANGE only,
//     so a reader who closes it with Escape while the page still says "true" is not fought; the page hears
//     the close event and catches up.
//   * HOW A MODAL IS DISMISSED (data-rask-modal). `closedby` covers three of the four combinations of
//     "Escape closes" and "a press outside closes" and is missing from some engines; the fourth — outside
//     yes, Escape no — it cannot say at all. data-rask-modal="any | press | escape | none" says all four.
//   * data-open, on the dialog while it is shown, so a stylesheet can animate it in and lock the page behind
//     it (`html:has(dialog[data-open])`) without knowing how it was opened.
//   * INVOKER COMMANDS where the engine has none: command="show-modal | close | request-close" with commandfor.
//
// All of it is delegated to the document; the two observers are filtered to the two attributes.

import {disown, isShown, listen, near, own, page, setShown} from "./rask-owned.js";

type Dialog = HTMLDialogElement & { requestClose?: (value?: string) => void; closedBy?: string };

const MODAL = "data-rask-modal";
const MODAL_OPEN = "data-rask-modal-open";

// ----- Popover focus ---------------------------------------------------------------------------------

// Whether `el` opens `panel`: its popovertarget, or the trigger a hover root pairs with it.
function invokes(el: Element, panel: Element): boolean {
    const invoker = el.closest("[popovertarget]");
    return !!invoker && invoker.getAttribute("popovertarget") === panel.id;
}

function isAuto(panel: Element): boolean {
    const kind = panel.getAttribute("popover");
    return kind === "" || kind === "auto";
}

let pressedAt = 0;
let pressed: EventTarget | null = null;

function installPopoverFocus(doc: Document): void {
    listen("focusout", function (e) {
        const panel = near(e.target, "[popover]");
        const next = e.relatedTarget;
        // No relatedTarget is focus leaving the window, or a press on something that takes none: not a Tab.
        if (!panel || !(next instanceof Element) || !isAuto(panel) || panel.contains(next) || invokes(next, panel)) {
            return;
        }
        setShown(panel, false);
    });

    listen("pointerdown", function (e) {
        pressedAt = Date.now();
        pressed = e.target;
    }, {capture: true, passive: true});

    // `toggle` does not bubble; it is caught on the way down. Only a close that a press outside caused, and
    // only when it left focus nowhere: a panel the pointer merely left, or one the page closed, takes no focus.
    listen("toggle", function (e) {
        const panel = e.target;
        if (!(panel instanceof HTMLElement) || !panel.id || (e as ToggleEvent).newState !== "closed"
            || !panel.hasAttribute("popover") || Date.now() - pressedAt > 1000
            || (pressed instanceof Node && panel.contains(pressed))
            || (doc.activeElement && doc.activeElement !== doc.body)) {
            return;
        }
        const invokers = doc.querySelectorAll<HTMLElement>("[popovertarget]");
        for (let i = 0; i < invokers.length; i++) {
            if (invokers[i].getAttribute("popovertarget") === panel.id) {
                invokers[i].focus({preventScroll: true});
                return;
            }
        }
    }, true);
}

// ----- Modal dialogs ---------------------------------------------------------------------------------

// Only on a dialog that asked: one carrying either attribute. Any other dialog on the page is left as it is.
function mark(dialog: Element, open: boolean): void {
    if (open && (dialog.hasAttribute(MODAL) || dialog.hasAttribute(MODAL_OPEN))) {
        own(dialog, "data-open");
        dialog.setAttribute("data-open", "");
    } else if (!open && dialog.hasAttribute("data-open")) {
        dialog.removeAttribute("data-open");
        disown(dialog, "data-open");
    }
}

function showModal(dialog: Dialog): void {
    try {
        if (dialog.open && !dialog.matches(":modal")) {
            dialog.close(); // a render wrote `open`: shown, but not modal
        }
        if (!dialog.open) {
            dialog.showModal();
        }
        mark(dialog, true);
    } catch (e) {
        // not connected, or it is an open popover
    }
}

function close(dialog: Dialog): void {
    if (dialog.open) {
        dialog.close();
    }
    mark(dialog, false);
}

// Closes the way the reader asked: a cancel the page may refuse, then the close. Flux's modal fires `cancel`
// for a press outside as it does for Escape, so one listener hears both.
function dismiss(dialog: Dialog): void {
    if (dialog.dispatchEvent(new Event("cancel", {cancelable: true}))) {
        close(dialog);
    }
}

const opened = new WeakSet<Element>();

function reconcile(el: Element): void {
    if (!(el instanceof HTMLDialogElement)) {
        return;
    }
    const want = el.getAttribute(MODAL_OPEN);
    if (want === "true") {
        opened.add(el);
        showModal(el);
    } else if (opened.has(el)) {
        // "false", or the render stopped saying anything: either way it no longer wants it open.
        opened.delete(el);
        close(el);
    }
}

function scan(root: Element): void {
    if (root.hasAttribute(MODAL_OPEN)) reconcile(root);
    root.querySelectorAll("[" + MODAL_OPEN + "]").forEach(reconcile);
}

function outside(dialog: Element, e: MouseEvent): boolean {
    const box = dialog.getBoundingClientRect();
    return e.clientX < box.left || e.clientX > box.right || e.clientY < box.top || e.clientY > box.bottom;
}

function installModal(doc: Document): void {
    if (typeof MutationObserver === "function") {
        new MutationObserver(function (records) {
            for (const record of records) {
                if (record.type === "attributes") {
                    reconcile(record.target as Element);
                    continue;
                }
                record.addedNodes.forEach(function (n) {
                    if (n instanceof Element) scan(n);
                });
            }
        }).observe(doc.documentElement, {subtree: true, childList: true, attributes: true, attributeFilter: [MODAL_OPEN]});
        scan(doc.documentElement);
    }

    // data-open follows the dialog, however it was opened or closed. `toggle` on a dialog is recent, so
    // `close` is listened for as well; neither bubbles.
    listen("toggle", function (e) {
        const dialog = e.target;
        if (dialog instanceof HTMLDialogElement) {
            mark(dialog, dialog.open);
        }
    }, true);
    listen("close", function (e) {
        if (e.target instanceof HTMLDialogElement) {
            mark(e.target, false);
        }
    }, true);

    // A press outside. The backdrop is the dialog's own pseudo-element, so the press arrives ON the dialog,
    // outside its box. Both ends of the press have to be outside: dragging a selection out of a field and
    // letting go over the backdrop is not a dismissal.
    let downOutside: Element | null = null;
    listen("pointerdown", function (e) {
        const dialog = e.target;
        downOutside = dialog instanceof HTMLDialogElement && dialog.hasAttribute(MODAL) && outside(dialog, e) ? dialog : null;
    }, true);
    listen("click", function (e) {
        const dialog = e.target;
        if (!(dialog instanceof HTMLDialogElement) || dialog !== downOutside || !outside(dialog, e)) {
            return;
        }
        downOutside = null;
        const by = dialog.getAttribute(MODAL);
        // closedby="any" is the platform doing this already, where it exists.
        if (by === "press" || (by === "any" && !("closedBy" in dialog && (dialog as Dialog).closedBy === "any"))) {
            dismiss(dialog);
        }
    }, true);

    // Escape, on a dialog that says it does not close on it. Preventing the key is what keeps the close
    // request from being made at all — refusing the `cancel` event instead is honoured only once in a row.
    // (closedby="none" beside it also covers a phone's back gesture, where the engine has it.)
    listen("keydown", function (e) {
        if (e.key !== "Escape") {
            return;
        }
        const dialog = near(e.target, "dialog[" + MODAL + "]");
        const by = dialog ? dialog.getAttribute(MODAL) : null;
        if (dialog && (by === "press" || by === "none") && dialog.matches(":modal")) {
            e.preventDefault();
        }
    }, true);

    // Invoker commands, for an engine without them. One with them never reaches the body of this: it has
    // already run the command, and the test is one property read.
    listen("click", function (e) {
        // Asked of the prototype, not of the button: the DOM typings say every button has the property, so
        // testing the element itself tells the compiler nothing is left to handle below.
        if ("commandForElement" in HTMLButtonElement.prototype || e.defaultPrevented) {
            return;
        }
        const button = near(e.target, "button[commandfor]");
        if (!(button instanceof HTMLButtonElement) || button.disabled) {
            return;
        }
        const target = doc.getElementById(button.getAttribute("commandfor") || "");
        const command = button.getAttribute("command");
        if (target instanceof HTMLDialogElement) {
            if (command === "show-modal") {
                showModal(target);
            } else if (command === "close") {
                close(target);
            } else if (command === "request-close") {
                dismiss(target);
            } else {
                return;
            }
            e.preventDefault(); // the popovertarget beside it was the fallback for exactly this
        } else if (target && target.hasAttribute("popover") && command) {
            const want = command === "show-popover" ? true : command === "hide-popover" ? false
                : command === "toggle-popover" ? !isShown(target) : null;
            if (want !== null && !button.hasAttribute("popovertarget")) {
                setShown(target, want);
            }
        }
    });
}

if (page) {
    installPopoverFocus(page);
    installModal(page);
}

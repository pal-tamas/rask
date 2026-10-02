// Which document clicks are the app's to take, the same on both hosts.
//
// Both hosts delegate `click` from `document`. What they do with a click they take differs — the
// Server host starts a navigation's progress bar and tracks loading by handler seq, the WASM host
// awaits its dispatch — but which clicks they take, and when they cancel the browser's own action,
// must not: these used to be two hand-kept copies of each guard, and the way that fails is one drifting.
//
// No DOM access at import: everything here runs per click.

import { inRoot } from "./rask-host.js";
import { isVisiblyLoading, loadingTarget } from "./rask-loading.js";
import { closestFrom } from "./rask-morph.js";

/**
 * The same-origin URL a plain left click on an `a[data-rask-nav]` navigates to, with the browser's own
 * navigation cancelled — or null, untouched, for a click the browser should handle (a modified or
 * non-primary click, `target="_blank"`, a cross-origin or unparsable href, or one already cancelled).
 */
export function navLinkClick(e: MouseEvent): URL | null {
    if (e.defaultPrevented) return null;
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return null;
    const a = closestFrom(e.target, "a[data-rask-nav]");
    if (!a) return null;
    if (a.getAttribute("target") === "_blank") return null;
    const href = a.getAttribute("href");
    if (!href) return null;
    let url;
    try {
        url = new URL(href, location.href);
    } catch {
        return null;
    }
    if (url.origin !== location.origin) return null;
    e.preventDefault();
    return url;
}

/**
 * The `[data-rask-on-click]` element a click dispatches to, with the click's default cancelled where
 * that is safe — or null when the click is not to be dispatched.
 */
export function handlerClick(e: MouseEvent): Element | null {
    const t = closestFrom(e.target, "[data-rask-on-click]");
    if (!t || !inRoot(t)) return null;
    // A submit/reset button is driven by native form submission (handled by the dedicated submit
    // listener). Don't let an ANCESTOR click handler (e.g. a modal's .modal-dialog shield) hijack it
    // and cancel the default — that would break the form submit. A handler on the button itself
    // still runs: note `button.type` defaults to "submit" for a bare <button>, so gating on the
    // ancestor (t !== btn) is what keeps a plain Button(OnClick:) working here.
    const btn = closestFrom(e.target, "button, input") as HTMLButtonElement | HTMLInputElement | null;
    if (btn && btn !== t && (btn.type === "submit" || btn.type === "reset")) return null;
    // A POPOVER INVOKER is the same case as a submit button, and for the same reason: opening the
    // popover IS the button's default action, so cancelling it leaves an element that says
    // `popovertarget` in the markup and does nothing when pressed. The C# handler still runs — this
    // only declines to cancel — so a control can have both a C# state and the browser's top layer,
    // which is exactly what a listbox or a menu built on [popover] needs. Handled here rather than
    // at the call site because nothing at the call site can reach this listener.
    //
    // An INVOKER COMMAND (`command` + `commandfor`, the HTML invoker API) is the same case again: its
    // default action is the command, so a C# handler on a button that also shows a modal must not
    // cancel the showing.
    const invoker = closestFrom(e.target, "[popovertarget], [commandfor]");
    // A second press on a control still visibly waiting on its first is the double submit a spinner
    // exists to prevent (rask-loading.ts). Before the spinner shows — a fast double-click on a fast
    // handler — it goes through, which keeps a rapid stepper working.
    if (isVisiblyLoading(loadingTarget(t))) {
        if (!invoker) { e.preventDefault(); }
        return null;
    }
    if (!invoker) { e.preventDefault(); }
    return t;
}

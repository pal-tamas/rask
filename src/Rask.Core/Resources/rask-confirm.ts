// Asking before an action, the same on both hosts: Livewire's `wire:confirm`, written by `.Confirm("…")`.
//
// `data-rask-confirm` holds the question, shown in the browser's own confirm(). With `data-rask-confirm-expect`
// beside it (`.Confirm("…", expect: name)`, Livewire's `.prompt`) the reader types the answer into prompt(), and
// the action goes only on an exact match. Asked by the hosts' click and submit listeners before anything is sent,
// so a cancelled action sends nothing — not the event, and not the fields waiting to go ahead of it.

/** Whether the action `el` is about to send may go: true when it asks nothing, or the reader said yes. */
export function confirmed(el: Element | null): boolean {
    if (!el) return true;
    const question = el.getAttribute("data-rask-confirm");
    if (question === null) return true;
    const expect = el.getAttribute("data-rask-confirm-expect");
    return expect === null ? window.confirm(question) : window.prompt(question) === expect;
}

/** Whether a submit may go: the form asks first, then the button that submitted it. */
export function submitConfirmed(form: Element, submitter: Element | null): boolean {
    return confirmed(form) && confirmed(submitter);
}

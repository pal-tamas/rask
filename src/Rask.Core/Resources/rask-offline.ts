// Whether the page can reach its app, said on <html> as `data-rask-offline` — Livewire's `wire:offline`, the same on
// both hosts. A page styles what it shows while the reader is offline with a variant: `Div.Class("hidden
// in-data-rask-offline:block")["You are offline"]`.
//
// The mark is on while the browser reports itself offline (`navigator.onLine`, and the `online` / `offline` events),
// and, on a host with a live connection of its own (the Server's socket), while that connection is down.
// Nothing is sent to the page: it is for style, and for the reader.

let browserOffline = typeof navigator !== "undefined" && "onLine" in navigator && !navigator.onLine;
let connectionDown = false;

function paint(): void {
    if (typeof document === "undefined" || !document.documentElement) return;
    const html = document.documentElement;
    if (browserOffline || connectionDown) html.setAttribute("data-rask-offline", "");
    else html.removeAttribute("data-rask-offline");
}

/** The host's live connection is down (true) or back (false). The browser's own report is kept apart from it. */
export function connectionIs(down: boolean): void {
    connectionDown = down;
    paint();
}

if (typeof window !== "undefined") {
    window.addEventListener("online", function () { browserOffline = false; paint(); });
    window.addEventListener("offline", function () { browserOffline = true; paint(); });
    paint();
}

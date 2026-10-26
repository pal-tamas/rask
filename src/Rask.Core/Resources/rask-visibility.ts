// The reader's tab went out of sight, or came back: the page is told, so a query that polls waits for the
// reader rather than asking the server for rows nobody is looking at (docs/query.md, "Polling").
//
// A `visibility` frame, on the route every frame takes, on both hosts. Nobody's action (`unasked`,
// rask-owned.ts): nothing the reader typed and has not sent goes with it, and nothing is rendered for it.

import {send} from "./rask-host.js";
import {unasked} from "./rask-owned.js";

function tell(hidden: boolean): void {
    unasked(function () { send({type: "visibility", hidden: hidden}); });
}

/** Tells a page that has just connected that its tab is out of sight. A page in sight says nothing. */
export function tellIfHidden(): void {
    if (typeof document !== "undefined" && document.visibilityState === "hidden") tell(true);
}

if (typeof document !== "undefined" && typeof document.addEventListener === "function") {
    document.addEventListener("visibilitychange", function () { tell(document.visibilityState === "hidden"); });
}

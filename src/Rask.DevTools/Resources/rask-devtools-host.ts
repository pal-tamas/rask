// The devtools host script. The server writes a tag for it into the <head> of a Debug page served in
// Development; it is never part of rask.js or rask.wasm.js, and neither runtime has code to load it, which is what
// keeps a Release page free of it.
//
// This is the entry point: it installs the pill and drawer (host/dock.ts); the panel iframe's bridge and the page
// overlays are its modules as they arrive. It is idempotent: whatever loads it, one document must never run the tools
// twice.

import {installDock, type DockHandle} from "./host/dock.js";

declare global {
    interface Window {
        /** Present once the devtools host script has run in this document. */
        __raskDevtoolsHost?: { readonly version: 1; readonly dock: DockHandle };
    }
}

// The tag the server wrote names the panel page; read while this script is still the current one. No panel, no pill:
// a corner button that opens an empty drawer is worse than none.
const tag = document.currentScript;
const panelUrl = tag ? tag.getAttribute("data-panel") : null;

if (!window.__raskDevtoolsHost && panelUrl) {
    window.__raskDevtoolsHost = {version: 1, dock: installDock({panelUrl})};
}

export {};

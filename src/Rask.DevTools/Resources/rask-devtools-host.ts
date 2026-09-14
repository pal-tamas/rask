// The devtools host script. The server writes a tag for it into the <head> of a Debug page served in
// Development; it is never part of rask.js or rask.wasm.js, and neither runtime has code to load it, which is what
// keeps a Release page free of it.
//
// This is the entry point: it installs the pill and drawer (host/dock.ts) and the page overlays (host/overlay.ts), and
// listens to the panel page the drawer frames (host/bridge.ts). It is idempotent: whatever loads it, one document must
// never run the tools twice.

// `window.__raskDevtoolsHost` is declared beside the dock, which both hosts' entry points share.
import {createBridge, listenToPanel, type PanelBridge} from "./host/bridge.js";
import {installDock} from "./host/dock.js";
import {installOverlay} from "./host/overlay.js";

// The tag the server wrote names the panel page; read while this script is still the current one. No panel, no pill:
// a corner button that opens an empty drawer is worse than none.
const tag = document.currentScript;
const panelUrl = tag ? tag.getAttribute("data-panel") : null;

if (!window.__raskDevtoolsHost && panelUrl) {
    let frameWindow: Window | null = null;
    let bridge: PanelBridge | null = null;

    const dock = installDock({
        panelUrl,
        onFrame: frame => frameWindow = frame.contentWindow,
        onClose: () => bridge?.closed(),
    });
    const overlay = installOverlay(dock.shadow, dock.element);
    // The panel is a page of this origin, so it is posted to in this origin only, and heard from in it only.
    bridge = createBridge(dock, overlay, message => frameWindow?.postMessage(message, location.origin));
    listenToPanel(() => frameWindow, location.origin, () => bridge);

    window.__raskDevtoolsHost = {version: 1, dock};
}

export {};

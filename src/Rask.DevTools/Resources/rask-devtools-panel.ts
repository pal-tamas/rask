// The Server panel page's own script: the page overlays' panel side (panel/panel-client.ts). The panel is a live Rask
// page served from /_rask-devtools/ in the app's origin and framed by the devtools drawer; its tab talks to the app over
// its own socket, and this talks to the page that framed it, in the page's own origin only.
//
// Served in Development only, like the host script, and loaded by nothing but the panel page's head.

import {installPanelClient} from "./panel/panel-client.js";

// A panel page opened on its own, not in the drawer, has no page to talk to.
if (window.parent !== window) {
    installPanelClient({
        post: message => window.parent.postMessage(message, location.origin),
        fromPage: e => e.source === window.parent && e.origin === location.origin,
    });
}

export {};

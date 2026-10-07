// The behaviour hooks: what an element asks the runtime to do by carrying a data-rask-* attribute, where a
// render cannot do it (it only writes attributes) and a round trip would be too late or would lose the gesture.
//
// One module per concern, each installing delegated listeners on the document and nothing per element.
// docs/js-interop-runtime.md lists every attribute.
//
// This is the ENTRY of a bundle of its own (rask-hooks.js), not part of either host runtime: a page loads it
// when it first carries one of those attributes, and a page that never does never downloads it
// (rask-hook-loader.ts, which every page does load, decides). What it shares with the runtime — the
// attributes held against the morph, its place among the document's listeners — goes through rask-owned.ts.

import {replayMissed} from "./rask-owned.js";
import "./rask-hover.js";    // data-rask-tooltip, data-rask-hover
import "./rask-overlay.js";  // popover focus, data-rask-modal, data-rask-modal-open, invoker-command fallback
import "./rask-lock.js";     // data-rask-lock
import "./rask-menu.js";     // data-rask-menu-pointer, data-rask-safe-area
import "./rask-field.js";    // data-rask-copy / -focus / -clear / -mask / -mask-money / -big-step
import "./rask-keys.js";     // data-rask-contain-keys, data-rask-listbox-button, data-rask-roving
import "./rask-focus.js";    // data-rask-focus-follows / -focus-target, aria-activedescendant, data-rask-press-keeps-focus
import "./rask-toggle.js";   // data-rask-toggle, aria-expanded on a popover's invokers
import "./rask-drag.js";     // data-rask-drag, data-rask-drag-inset
import "./rask-requires.js"; // data-rask-requires
import "./rask-plot.js";     // data-rask-plot / -plot-area / -plot-row / -plot-tooltip, data-rask-measure
import "./rask-otp.js";      // data-rask-otp
import "./rask-segments.js"; // data-rask-segments, data-rask-segment
import "./rask-toast.js";    // data-rask-dismiss-scope, data-rask-stack
import "./rask-persist.js";  // data-rask-persist, data-rask-uncheck-on-navigate
import "./rask-carousel.js"; // data-rask-carousel

// Last, when every hook is listening: what the reader did while this bundle was on its way.
replayMissed();

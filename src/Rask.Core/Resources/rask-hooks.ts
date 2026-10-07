// The behaviour hooks: what an element asks the runtime to do by carrying a data-rask-* attribute, where a
// render cannot do it (it only writes attributes) and a round trip would be too late or would lose the gesture.
//
// One module per concern, each installing delegated listeners on the document and nothing per element, so a
// page that uses none of them pays for none of them. Imported once by each host runtime (rask.ts,
// rask.wasm.ts) for its side effects; docs/js-interop-runtime.md lists every attribute.

import "./rask-hover.js";    // data-rask-tooltip, data-rask-hover
import "./rask-overlay.js";  // popover focus, data-rask-modal, data-rask-modal-open, invoker-command fallback
import "./rask-lock.js";     // data-rask-lock
import "./rask-menu.js";     // data-rask-menu-pointer, data-rask-safe-area
import "./rask-field.js";    // data-rask-copy / -focus / -clear / -mask / -mask-money / -big-step
import "./rask-keys.js";     // data-rask-contain-keys, data-rask-listbox-button, data-rask-roving
import "./rask-focus.js";    // data-rask-focus-follows / -focus-target, aria-activedescendant, data-rask-press-keeps-focus
import "./rask-toggle.js";   // data-rask-toggle, aria-expanded on a popover's invokers
import "./rask-otp.js";      // data-rask-otp
import "./rask-toast.js";    // data-rask-dismiss-scope, data-rask-stack
import "./rask-persist.js";  // data-rask-persist, data-rask-uncheck-on-navigate
import "./rask-carousel.js"; // data-rask-carousel

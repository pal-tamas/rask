// The shared morph, published on `window` for MorphSelectSelectionTests to drive in a real browser.
//
// The test exercises select/option selection semantics that only a real browser has: the live
// `selectedOptions` list, the way setting `selectedIndex` clears every other option, and the split
// between an option's `selected` ATTRIBUTE and its property. A stub DOM cannot stand in for those,
// which is why this one runs in Playwright rather than under Node like its siblings.
//
// It used to reach the morph by reading `rask-morph.js` and `rask-dom.js` off disk and evaluating
// them with `new Function(...)` — the only way in, while those files were bare declarations meant to
// be pasted into a host's scope. They are modules now, so this is an entry point esbuild bundles:
// the import is checked by the compiler, and the test loads one self-contained script.

import {morph, raskNotePendingFormState} from "../../../src/Rask.Core/Resources/rask-morph.js";

// Imported for its side effects: rask-dom.ts owns syncFormProperty and the install-once listeners
// the morph path expects to be present. A bare import, because nothing here names an export of
// it — and a named import left unreferenced would let esbuild judge the module unreachable and drop
// it from the bundle entirely.
import "../../../src/Rask.Core/Resources/rask-dom.js";

declare global {
    interface Window {
        __raskMorph: typeof morph;
        __raskNote: typeof raskNotePendingFormState;
    }
}

window.__raskMorph = morph;
window.__raskNote = raskNotePendingFormState;

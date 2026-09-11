// The devtools host script. The server writes a tag for it into the <head> of a Debug page served in
// Development; it is never part of rask.js or rask.wasm.js, and neither runtime has code to load it, which is what
// keeps a Release page free of it.
//
// This is the entry point. The pill, the dock, the panel iframe and the bridge to it are its modules as they
// arrive. It is idempotent: whatever loads it, one document must never run the tools twice.

declare global {
  interface Window {
    /** Present once the devtools host script has run in this document. */
    __raskDevtoolsHost?: { readonly version: 1 };
  }
}

if (!window.__raskDevtoolsHost) {
  window.__raskDevtoolsHost = { version: 1 };
}

export {};

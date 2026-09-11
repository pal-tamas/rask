// What crosses between a WASM page and its devtools panel frame. Types and a constant only: both sides import it, and
// neither may pick up the other's side effects through it — the frame client binds document listeners when it loads.

/** Marks the devtools' messages among whatever else the page and its frames post. */
export const CHANNEL = "rask-devtools";

export type FrameMessage =
    /** Page → frame: one render frame from the panel session, as UTF-8 JSON. */
    | {readonly channel: typeof CHANNEL; readonly kind: "frame"; readonly bytes: Uint8Array}
    /** Frame → page: the frame client is listening; frames sent before this are held until it arrives. */
    | {readonly channel: typeof CHANNEL; readonly kind: "ready"}
    /** Frame → page: an event for the panel session, the same payload the runtime sends for a page event. */
    | {readonly channel: typeof CHANNEL; readonly kind: "event"; readonly payload: unknown}
    /** Frame → page: the devtools shortcut, pressed with focus inside the panel. */
    | {readonly channel: typeof CHANNEL; readonly kind: "toggle"};

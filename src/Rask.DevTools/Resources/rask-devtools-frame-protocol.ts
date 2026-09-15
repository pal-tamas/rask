// What crosses between a page and its devtools panel frame. Types and a constant only: both sides import it, and
// neither may pick up the other's side effects through it — the frame client binds document listeners when it loads.
//
// Two panels speak it. On a WASM page the frame is a srcdoc document the page posts render frames into, and every kind
// below is used. On a Server page the frame is the panel's own live page, which talks to the app over its own socket;
// only the page-overlay kinds (highlight, pick, picked, pick-cancelled) and toggle cross, posted by its panel script.

/** Marks the devtools' messages among whatever else the page and its frames post. */
export const CHANNEL = "rask-devtools";

export type FrameMessage =
    /** Page → frame (WASM): one render frame from the panel session, as UTF-8 JSON. */
    | {readonly channel: typeof CHANNEL; readonly kind: "frame"; readonly bytes: Uint8Array}
    /** Frame → page (WASM): the frame client is listening; frames sent before this are held until it arrives. */
    | {readonly channel: typeof CHANNEL; readonly kind: "ready"}
    /** Frame → page (WASM): an event for the panel session, the same payload the runtime sends for a page event. */
    | {readonly channel: typeof CHANNEL; readonly kind: "event"; readonly payload: unknown}
    /** Frame → page: the devtools shortcut, pressed with focus inside the panel. */
    | {readonly channel: typeof CHANNEL; readonly kind: "toggle"}
    /**
     * Frame → page: box the page nodes at `at` (`path|firstSlot|count`, as the Tree tab writes it) under `label`; null
     * clears the box.
     */
    | {readonly channel: typeof CHANNEL; readonly kind: "highlight"; readonly at: string | null; readonly label: string | null}
    /**
     * Frame → page: start picking against `anchors` (the Tree tab's JSON, `[[id, at, label], …]`), or stop when null.
     * Sent again whenever the anchors change, so a pick in progress follows the page's re-renders.
     */
    | {readonly channel: typeof CHANNEL; readonly kind: "pick"; readonly anchors: string | null}
    /** Page → frame: the developer clicked the node `id` while picking. */
    | {readonly channel: typeof CHANNEL; readonly kind: "picked"; readonly id: string}
    /** Page → frame: the pick was abandoned — Esc, or the drawer closed. */
    | {readonly channel: typeof CHANNEL; readonly kind: "pick-cancelled"}
    /** Frame → page: flashing was switched on or off in the Renders tab. The page remembers it and flashes DOM changes. */
    | {readonly channel: typeof CHANNEL; readonly kind: "flash-setting"; readonly on: boolean}
    /**
     * Page → frame: the page applied a frame from the app in `ms` milliseconds. `bytes` is that frame's size on the wire,
     * which is what the app matches the time to, or -1 when the page did not see it arrive.
     */
    | {readonly channel: typeof CHANNEL; readonly kind: "patch"; readonly ms: number; readonly bytes: number}
    /** Frame → page: how many errors the Errors tab has that nobody has looked at yet, for the pill. */
    | {readonly channel: typeof CHANNEL; readonly kind: "error-count"; readonly count: number}
    /** Page → frame: show the Errors tab — the overlay's "Open in DevTools" was pressed. */
    | {readonly channel: typeof CHANNEL; readonly kind: "show-errors"}
    /** Frame → page: flash these components, which rendered in a commit, as `[at, label]` pairs. */
    | {readonly channel: typeof CHANNEL; readonly kind: "flash"; readonly boxes: readonly (readonly [string, string])[]};

/** Where the page remembers whether flashing is on: `"on"`, or nothing. Both the page and a same-origin panel read it. */
export const FLASH_STORAGE_KEY = "rask.devtools.flash";

/** Whether the page remembered flashing as on. Storage can be blocked (a sandboxed frame, a privacy mode): then off. */
export function readFlashSetting(): boolean {
    try {
        return localStorage.getItem(FLASH_STORAGE_KEY) === "on";
    } catch {
        return false;
    }
}

export function writeFlashSetting(on: boolean): void {
    try {
        if (on) {
            localStorage.setItem(FLASH_STORAGE_KEY, "on");
        } else {
            localStorage.removeItem(FLASH_STORAGE_KEY);
        }
    } catch {
        // Blocked, as above: the setting then lasts this page.
    }
}

/** Narrows a posted message to one of ours, of any kind. Everything else a page's frames post is ignored. */
export function asFrameMessage(data: unknown): FrameMessage | null {
    return data !== null && typeof data === "object" && (data as {channel?: unknown}).channel === CHANNEL
        && typeof (data as {kind?: unknown}).kind === "string"
        ? data as FrameMessage
        : null;
}

// The panel's side of the page overlays, shared by both panels: the Server panel page loads it as its own script, and
// the WASM panel frame bundles it into its frame client.
//
// It never draws anything and never talks to the app. It reads what the Tree tab rendered — the place of the row under
// the pointer, the anchors while picking — and posts it to the page, where the devtools host draws the box; and it hands
// a pick the page reports back to the tab as a keydown on the tab's hidden element, the one event both panels forward to
// their session with a value. A hover therefore costs no round trip: the row already says where its node is.

import {isToggleShortcut} from "../host/dock.js";
import {asFrameMessage, CHANNEL, type FrameMessage, readFlashSetting} from "../rask-devtools-frame-protocol.js";

/** The prefix of the keydown `key` a pick is reported as; `DevToolsTreeTab.PickKeyPrefix` on the C# side. */
export const PICK_KEY_PREFIX = "pick:";

/** The prefix of the keydown `key` the remembered flash setting is reported as; `DevToolsFlashEmitter.SettingKeyPrefix`. */
export const FLASH_KEY_PREFIX = "flash:";

/** The keydown `key` that asks the panel to show its Errors tab; `DevToolsTabIds.ShowErrorsKey`. */
export const SHOW_ERRORS_KEY = "errors:show";

/** The prefix of the keydown `key` the page's patch times are reported as; `DevToolsPatchReceiver.KeyPrefix`. */
export const PATCH_KEY_PREFIX = "patch:";

/** How long patch times are collected before one report carries them all, in milliseconds. */
export const PATCH_BATCH_MS = 250;

export interface PanelClientOptions {
    /** Posts a message to the page. */
    readonly post: (message: FrameMessage) => void;
    /** Whether a message event came from the page this panel belongs to. */
    readonly fromPage: (e: MessageEvent) => boolean;
}

/** Binds the panel's listeners to this document. Call once. */
export function installPanelClient(options: PanelClientOptions): void {
    const {post, fromPage} = options;
    let lastAt: string | null = null;
    let lastAnchors: string | null = null;

    const highlight = (at: string | null, label: string | null) => {
        if (at === lastAt) return;
        lastAt = at;
        post({channel: CHANNEL, kind: "highlight", at, label});
    };

    // Delegated, so rows the virtualized tree renders later are covered without binding anything to them.
    document.addEventListener("pointerover", (e: PointerEvent) => {
        const target = e.target instanceof Element ? e.target : null;
        const row = target ? target.closest(".ui-tree-row") : null;
        const place = row ? row.querySelector("[data-rask-devtools-at]") : null;
        highlight(
            place ? place.getAttribute("data-rask-devtools-at") : null,
            place ? place.getAttribute("data-rask-devtools-label") : null);
    });

    // The pointer leaving the panel's document altogether: no pointerover follows inside it to clear the box.
    document.documentElement.addEventListener("pointerleave", () => highlight(null, null));

    const syncAnchors = () => {
        const el = document.querySelector("[data-rask-devtools-anchors]");
        const anchors = el ? el.getAttribute("data-rask-devtools-anchors") : null;
        if (anchors === lastAnchors) return;
        lastAnchors = anchors;
        post({channel: CHANNEL, kind: "pick", anchors});
    };

    // The Errors tab's unseen count, handed to the page for the pill; and the page's request to show the tab, held until
    // the tab strip has rendered the element that takes it.
    let lastErrorCount: string | null = null;
    let showErrorsWanted = false;
    const syncErrors = () => {
        const el = document.querySelector("[data-rask-devtools-errors]");
        if (!el) return;
        const count = el.getAttribute("data-rask-devtools-errors");
        if (count !== lastErrorCount) {
            lastErrorCount = count;
            const parsed = Number(count);
            post({channel: CHANNEL, kind: "error-count", count: Number.isFinite(parsed) ? parsed : 0});
        }
        if (showErrorsWanted) {
            showErrorsWanted = false;
            el.dispatchEvent(new KeyboardEvent("keydown", {key: SHOW_ERRORS_KEY, bubbles: true}));
        }
    };

    // Patch times, collected and reported a few at a time as `ms@bytes`: a keydown per frame would be a round trip per frame.
    const patches: string[] = [];
    let patchTimer: ReturnType<typeof setTimeout> | null = null;
    const flushPatches = () => {
        patchTimer = null;
        const batch = patches.splice(0, 64);
        if (patches.length > 0) patchTimer = setTimeout(flushPatches, PATCH_BATCH_MS);
        const el = document.querySelector("[data-rask-devtools-patch]");
        if (!el || batch.length === 0) return;
        el.dispatchEvent(new KeyboardEvent("keydown", {
            key: PATCH_KEY_PREFIX + batch.join(","),
            bubbles: true,
        }));
    };
    const queuePatch = (patch: string) => {
        patches.push(patch);
        if (patchTimer === null) patchTimer = setTimeout(flushPatches, PATCH_BATCH_MS);
    };

    // Flashing. The panel starts with it off and cannot read the page's storage, so the setting the page remembered is
    // reported once, as soon as the panel has rendered the element that takes it. After that the panel's own switch is
    // the truth: every change is handed to the page, which remembers it. Commits are flashed once each, by sequence, and
    // the ones already rendered when flashing came on are the baseline rather than a burst of stale boxes.
    let flashReported = false;
    let lastFlashSetting: string | null = null;
    let lastFlashed = -1;
    const syncFlash = () => {
        const el = document.querySelector("[data-rask-devtools-flash]");
        if (!el) return;
        const setting = el.getAttribute("data-rask-devtools-flash");
        if (!flashReported) {
            flashReported = true;
            const remembered = readFlashSetting();
            if (remembered !== (setting === "on")) {
                el.dispatchEvent(new KeyboardEvent("keydown", {key: FLASH_KEY_PREFIX + (remembered ? "on" : "off"), bubbles: true}));
                return;
            }
        }
        if (setting !== lastFlashSetting) {
            lastFlashSetting = setting;
            post({channel: CHANNEL, kind: "flash-setting", on: setting === "on"});
            if (setting !== "on") lastFlashed = -1;
        }

        const raw = document.querySelector("[data-rask-devtools-flashes]")?.getAttribute("data-rask-devtools-flashes");
        if (setting !== "on" || !raw) return;
        const commits = parseFlashes(raw);
        if (lastFlashed < 0) {
            lastFlashed = commits.length > 0 ? commits[commits.length - 1][0] : 0;
            return;
        }
        for (const [sequence, boxes] of commits) {
            if (sequence <= lastFlashed) continue;
            lastFlashed = sequence;
            if (boxes.length > 0) post({channel: CHANNEL, kind: "flash", boxes});
        }
    };

    // The tabs' renders are what start, update and stop a pick and a flash; the panel's runtime applies them to this
    // document.
    if (typeof MutationObserver === "function") {
        new MutationObserver(() => {
            syncAnchors();
            syncFlash();
            syncErrors();
        }).observe(document.documentElement, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: [
                "data-rask-devtools-anchors", "data-rask-devtools-flash", "data-rask-devtools-flashes",
                "data-rask-devtools-errors",
            ],
        });
    }
    syncAnchors();
    syncFlash();
    syncErrors();

    window.addEventListener("message", (e: MessageEvent) => {
        if (!fromPage(e)) return;
        const message = asFrameMessage(e.data);
        if (!message) return;
        if (message.kind === "picked" && typeof message.id === "string") {
            reportPick(message.id);
        } else if (message.kind === "pick-cancelled") {
            reportPick("cancel");
        } else if (message.kind === "show-errors") {
            showErrorsWanted = true;
            syncErrors();
        } else if (message.kind === "patch" && typeof message.ms === "number" && Number.isFinite(message.ms) && message.ms >= 0
                   && Number.isInteger(message.bytes) && message.bytes >= -1) {
            queuePatch(message.ms.toFixed(2) + "@" + message.bytes);
        }
    });

    // With focus inside the panel — where it is right after pressing Pick — the page never sees a keystroke. The shortcut
    // is handed up; Esc during a pick ends it here, and the anchors leaving the tab's render stop the page's picker.
    window.addEventListener("keydown", (e: KeyboardEvent) => {
        if (isToggleShortcut(e)) {
            e.preventDefault();
            e.stopImmediatePropagation();
            post({channel: CHANNEL, kind: "toggle"});
        } else if (e.key === "Escape" && lastAnchors !== null) {
            e.preventDefault();
            reportPick("cancel");
        }
    }, true);
}

/** The emitter's `[[sequence, [[at, label], …]], …]`, keeping only well-formed entries; garbage reads as none. */
export function parseFlashes(raw: string): [number, [string, string][]][] {
    let data: unknown;
    try {
        data = JSON.parse(raw);
    } catch {
        return [];
    }
    if (!Array.isArray(data)) return [];
    const commits: [number, [string, string][]][] = [];
    for (const entry of data) {
        if (!Array.isArray(entry) || typeof entry[0] !== "number" || !Array.isArray(entry[1])) continue;
        const boxes = (entry[1] as unknown[]).filter((b): b is [string, string] =>
            Array.isArray(b) && typeof b[0] === "string" && typeof b[1] === "string");
        commits.push([entry[0], boxes]);
    }
    return commits;
}

// A keydown on the tab's hidden element: the runtime's shared key forwarding sends it to the tab with its key.
function reportPick(value: string): void {
    const el = document.querySelector("[data-rask-devtools-picked]");
    if (!el) return;
    el.dispatchEvent(new KeyboardEvent("keydown", {key: PICK_KEY_PREFIX + value, bubbles: true}));
}

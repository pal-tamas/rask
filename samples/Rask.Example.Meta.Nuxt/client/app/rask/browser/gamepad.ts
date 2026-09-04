// Game controllers — navigator.getGamepads.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// The Gamepad API has no input event: the only way to read a pad is to poll it. So this runs a
// requestAnimationFrame loop and reports only what CHANGED — a held stick or a resting pad produces
// nothing. rAF also means the browser pauses the poll while the tab is hidden, which is the behaviour
// you want and would have to build by hand on a timer.
//
// A pad stays invisible to the page until the user presses something on it. That is a deliberate
// anti-fingerprinting measure, not a bug to work around.

export interface GamepadReading {
    /** The slot the browser assigned. Stable while the pad stays connected. */
    index: number;
    /** The controller's self-reported name. Empty on a disconnect reading. */
    id: string;
    connected: boolean;
    /** Stick positions, -1 to 1, rounded to three places so noise does not read as movement. */
    axes: number[];
    /** Button pressure, 0 to 1. A digital button reports 0 or 1. */
    buttons: number[];
}

export interface GamepadOptions {
    /**
     * Minimum milliseconds between polls. 0 (the default) polls every animation frame; readings are
     * change-gated regardless, so this only bounds how quickly a change is noticed.
     */
    throttleMs?: number;
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && "getGamepads" in navigator;
}

/** Watch every connected pad. Returns the stop function. */
export function watch(
    onReading: (reading: GamepadReading) => void,
    options?: GamepadOptions): () => void {
    const throttleMs = (options && options.throttleMs) || 0;
    let last = 0;
    let raf = 0;

    // pad index -> last serialized snapshot, so only a real change is reported
    const previous = new Map<number, string>();

    const tick = () => {
        const now = Date.now();
        if (now - last >= throttleMs) {
            last = now;
            const pads = navigator.getGamepads ? navigator.getGamepads() : [];
            const live = new Set<number>();

            for (let i = 0; i < pads.length; i++) {
                const p = pads[i];
                if (!p) {
                    continue;
                }
                live.add(p.index);
                const axes = Array.from(p.axes, (a) => Math.round(a * 1000) / 1000);
                const buttons = Array.from(p.buttons, (b) => b.value);
                const snapshot = axes.join(",") + "|" + buttons.join(",") + "|" + p.connected;
                if (previous.get(p.index) !== snapshot) {
                    previous.set(p.index, snapshot);
                    onReading({
                        index: p.index,
                        id: p.id,
                        connected: p.connected,
                        axes,
                        buttons
                    });
                }
            }

            // A pad that vanished between polls gets one last reading, so a caller's state does not
            // keep a controller that is no longer there.
            previous.forEach((_snapshot, index) => {
                if (!live.has(index)) {
                    previous.delete(index);
                    onReading({index, id: "", connected: false, axes: [], buttons: []});
                }
            });
        }
        raf = requestAnimationFrame(tick);
    };

    raf = requestAnimationFrame(tick);

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        cancelAnimationFrame(raf);
    };
}

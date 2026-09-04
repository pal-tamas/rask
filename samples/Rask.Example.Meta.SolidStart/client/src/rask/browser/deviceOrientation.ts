// Device orientation — the "deviceorientation" event.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Compass and tilt. iOS gates this behind a permission that can only be requested from inside a user
// gesture, which is why `requestPermission` exists and why calling it from a timer silently fails.

/** Safari added `requestPermission` as a static on the event constructor; lib.dom declares neither. */
interface PermissionGatedEvent {
    requestPermission?(): Promise<string>;
}

function gate(name: "DeviceOrientationEvent" | "DeviceMotionEvent"): PermissionGatedEvent | undefined {
    if (typeof window === "undefined") {
        return undefined;
    }
    return (window as unknown as Record<string, PermissionGatedEvent | undefined>)[name];
}

export interface OrientationReading {
    /** Compass direction, 0–360. Null when the device cannot tell. */
    alpha: number | null;
    /** Front-to-back tilt, -180–180. */
    beta: number | null;
    /** Left-to-right tilt, -90–90. */
    gamma: number | null;
    /** Whether the reading is against the earth's frame rather than an arbitrary starting point. */
    absolute: boolean;
}

export interface SensorOptions {
    /**
     * Drop readings that arrive within this many milliseconds of the last one. 0 (the default) passes
     * every event through — these sensors fire at roughly 60 Hz, so a callback that does real work,
     * or one that crosses a network boundary, wants a throttle.
     */
    throttleMs?: number;
}

export function isSupported(): boolean {
    return typeof window !== "undefined" && "DeviceOrientationEvent" in window;
}

/**
 * Ask for permission. Resolves "granted" on browsers that require no prompt, "denied" where the API
 * is absent or the user refused.
 *
 * Must be called from within a user gesture on iOS — from a click handler, not after an await.
 */
export function requestPermission(): Promise<string> {
    const evt = gate("DeviceOrientationEvent");
    if (!evt) {
        return Promise.resolve("denied");
    }
    if (typeof evt.requestPermission === "function") {
        return evt.requestPermission().catch(() => "denied");
    }
    return Promise.resolve("granted");
}

/** Watch the device's orientation. Returns the stop function. */
export function watch(
    onReading: (reading: OrientationReading) => void,
    options?: SensorOptions): () => void {
    const throttleMs = (options && options.throttleMs) || 0;
    let last = 0;

    const handler = (e: DeviceOrientationEvent) => {
        if (throttleMs > 0) {
            const now = Date.now();
            if (now - last < throttleMs) {
                return;
            }
            last = now;
        }
        onReading({alpha: e.alpha, beta: e.beta, gamma: e.gamma, absolute: !!e.absolute});
    };

    window.addEventListener("deviceorientation", handler as EventListener);

    let stopped = false;
    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        window.removeEventListener("deviceorientation", handler as EventListener);
    };
}

// Battery Status — navigator.getBattery.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Chromium-only in practice; lib.dom dropped the types, so BatteryManagerLike is declared in
// rask-window.d.ts. Firefox and Safari removed it deliberately (it was a fingerprinting vector), so
// treat an unsupported browser as the normal case rather than the exception.

/** The four events the manager fires; any of them means "read the snapshot again". */
const EVENTS = ["levelchange", "chargingchange", "chargingtimechange", "dischargingtimechange"];

export interface BatteryStatus {
    /** 0–1. */
    level: number;
    charging: boolean;
    /** Seconds until full, or null when the browser does not know (it reports Infinity). */
    chargingTime: number | null;
    /** Seconds until empty, or null when unknown. */
    dischargingTime: number | null;
}

/**
 * Infinity is what the platform reports for "unknown", and JSON cannot carry it — it would serialize
 * as null on one transport and throw on another, so it is mapped here rather than at the boundary.
 */
function read(b: BatteryManagerLike): BatteryStatus {
    return {
        level: b.level,
        charging: b.charging,
        chargingTime: isFinite(b.chargingTime) ? b.chargingTime : null,
        dischargingTime: isFinite(b.dischargingTime) ? b.dischargingTime : null
    };
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && typeof navigator.getBattery === "function";
}

/** One snapshot, or null where unsupported. */
export function getStatus(): Promise<BatteryStatus | null> {
    return isSupported() ? navigator.getBattery!().then(read) : Promise.resolve(null);
}

/**
 * Watch the battery, calling back on every change. Returns the stop function SYNCHRONOUSLY, even
 * though the manager arrives asynchronously.
 *
 * That shape is deliberate. `getBattery()` is a promise, so a stop that had to be awaited would leave
 * a window in which a caller has already torn down but the listeners have not been attached yet — and
 * attaching them afterwards leaks a subscription nobody holds a handle to. Resolving into a `stopped`
 * flag makes that interleaving unrepresentable instead of merely unlikely.
 */
export function watch(onChange: (status: BatteryStatus) => void): () => void {
    let stopped = false;
    let detach: (() => void) | null = null;

    if (isSupported()) {
        navigator.getBattery!().then((b: BatteryManagerLike) => {
            if (stopped) {
                return;
            }
            const handler = () => onChange(read(b));
            EVENTS.forEach((e: string) => b.addEventListener(e, handler));
            detach = () => EVENTS.forEach((e: string) => b.removeEventListener(e, handler));
        });
    }

    return () => {
        if (stopped) {
            return;
        }
        stopped = true;
        if (detach) {
            detach();
            detach = null;
        }
    };
}

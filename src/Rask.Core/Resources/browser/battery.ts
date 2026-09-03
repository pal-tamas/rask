// Battery Status — navigator.getBattery.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// Chromium-only in practice. Firefox and Safari removed it deliberately (it was a fingerprinting
// vector), so treat an unsupported browser as the normal case rather than the exception.
//
// The vendor shapes below are declared HERE rather than in a shared .d.ts, and that is the point: a
// module in this directory ships to front ends that have lib.dom and nothing else. One depending on an
// ambient declaration the consumer lacks compiles inside the framework and fails in their build — which
// is exactly how this was found, when the CLI gate refused a push over a scaffolded client that could
// not compile these files.

/** The four events the manager fires; any of them means "read the snapshot again". */
const EVENTS = ["levelchange", "chargingchange", "chargingtimechange", "dischargingtimechange"];

/**
 * lib.dom dropped BatteryManager, so the shape is declared here rather than assumed.
 *
 * Locally, and not in a shared .d.ts, because these modules ship to front ends that have only
 * lib.dom: a module depending on an ambient declaration the consumer does not have compiles here and
 * fails in their build, which is exactly how this was found — the CLI gate refused a push over a
 * scaffolded React client that could not compile `browser/battery.ts`.
 */
interface BatteryManagerLike extends EventTarget {
    level: number;
    charging: boolean;
    chargingTime: number;
    dischargingTime: number;
}

interface NavigatorWithBattery extends Navigator {
    getBattery?(): Promise<BatteryManagerLike>;
}

/** The one place the cast happens, so no call site has to know about it. */
function battery(): NavigatorWithBattery | null {
    return typeof navigator === "undefined" ? null : navigator as NavigatorWithBattery;
}

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
    return typeof battery()?.getBattery === "function";
}

/** One snapshot, or null where unsupported. */
export function getStatus(): Promise<BatteryStatus | null> {
    const nav = battery();
    return nav?.getBattery ? nav.getBattery().then(read) : Promise.resolve(null);
}

/** A live battery subscription. */
export interface BatteryWatch {
    /**
     * Resolves once the listeners are attached, and REJECTS if the manager could not be obtained —
     * which Chromium does in a cross-origin iframe without the `battery` permission policy.
     *
     * Separate from `stop` on purpose. A caller that ignores this still gets a working subscription
     * where one is possible; a caller that awaits it learns that it never started, instead of holding
     * a handle to a subscription that will never fire.
     */
    readonly attached: Promise<void>;

    stop(): void;
}

/**
 * Watch the battery, calling back on every change. `stop` is available SYNCHRONOUSLY, even though the
 * manager arrives asynchronously.
 *
 * That shape is deliberate. `getBattery()` is a promise, so a stop that had to be awaited would leave
 * a window in which a caller has already torn down but the listeners have not been attached yet — and
 * attaching them afterwards leaks a subscription nobody holds a handle to. Resolving into a `stopped`
 * flag makes that interleaving unrepresentable instead of merely unlikely.
 */
export function watch(onChange: (status: BatteryStatus) => void): BatteryWatch {
    let stopped = false;
    let detach: (() => void) | null = null;

    const nav = battery();
    const attached = nav?.getBattery
        ? nav.getBattery().then((b: BatteryManagerLike) => {
            if (stopped) {
                return;
            }
            const handler = () => onChange(read(b));
            EVENTS.forEach((e: string) => b.addEventListener(e, handler));
            detach = () => EVENTS.forEach((e: string) => b.removeEventListener(e, handler));
        })
        : Promise.resolve();

    const stop = () => {
        if (stopped) {
            return;
        }
        stopped = true;
        if (detach) {
            detach();
            detach = null;
        }
    };

    return {attached, stop};
}
